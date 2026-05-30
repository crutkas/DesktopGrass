// Sim.cpp
//
// Pure simulation code. Ports docs/architecture.md §§3-10 verbatim.

#include "Sim.h"

#include <cmath>
#include <algorithm>
#include <chrono>
#include <ctime>

namespace desktopgrass {

namespace {
constexpr double TWO_PI = 6.28318530717958647692;

bool hour_in_half_open_range(int hour, int start, int end) noexcept {
    if (start <= end) return hour >= start && hour < end;
    return hour >= start || hour < end;
}

int current_local_hour() noexcept {
    const std::time_t now = std::chrono::system_clock::to_time_t(
        std::chrono::system_clock::now());
    std::tm local{};
    if (localtime_s(&local, &now) != 0) return SHEEP_MORNING_END_HOUR;
    return local.tm_hour;
}
}

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

uint32_t prng_next_u32(Prng& p) noexcept {
    return static_cast<uint32_t>(prng_next_u64(p) >> 32);
}

double prng_next_unit(Prng& p) noexcept {
    // Top 53 bits → IEEE-754 mantissa precision.
    return static_cast<double>(prng_next_u64(p) >> 11) * (1.0 / 9007199254740992.0);
}

double prng_uniform(Prng& p, double lo, double hi) noexcept {
    return lo + prng_next_unit(p) * (hi - lo);
}

double prng_exponential(Prng& p, double lambda) noexcept {
    return -std::log(1.0 - prng_uniform(p, 0.0, 1.0)) / lambda;
}

uint32_t prng_index(Prng& p, uint32_t n) noexcept {
    return static_cast<uint32_t>(prng_next_unit(p) * static_cast<double>(n));
}

double sheep_sleep_prob_for_local_hour(int hour) noexcept {
    if (hour < 0 || hour > 23) return SHEEP_SLEEP_PROB_DEFAULT;
    if (hour_in_half_open_range(hour, SHEEP_MORNING_START_HOUR, SHEEP_MORNING_END_HOUR)) {
        return SHEEP_SLEEP_PROB_MORNING;
    }
    if (hour_in_half_open_range(hour, SHEEP_NIGHT_START_HOUR, SHEEP_NIGHT_END_HOUR)) {
        return SHEEP_SLEEP_PROB_NIGHT;
    }
    return SHEEP_SLEEP_PROB_DEFAULT;
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

    // Flower stream — INDEPENDENT from main and regrowth. Every blade
    // consumes exactly one unconditional draw (the probability check);
    // flowers additionally consume 3 more draws. The order MUST be:
    // probability, then (if flower) head-color, head-radius, height-bonus.
    Prng pFlower;
    prng_init(pFlower, seed ^ FLOWER_PRNG_SALT);

    // Mushroom stream (PROTOTYPE — Native-only). Fourth independent stream
    // salted with MUSHROOM_PRNG_SALT so adding mushrooms does NOT perturb
    // the existing flower / regrowth / main sequences.
    Prng pMushroom;
    prng_init(pMushroom, seed ^ MUSHROOM_PRNG_SALT);

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

        // Flower stream (architecture.md §5). Order MUST be: probability,
        // then head-color, head-radius, height-bonus IF flower.
        const bool isFlower = prng_uniform(pFlower, 0.0, 1.0) < FLOWER_PROBABILITY;
        b.isFlower = isFlower;
        if (isFlower) {
            b.flowerHeadColorIdx = static_cast<uint8_t>(prng_index(pFlower, FLOWER_PALETTE_SIZE));
            b.flowerHeadRadius   = prng_uniform(pFlower, FLOWER_HEAD_RADIUS_MIN, FLOWER_HEAD_RADIUS_MAX);
            b.heightBonus        = prng_uniform(pFlower, FLOWER_HEIGHT_BONUS_MIN, FLOWER_HEIGHT_BONUS_MAX);
        } else {
            b.flowerHeadColorIdx = 0;
            b.flowerHeadRadius   = 0.0;
            b.heightBonus        = 1.0;
        }

        // Mushroom stream (PROTOTYPE). One unconditional draw (probability
        // check), then 5 more conditional draws for cap color, cap width,
        // cap height, stem height, stem thickness.
        const bool isMushroom = prng_uniform(pMushroom, 0.0, 1.0) < MUSHROOM_PROBABILITY;
        b.isMushroom = isMushroom;
        if (isMushroom) {
            b.mushroomCapColorIdx     = static_cast<uint8_t>(prng_index(pMushroom, MUSHROOM_PALETTE_SIZE));
            b.mushroomCapWidth        = prng_uniform(pMushroom, MUSHROOM_CAP_WIDTH_MIN,      MUSHROOM_CAP_WIDTH_MAX);
            b.mushroomCapHeight       = prng_uniform(pMushroom, MUSHROOM_CAP_HEIGHT_MIN,     MUSHROOM_CAP_HEIGHT_MAX);
            b.mushroomStemHeight      = prng_uniform(pMushroom, MUSHROOM_STEM_HEIGHT_MIN,    MUSHROOM_STEM_HEIGHT_MAX);
            b.mushroomStemThickness   = prng_uniform(pMushroom, MUSHROOM_STEM_THICKNESS_MIN, MUSHROOM_STEM_THICKNESS_MAX);
        } else {
            b.mushroomCapColorIdx     = 0;
            b.mushroomCapWidth        = 0.0;
            b.mushroomCapHeight       = 0.0;
            b.mushroomStemHeight      = 0.0;
            b.mushroomStemThickness   = 0.0;
        }

        b.originalIsFlower   = b.isFlower;
        b.originalIsMushroom = b.isMushroom;

        out.push_back(b);
    }
}

