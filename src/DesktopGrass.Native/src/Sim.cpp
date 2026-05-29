// Sim.cpp
//
// Pure simulation code. Ports docs/architecture.md §§3-10 verbatim.

#include "Sim.h"

#include <cmath>
#include <algorithm>

namespace desktopgrass {

// ---------------------------------------------------------------------------
// PRNG
// ---------------------------------------------------------------------------

uint64_t splitmix64(uint64_t z) noexcept {
    z = z + 0x9E3779B97F4A7C15ull;
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ull;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EBull;
    return z ^ (z >> 31);
}

void prng_init(Prng& p, uint64_t seed) noexcept {
    p.state = splitmix64(seed);
    if (p.state == 0) {
        // Belt-and-suspenders for the (highly unlikely) zero state. Matches the
        // spec.
        p.state = 0x9E3779B97F4A7C15ull;
    }
}

uint64_t prng_next_u64(Prng& p) noexcept {
    uint64_t x = p.state;
    x ^= x << 13;
    x ^= x >> 7;
    x ^= x << 17;
    p.state = x;
    return x;
}

double prng_next_unit(Prng& p) noexcept {
    // Top 53 bits → IEEE-754 mantissa precision.
    return static_cast<double>(prng_next_u64(p) >> 11) * (1.0 / 9007199254740992.0);
}

double prng_uniform(Prng& p, double lo, double hi) noexcept {
    return lo + prng_next_unit(p) * (hi - lo);
}

uint32_t prng_index(Prng& p, uint32_t n) noexcept {
    return static_cast<uint32_t>(prng_next_unit(p) * static_cast<double>(n));
}

// ---------------------------------------------------------------------------
// Blade generation. Field-draw order MUST be (height, thickness, hue,
// swayPhaseOffset, stiffness) — see architecture.md §5.
// ---------------------------------------------------------------------------

void generate_blades(uint64_t seed, double monitorWidth, double density,
                     std::vector<Blade>& out)
{
    out.clear();
    if (monitorWidth <= 0.0 || density <= 0.0) return;

    Prng p;
    prng_init(p, seed);

    // Independent stream for regrowth jitter. Seeding it from `seed XOR salt`
    // makes the jitter deterministic for a given seed *without* advancing the
    // main PRNG state — existing snapshot tests / cross-impl conformance
    // (10,787 unique colors for the canonical seed) stay bit-identical.
    Prng pRegrow;
    prng_init(pRegrow, seed ^ REGROW_PRNG_SALT);

    double x = 0.0;
    while (true) {
        double step = prng_uniform(p, BLADE_SPACING_MIN, BLADE_SPACING_MAX) / density;
        x += step;
        if (x >= monitorWidth) break;

        Blade b{};
        b.baseX            = x;
        b.height           = prng_uniform(p, BLADE_HEIGHT_MIN,    BLADE_HEIGHT_MAX);
        b.thickness        = prng_uniform(p, BLADE_THICKNESS_MIN, BLADE_THICKNESS_MAX);
        b.hue              = static_cast<uint8_t>(prng_index(p, PALETTE_SIZE));
        b.swayPhaseOffset  = prng_uniform(p, 0.0, 2.0 * 3.14159265358979323846);
        b.stiffness        = prng_uniform(p, STIFFNESS_MIN, STIFFNESS_MAX);

        b.cutHeight        = 1.0;
        b.gustVelocity     = 0.0;
        b.cutAnimStart     = -1.0;
        b.cutInitialHeight = 1.0;
        b.effectiveLean    = 0.0;

        // Regrowth jitter — independent stream. Draw delay first, then duration
        // (field-draw order MUST match across all three impls).
        b.regrowDelay      = prng_uniform(pRegrow, REGROW_DELAY_MIN,    REGROW_DELAY_MAX);
        b.regrowDuration   = prng_uniform(pRegrow, REGROW_DURATION_MIN, REGROW_DURATION_MAX);
        b.regrowStart      = -1.0;

        out.push_back(b);
    }
}

// ---------------------------------------------------------------------------
// Sway / gust dynamics
// ---------------------------------------------------------------------------

void update_blade_dynamics(Blade& b, double globalTime, double dt) noexcept {
    // 1. Gust velocity decays exponentially.
    b.gustVelocity *= std::exp(-DECAY_RATE * dt);

    // 2. Sway is a pure function of globalTime + per-blade phase offset.
    const double swayPhase = b.swayPhaseOffset + globalTime * BASE_SWAY_SPEED;
    const double baseLean  = std::sin(swayPhase) * BASE_AMPLITUDE * b.stiffness;

    // 3. Combined lean.
    b.effectiveLean = baseLean + b.gustVelocity * GUST_TO_LEAN_FACTOR;
}

// ---------------------------------------------------------------------------
// Cut animation
// ---------------------------------------------------------------------------

void advance_cut(Blade& b, double globalTime) noexcept {
    // Phase 1: cut animation is running.
    if (b.cutAnimStart >= 0.0) {
        const double elapsed = globalTime - b.cutAnimStart;
        const double t       = elapsed / CUT_DURATION_SEC;
        if (t >= 1.0) {
            b.cutHeight    = 0.0;
            b.cutAnimStart = -1.0;
            // Schedule regrowth only if the per-blade jitter is well-defined.
            // Production blades from generate_blades always satisfy this;
            // test fixtures that zero-init Blade end up with delay=0 and
            // therefore stay cut, which matches their pre-regrowth contract.
            if (b.regrowDelay > 0.0 && b.regrowDuration > 0.0) {
                b.regrowStart = globalTime + b.regrowDelay;
            }
        } else {
            b.cutHeight = b.cutInitialHeight * (1.0 - t);
        }
        return;
    }

    // Phase 2: regrowth scheduled / running. Idle if regrowStart < 0, the
    // scheduled time hasn't arrived yet, or duration is non-positive.
    if (b.regrowStart < 0.0 || globalTime < b.regrowStart) return;
    if (b.regrowDuration <= 0.0) {
        b.cutHeight   = 1.0;
        b.regrowStart = -1.0;
        return;
    }

    const double elapsed = globalTime - b.regrowStart;
    const double t       = elapsed / b.regrowDuration;
    if (t >= 1.0) {
        b.cutHeight   = 1.0;
        b.regrowStart = -1.0;
    } else {
        // Linear regrowth 0 -> 1. Same easing curve as the cut animation,
        // just in reverse and over a longer span.
        b.cutHeight = t;
    }
}

// ---------------------------------------------------------------------------
// Move / click event handlers
// ---------------------------------------------------------------------------

void sim_apply_move(Sim& sim, const InputEvent& e) noexcept {
    const double groundY        = sim.windowHeight;
    const double gustBandTop    = groundY - STRIP_HEIGHT - HEADROOM;
    const double gustBandBottom = groundY;

    // First / re-init event: don't emit an impulse.
    const bool firstEvent =
        sim.prevCursorTime < 0.0 ||
        (e.time - sim.prevCursorTime) > CURSOR_REINIT_GAP_SEC;

    if (firstEvent) {
        sim.prevCursorX    = e.x;
        sim.prevCursorTime = e.time;
        return;
    }

    if (e.y < gustBandTop || e.y > gustBandBottom) {
        // Outside the gust band — update baseline (so the *next* in-band event
        // gets a sensible velocity), but emit no impulse.
        sim.prevCursorX    = e.x;
        sim.prevCursorTime = e.time;
        return;
    }

    const double dt_ev  = std::max(e.time - sim.prevCursorTime, 1.0 / 1000.0);
    const double velX   = (e.x - sim.prevCursorX) / dt_ev;
    const double capped = std::max(-MAX_CURSOR_SPEED, std::min(velX, MAX_CURSOR_SPEED));

    sim.prevCursorX    = e.x;
    sim.prevCursorTime = e.time;

    const double impulseMagnitude = std::fabs(capped) * IMPULSE_SCALE;
    const double signDir = (capped > 0.0) ? 1.0 : (capped < 0.0 ? -1.0 : 0.0);
    if (signDir == 0.0 || impulseMagnitude == 0.0) return;

    for (Blade& b : sim.blades) {
        const double dxAbs = std::fabs(b.baseX - e.x);
        if (dxAbs >= GUST_RADIUS) continue;

        const double tRaw  = 1.0 - dxAbs / GUST_RADIUS;
        const double s     = std::max(0.0, std::min(1.0, tRaw));
        const double smooth = s * s * (3.0 - 2.0 * s);
        b.gustVelocity += impulseMagnitude * smooth * signDir;
    }
}

void sim_apply_click(Sim& sim, const InputEvent& e) noexcept {
    const double groundY       = sim.windowHeight;
    const double cutBandTop    = groundY - STRIP_HEIGHT;
    const double cutBandBottom = groundY;

    if (e.y < cutBandTop || e.y > cutBandBottom) return;

    for (Blade& b : sim.blades) {
        if (std::fabs(b.baseX - e.x) >= CUT_RADIUS) continue;
        if (b.cutHeight <= 0.0) continue;
        if (b.cutAnimStart >= 0.0) continue;

        b.cutAnimStart     = sim.globalTime;
        b.cutInitialHeight = b.cutHeight;
        // Cancel any pending or in-progress regrowth: we're going back down.
        b.regrowStart      = -1.0;
    }
}

// ---------------------------------------------------------------------------
// Stroke geometry. architecture.md §7.
// ---------------------------------------------------------------------------

Stroke compute_blade_stroke(const Blade& b, double groundY) noexcept {
    Stroke s{};
    s.argb      = PALETTE[b.hue];
    s.thickness = b.thickness;

    if (b.cutHeight < CUT_STUMP_THRESHOLD) {
        s.base    = { b.baseX, groundY };
        s.control = { b.baseX, groundY - 1.0 };
        s.tip     = { b.baseX, groundY - STUMP_HEIGHT };
        return s;
    }

    const double tipX = b.baseX + b.effectiveLean;
    const double tipY = groundY - b.height * b.cutHeight;

    const double dx   = tipX - b.baseX;
    const double dy   = tipY - groundY;
    const double len  = std::sqrt(dx * dx + dy * dy);

    // Perpendicular rotated 90° CCW: (x, y) -> (-y, x).
    const double nx = (len > 0.0) ? (-dy / len) : 0.0;
    const double ny = (len > 0.0) ? ( dx / len) : 0.0;

    const double midX   = (b.baseX + tipX) * 0.5;
    const double midY   = (groundY + tipY) * 0.5;
    const double offset = CTRL_OFFSET_FACTOR * b.effectiveLean;

    s.base    = { b.baseX, groundY };
    s.control = { midX + nx * offset, midY + ny * offset };
    s.tip     = { tipX, tipY };
    return s;
}

// ---------------------------------------------------------------------------
// Sim glue
// ---------------------------------------------------------------------------

Sim sim_init(uint64_t seed, double monitorWidth, double density) {
    Sim s;
    s.windowHeight = STRIP_HEIGHT + HEADROOM;
    generate_blades(seed, monitorWidth, density, s.blades);
    return s;
}

void sim_regenerate(Sim& sim, uint64_t seed, double monitorWidth, double density) {
    sim.globalTime     = 0.0;
    sim.prevCursorX    = 0.0;
    sim.prevCursorTime = -1.0;
    generate_blades(seed, monitorWidth, density, sim.blades);
}

void sim_tick(Sim& sim, double dt,
              const InputEvent* events, std::size_t numEvents) noexcept
{
    sim.globalTime += dt;

    for (std::size_t i = 0; i < numEvents; ++i) {
        const InputEvent& e = events[i];
        switch (e.type) {
            case EventType::Move:  sim_apply_move(sim, e);  break;
            case EventType::Click: sim_apply_click(sim, e); break;
        }
    }

    for (Blade& b : sim.blades) {
        update_blade_dynamics(b, sim.globalTime, dt);
        advance_cut(b, sim.globalTime);
    }
}

} // namespace desktopgrass
