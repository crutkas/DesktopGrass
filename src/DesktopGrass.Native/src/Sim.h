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
#include "Persistence.h"

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
uint32_t prng_next_u32(Prng& p) noexcept;
double   prng_next_unit(Prng& p) noexcept;
double   prng_uniform(Prng& p, double lo, double hi) noexcept;
double   prng_exponential(Prng& p, double lambda) noexcept;
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

    // Original Grass-scene slot variants. Desert cacti temporarily replace
    // flower/mushroom tags; switching back to Grass restores these snapshots.
    bool    originalIsFlower        = false;
    bool    originalIsMushroom      = false;

    // Cactus (§14). Desert-only slot-bound blade variant.
    bool    isCactus                = false;
    uint8_t cactusType              = 0;     // 0 = column, 1 = single-arm, 2 = saguaro
    double  cactusHeight            = 0.0;   // DIP
    double  cactusWidth             = 0.0;   // DIP
    int8_t  cactusArmSide           = +1;    // -1 or +1 for type 1

    // Pine (§15.1). Winter-only slot-bound blade variant.
    bool    isPine                  = false;
    uint8_t pineTierCount           = 0;     // 2..4 (only meaningful for treeVariant == 0)
    uint8_t treeVariant             = 0;     // 0 = pine, 1 = birch
    double  pineHeight              = 0.0;   // DIP
    double  pineWidth               = 0.0;   // DIP, base-tier width (pine) or trunk width (birch)

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

Stroke compute_blade_stroke(const Blade& b, double groundY, Scene scene) noexcept;

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
// Roaming entities (architecture.md §13.2). Tumbleweeds (Desert §14),
// snowflakes (Winter §15), sheep (§16), cats (§17), and raindrops (§20) live in sim.entities.
// The struct fields are shared across kinds; per-kind tick logic branches on `kind`.
// ---------------------------------------------------------------------------

struct Entity {
    EntityKind kind          = EntityKind::None;
    double     x             = 0.0;
    double     y             = 0.0;
    double     vx            = 0.0;
    double     vy            = 0.0;
    double     size          = 0.0;   // radius (DIP)
    double     rotation      = 0.0;   // radians
    double     rotationSpeed = 0.0;   // rad/sec
    double     age           = 0.0;
    double     lifetime      = -1.0;  // <= 0 means infinite (respawn-in-place)
    uint32_t   seed          = 0;
    // Critter state machine (§16, §17). Sheep and Cat share state bytes;
    // Cat reuses Hopping semantically as Pouncing.
    // Values are ignored by tumbleweeds/snowflakes and inert by default.
    uint8_t    state         = 0;     // sheep/cat: see SHEEP_STATE_* constants
    double     stateTimer    = 0.0;   // sec remaining in current state
    uint8_t    nameIndex     = 0;     // sheep/cat: index into species name pool
    uint8_t    coatVariantIndex = 0;  // cat: index into CAT_COAT_PALETTES
};

// ---------------------------------------------------------------------------
// Sim — the simulation state for one monitor window.
// ---------------------------------------------------------------------------

struct Sim {
    std::vector<Blade> blades;
    double             globalTime          = 0.0;
    double             prevCursorX         = 0.0;
    double             prevCursorTime      = -1.0;
    double             windowHeight        = STRIP_HEIGHT + HEADROOM;

    // Ambient gust scheduler (§8.1). Initialized by sim_init / sim_regenerate.
    Prng               ambientPrng         = { 0 };
    double             nextAmbientGustTime = 0.0;
    double             monitorWidth        = 0.0;

    // Current scene (§13). Default Grass; updated by sim_set_scene.
    Scene              currentScene        = SCENE_DEFAULT;

    // Roaming entities (§13.2). Grass emits raindrops over time; Desert and
    // Winter add their own scene entities via sim_set_scene. Pre-reserved to
    // MAX_ENTITIES_PER_MONITOR at init so the tick path never grows the vector.
    std::vector<Entity> entities;

    // Per-scene entity-stream seed. Set at sim_init; used by sim_set_scene
    // to construct the per-kind generator PRNGs.
    uint64_t           entitySeed          = 0;

    // Persistent tumbleweed stream (§14). Initialized when entering Desert and
    // consumed by off-edge respawns so replay stays deterministic.
    Prng               tumbleweedPrng      = { 0 };

    // §15 snowflake emitter (Winter scene only)
    Prng               snowflakePrng       = { 0 };
    double             nextSnowflakeSpawnTime = 0.0;

    // §20 raindrop emitter (Grass scene only). Scene transitions preserve
    // existing raindrops for a soft fade-out while the spawner is scene-gated.
    Prng               raindropPrng        = { 0 };
    double             nextRaindropSpawnTime = 0.0;

