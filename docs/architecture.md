# DesktopGrass — Shared Algorithm Specification

This document is the **single source of truth** for the grass simulation. The three v1 implementations — `DesktopGrass.Native` (Win32 + Direct2D, C++), `DesktopGrass.Win2D` (C# + Win2D), and `DesktopGrass.WinUI3` (packaged WinUI 3, C#) — each port the algorithms below into their own language. There is no shared library; the spec **is** the contract.

For the product goals, window model, input model, and project layout, see [`plan.md`](../plan.md). This document covers only the math/state machine that every implementation must reproduce.

---

## 1. Overview

DesktopGrass paints a strip of procedurally generated grass along the bottom of every monitor, on top of all windows, fully click-through. The strip sways gently on its own, reacts to cursor motion with localized gusts, and reacts to left-clicks by cutting blades.

The v1 plan calls for **three independent implementations** of the same feature set, so they can be compared side-by-side on LoC, CPU/GPU cost, startup time, and binary size. To make that comparison honest, all three must produce **pixel-equivalent output** for a given `(seed, monitorWidth, density)` and an identical event stream. This spec fixes every constant, function, and ordering decision needed to make that true.

Pseudocode is given in a C-like form chosen to port cleanly to C++ (Native), C# (Win2D, WinUI 3), and any future Rust/Go port. Where a port idiom differs (e.g., `Math.Sin` vs `std::sin`), the spec uses the mathematical name (`sin`, `exp`, `sqrt`, `clamp`).

---

## 2. Coordinate system

All coordinates are in **DIPs** (device-independent pixels, 1 DIP = 1/96 inch). Each implementation is responsible for DPI scaling at the window/swap-chain level; algorithm code only sees DIPs.

- **Origin**: top-left of the window, matching Win32/Direct2D convention.
- **y axis**: grows **downward** in screen space.
- **Window placement**: the per-monitor window spans the monitor's full width. Its bottom edge sits on the monitor's bottom edge. Its height is `stripHeight + headroom` DIP (see constants table). The window is the algorithm's render surface; everything below is computed in window-local coordinates.
- **Ground line**: `groundY = windowHeight` (the bottom edge of the window in window-local coordinates). Blades anchor here.

For clarity, the spec talks about a blade's **`height` above ground** as a positive scalar. The actual screen-space tip coordinate is:

```
tipY = groundY - height * cutHeight
```

That is, a tall uncut blade has a smaller `tipY` (higher on screen) than a short or cut blade.

A monitor's "work area" is **not** used; the grass anchors to the screen bottom regardless of taskbar position, so each implementation should query the monitor's full bounds and place the window accordingly.

---

## 3. Random number source

All randomness is driven by a deterministic PRNG so that the same `(seed, monitorWidth, density)` produces a bit-identical blade list across implementations.

### Algorithm: xorshift64, seeded via SplitMix64

The 64-bit state starts non-zero. To handle `seed == 0` gracefully and to decorrelate adjacent seeds, the raw user seed is first folded through one round of SplitMix64.

```c
// Seed-mix step (run once, at PRNG construction).
uint64_t splitmix64(uint64_t z) {
    z = z + 0x9E3779B97F4A7C15ULL;
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
    return z ^ (z >> 31);
}

// PRNG state.
typedef struct { uint64_t state; } Prng;

void prng_init(Prng* p, uint64_t seed) {
    p->state = splitmix64(seed);
    if (p->state == 0) p->state = 0x9E3779B97F4A7C15ULL; // belt-and-suspenders
}

// Transition function: xorshift64 (Marsaglia, 2003).
uint64_t prng_next_u64(Prng* p) {
    uint64_t x = p->state;
    x ^= x << 13;
    x ^= x >> 7;
    x ^= x << 17;
    p->state = x;
    return x;
}
```

### Derived helpers

```c
// Uniform double in [0, 1). Uses top 53 bits (matches IEEE-754 mantissa precision).
double prng_next_unit(Prng* p) {
    return (prng_next_u64(p) >> 11) * (1.0 / 9007199254740992.0); // 1.0 / 2^53
}

// Uniform double in [lo, hi).
double prng_uniform(Prng* p, double lo, double hi) {
    return lo + prng_next_unit(p) * (hi - lo);
}

// Uniform int in [0, n).
uint32_t prng_index(Prng* p, uint32_t n) {
    return (uint32_t)(prng_next_unit(p) * (double)n);
}
```

**Conformance requirement:** every implementation MUST produce the identical `uint64_t` sequence from `prng_next_u64` for a given seed, and the identical IEEE-754 `double` from `prng_next_unit`. Tests assert this against a snapshot for the canonical seed (see §12).

---

## 4. Blade data model

Each blade is a plain-old-data struct. Field order is not load-bearing across implementations; only the field set and ranges are.

| Field | Type | Range | Lifetime | Description |
| --- | --- | --- | --- | --- |
| `baseX` | double (DIP) | `[0, monitorWidth)` | static | Anchor point on the ground line. |
| `height` | double (DIP) | `[8, 40]` | static | Blade length from ground to tip when fully uncut. |
| `thickness` | double (DIP) | `[1.0, 2.5]` | static | Stroke width for the Bezier. |
| `hue` | uint8 | `[0, 5]` | static | Index into `PALETTE` (see below). |
| `swayPhaseOffset` | double (rad) | `[0, 2π)` | static | Per-blade phase offset for sway. |
| `stiffness` | double | `[0.6, 1.0]` | static | Multiplier on sway amplitude. |
| `cutHeight` | double | `[0.0, 1.0]` | runtime | 1.0 = uncut, 0.0 = fully cut. Initial: 1.0. |
| `gustVelocity` | double (rad/sec) | unbounded | runtime | Wind impulse, decays exponentially. Initial: 0.0. |
| `cutAnimStart` | double (sec) | `-1` or `≥ 0` | runtime | `globalTime` when the current cut animation began; `-1` = idle. Initial: -1. |
| `cutInitialHeight` | double | `[0.0, 1.0]` | runtime | `cutHeight` at the moment the current cut animation began. Initial: 1.0. |

### Color palette

Exactly 6 ARGB colors, indexed by `hue`. Implementations may store these as their native color type (`D2D1_COLOR_F`, `Windows.UI.Color`, `Microsoft.UI.Colors`, etc.), but the source-of-truth values are:

| Index | ARGB (hex) | Approx swatch |
| --- | --- | --- |
| 0 | `0xFF2C5E1A` | deep forest |
| 1 | `0xFF3A7A24` | dark green |
| 2 | `0xFF4C9A2E` | mid green |
| 3 | `0xFF66B845` | grass green |
| 4 | `0xFF7AC957` | bright green |
| 5 | `0xFF8FD96A` | light green |

Alpha is always `0xFF`; window-level transparency is handled by the compositor.

---

## 5. Procedural generation

Inputs: `seed: uint64`, `monitorWidth: double` (DIP), `density: double` (default 1.0, larger = more blades).

Output: an ordered list of `Blade` records, x positions strictly increasing.

```c
void generate_blades(uint64_t seed, double monitorWidth, double density,
                     BladeList* out)
{
    Prng p;
    prng_init(&p, seed);

    double x = 0.0;
    while (x < monitorWidth) {
        // Step first, place blade at new x. This guarantees the first blade
        // is offset from x=0 (avoids a sliver against the screen edge).
        double step = prng_uniform(&p, 4.0, 8.0) / density;
        x += step;
        if (x >= monitorWidth) break;

        Blade b;
        b.baseX            = x;
        b.height           = prng_uniform(&p, 8.0, 40.0);
        b.thickness        = prng_uniform(&p, 1.0, 2.5);
        b.hue              = (uint8_t)prng_index(&p, 6);
        b.swayPhaseOffset  = prng_uniform(&p, 0.0, 2.0 * M_PI);
        b.stiffness        = prng_uniform(&p, 0.6, 1.0);

        b.cutHeight        = 1.0;
        b.gustVelocity     = 0.0;
        b.cutAnimStart     = -1.0;
        b.cutInitialHeight = 1.0;

        bladelist_push(out, b);
    }
}
```

**Field-draw order is fixed.** Implementations MUST draw the six static fields in this exact order (`height`, `thickness`, `hue`, `swayPhaseOffset`, `stiffness`) from the PRNG. Reordering changes the blade list for the same seed and breaks the snapshot tests.

At a 1920-DIP-wide monitor with `density = 1.0`, the expected blade count is approximately `2 * 1920 / (4 + 8) ≈ 320`. The plan's "~400 blades per 1920 px" target is met by `density ≈ 1.25` (a tunable for v1).

---

## 6. Sway physics

Per frame, for each blade, compute the current **effective lean** (signed horizontal tip displacement in DIP).

```c
void update_blade_dynamics(Blade* b, double globalTime, double dt) {
    // 1. Gust velocity decays exponentially.
    b->gustVelocity *= exp(-DECAY_RATE * dt);

    // 2. Base oscillation. baseSwaySpeed gives a ~3-second period.
    double swayPhase = b->swayPhaseOffset + globalTime * BASE_SWAY_SPEED;
    double baseLean  = sin(swayPhase) * BASE_AMPLITUDE * b->stiffness;

    // 3. Gust contribution.
    double effectiveLean = baseLean + b->gustVelocity * GUST_TO_LEAN_FACTOR;

    // Stored or returned; see tick() in §10.
    b->effectiveLean = effectiveLean;
}
```

Constants (see also §11):
- `BASE_SWAY_SPEED = 2π / 3` rad/sec → 3-second sway period.
- `BASE_AMPLITUDE = 6.0` DIP → peak horizontal tip displacement under sway alone (before stiffness).
- `DECAY_RATE = 2.5` /sec → gust velocity half-life ≈ 0.277 sec.
- `GUST_TO_LEAN_FACTOR = 1.5` DIP·sec/rad → converts the (informal) angular gust velocity into a DIP offset.

Sway is **stateless w.r.t. dt** — it is a pure function of `globalTime` and the static fields. Only `gustVelocity` and the cut state accumulate.

---

## 7. Quadratic Bezier rendering geometry

For each blade, the renderer needs three points (base, control, tip), a stroke width, and a color.

```c
typedef struct { double x, y; } Point;
typedef struct { Point base, control, tip; double thickness; uint32_t argb; } Stroke;

Stroke compute_blade_stroke(const Blade* b, double groundY) {
    Stroke s;
    s.argb      = PALETTE[b->hue];
    s.thickness = b->thickness;

    // Stump short-circuit: cut so short that drawing a Bezier is wasted work
    // and the result looks like a dot. Render a tiny vertical line instead.
    if (b->cutHeight < 0.05) {
        s.base    = (Point){ b->baseX, groundY };
        s.control = (Point){ b->baseX, groundY - 1.0 };
        s.tip     = (Point){ b->baseX, groundY - STUMP_HEIGHT }; // 2 DIP tall
        return s;
    }

    double tipX = b->baseX + b->effectiveLean;
    double tipY = groundY - b->height * b->cutHeight;

    // base->tip vector (note: dy is negative because y grows down).
    double dx = tipX - b->baseX;        // == effectiveLean
    double dy = tipY - groundY;         // == -height*cutHeight (negative)
    double len = sqrt(dx*dx + dy*dy);

    // Perpendicular to (dx,dy), rotated 90° CCW in math coords
    // (which, in screen-y-down, points "outward in the direction of lean").
    // Rotation: (x,y) -> (-y, x).
    double nx = -dy / len;
    double ny =  dx / len;

    // Offset the midpoint along the perpendicular by 0.6 * effectiveLean.
    // The factor is signed, so when the blade leans right (effectiveLean > 0)
    // the control point shifts to the right of the base->tip chord, bowing
    // the blade into a natural curve. When effectiveLean == 0, the bezier
    // degenerates to a straight vertical line (control == midpoint).
    double midX = (b->baseX + tipX) * 0.5;
    double midY = (groundY + tipY) * 0.5;
    double offset = CTRL_OFFSET_FACTOR * b->effectiveLean;

    s.base    = (Point){ b->baseX, groundY };
    s.control = (Point){ midX + nx * offset, midY + ny * offset };
    s.tip     = (Point){ tipX, tipY };
    return s;
}
```

The renderer draws each `Stroke` as a quadratic Bezier with rounded line caps. Anti-aliasing is enabled. Implementations MAY batch strokes by color for GPU efficiency; ordering within a batch doesn't affect correctness (blades don't overlap meaningfully at typical density).

