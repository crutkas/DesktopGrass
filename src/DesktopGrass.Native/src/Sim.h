// Sim.h
//
// Pure data + math: PRNG, blade generation, sway, gust, cut. NO Direct2D, D3D,
// COM, Win32 UI, or threading dependencies. This is what the unit-test project
// links against.
//
// Mirrors docs/architecture.md §§3-10. Constants live in Constants.h.

#pragma once

#include <cstdint>
#include <cstddef>
#include <vector>

#include "Constants.h"

namespace desktopgrass {

// ---------------------------------------------------------------------------
// PRNG: xorshift64 seeded via SplitMix64. See architecture.md §3.
// ---------------------------------------------------------------------------

struct Prng {
    uint64_t state;
};

uint64_t splitmix64(uint64_t z) noexcept;
void     prng_init(Prng& p, uint64_t seed) noexcept;
uint64_t prng_next_u64(Prng& p) noexcept;
double   prng_next_unit(Prng& p) noexcept;
double   prng_uniform(Prng& p, double lo, double hi) noexcept;
uint32_t prng_index(Prng& p, uint32_t n) noexcept;

// ---------------------------------------------------------------------------
// Blade. Layout matches architecture.md §4 — field set, not field order, is
// load-bearing.
// ---------------------------------------------------------------------------

struct Blade {
    // Static (set once at generation).
    double  baseX;
    double  height;
    double  thickness;
    uint8_t hue;
    double  swayPhaseOffset;
    double  stiffness;

    // Runtime.
    double  cutHeight;
    double  gustVelocity;
    double  cutAnimStart;
    double  cutInitialHeight;

    // Regrowth (Constants.h §"Regrowth"). regrowDelay / regrowDuration are
    // assigned once at generation from an independent PRNG stream (so they do
    // not perturb the static fields' draws). regrowStart is the absolute
    // globalTime at which the regrow animation begins; -1 means "not
    // scheduled". When cutAnimStart finishes, advance_cut sets
    // regrowStart = globalTime + regrowDelay. A click on a regrowing blade
    // cancels regrowth (clears regrowStart) and restarts the cut from current
    // cutHeight. In-class initializers keep `Blade b{};` (used by tests &
    // helpers) in the correct "no regrowth scheduled" state.
    double  regrowDelay    = 0.0;
    double  regrowDuration = 0.0;
    double  regrowStart    = -1.0;

    // Flower (§4, §5, §7). Static, set once at generation from an
    // independent PRNG stream. isFlower=false means this is an ordinary
    // grass blade; heightBonus defaults to 1.0 so the L formula in
    // compute_blade_stroke is a no-op for non-flowers.
    bool    isFlower            = false;
    uint8_t flowerHeadColorIdx  = 0;
    double  flowerHeadRadius    = 0.0;
    double  heightBonus         = 1.0;

    // Mushroom (PROTOTYPE — Native-only). Static, set once at generation from
    // a fourth independent PRNG stream. When isMushroom=true the renderer
    // draws a filled-ellipse cap on a short stem at this slot and skips the
    // grass blade + flower head. cutHeight still drives cut/regrow animation
    // for mushrooms (cap+stem shrink/grow linearly with it).
    bool    isMushroom              = false;
    uint8_t mushroomCapColorIdx     = 0;
    double  mushroomCapWidth        = 0.0;   // radius X (DIP)
    double  mushroomCapHeight       = 0.0;   // radius Y (DIP)
    double  mushroomStemHeight      = 0.0;   // DIP
    double  mushroomStemThickness   = 0.0;   // DIP

    // Derived per-frame. Stored on the blade for the renderer to consume; not
    // part of the persistent state and ignored by snapshot tests.
    double  effectiveLean;
};

// ---------------------------------------------------------------------------
// Stroke (rendering geometry). architecture.md §7.
// ---------------------------------------------------------------------------

struct Point { double x, y; };

struct Stroke {
    Point    base;
    Point    control;
    Point    tip;
    double   thickness;
    uint32_t argb;
};

Stroke compute_blade_stroke(const Blade& b, double groundY) noexcept;

// ---------------------------------------------------------------------------
// Input event queue. The renderer drains the OS hook into this struct each
// frame, then calls sim_tick.
// ---------------------------------------------------------------------------

enum class EventType : uint8_t {
    Move  = 0,
    Click = 1,
};

struct InputEvent {
    EventType type;
    double    x;        // window-local DIP
    double    y;        // window-local DIP
    double    time;     // seconds, monotonic
};

// ---------------------------------------------------------------------------
// Sim — the simulation state for one monitor window.
// ---------------------------------------------------------------------------

struct Sim {
    std::vector<Blade> blades;
    double             globalTime      = 0.0;
    double             prevCursorX     = 0.0;
    double             prevCursorTime  = -1.0;
    double             windowHeight    = STRIP_HEIGHT + HEADROOM;
};

// Construct a sim with blades generated for the given monitor width, density,
// and seed. windowHeight defaults to STRIP_HEIGHT + HEADROOM.
Sim sim_init(uint64_t seed, double monitorWidth, double density = 1.0);

// Re-run generation in place, resetting all runtime state.
void sim_regenerate(Sim& sim, uint64_t seed, double monitorWidth, double density = 1.0);

// Apply a move event. Updates prevCursorX/prevCursorTime and distributes gust
// impulse. Caller is responsible for the dt-clamp / cap on cursor speed; this
// function performs the cap internally per the spec.
void sim_apply_move(Sim& sim, const InputEvent& e) noexcept;

// Apply a click event. cutBand filtering uses sim.windowHeight as groundY.
void sim_apply_click(Sim& sim, const InputEvent& e) noexcept;

// Advance the simulation by dt seconds. Drains the provided event list in
// order, then runs per-blade dynamics + cut animation. Pass numEvents = 0 if
// no events fired this frame.
void sim_tick(Sim& sim, double dt,
              const InputEvent* events, std::size_t numEvents) noexcept;

// Generator used by sim_init / sim_regenerate. Exposed for unit tests.
void generate_blades(uint64_t seed, double monitorWidth, double density,
                     std::vector<Blade>& out);

// Per-blade dynamics helper (visible for tests).
void update_blade_dynamics(Blade& b, double globalTime, double dt) noexcept;
void advance_cut(Blade& b, double globalTime) noexcept;

// dt clamp helper. Required at the renderer boundary so a long pause does not
// produce visible artifacts. See architecture.md §10.
constexpr double DT_MIN = 0.001;
constexpr double DT_MAX = 0.1;
constexpr double clamp_dt(double dt) noexcept {
    return (dt < DT_MIN) ? DT_MIN : (dt > DT_MAX ? DT_MAX : dt);
}

} // namespace desktopgrass