    // Critter subsystem (§16). Independent of currentScene. critterPrng is
    // seeded from entitySeed XOR CRITTER_PRNG_SALT at generation time.
    // critterCountOverride is 0 for random species count, or a fixed count
    // capped by PET_COUNT_MAX_PER_MONITOR during generation.
    CritterKind        currentCritter      = CRITTER_DEFAULT;
    Prng               critterPrng         = { 0 };
    int                critterCountOverride = 0;
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

// Ambient gust application (§8.1). Same impulse kernel as cursor gusts but
// uses GUST_RADIUS * AMBIENT_GUST_RADIUS_FACTOR and an impulse magnitude
// parameterised by magFactor (a fraction of a saturated cursor sweep) instead
// of the cursor-derived velocity. Exposed for unit tests.
void sim_apply_ambient_gust(Sim& sim, double x, double signDir, double magFactor) noexcept;

// Run the ambient gust scheduler one tick. Exposed for unit tests. Idempotent
// on idle ticks (zero PRNG draws); fires zero or more puffs depending on how
// many scheduled fire times sim.globalTime has crossed.
void sim_tick_ambient_gusts(Sim& sim) noexcept;

// Set the current scene (§13). State-only update — does not regenerate
// blades or perturb any PRNG stream. Renderer reads sim.currentScene at
// draw time.
//
// Phase-3 amendment (§13.1): in addition to the field assign, this clears
// sim.entities and dispatches to per-scene generators (none yet — empty
// generator hooks for Grass / Desert / Winter; Desert and Winter content
// agents add their generators here).
void sim_set_scene(Sim& sim, Scene s) noexcept;

// Advance roaming entities by dt (§13.2). Generic per-kind tick: position
// integration, rotation update, age advance, snowflake horizontal sway,
// tumbleweed off-edge respawn, snowflake culling. Currently a no-op when
// sim.entities is empty (which is always until §14/§15 generators run).
void sim_tick_entities(Sim& sim, double dt) noexcept;

// Critter selection (§16). Independent of sim_set_scene. State change:
// removes existing critter-kind entities (preserving scene entities like
// tumbleweeds/snowflakes), then re-runs the per-kind generator. Default
// CritterKind::None = no critters spawned.
void sim_set_critter(Sim& sim, CritterKind c) noexcept;

// Fixed critter count override (§13.3). n=0 clears to random; positive values
// are capped at PET_COUNT_MAX_PER_MONITOR during generation.
void sim_set_critter_count(Sim& sim, int n) noexcept;

double sheep_sleep_prob_for_local_hour(int hour) noexcept;
double cat_sleep_prob_for_local_hour(int hour) noexcept;

// Advance the simulation by dt seconds. Drains the provided event list in
// order, then runs per-blade dynamics + cut animation. Pass numEvents = 0 if
// no events fired this frame.
void sim_tick(Sim& sim, double dt,
              const InputEvent* events, std::size_t numEvents) noexcept;

// Generator used by sim_init / sim_regenerate. Exposed for unit tests.
void generate_blades(uint64_t seed, double monitorWidth, double density,
                     std::vector<Blade>& out);

// Desert generators (§14). Exposed for unit tests.
void generate_cacti_for_desert(Sim& sim) noexcept;
void generate_tumbleweeds(Sim& sim) noexcept;

// Pine tree generator (§15.1). Iterates blade slots; promotes a small
// fraction to pines from the PINE_PRNG_SALT stream when entering Winter.
// Slot-bound and reversed by restore_original_variants on scene exit.
void generate_pines_for_winter(Sim& sim) noexcept;

// Per-blade dynamics helper (visible for tests).
void update_blade_dynamics(Blade& b, double globalTime, double dt) noexcept;
void advance_cut(Blade& b, double globalTime) noexcept;

// Persistence helpers. GetCuts stores cut timestamps shifted relative to the
// current sim time (for example, -20 means the blade was cut 20 seconds ago)
// so a fresh sim can resume regrowth after restart.
std::vector<persistence::CutRecord> sim_get_cuts(const Sim& sim);
void sim_apply_cuts(Sim& sim, const std::vector<persistence::CutRecord>& cuts) noexcept;

// dt clamp helper. Required at the renderer boundary so a long pause does not
// produce visible artifacts. See architecture.md §10.
constexpr double DT_MIN = 0.001;
constexpr double DT_MAX = 0.1;
constexpr double clamp_dt(double dt) noexcept {
    return (dt < DT_MIN) ? DT_MIN : (dt > DT_MAX ? DT_MAX : dt);
}

} // namespace desktopgrass