---

## 8. Gust impulse model

The mouse hook (see plan §"Input observation") feeds cursor-move events into the simulation. Each event carries `(cursorX, cursorY, eventTime)` in DIP / seconds.

### Bands

Let `groundY` and `windowHeight` be as defined in §2. The **gust band** is wider than the visible grass so blades start reacting before the cursor touches them:

```
gustBandTop    = groundY - STRIP_HEIGHT - HEADROOM   // 80 + 30 = 110 DIP above ground
gustBandBottom = groundY
```

If `cursorY < gustBandTop` or `cursorY > gustBandBottom`, the move is ignored for gust purposes (but still consumed for the velocity baseline; see below).

### Cursor speed

The simulation keeps two scalars per monitor: `prevCursorX` and `prevCursorTime`. On each accepted move event:

```c
double dt_ev = max(eventTime - prevCursorTime, 1.0/1000.0); // floor at 1 ms
double velX  = (cursorX - prevCursorX) / dt_ev;             // DIP/sec, signed
double capped = clamp(velX, -MAX_CURSOR_SPEED, +MAX_CURSOR_SPEED);

prevCursorX    = cursorX;
prevCursorTime = eventTime;
```

`MAX_CURSOR_SPEED = 4000` DIP/sec. Beyond this, the impulse saturates — necessary because a teleporting cursor (RDP, hot-plug) would otherwise produce a huge spike.