namespace {

void restore_original_variants(Blade& b) noexcept {
    b.isCactus       = false;
    b.cactusType     = 0;
    b.cactusHeight   = 0.0;
    b.cactusWidth    = 0.0;
    b.cactusArmSide  = +1;
    b.isPine         = false;
    b.pineTierCount  = 0;
    b.treeVariant    = 0;
    b.pineHeight     = 0.0;
    b.pineWidth      = 0.0;
    b.isFlower       = b.originalIsFlower;
    b.isMushroom     = b.originalIsMushroom;
}

int tumbleweed_count_for_width(double monitorWidth) noexcept {
    if (monitorWidth < 480.0) return 0;
    const double scaled = std::floor(monitorWidth / 1920.0 * static_cast<double>(TUMBLEWEED_COUNT_PER_1920DIP));
    int count = static_cast<int>(scaled);
    if (count < 1) count = 1;
    return std::min(count, MAX_ENTITIES_PER_MONITOR);
}

Entity make_tumbleweed(Prng& prng, double monitorWidth, double groundY) noexcept {
    Entity e{};
    e.kind = EntityKind::Tumbleweed;
    e.size = prng_uniform(prng, TUMBLEWEED_SIZE_MIN, TUMBLEWEED_SIZE_MAX);
    e.x    = prng_uniform(prng, 0.0, monitorWidth);
    e.y    = groundY - prng_uniform(prng, TUMBLEWEED_Y_OFFSET_MIN, TUMBLEWEED_Y_OFFSET_MAX);
    const double speed = prng_uniform(prng, TUMBLEWEED_SPEED_MIN, TUMBLEWEED_SPEED_MAX);
    const double direction = prng_uniform(prng, 0.0, 1.0) < 0.5 ? -1.0 : 1.0;
    e.vx = direction * speed;
    e.vy = 0.0;
    e.rotation      = prng_uniform(prng, 0.0, TWO_PI);
    e.rotationSpeed = e.vx / e.size;
    e.age           = 0.0;
    e.lifetime      = -1.0;
    e.seed          = prng_next_u32(prng);
    return e;
}

void respawn_tumbleweed(Entity& e, Prng& prng, double monitorWidth,
                        double groundY, bool fromLeft) noexcept {
    e.size = prng_uniform(prng, TUMBLEWEED_SIZE_MIN, TUMBLEWEED_SIZE_MAX);
    e.y    = groundY - prng_uniform(prng, TUMBLEWEED_Y_OFFSET_MIN, TUMBLEWEED_Y_OFFSET_MAX);
    const double speed = prng_uniform(prng, TUMBLEWEED_SPEED_MIN, TUMBLEWEED_SPEED_MAX);
    e.x  = fromLeft ? -e.size : monitorWidth + e.size;
    e.vx = fromLeft ? speed : -speed;
    e.vy = 0.0;
    e.rotationSpeed = e.vx / e.size;
    e.age = 0.0;
    e.lifetime = -1.0;
}

} // anonymous

