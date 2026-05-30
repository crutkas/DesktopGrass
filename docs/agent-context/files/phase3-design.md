# Phase 3 — Desert + Winter scene content

Draft architecture for spec §14 (Desert) and §15 (Winter). Lock to
docs/architecture.md ONLY after Phase 2 (Win2D scene infra backport)
ships green.

## Goal
- Desert scene: cacti (slot-bound) + tumbleweeds (rolling roamers)
- Winter scene: snowflakes (drifting roamers) + snow-tipped blades

## New subsystem: roaming entities

Add to `Sim` alongside `blades`:

```cpp
enum EntityKind : uint8_t {
    None       = 0,
    Tumbleweed = 1,
    Snowflake  = 2,
};

struct Entity {
    EntityKind kind;
    double x, y;
    double vx, vy;
    double size;          // radius (DIP)
    double rotation;      // radians
    double rotationSpeed; // rad/sec
    double age;
    double lifetime;      // sec until removal (snowflakes); -1 = infinite (tumbleweeds)
    uint32_t paletteIdx;
};
```

- Stored as `std::vector<Entity>` / `List<Entity>`.
- Updated in `sim_tick` before blade physics (so per-blade impulse from a
  passing tumbleweed can be applied that same frame — Phase 3.1, optional).
- Rendered after blades in DrawGrass.

## PRNG salts (new, never collide with §3 / §5 / §8.1 streams)

```
TUMBLEWEED_PRNG_SALT = 0x7B0117CA7B0117CAULL  // "boil tea"
SNOWFLAKE_PRNG_SALT  = 0xC0FFEE1CECAFEBABEULL // "coffee ice"
```

(Pick final values at spec-lock time; mnemonic doesn't matter as long as
they're distinct from the existing four salts.)

## sim_set_scene becomes generator-aware

**Amendment to §13:** Phase 2 promised `sim_set_scene` is state-only. With
§14/§15, switching scenes additionally:

1. Clears `sim.entities`
2. Generates the new scene's entity set using its salted PRNG streams
3. Leaves `blades` untouched — §12 first-blade snapshot still passes.

The blade palette swap is still the only visual change to existing
blades; entities are the new layer.

## Tumbleweed lifecycle (Desert)

- Pool of `TUMBLEWEED_COUNT_PER_MONITOR_WIDTH * monitorWidth / 1920.0`
  (target ~4 per 1920 DIP monitor).
- Each tumbleweed spawns at a random Y in the grass band (`groundY - 20..groundY - 8`),
  random X (uniform across width), random vx (40..120 DIP/sec, sign random).
- Rotation speed proportional to |vx| / size (rolling without slipping).
- When off-screen: respawn at opposite edge with new random parameters.
- Size: 8..18 DIP.
- Render: concentric arcs of brown strokes in `TUMBLEWEED_PALETTE` (2-3 brown shades).
- Optional: tumbleweeds receive ambient gust impulse → brief vx boost in
  gust direction (Phase 3.1).

## Cactus lifecycle (Desert) — slot-bound

Cacti replace random blade slots (probability `CACTUS_PROBABILITY ≈ 0.005` per
blade slot). Like flowers/mushrooms, they're a `Blade` variant tag:

```cpp
struct Blade {
    // ... existing fields ...
    bool isCactus = false;
    uint8_t cactusType;     // 0 = single column, 1 = two-arm, 2 = saguaro
    double cactusHeight;    // 30..80 DIP (taller than grass)
    double cactusWidth;     // 8..14 DIP
};
```

- Static (no sway).
- Three variants for visual variety:
  - **Type 0**: simple vertical column with rounded top.
  - **Type 1**: column + one short arm on a random side.
  - **Type 2**: column + two arms (saguaro-style).
- Stroke: dark saturated green `CACTUS_COLOR = 0xFF2D7A2D`.
- Cuttable: clicking with `CUT_RADIUS` cuts the cactus to a stump like grass.
- Cactus generation uses the cactus PRNG salt to draw type + dimensions.

```
CACTUS_PRNG_SALT = 0xCAC75CAC75CAC75CULL
```

## Snowflake lifecycle (Winter)

Continuous emission. Each tick:
- Spawn rate: `SNOWFLAKE_EMIT_RATE_PER_SEC ≈ 8.0` per 1920 DIP monitor.
- Use Poisson-like scheduler (similar to ambient gusts §8.1):
  `nextSnowflakeSpawnTime`, drawn from exp / uniform distribution.
- New flake: random X (0..monitorWidth), Y = -10 (just above window top),
  vx = 0 + slight ambient gust offset, vy = SNOWFLAKE_FALL_SPEED (20..40 DIP/sec),
  rotationSpeed = uniform(-1.5, 1.5) rad/sec, lifetime = monitorHeight / vy + 2.
- Update: position += velocity * dt, age += dt
- Horizontal sway: `vx += sin(age * 2.0 + paletteIdx) * 0.5` per frame
  (gentle wiggle). Ambient gusts add transient lateral drift.
- Cull: when age > lifetime OR y > groundY (lands on grass — could
  accumulate in Phase 3.5).
- Render: small 5..9 DIP circles in `SNOWFLAKE_COLOR = 0xFFFFFFFF` with
  slight transparency variation by paletteIdx.

## Snow-tipped blades (Winter)

Render-only effect; no model changes. When `sim.currentScene == Winter`:
- After drawing the blade bezier, draw a small ellipse cap (`2..3 DIP` radius)
  at the tip in white. Cap shrinks with `cutHeight` like the flower head.
- Adds visual identity to Winter without changing the blade vector.

## What ships when

- **§14 commit** (Desert): cacti + tumbleweeds, both impls + tests.
- **§15 commit** (Winter): snowflakes + snow-tipped blades, both impls + tests.

Both can be fleeted in parallel — they share the new entity subsystem
(which must land first as a §13.1 amendment + sim_set_scene generator rewrite).

## Fleeting plan

1. **Lock spec §13.1 amendment** (sim_set_scene becomes generator-aware) +
   **§14 (Desert)** + **§15 (Winter)** in `docs/architecture.md` as ONE
   commit. Push.
2. **Implement entity subsystem in Native + Win2D** (myself): the storage
   container, generator hook in `sim_set_scene`, tick wrapper, render
   wrapper. No actual entity types yet — just the skeleton. Commit + push.
3. **Fleet 2 parallel general-purpose agents**:
   - **Agent #A — Desert (§14)**: cacti (slot-bound) + tumbleweeds (roamers).
     Both impls + tests.
   - **Agent #B — Winter (§15)**: snowflakes (roamers) + snow-tipped blades.
     Both impls + tests.
4. Validate, commit each as one focused commit, push.
5. Relaunch native — user gets all three scenes live.

## Open decisions for spec-lock time
- Tumbleweed Y axis: roll along the strip or just above? Decision: just
  above the grass strip top (so they're visible even when grass is uncut).
- Snowflake accumulation on grass: deferred to Phase 3.5 (or never).
- Cactus cut behavior: hard cut to stump, or pieces fly off? Deferred —
  v1 uses same grass cut model.
- Should snowflakes also generate on Desert? No — strictly scene-gated.
- Ambient gust effect on roamers: nice-to-have. Defer to Phase 3.1.