### Impulse distribution

```c
double impulseMagnitude = fabs(capped) * IMPULSE_SCALE;       // rad/sec
double signDir = (capped > 0) ? 1.0 : (capped < 0) ? -1.0 : 0;

for (Blade* b in blades) {
    double dxAbs = fabs(b->baseX - cursorX);
    if (dxAbs >= GUST_RADIUS) continue;

    double t = 1.0 - dxAbs / GUST_RADIUS;   // 1.0 at cursor, 0.0 at edge
    double s = clamp(t, 0.0, 1.0);
    double smooth = s * s * (3.0 - 2.0 * s);

    double delta = impulseMagnitude * smooth * signDir;
    b->gustVelocity += delta;
}
```

`IMPULSE_SCALE = 0.003` rad/DIP (so the unit of `capped` cancels against this and `impulseMagnitude` comes out in rad/sec). At the speed cap of 4000 DIP/sec this yields a peak `delta ≈ 12` rad/sec at the cursor, which through `GUST_TO_LEAN_FACTOR = 1.5` corresponds to ≈ 18 DIP of additional lean — a visible but not slapstick gust.

`GUST_RADIUS = 150` DIP.

### Edge cases

- If `prevCursorTime` has not been initialized (first event or after a long idle), set `prevCursorX = cursorX`, `prevCursorTime = eventTime`, and emit no impulse for that event.
- If the time since the last accepted event exceeds 250 ms, treat the next event as a re-initialization (no impulse) to avoid huge synthetic velocities after a window minimize / RDP reconnect.
- `cursorVelocityY` is ignored in v1.