void generate_cacti_for_desert(Sim& sim) noexcept {
    Prng cactusPrng;
    prng_init(cactusPrng, sim.entitySeed ^ CACTUS_PRNG_SALT);

    for (Blade& b : sim.blades) {
        restore_original_variants(b);

        const double r = prng_uniform(cactusPrng, 0.0, 1.0);
        if (r >= CACTUS_PROBABILITY) continue;

        b.isCactus      = true;
        b.isFlower      = false;
        b.isMushroom    = false;
        b.cactusHeight  = prng_uniform(cactusPrng, CACTUS_HEIGHT_MIN, CACTUS_HEIGHT_MAX);
        b.cactusWidth   = prng_uniform(cactusPrng, CACTUS_WIDTH_MIN, CACTUS_WIDTH_MAX);

        const double armDraw = prng_uniform(cactusPrng, 0.0, 1.0);
        const double noArmThreshold = 1.0 - CACTUS_ARM_PROBABILITY;
        const double twoArmThreshold = noArmThreshold + CACTUS_TWO_ARM_PROBABILITY * CACTUS_ARM_PROBABILITY;
        if (armDraw < noArmThreshold) {
            b.cactusType = 0;
            b.cactusArmSide = +1;
        } else if (armDraw < twoArmThreshold) {
            b.cactusType = 2;
            b.cactusArmSide = +1;
        } else {
            b.cactusType = 1;
            b.cactusArmSide = prng_uniform(cactusPrng, 0.0, 1.0) < 0.5
                ? static_cast<int8_t>(-1)
                : static_cast<int8_t>(+1);
        }
    }
}

void generate_tumbleweeds(Sim& sim) noexcept {
    prng_init(sim.tumbleweedPrng, sim.entitySeed ^ TUMBLEWEED_PRNG_SALT);
    const int count = tumbleweed_count_for_width(sim.monitorWidth);
    for (int i = 0; i < count; ++i) {
        sim.entities.push_back(make_tumbleweed(sim.tumbleweedPrng, sim.monitorWidth, sim.windowHeight));
    }
}