---

## 9. Cut state animation

The mouse hook also delivers `WM_LBUTTONDOWN` events as `(clickX, clickY, eventTime)`.

### Cut band

Tighter than the gust band — only clicks **inside the visible grass strip** count:

```
cutBandTop    = groundY - STRIP_HEIGHT   // 80 DIP above ground
cutBandBottom = groundY
```

Reject the click if `clickY < cutBandTop` or `clickY > cutBandBottom`.

### Apply cut

```c
void apply_click(BladeList* blades, double clickX, double globalTime) {
    for (Blade* b in blades) {
        if (fabs(b->baseX - clickX) >= CUT_RADIUS) continue;
        if (b->cutHeight <= 0.0) continue;                   // already fully cut
        if (b->cutAnimStart >= 0.0) continue;                // already animating

        b->cutAnimStart     = globalTime;
        b->cutInitialHeight = b->cutHeight;
    }
}
```

`CUT_RADIUS = 30` DIP. The "already animating" guard makes repeated clicks on an in-flight blade a no-op — the original 200 ms animation runs to completion.

### Advance animation (per frame)

Called from `tick()` (see §10) after `globalTime` is updated:

```c
void advance_cut(Blade* b, double globalTime) {
    if (b->cutAnimStart < 0.0) return;

    double elapsed = globalTime - b->cutAnimStart;
    double t = elapsed / CUT_DURATION_SEC;       // 0..1 across 200 ms
    if (t >= 1.0) {
        b->cutHeight    = 0.0;
        b->cutAnimStart = -1.0;
    } else {
        b->cutHeight = b->cutInitialHeight * (1.0 - t);  // linear to 0
    }
}
```

`CUT_DURATION_SEC = 0.2` sec. Easing is linear by spec; visually it's brief enough that polynomial easing doesn't pay for itself.

Cut state is **per-session only**. There is no persistence in v1, and re-generating the blade list (DPI change, display hot-plug) resets all `cutHeight` to 1.0.

---

## 10. Per-frame update loop

The renderer calls a single `tick(dt, events)` once per frame, after which it iterates the blade list to produce strokes (§7) and draws them.

```c
typedef enum { EVT_MOVE, EVT_CLICK } EventType;
typedef struct {
    EventType type;
    double x, y;        // DIP, in window-local coords
    double time;        // seconds; monotonic, e.g. QueryPerformanceCounter
} InputEvent;

typedef struct {
    BladeList blades;
    double    globalTime;       // accumulates dt
    double    prevCursorX;
    double    prevCursorTime;   // -1 = uninitialized
} Sim;

void tick(Sim* sim, double dt, const InputEvent* events, size_t numEvents) {
    sim->globalTime += dt;

    // 1. Drain events in order. Move events update prevCursor*; click events
    //    apply cuts. Both reference sim->globalTime indirectly via event.time.
    for (size_t i = 0; i < numEvents; i++) {
        const InputEvent* e = &events[i];
        switch (e->type) {
            case EVT_MOVE:  apply_cursor_move(sim, e); break;
            case EVT_CLICK: apply_click(&sim->blades, e->x, sim->globalTime); break;
        }
    }

    // 2. Per-blade update: gust decay + sway + cut anim + effective lean.
    for (size_t i = 0; i < sim->blades.count; i++) {
        Blade* b = &sim->blades.items[i];
        update_blade_dynamics(b, sim->globalTime, dt);   // §6
        advance_cut(b, sim->globalTime);                 // §9
    }
}
```

Caller (the renderer) then:

```c
for (Blade* b in sim.blades) {
    Stroke s = compute_blade_stroke(b, groundY);    // §7
    draw_quadratic_bezier(s.base, s.control, s.tip, s.thickness, s.argb);
}
```

`dt` is the time since the previous `tick`. It SHOULD be the frame interval as measured by the swap chain / composition target (≈ 1/60 sec at vsync). If the implementation pauses (e.g., the window is occluded), it should resume with a small `dt` (≤ 1/30 sec); never feed in a multi-second `dt`, since that would cause `exp(-DECAY_RATE * dt)` to underflow predictably and `sin(globalTime * BASE_SWAY_SPEED)` to jump, both of which are visible artifacts.

---

## 11. Default constants table

All constants are referenced by name in the pseudocode above. Implementations SHOULD declare them as `const` / `constexpr` / `readonly static` in a single file per project.