void generate_pines_for_winter(Sim& sim) noexcept {
    Prng pinePrng;
    prng_init(pinePrng, sim.entitySeed ^ PINE_PRNG_SALT);

    for (Blade& b : sim.blades) {
        restore_original_variants(b);
        // Winter biome suppresses mushrooms — they don't fit a snowy, cold scene.
        b.isMushroom = false;

        const double r = prng_uniform(pinePrng, 0.0, 1.0);
        if (r >= PINE_PROBABILITY) continue;

        // Draw order: r (consumed above), variant, height, width, tierCount.
        // Locked sequence so both impls are bit-identical regardless of variant.
        const double variantDraw = prng_uniform(pinePrng, 0.0, 1.0);
        const uint8_t variant    = (variantDraw < BIRCH_VARIANT_PROBABILITY)
                                   ? static_cast<uint8_t>(1)
                                   : static_cast<uint8_t>(0);

        b.isPine      = true;
        b.isFlower    = false;
        b.isMushroom  = false;
        b.treeVariant = variant;
        b.pineHeight  = prng_uniform(pinePrng, PINE_HEIGHT_MIN, PINE_HEIGHT_MAX);

        if (variant == 1) {
            b.pineWidth = prng_uniform(pinePrng, BIRCH_TRUNK_WIDTH_MIN, BIRCH_TRUNK_WIDTH_MAX);
        } else {
            b.pineWidth = prng_uniform(pinePrng, PINE_WIDTH_MIN, PINE_WIDTH_MAX);
        }

        // Tier count drawn for every tree slot to keep the PRNG stream
        // bit-identical across variant outcomes. Only used by pines.
        const double tierDraw = prng_uniform(pinePrng,
            static_cast<double>(PINE_TIER_COUNT_MIN),
            static_cast<double>(PINE_TIER_COUNT_MAX + 1));
        int tiers = static_cast<int>(std::floor(tierDraw));
        if (tiers < PINE_TIER_COUNT_MIN) tiers = PINE_TIER_COUNT_MIN;
        if (tiers > PINE_TIER_COUNT_MAX) tiers = PINE_TIER_COUNT_MAX;
        b.pineTierCount = static_cast<uint8_t>(tiers);
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

    // Sheep startle (§16). A click within SHEEP_STARTLE_RADIUS of a sheep
    // makes it hop AWAY from the cursor: state → Hopping, vx flipped to the
    // away direction, speed boosted (capped so repeated clicks don't
    // compound), age reset so the hop arc starts fresh.
    for (Entity& ent : sim.entities) {
        if (ent.kind != EntityKind::Sheep) continue;
        const double dxClick = ent.x - e.x;
        if (std::fabs(dxClick) >= SHEEP_STARTLE_RADIUS) continue;
        const double awayDir = dxClick >= 0.0 ? 1.0 : -1.0;
        const double speed = std::min(std::abs(ent.vx) * SHEEP_STARTLE_BOOST,
                                      SHEEP_WALK_SPEED_MAX * SHEEP_STARTLE_BOOST);
        ent.vx = speed * awayDir;
        ent.state      = SHEEP_STATE_HOPPING;
        ent.stateTimer = SHEEP_HOP_DURATION;
        ent.age        = 0.0;
    }
}

// ---------------------------------------------------------------------------
// Ambient gusts (§8.1)
// ---------------------------------------------------------------------------

void sim_apply_ambient_gust(Sim& sim, double x, double signDir, double magFactor) noexcept {
    const double impulseMagnitude = MAX_CURSOR_SPEED * magFactor * IMPULSE_SCALE;
    const double radius           = GUST_RADIUS * AMBIENT_GUST_RADIUS_FACTOR;
    if (signDir == 0.0 || impulseMagnitude == 0.0 || radius <= 0.0) return;

    for (Blade& b : sim.blades) {
        const double dxAbs = std::fabs(b.baseX - x);
        if (dxAbs >= radius) continue;

        const double tRaw   = 1.0 - dxAbs / radius;
        const double s      = std::max(0.0, std::min(1.0, tRaw));
        const double smooth = s * s * (3.0 - 2.0 * s);
        b.gustVelocity += impulseMagnitude * smooth * signDir;
    }
}

void sim_tick_ambient_gusts(Sim& sim) noexcept {
    // Per-fire draw order is fixed (§8.1): x, signDir, magFactor, interval.
    while (sim.globalTime >= sim.nextAmbientGustTime) {
        const double x         = prng_uniform(sim.ambientPrng, 0.0, sim.monitorWidth);
        const double signDir   = prng_uniform(sim.ambientPrng, 0.0, 1.0) < 0.5 ? -1.0 : 1.0;
        const double magFactor = prng_uniform(sim.ambientPrng,
                                              AMBIENT_GUST_MAG_FACTOR_MIN,
                                              AMBIENT_GUST_MAG_FACTOR_MAX);
        sim_apply_ambient_gust(sim, x, signDir, magFactor);

        const double interval  = prng_uniform(sim.ambientPrng,
                                              AMBIENT_GUST_INTERVAL_MIN,
                                              AMBIENT_GUST_INTERVAL_MAX);
        sim.nextAmbientGustTime += interval;
    }
}

// ---------------------------------------------------------------------------
// Scenes (§13)
// ---------------------------------------------------------------------------

namespace {

void generate_critters_sheep(Sim& sim) noexcept {
    // sheep count: uniform[MIN, MAX] inclusive
    const double countDraw = prng_uniform(sim.critterPrng,
        static_cast<double>(SHEEP_COUNT_MIN),
        static_cast<double>(SHEEP_COUNT_MAX + 1));
    int count = static_cast<int>(std::floor(countDraw));
    if (count < SHEEP_COUNT_MIN) count = SHEEP_COUNT_MIN;
    if (count > SHEEP_COUNT_MAX) count = SHEEP_COUNT_MAX;

    const double groundY = sim.windowHeight;
    for (int i = 0; i < count
         && static_cast<int>(sim.entities.size()) < MAX_ENTITIES_PER_MONITOR; ++i) {
        Entity e{};
        e.kind = EntityKind::Sheep;
        e.size = SHEEP_BODY_RADIUS;
        // Spawn anywhere across the width, leaving an edge margin so the
        // bounce-on-edge logic doesn't fire instantly.
        const double margin = e.size + 8.0;
        e.x  = prng_uniform(sim.critterPrng, margin, sim.monitorWidth - margin);
        // Sit the sheep so its leg-tips touch the ground line. Body center
        // is one body-height + leg-length above the ground.
        e.y  = groundY - SHEEP_BODY_HEIGHT - SHEEP_LEG_LENGTH;
        const double speed = prng_uniform(sim.critterPrng,
                                          SHEEP_WALK_SPEED_MIN,
                                          SHEEP_WALK_SPEED_MAX);
        const double dir = prng_uniform(sim.critterPrng, 0.0, 1.0) < 0.5 ? -1.0 : 1.0;
        e.vx = dir * speed;
        e.vy = 0.0;
        e.rotation = 0.0;
        e.rotationSpeed = 0.0;
        e.age = 0.0;
        e.lifetime = -1.0;
        e.seed = prng_next_u32(sim.critterPrng);
        // Initial state: Walking, with a random walk-leg duration.
        e.state = SHEEP_STATE_WALKING;
        e.stateTimer = prng_uniform(sim.critterPrng,
                                    SHEEP_WALK_DURATION_MIN,
                                    SHEEP_WALK_DURATION_MAX);
        sim.entities.push_back(e);
    }
}

void generate_critters_for_kind(Sim& sim) noexcept {
    prng_init(sim.critterPrng, sim.entitySeed ^ CRITTER_PRNG_SALT);
    switch (sim.currentCritter) {
    case CritterKind::None:                                  break;
    case CritterKind::Sheep: generate_critters_sheep(sim);   break;
    }
}

} // anonymous

void sim_set_scene(Sim& sim, Scene s) noexcept {
    sim.currentScene = s;
    sim.entities.clear();

    // Every scene transition starts from a clean blade-variant slate so that
    // e.g. Desert→Winter doesn't leave cacti on screen. Desert then promotes
    // selected slots back into cacti below.
    for (Blade& b : sim.blades) {
        restore_original_variants(b);
    }

    switch (s) {
    case Scene::Grass:
        break;
    case Scene::Desert:
        generate_cacti_for_desert(sim);
        generate_tumbleweeds(sim);
        break;
    case Scene::Winter: {
        generate_pines_for_winter(sim);
        prng_init(sim.snowflakePrng, sim.entitySeed ^ SNOWFLAKE_PRNG_SALT);
        const double lambda = SNOWFLAKE_EMIT_RATE_PER_1920DIP * sim.monitorWidth / 1920.0;
        sim.nextSnowflakeSpawnTime = sim.globalTime + prng_exponential(sim.snowflakePrng, lambda);
        break;
    }
    }

    // Critters survive scene changes — re-spawn the current selection on
    // top of whatever biome we just configured. Always runs LAST so that
    // entities[0..N-1] for tumbleweeds/snowflakes still match pinned
    // conformance snapshots from §12.
    generate_critters_for_kind(sim);
}

void sim_set_critter(Sim& sim, CritterKind c) noexcept {
    sim.currentCritter = c;
    // Erase only critter entities — scene entities (tumbleweeds, snowflakes)
    // are preserved across critter toggles.
    sim.entities.erase(
        std::remove_if(sim.entities.begin(), sim.entities.end(),
            [](const Entity& e) { return e.kind == EntityKind::Sheep; }),
        sim.entities.end());
    generate_critters_for_kind(sim);
}

// ---------------------------------------------------------------------------
// Roaming entities (§13.2)
//
// Generic tick: integrate position, advance rotation, age the entity.
// Per-kind branches handle off-screen respawn (tumbleweed), horizontal
// sway (snowflake), and end-of-life culling (snowflake). The skeleton is
// safe to call on an empty vector — it walks zero iterations and exits.
// ---------------------------------------------------------------------------

void sim_tick_entities(Sim& sim, double dt) noexcept {
    const double groundY = sim.windowHeight;

    // Forward pass: update positions / rotations / generic age.
    for (Entity& e : sim.entities) {
        e.x        += e.vx * dt;
        e.y        += e.vy * dt;
        e.rotation += e.rotationSpeed * dt;
        e.age      += dt;

        if (e.kind == EntityKind::Tumbleweed) {
            if (e.x < -e.size - 10.0) {
                respawn_tumbleweed(e, sim.tumbleweedPrng, sim.monitorWidth, groundY, false);
            } else if (e.x > sim.monitorWidth + e.size + 10.0) {
                respawn_tumbleweed(e, sim.tumbleweedPrng, sim.monitorWidth, groundY, true);
            }
        }
    }

    for (Entity& e : sim.entities) {
        if (e.kind != EntityKind::Snowflake) continue;
        const double phase = static_cast<double>(e.seed) / 4294967295.0 * TWO_PI;
        e.vx = SNOWFLAKE_SWAY_AMPLITUDE * SNOWFLAKE_SWAY_FREQUENCY * TWO_PI
             * std::cos(e.age * TWO_PI * SNOWFLAKE_SWAY_FREQUENCY + phase);
    }

    sim.entities.erase(
        std::remove_if(sim.entities.begin(), sim.entities.end(),
            [groundY](const Entity& e) {
                return e.kind == EntityKind::Snowflake
                    && (e.age >= e.lifetime || e.y > groundY);
            }),
        sim.entities.end());

    // Critter tick — Sheep (§16). State machine drives behavior:
    //   Walking  : moves at vx, bounces on edges, leg cycle in renderer.
    //   Grazing  : frozen, head will be drawn pointing down at the grass.
    //   Idle     : frozen, head sweeps side-to-side in the renderer.
    //   Sleeping : frozen, body tucks down, Z glyphs drift up.
    //   Hopping  : moves AND visually arcs upward (renderer applies the
    //              parabolic Y offset). Triggered by random transition or
    //              by sim_apply_click within SHEEP_STARTLE_RADIUS.
    //   Greeting : frozen, faces another sheep until both walk apart.
    // The generic forward pass already added (vx * dt) to e.x; for frozen
    // states we undo that integration so the sheep stays planted.
    for (Entity& e : sim.entities) {
        if (e.kind != EntityKind::Sheep) continue;

        const bool frozen = (e.state == SHEEP_STATE_GRAZING)
                         || (e.state == SHEEP_STATE_IDLE)
                         || (e.state == SHEEP_STATE_SLEEPING)
                         || (e.state == SHEEP_STATE_GREETING);
        if (frozen) {
            e.x -= e.vx * dt;
        }

        // Edge bounce — runs in every state. Even a stationary sheep that
        // spawned near the edge gets reflected on its first walk tick.
        const double margin = e.size + 2.0;
        if (e.x < margin) {
            e.x  = margin;
            e.vx = std::abs(e.vx);
        } else if (e.x > sim.monitorWidth - margin) {
            e.x  = sim.monitorWidth - margin;
            e.vx = -std::abs(e.vx);
        }

        // State transitions. Decrement the timer; when it expires, pick the
        // next state and roll a fresh duration. PRNG draws are sequenced
        // so cross-impl bit-identity is preservable once Win2D mirrors.
        e.stateTimer -= dt;
        if (e.stateTimer <= 0.0) {
            const uint8_t oldState = e.state;
            if (oldState == SHEEP_STATE_WALKING) {
                const double r = prng_uniform(sim.critterPrng, 0.0, 1.0);
                if (r < SHEEP_GRAZE_PROBABILITY) {
                    e.state = SHEEP_STATE_GRAZING;
                    e.stateTimer = prng_uniform(sim.critterPrng,
                                                SHEEP_GRAZE_DURATION_MIN,
                                                SHEEP_GRAZE_DURATION_MAX);
                } else if (r < SHEEP_GRAZE_PROBABILITY + SHEEP_IDLE_PROBABILITY) {
                    e.state = SHEEP_STATE_IDLE;
                    e.stateTimer = prng_uniform(sim.critterPrng,
                                                SHEEP_IDLE_DURATION_MIN,
                                                SHEEP_IDLE_DURATION_MAX);
                } else {
                    e.state = SHEEP_STATE_HOPPING;
                    e.stateTimer = SHEEP_HOP_DURATION;
                }
            } else if (oldState == SHEEP_STATE_IDLE) {
                const double r = prng_uniform(sim.critterPrng, 0.0, 1.0);
                const double sleepProb = sheep_sleep_prob_for_local_hour(current_local_hour());
                if (r < sleepProb) {
                    e.state = SHEEP_STATE_SLEEPING;
                    e.stateTimer = prng_uniform(sim.critterPrng,
                                                SHEEP_SLEEP_DURATION_MIN,
                                                SHEEP_SLEEP_DURATION_MAX);
                } else {
                    e.state = SHEEP_STATE_WALKING;
                    e.stateTimer = prng_uniform(sim.critterPrng,
                                                SHEEP_WALK_DURATION_MIN,
                                                SHEEP_WALK_DURATION_MAX);
                }
            } else {
                // Grazing / Sleeping / Hopping / Greeting → return to Walking.
                e.state = SHEEP_STATE_WALKING;
                e.stateTimer = prng_uniform(sim.critterPrng,
                                            SHEEP_WALK_DURATION_MIN,
                                            SHEEP_WALK_DURATION_MAX);
                if (oldState == SHEEP_STATE_GREETING) {
                    e.vx = -e.vx;
                }
            }
            // age reset so hop arc / walk cycle / sleep-Z animation start
            // from phase 0 at every state entry (else the hop would catch
            // mid-arc and a long-running sheep's leg cycle would jitter).
            e.age = 0.0;
        }
    }

    // Pair-wise sheep greeting trigger. Runs after all per-sheep transitions
    // and before the snowflake spawner so critter PRNG draw order stays locked.
    auto canGreet = [](const Entity& sheep) noexcept {
        return sheep.kind == EntityKind::Sheep
            && (sheep.state == SHEEP_STATE_WALKING
             || sheep.state == SHEEP_STATE_GRAZING
             || sheep.state == SHEEP_STATE_IDLE)
            && sheep.age >= SHEEP_GREET_MIN_AGE;
    };
    for (std::size_t i = 0; i < sim.entities.size(); ++i) {
        Entity& a = sim.entities[i];
        if (!canGreet(a)) continue;

        for (std::size_t j = i + 1; j < sim.entities.size(); ++j) {
            Entity& b = sim.entities[j];
            if (!canGreet(b)) continue;

            const double dx = b.x - a.x;
            if (std::abs(dx) >= SHEEP_GREET_RADIUS) continue;

            const double duration = prng_uniform(sim.critterPrng,
                                                SHEEP_GREET_DURATION_MIN,
                                                SHEEP_GREET_DURATION_MAX);
            const double dir = (dx >= 0.0) ? 1.0 : -1.0;
            a.vx =  dir * std::abs(a.vx);
            b.vx = -dir * std::abs(b.vx);
            a.state = SHEEP_STATE_GREETING;
            b.state = SHEEP_STATE_GREETING;
            a.stateTimer = duration;
            b.stateTimer = duration;
            a.age = 0.0;
            b.age = 0.0;
            break;
        }
    }

    if (sim.currentScene == Scene::Winter) {
        const double lambda = SNOWFLAKE_EMIT_RATE_PER_1920DIP * sim.monitorWidth / 1920.0;
        while (sim.globalTime >= sim.nextSnowflakeSpawnTime
               && static_cast<int>(sim.entities.size()) < MAX_ENTITIES_PER_MONITOR) {
            Entity e{};
            e.kind          = EntityKind::Snowflake;
            e.size          = prng_uniform(sim.snowflakePrng, SNOWFLAKE_SIZE_MIN, SNOWFLAKE_SIZE_MAX);
            e.x             = prng_uniform(sim.snowflakePrng, -20.0, sim.monitorWidth + 20.0);
            const double fallSpeed = prng_uniform(sim.snowflakePrng, SNOWFLAKE_FALL_SPEED_MIN, SNOWFLAKE_FALL_SPEED_MAX);
            e.y             = -e.size - 4.0;
            e.vx            = 0.0;
            e.vy            = fallSpeed;
            e.rotation      = prng_uniform(sim.snowflakePrng, 0.0, TWO_PI);
            e.rotationSpeed = prng_uniform(sim.snowflakePrng, -1.5, 1.5);
            e.age           = 0.0;
            e.lifetime      = (groundY + e.size) / fallSpeed + SNOWFLAKE_LIFETIME_PADDING_SEC;
            e.seed          = prng_next_u32(sim.snowflakePrng);
            sim.entities.push_back(e);
            sim.nextSnowflakeSpawnTime += prng_exponential(sim.snowflakePrng, lambda);
        }
    }
}

// ---------------------------------------------------------------------------
// Stroke geometry. architecture.md §7.
// ---------------------------------------------------------------------------

Stroke compute_blade_stroke(const Blade& b, double groundY, Scene scene) noexcept {
    Stroke s{};
    s.argb      = PALETTE[b.hue];
    s.thickness = b.thickness;

    if (b.cutHeight < CUT_STUMP_THRESHOLD) {
        s.base    = { b.baseX, groundY };
        s.control = { b.baseX, groundY - 1.0 };
        s.tip     = { b.baseX, groundY - STUMP_HEIGHT };
        return s;
    }

    const double heightScale =
        (scene == Scene::Desert && !b.isCactus && !b.isMushroom) ? DESERT_GRASS_HEIGHT_SCALE :
        (scene == Scene::Winter && !b.isPine && !b.isMushroom)   ? WINTER_GRASS_HEIGHT_SCALE :
        1.0;
    const double L = b.height * b.heightBonus * b.cutHeight * heightScale;

    // Chord preservation: blades have a fixed length L. As effectiveLean
    // grows, the tip arcs over (Y drops) rather than the blade stretching
    // diagonally. Clamp to MAX_LEAN_FRACTION * L so the sqrt is always
    // positive even under strong gust impulses.
    double lean = b.effectiveLean;
    const double maxLean = MAX_LEAN_FRACTION * L;
    if (lean >  maxLean) lean =  maxLean;
    if (lean < -maxLean) lean = -maxLean;

    const double dropFactor = std::sqrt(1.0 - (lean / L) * (lean / L));

    const double tipX = b.baseX + lean;
    const double tipY = groundY - L * dropFactor;

    // Rooted-bend control point: directly above the base, at a fraction
    // CTRL_OFFSET_FACTOR of the (current, foreshortened) blade height.
    s.base    = { b.baseX, groundY };
    s.control = { b.baseX, groundY - L * CTRL_OFFSET_FACTOR * dropFactor };
    s.tip     = { tipX, tipY };
    return s;
}

// ---------------------------------------------------------------------------
// Sim glue
// ---------------------------------------------------------------------------

Sim sim_init(uint64_t seed, double monitorWidth, double density) {
    Sim s;
    s.windowHeight = STRIP_HEIGHT + HEADROOM;
    s.monitorWidth = monitorWidth;
    s.entitySeed   = seed;
    s.entities.reserve(MAX_ENTITIES_PER_MONITOR);
    generate_blades(seed, monitorWidth, density, s.blades);

    // §8.1 ambient gust scheduler — fifth independent stream, salted off
    // the same seed. The first interval is drawn immediately so the first
    // puff never fires at t=0 and every subsequent fire consumes exactly
    // 4 PRNG draws.
    prng_init(s.ambientPrng, seed ^ AMBIENT_GUST_PRNG_SALT);
    s.nextAmbientGustTime = s.globalTime
                          + prng_uniform(s.ambientPrng,
                                         AMBIENT_GUST_INTERVAL_MIN,
                                         AMBIENT_GUST_INTERVAL_MAX);
    return s;
}

void sim_regenerate(Sim& sim, uint64_t seed, double monitorWidth, double density) {
    sim.globalTime     = 0.0;
    sim.prevCursorX    = 0.0;
    sim.prevCursorTime = -1.0;
    sim.monitorWidth   = monitorWidth;
    sim.entitySeed     = seed;
    sim.entities.clear();
    if (sim.entities.capacity() < static_cast<std::size_t>(MAX_ENTITIES_PER_MONITOR)) {
        sim.entities.reserve(MAX_ENTITIES_PER_MONITOR);
    }
    generate_blades(seed, monitorWidth, density, sim.blades);

    prng_init(sim.ambientPrng, seed ^ AMBIENT_GUST_PRNG_SALT);
    sim.nextAmbientGustTime = sim.globalTime
                            + prng_uniform(sim.ambientPrng,
                                           AMBIENT_GUST_INTERVAL_MIN,
                                           AMBIENT_GUST_INTERVAL_MAX);
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

    sim_tick_ambient_gusts(sim);

    for (Blade& b : sim.blades) {
        update_blade_dynamics(b, sim.globalTime, dt);
        advance_cut(b, sim.globalTime);
    }

    sim_tick_entities(sim, dt);
}

} // namespace desktopgrass