| Constant | Value | Unit | Section |
| --- | --- | --- | --- |
| `STRIP_HEIGHT` | 80 | DIP | §2, §8, §9 |
| `HEADROOM` | 30 | DIP | §2, §8 |
| `BLADE_SPACING_MIN` | 4.0 | DIP | §5 |
| `BLADE_SPACING_MAX` | 8.0 | DIP | §5 |
| `BLADE_HEIGHT_MIN` | 8.0 | DIP | §4, §5 |
| `BLADE_HEIGHT_MAX` | 40.0 | DIP | §4, §5 |
| `BLADE_THICKNESS_MIN` | 1.0 | DIP | §4, §5 |
| `BLADE_THICKNESS_MAX` | 2.5 | DIP | §4, §5 |
| `STIFFNESS_MIN` | 0.6 | (unitless) | §4, §5 |
| `STIFFNESS_MAX` | 1.0 | (unitless) | §4, §5 |
| `PALETTE_SIZE` | 6 | colors | §4 |
| `BASE_SWAY_SPEED` | 2π / 3 ≈ 2.0943951 | rad/sec | §6 |
| `BASE_AMPLITUDE` | 6.0 | DIP | §6 |
| `DECAY_RATE` | 2.5 | /sec | §6 |
| `GUST_TO_LEAN_FACTOR` | 1.5 | DIP·sec/rad | §6, §8 |
| `MAX_CURSOR_SPEED` | 4000.0 | DIP/sec | §8 |
| `IMPULSE_SCALE` | 0.003 | rad/DIP | §8 |
| `GUST_RADIUS` | 150.0 | DIP | §8 |
| `CUT_RADIUS` | 30.0 | DIP | §9 |
| `CUT_DURATION_SEC` | 0.2 | sec | §9 |
| `CUT_STUMP_THRESHOLD` | 0.05 | (unitless) | §7 |
| `STUMP_HEIGHT` | 2.0 | DIP | §7 |
| `CTRL_OFFSET_FACTOR` | 0.6 | (unitless) | §7 |
| `CURSOR_REINIT_GAP_SEC` | 0.25 | sec | §8 |
| `CANONICAL_TEST_SEED` | `0x6B6173746F` | uint64 | §12 |

The palette table from §4 (six ARGB values) is also part of this constants set.

---

## 12. Conformance

Because every implementation ports this spec verbatim, the unit tests in `DesktopGrass.Native.Tests`, `DesktopGrass.Win2D.Tests`, and `DesktopGrass.WinUI3.Tests` can share a single snapshot fixture.

### Canonical test seed

```
CANONICAL_TEST_SEED = 0x6B6173746F  // uint64
```

Tests at minimum SHOULD assert:

1. **PRNG snapshot.** With `prng_init(seed = CANONICAL_TEST_SEED)`, the first 16 outputs of `prng_next_u64` match a fixed snapshot array embedded in each test project. Identical across all three impls.

2. **Blade generation snapshot.** With `(seed = CANONICAL_TEST_SEED, monitorWidth = 1920.0, density = 1.0)`:
   - The blade count matches across impls.
   - For the first 10 and last 10 blades, every static field (`baseX`, `height`, `thickness`, `hue`, `swayPhaseOffset`, `stiffness`) matches to within `1e-12` (double precision round-trip; should be exact).
   - `baseX` is strictly increasing.
   - All `height ∈ [8, 40]`, `thickness ∈ [1.0, 2.5]`, `hue ∈ [0, 5]`, `swayPhaseOffset ∈ [0, 2π)`, `stiffness ∈ [0.6, 1.0]`.

3. **Sway determinism.** Given a fixed blade and `globalTime`, `effectiveLean` (with `gustVelocity = 0`) matches across impls to within `1e-9`. The bound is loose because `sin` may differ in last-bit precision between CRTs.

4. **Gust impulse.** Feeding a synthetic move event with a known `(prevCursorX, cursorX, dt_ev)` produces `gustVelocity` deltas on each blade matching a snapshot to within `1e-9`.

5. **Cut animation.** A click at `(clickX = 500, clickY = groundY - 40, time = 0)` followed by `tick(dt = 0.05)` calls for 5 frames produces the expected `cutHeight` series for blades within `CUT_RADIUS` (linear from 1.0 to 0.0 across 4 frames, then 0.0 thereafter), and leaves blades outside the radius at `cutHeight = 1.0`.

6. **Idempotence.** Clicking twice on the same blade within the 200 ms cut window does not change the trajectory of the first cut. Clicking on an already-cut blade is a no-op.

When any test in this list fails on a single impl, that impl has diverged from the spec — fix the impl, not the spec, unless the divergence reveals a spec ambiguity, in which case update this document first and propagate the fix to all three impls.
