# DesktopGrass — Shared Algorithm Specification

This document is the **single source of truth** for the grass simulation. The two shipping implementations — `DesktopGrass.Native` (Win32 + Direct2D, C++) and `DesktopGrass.Win2D` (C# + Vortice Direct2D) — each port the algorithms below into their own language. There is no shared library; the spec **is** the contract.

> **History:** the repo originally shipped four parallel implementations (Native, Win2D, packaged WinUI 3, vanilla WPF). The WinUI 3 and WPF impls were dropped after a head-to-head A/B because they were 3–10× heavier on working set than the Native and Win2D builds while offering no behavioral advantage for a transparent, click-through, topmost overlay. See `docs/comparison.md` for the full evaluation.

For the product goals, window model, input model, and project layout, see [`plan.md`](../plan.md). This document covers only the math/state machine that every implementation must reproduce.

---

## 1. Overview

DesktopGrass paints a strip of procedurally generated grass along the bottom of every monitor, on top of all windows, fully click-through. The strip sways gently on its own, reacts to cursor motion with localized gusts, and reacts to left-clicks by cutting blades.

The v1 plan called for **four independent implementations** of the same feature set so they could be compared side-by-side on LoC, CPU/GPU cost, startup time, and binary size; two of those impls (WinUI 3, WPF) were dropped after the comparison and the remaining two (Native, Win2D) continue to ship together specifically so cross-impl conformance keeps the spec honest. To make that conformance meaningful, both impls must produce **pixel-equivalent output** for a given `(seed, monitorWidth, density)` and an identical event stream. This spec fixes every constant, function, and ordering decision needed to make that true.

Pseudocode is given in a C-like form chosen to port cleanly to C++ (Native), C# (Win2D), and any future Rust/Go port. Where a port idiom differs (e.g., `Math.Sin` vs `std::sin`), the spec uses the mathematical name (`sin`, `exp`, `sqrt`, `clamp`).

---

## 2. Coordinate system

All coordinates are in **DIPs** (device-independent pixels, 1 DIP = 1/96 inch). Each implementation is responsible for DPI scaling at the window/swap-chain level; algorithm code only sees DIPs.

- **Origin**: top-left of the window, matching Win32/Direct2D convention.
- **y axis**: grows **downward** in screen space.
- **Window placement**: the per-monitor window spans the monitor's full width. Its bottom edge sits on the monitor's bottom edge. Its height is `stripHeight + headroom` DIP (see constants table). The window is the algorithm's render surface; everything below is computed in window-local coordinates.
- **Ground line**: `groundY = windowHeight` (the bottom edge of the window in window-local coordinates). Blades anchor here.

For clarity, the spec talks about a blade's **`height` above ground** as a positive scalar. The visible blade length is:

```
L = height * cutHeight
```

When a blade is perfectly vertical, its endpoint sits at `groundY - L`, so a tall uncut blade reaches higher on screen than a short or cut blade. Once lean is applied, §7 computes the rendered tip with chord preservation rather than keeping this Y coordinate fixed.

The grass anchors to the bottom of the monitor's **work area** (`MONITORINFO.rcWork.bottom`), so it sits directly on top of the taskbar rather than being clipped behind it. Each implementation queries `GetMonitorInfo` per monitor and uses `rcWork` for the window position. (Side- or top-docked taskbars work the same way: the work area excludes whatever side the taskbar is on, and the grass strip lands on the bottom edge of what remains.)

### Click-through verification

A `[smoke]` test spawns a probe window beneath an overlay with the same click-through extended styles, sends a synthetic click via `SendInput`, and asserts the probe receives `WM_LBUTTONDOWN`. The test skips gracefully when no interactive desktop is available, so headless CI does not fail only because `SendInput` cannot run.

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
| `height` | double (DIP) | `[6, 30]` | static | Blade length from ground to tip when fully uncut. |
| `thickness` | double (DIP) | `[1.0, 2.5]` | static | Stroke width for the Bezier. |
| `hue` | uint8 | `[0, 5]` | static | Index into `PALETTE` (see below). |
| `swayPhaseOffset` | double (rad) | `[0, 2π)` | static | Per-blade phase offset for sway. |
| `stiffness` | double | `[0.6, 1.0]` | static | Multiplier on sway amplitude. |
| `cutHeight` | double | `[0.0, 1.0]` | runtime | 1.0 = uncut, 0.0 = fully cut. Initial: 1.0. |
| `gustVelocity` | double (rad/sec) | unbounded | runtime | Wind impulse, decays exponentially. Initial: 0.0. |
| `cutAnimStart` | double (sec) | `-1` or `≥ 0` | runtime | `globalTime` when the current cut animation began; `-1` = idle. Initial: -1. |
| `cutInitialHeight` | double | `[0.0, 1.0]` | runtime | `cutHeight` at the moment the current cut animation began. Initial: 1.0. |
| `cutFloor` | double | `[0.06, 0.16)` | static | Per-blade residual height a mowed blade settles at (stubble), so the cut line varies naturally instead of being perfectly flat. Drawn from an independent salted PRNG stream (`CUT_FLOOR_PRNG_SALT`). Both bounds sit above `CUT_STUMP_THRESHOLD` so stubble renders as a short blade, not a degenerate stump. Default-constructed `Blade` (test fixtures) has `0.0`, which reduces all cut/regrow math to the original collapse-to-zero behavior. |
| `regrowDelay` | double (sec) | `[30, 90]` | static | Per-blade wait after being cut before regrowth begins. |
| `regrowDuration` | double (sec) | `[2, 4]` | static | Per-blade time to grow back from stump to full height. |
| `regrowStart` | double (sec) | `-1` or `≥ 0` | runtime | `globalTime` at which regrowth begins (set when cut anim completes); `-1` = idle. Initial: -1. |
| `isFlower` | bool | `{false, true}` | static | If true, this blade is rendered as a flower (taller stem + colored head at the tip). See §5 "Flower stream" and §7 "Flower head". |
| `flowerHeadColorIdx` | uint8 | `[0, 5]` | static | Index into `FLOWER_PALETTE`. Unused when `isFlower == false`. |
| `flowerHeadRadius` | double (DIP) | `[1.8, 3.0]` | static | Radius of the filled circle drawn at the tip. Unused when `isFlower == false`. |
| `heightBonus` | double | `[1.0, 1.5]` | static | Multiplier on `height` for stem length. `1.0` for non-flowers; `[1.2, 1.5]` for flowers — flowers stand visibly taller than the surrounding grass. |
| `isMushroom` | bool | `{false, true}` | static | If true, this slot renders as a mushroom (filled-ellipse cap on a short stem) and the grass blade + flower head are NOT drawn for the slot. See §5 "Mushroom stream" and §7 "Mushroom". `isMushroom` and `isFlower` are independently sampled; if both happen to be true on the same slot, `isMushroom` wins at render time (the slot is treated as a mushroom). |
| `mushroomCapColorIdx` | uint8 | `[0, 5]` | static | Index into `MUSHROOM_PALETTE`. Unused when `isMushroom == false`. |
| `mushroomCapWidth` | double (DIP) | `[4.0, 8.0]` | static | Horizontal radius of the cap ellipse. Unused when `isMushroom == false`. |
| `mushroomCapHeight` | double (DIP) | `[2.5, 5.0]` | static | Vertical radius of the cap ellipse (always less than `mushroomCapWidth` so the dome is flatter than it is wide). Unused when `isMushroom == false`. |
| `mushroomStemHeight` | double (DIP) | `[4.0, 10.0]` | static | Stem length from `groundY` to the cap center. Unused when `isMushroom == false`. |
| `mushroomStemThickness` | double (DIP) | `[2.0, 4.0]` | static | Stem stroke thickness. Unused when `isMushroom == false`. |

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

### Flower palette

Flowers use a separate 6-color palette so their heads contrast with the grass behind them.

| Index | ARGB (hex) | Approx swatch |
| --- | --- | --- |
| 0 | `0xFFFFEB3B` | yellow (dandelion) |
| 1 | `0xFFFFA726` | orange (marigold) |
| 2 | `0xFFFF80AB` | pink (cosmos) |
| 3 | `0xFFE1BEE7` | lavender |
| 4 | `0xFFFFFFFF` | white (daisy) |
| 5 | `0xFFEF5350` | red (poppy) |

Alpha is always `0xFF` here too.

### Mushroom palette

Mushrooms have their own 6-color palette. Stems are always the single fixed color `MUSHROOM_STEM_COLOR`.

| Index | ARGB (hex) | Approx swatch |
| --- | --- | --- |
| 0 | `0xFFD32F2F` | red (amanita) |
| 1 | `0xFF8D6E63` | brown |
| 2 | `0xFFC9A66B` | tan |
| 3 | `0xFFFFF8E1` | ivory |
| 4 | `0xFFE57373` | dusty pink |
| 5 | `0xFF6D4C41` | dark brown |

The stem color is `MUSHROOM_STEM_COLOR = 0xFFF5F5DC` (beige / ivory). Alpha is always `0xFF`.

---

## 5. Procedural generation

Inputs: `seed: uint64`, `monitorWidth: double` (DIP), `density: double` (default 1.0, larger = more blades).

Output: an ordered list of `Blade` records, x positions strictly increasing.

```c
void generate_blades(uint64_t seed, double monitorWidth, double density,
                     BladeList* out)
{
    Prng p;        // main stream — drives geometry, palette, sway
    prng_init(&p, seed);

    Prng pr;       // regrowth stream — drives regrowDelay / regrowDuration
    prng_init(&pr, seed ^ REGROW_PRNG_SALT);

    Prng pf;       // flower stream — decides flower-or-not + flower props
    prng_init(&pf, seed ^ FLOWER_PRNG_SALT);

    Prng pm;       // mushroom stream — decides mushroom-or-not + mushroom props
    prng_init(&pm, seed ^ MUSHROOM_PRNG_SALT);

    double x = 0.0;
    while (x < monitorWidth) {
        // Step first, place blade at new x. This guarantees the first blade
        // is offset from x=0 (avoids a sliver against the screen edge).
        double step = prng_uniform(&p, 4.0, 8.0) / density;
        x += step;
        if (x >= monitorWidth) break;

        Blade b;
        b.baseX            = x;
        b.height           = prng_uniform(&p, 6.0, 30.0);
        b.thickness        = prng_uniform(&p, 1.0, 2.5);
        b.hue              = (uint8_t)prng_index(&p, 6);
        b.swayPhaseOffset  = prng_uniform(&p, 0.0, 2.0 * M_PI);
        b.stiffness        = prng_uniform(&p, 0.6, 1.0);

        // Regrowth jitter is drawn from a SEPARATE stream so the main
        // sequence (and thus geometry / sway / palette) is bit-identical
        // whether regrowth is enabled or not.
        b.regrowDelay      = prng_uniform(&pr, REGROW_DELAY_MIN,    REGROW_DELAY_MAX);
        b.regrowDuration   = prng_uniform(&pr, REGROW_DURATION_MIN, REGROW_DURATION_MAX);

        // Flower stream. Every blade consumes EXACTLY ONE unconditional
        // draw from this stream (the probability check). Flower blades
        // additionally consume three more draws for head color / radius
        // / height bonus. Non-flower blades stop after the first draw.
        // This ordering is required for the flower stream to produce an
        // identical (isFlower, headColor, headRadius, heightBonus)
        // sequence across both implementations for a given seed.
        bool isFlower = prng_uniform(&pf, 0.0, 1.0) < FLOWER_PROBABILITY;
        b.isFlower            = isFlower;
        if (isFlower) {
            b.flowerHeadColorIdx = (uint8_t)prng_index(&pf, FLOWER_PALETTE_SIZE);
            b.flowerHeadRadius   = prng_uniform(&pf,
                                                FLOWER_HEAD_RADIUS_MIN,
                                                FLOWER_HEAD_RADIUS_MAX);
            b.heightBonus        = prng_uniform(&pf,
                                                FLOWER_HEIGHT_BONUS_MIN,
                                                FLOWER_HEIGHT_BONUS_MAX);
        } else {
            b.flowerHeadColorIdx = 0;
            b.flowerHeadRadius   = 0.0;
            b.heightBonus        = 1.0;
        }

        // Mushroom stream. Every blade consumes EXACTLY ONE unconditional
        // draw (the probability check). Mushroom slots additionally
        // consume five more conditional draws for cap-color, cap-width,
        // cap-height, stem-height, stem-thickness. Field-draw order is
        // fixed and identical across both implementations.
        bool isMushroom = prng_uniform(&pm, 0.0, 1.0) < MUSHROOM_PROBABILITY;
        b.isMushroom = isMushroom;
        if (isMushroom) {
            b.mushroomCapColorIdx     = (uint8_t)prng_index(&pm, MUSHROOM_PALETTE_SIZE);
            b.mushroomCapWidth        = lerp(MUSHROOM_CAP_WIDTH_MIN,      MUSHROOM_CAP_WIDTH_MAX,      prng_uniform_unit(&pm));
            b.mushroomCapHeight       = lerp(MUSHROOM_CAP_HEIGHT_MIN,     MUSHROOM_CAP_HEIGHT_MAX,     prng_uniform_unit(&pm));
            b.mushroomStemHeight      = lerp(MUSHROOM_STEM_HEIGHT_MIN,    MUSHROOM_STEM_HEIGHT_MAX,    prng_uniform_unit(&pm));
            b.mushroomStemThickness   = lerp(MUSHROOM_STEM_THICKNESS_MIN, MUSHROOM_STEM_THICKNESS_MAX, prng_uniform_unit(&pm));
        } else {
            b.mushroomCapColorIdx     = 0;
            b.mushroomCapWidth        = 0.0;
            b.mushroomCapHeight       = 0.0;
            b.mushroomStemHeight      = 0.0;
            b.mushroomStemThickness   = 0.0;
        }

        b.cutHeight        = 1.0;
        b.gustVelocity     = 0.0;
        b.cutAnimStart     = -1.0;
        b.cutInitialHeight = 1.0;
        b.regrowStart      = -1.0;

        bladelist_push(out, b);
    }
}
```

**Field-draw order is fixed, per stream.** From the main stream `p`, implementations MUST draw the six static fields in this exact order: `step`, `height`, `thickness`, `hue`, `swayPhaseOffset`, `stiffness`. From the regrowth stream `pr`, the order is `regrowDelay`, then `regrowDuration`. From the flower stream `pf`, the order is `isFlower` decision (always one draw), and **only if** `isFlower == true`, then `flowerHeadColorIdx`, `flowerHeadRadius`, `heightBonus`. From the mushroom stream `pm`, the order is `isMushroom` decision (always one draw), and **only if** `isMushroom == true`, then `mushroomCapColorIdx`, `mushroomCapWidth`, `mushroomCapHeight`, `mushroomStemHeight`, `mushroomStemThickness`. Reordering or interleaving the four streams changes the per-blade values for a given seed and breaks the snapshot tests. The four streams are completely independent — the main stream's draw count per blade does not depend on whether regrowth, flowers, or mushrooms are enabled. Both impls MUST emit the same per-stream sequence; cross-impl tests pin this.

At a 1920-DIP-wide monitor with `density = 2.25`, the expected blade count is approximately `2 * 1920 * 2.25 / (4 + 8) ≈ 720`; this is the current app default tuning for a denser field.

---

## 6. Sway physics

Per frame, for each blade, compute the current **effective lean** (signed horizontal tip displacement in DIP).

```c
void update_blade_dynamics(Blade* b, double globalTime, double dt) {
    // 1. Gust velocity decays exponentially.
    b->gustVelocity *= exp(-DECAY_RATE * dt);

    // 2. Base oscillation. baseSwaySpeed gives a ~6-second period.
    double swayPhase = b->swayPhaseOffset + globalTime * BASE_SWAY_SPEED;
    double baseLean  = sin(swayPhase) * BASE_AMPLITUDE * b->stiffness;

    // 3. Gust contribution.
    double effectiveLean = baseLean + b->gustVelocity * GUST_TO_LEAN_FACTOR;

    // Stored or returned; see tick() in §10.
    b->effectiveLean = effectiveLean;
}
```

Constants (see also §11):
- `BASE_SWAY_SPEED = π / 3 ≈ 1.0471975511965976` rad/sec → 6-second sway period.
- `BASE_AMPLITUDE = 3.3` DIP → peak horizontal tip displacement under sway alone (before stiffness).
- `DECAY_RATE = 2.5` /sec → gust velocity half-life ≈ 0.277 sec.
- `GUST_TO_LEAN_FACTOR = 0.75` DIP·sec/rad → converts the (informal) angular gust velocity into a DIP offset.

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

    double L = b->height * b->heightBonus * b->cutHeight;

    // Chord preservation: blades have a fixed length L. As effectiveLean
    // grows, the tip arcs OVER (Y drops) so the blade's chord stays equal
    // to L — rather than the blade stretching diagonally. Clamp to
    // MAX_LEAN_FRACTION * L so the sqrt is always non-negative under
    // strong gust impulses.
    double lean = b->effectiveLean;
    double maxLean = MAX_LEAN_FRACTION * L;
    if (lean >  maxLean) lean =  maxLean;
    if (lean < -maxLean) lean = -maxLean;

    double dropFactor = sqrt(1.0 - (lean / L) * (lean / L));

    double tipX = b->baseX + lean;
    double tipY = groundY - L * dropFactor;

    // Rooted-bend control point: directly above the base, at a fraction
    // CTRL_OFFSET_FACTOR of the (current, foreshortened) blade height.
    s.base    = (Point){ b->baseX, groundY };
    s.control = (Point){ b->baseX, groundY - L * CTRL_OFFSET_FACTOR * dropFactor };
    s.tip     = (Point){ tipX, tipY };
    return s;
}
```

The renderer draws each `Stroke` as a quadratic Bezier with rounded line caps. Anti-aliasing is enabled. Implementations MAY batch strokes by color for GPU efficiency; ordering within a batch doesn't affect correctness (blades don't overlap meaningfully at typical density).

`heightBonus` defaults to `1.0` for ordinary blades, so the formula `L = height * heightBonus * cutHeight` is a no-op for them and chord preservation works exactly as before. Flowers carry `heightBonus` in `[1.2, 1.5]`, so the stem stands proportionally taller while keeping the same chord-preserving bend.

### Flower head

After drawing the stem, if `blade.isFlower == true`, the renderer additionally draws a filled circle at the **tip** of the stroke (`s.tip`):

```c
if (b->isFlower && b->cutHeight >= CUT_STUMP_THRESHOLD) {
    fill_circle(s.tip, b->flowerHeadRadius, FLOWER_PALETTE[b->flowerHeadColorIdx]);
}
```

The head is suppressed once the stem has been cut down past `CUT_STUMP_THRESHOLD` (the same threshold that switches the stem itself to the stump short-circuit). This keeps a cut flower from leaving a colored dot floating just above the ground. No outline / no anti-aliasing toggle is required — implementations may use whatever filled-ellipse primitive their renderer provides (`FillEllipse` on D2D, `FillEllipse` on Vortice). Both implementations MUST place the head **at the same tip point** the chord-preserving stroke math computed.

Chord preservation matters because `effectiveLean` is the horizontal tip displacement: if the tip kept the fixed vertical position `groundY - L` while moving sideways, the painted base-to-tip chord would become longer than the blade and read visually as stretching. Instead, the tip moves on a circle of radius `L` around the base, so `lean / L = sin(θ)` and `dropFactor = sqrt(1 - (lean / L)^2) = cos(θ)`, where `θ` is the bend angle from vertical. At zero lean, `dropFactor` is `1`; at the maximum lean of `0.95 * L`, it is approximately `0.312`.

### Mushroom

When `blade.isMushroom == true`, the renderer **short-circuits** the grass-blade + flower-head path entirely for that slot and instead draws a mushroom: a short ivory stem with a filled-ellipse cap sitting on top of it. Mushrooms are rigid (no sway, no gust response — `effectiveLean` is ignored). They are cuttable and regrowable via the same `cutHeight` machinery as grass; both the stem and the cap scale linearly with `cutHeight` as the cut animation runs.

```c
if (b->isMushroom) {
    double baseX = b->baseX;
    double gy    = groundY;

    if (b->cutHeight < CUT_STUMP_THRESHOLD) {
        // Stump stub: short ivory stem of MUSHROOM_STUMP_HEIGHT, no cap.
        // Slightly taller than the grass STUMP_HEIGHT so that a cut
        // mushroom nub reads as visually distinct from a cut blade.
        draw_line(
            { baseX, gy },
            { baseX, gy - MUSHROOM_STUMP_HEIGHT },
            b->mushroomStemThickness,
            MUSHROOM_STEM_COLOR);
        return;
    }

    double scale = b->cutHeight;
    double stemH = b->mushroomStemHeight * scale;
    double capRX = b->mushroomCapWidth  * scale;
    double capRY = b->mushroomCapHeight * scale;
    double capCY = gy - stemH;

    // Stem.
    draw_line(
        { baseX, gy },
        { baseX, capCY },
        b->mushroomStemThickness,
        MUSHROOM_STEM_COLOR);

    // Cap. Centered above the stem top; wider than tall by spec
    // (capWidth > capHeight, so the dome reads as flattened).
    fill_ellipse(
        { baseX, capCY }, capRX, capRY,
        MUSHROOM_PALETTE[b->mushroomCapColorIdx]);
    return;   // mushroom slots do NOT also draw the grass blade or flower
}
```

Implementations are free to use whatever line-stroke and filled-ellipse primitives their renderer provides (D2D `DrawLine` + `FillEllipse` on Native, Vortice `DrawLine` + `FillEllipse` on Win2D). The stem thickness uses no caps requirement — round caps are fine, butt caps are fine. The cap MUST be filled, not stroked.

If `isMushroom` and `isFlower` are both true on the same slot (which can happen — the two streams are independent), the renderer treats the slot as a mushroom: it short-circuits before reaching the flower-head code, so the flower never paints.

The rooted-bend control point gives the curve a vertical tangent at the base because `base->control` is purely vertical: both points have `x = baseX`, so the blade emerges rooted from the ground regardless of lean. The tip tangent direction is `(tip - control) = (lean, -L * (1 - CTRL_OFFSET_FACTOR) * dropFactor)`, which points up-and-toward-the-lean so the tip trails naturally instead of the blade bulging evenly around the chord.

`CTRL_OFFSET_FACTOR` now means the fraction of the current, foreshortened blade height where the control point sits. The default remains `0.6`: 60% up the foreshortened height balances a tighter bend near the middle against a whippier curve with the control point nearer the tip.

`MAX_LEAN_FRACTION` clamps `effectiveLean` before rendering because gust impulses can briefly exceed `L`, especially for short blades. The default `0.95` lets a blade lay down nearly horizontal while keeping it from folding completely flat and guarantees the square root never receives a negative input.

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

`IMPULSE_SCALE = 0.003` rad/DIP (so the unit of `capped` cancels against this and `impulseMagnitude` comes out in rad/sec). At the speed cap of 4000 DIP/sec this yields a peak `delta ≈ 12` rad/sec at the cursor, which through `GUST_TO_LEAN_FACTOR = 0.75` corresponds to ≈ 9 DIP of additional lean — a visible nudge that only saturates the chord-preservation clamp on the shortest blades right at the cursor.

`GUST_RADIUS = 150` DIP.

### Edge cases

- If `prevCursorTime` has not been initialized (first event or after a long idle), set `prevCursorX = cursorX`, `prevCursorTime = eventTime`, and emit no impulse for that event.
- If the time since the last accepted event exceeds 250 ms, treat the next event as a re-initialization (no impulse) to avoid huge synthetic velocities after a window minimize / RDP reconnect.
- `cursorVelocityY` is ignored in v1.

---

## 8.1 Ambient gusts

Real grass moves even when no one is touching it. The simulation therefore emits **ambient gusts**: small, randomly scheduled puffs of wind that fire independently of cursor input. Each puff reuses the §8 impulse-distribution kernel — same `gustVelocity` field on the blades, same `DECAY_RATE` for the trailing exponential — so visually they read as the same wind, just smaller and self-driven.

### Scheduler state

Each `Sim` carries three additional pieces of state:

- `Prng ambientPrng` — fifth independent xorshift64 stream, seeded with `seed XOR AMBIENT_GUST_PRNG_SALT`. Never mixed into the main / regrowth / flower / mushroom streams.
- `double nextAmbientGustTime` — absolute `globalTime` at which the next puff fires.
- `double monitorWidth` — snapshotted at `sim_init` / `sim_regenerate` so the ambient X distribution is unaffected by later window resizes.

At init / regenerate:

```c
prng_init(&sim->ambientPrng, seed ^ AMBIENT_GUST_PRNG_SALT);
sim->monitorWidth        = monitorWidth;
sim->nextAmbientGustTime = sim->globalTime
                         + prng_uniform(&sim->ambientPrng,
                                         AMBIENT_GUST_INTERVAL_MIN,
                                         AMBIENT_GUST_INTERVAL_MAX);
```

The very first interval is sampled at init — i.e. the first puff never fires at `globalTime = 0`. This keeps the four-draws-per-fire ordering below consistent for every subsequent puff.

### Per-frame step

Inside `tick` (§10), after draining cursor / click events and **before** the per-blade dynamics update, run:

```c
while (sim->globalTime >= sim->nextAmbientGustTime) {
    double x         = prng_uniform(&sim->ambientPrng, 0.0, sim->monitorWidth);
    double signDir   = prng_uniform(&sim->ambientPrng, 0.0, 1.0) < 0.5 ? -1.0 : 1.0;
    double magFactor = prng_uniform(&sim->ambientPrng,
                                     AMBIENT_GUST_MAG_FACTOR_MIN,
                                     AMBIENT_GUST_MAG_FACTOR_MAX);
    apply_ambient_gust(sim, x, signDir, magFactor);

    double interval  = prng_uniform(&sim->ambientPrng,
                                     AMBIENT_GUST_INTERVAL_MIN,
                                     AMBIENT_GUST_INTERVAL_MAX);
    sim->nextAmbientGustTime += interval;
}
```

**Field-draw order is fixed: `x`, `signDir`, `magFactor`, `interval`** — exactly four draws per puff. Both impls MUST emit them in this order; reordering breaks the cross-impl scheduler snapshot.

The `while` (not `if`) is intentional: if `dt` was large enough to skip multiple intervals, all of them fire in chronological order. In practice `dt ≈ 1/60` and `AMBIENT_GUST_INTERVAL_MIN = 5.0`, so the loop almost always runs zero or one iterations.

### Impulse kernel

```c
void apply_ambient_gust(Sim* sim, double x, double signDir, double magFactor) {
    double impulseMagnitude = MAX_CURSOR_SPEED * magFactor * IMPULSE_SCALE;
    double radius           = GUST_RADIUS * AMBIENT_GUST_RADIUS_FACTOR;

    for (Blade* b in sim->blades) {
        double dxAbs = fabs(b->baseX - x);
        if (dxAbs >= radius) continue;

        double t      = 1.0 - dxAbs / radius;
        double s      = clamp(t, 0.0, 1.0);
        double smooth = s * s * (3.0 - 2.0 * s);

        b->gustVelocity += impulseMagnitude * smooth * signDir;
    }
}
```

Same `s * s * (3 - 2s)` smoothstep as §8. The synthetic `impulseMagnitude` is parameterised by `magFactor` (a fraction of a saturated cursor sweep) rather than by `capped / MAX_CURSOR_SPEED`. With the defaults below a peak ambient puff delivers ≈ 30–60 % of a saturated cursor gust, over half the radius — a small localised breeze rather than a swipe.

### Defaults & conformance

Constants land in §11. Adding ambient gusts MUST NOT change the §12 static blade snapshot — the four pre-existing streams are untouched. A new snapshot SHOULD pin the first eight emitted puffs for `(seed = CANONICAL_TEST_SEED, monitorWidth = 1920.0)`: each entry is the tuple `(fireTime, x, signDir, magFactor)` and MUST match across both impls bit-for-bit (in the integer / discrete fields) and to ≤ 1 ULP in the doubles drawn from `prng_uniform`.

---

## 9. Cut and regrowth state animation

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
        if (b->cutHeight <= b->cutFloor) continue;           // already at its stubble floor
        if (b->cutAnimStart >= 0.0) continue;                // already animating a cut

        b->cutAnimStart     = globalTime;
        b->cutInitialHeight = b->cutHeight;
        b->regrowStart      = -1.0;   // cancel any pending regrowth — phase 1
                                      // will reschedule a fresh delay on completion.
    }
}
```

`CUT_RADIUS = 30` DIP. The "already animating" guard makes repeated clicks on an in-flight blade a no-op — the original 200 ms animation runs to completion. The `cutHeight <= cutFloor` check makes clicks on a blade already mowed to its stubble floor a no-op (with `cutFloor == 0` for test fixtures this is the original `cutHeight <= 0` stump check); clicks on a mid-regrowing blade (`cutHeight ∈ (cutFloor, 1)`) do re-cut, and the cancellation of `regrowStart` keeps phase 2 from firing on top of phase 1.

### Advance animation (per frame)

Called from `tick()` (see §10) after `globalTime` is updated. Each blade is either idle, in phase 1 (cut animation), in the regrowth delay (idle with `regrowStart > globalTime`), or in phase 2 (regrowth animation):

```c
void advance_cut(Blade* b, double globalTime) {
    // Phase 1: cut animation.
    if (b->cutAnimStart >= 0.0) {
        double elapsed = globalTime - b->cutAnimStart;
        double t = elapsed / CUT_DURATION_SEC;       // 0..1 across 200 ms
        if (t >= 1.0) {
            b->cutHeight    = b->cutFloor;   // settle at the stubble floor (0 for fixtures)
            b->cutAnimStart = -1.0;
            // Schedule regrowth — but only if this blade has valid jitter values.
            // (A zero-initialized Blade with regrowDelay == regrowDuration == 0
            // stays a permanent stump; this is the v1 test-fixture path.)
            if (b->regrowDelay > 0.0 && b->regrowDuration > 0.0) {
                b->regrowStart = globalTime + b->regrowDelay;
            }
        } else {
            // Lerp from the height at cut time down to the per-blade floor.
            b->cutHeight = b->cutFloor + (b->cutInitialHeight - b->cutFloor) * (1.0 - t);
        }
        return;
    }

    // Phase 2: regrowth animation, after the delay has elapsed.
    if (b->regrowStart >= 0.0 && globalTime >= b->regrowStart
        && b->regrowDuration > 0.0)
    {
        double elapsed = globalTime - b->regrowStart;
        double t = elapsed / b->regrowDuration;      // 0..1 across regrowDuration
        if (t >= 1.0) {
            b->cutHeight   = 1.0;
            b->regrowStart = -1.0;
        } else {
            b->cutHeight = b->cutFloor + (1.0 - b->cutFloor) * t;  // linear floor → 1
        }
    }
}
```

`CUT_DURATION_SEC = 0.2` sec. `REGROW_DELAY_*` and `REGROW_DURATION_*` (see §11) bracket per-blade jitter sampled in §5. Both animations use linear easing — the cut is brief enough that polynomial easing doesn't pay for itself, and the regrowth is slow enough that the eye doesn't read a curve.

### Lifecycle

For a single click on an uncut blade:
1. `apply_click` sets `cutAnimStart = globalTime`, leaves `regrowStart = -1`.
2. Over `CUT_DURATION_SEC` (200 ms), phase 1 drives `cutHeight` from its current value down to the per-blade `cutFloor` (the stubble height; `0` for zero-floor test fixtures).
3. On phase 1 completion, `cutHeight = cutFloor`, `cutAnimStart = -1`, and `regrowStart = globalTime + regrowDelay`.
4. For `regrowDelay` seconds (30–90 s, per-blade), the blade rests at its stubble floor.
5. Once `globalTime >= regrowStart`, phase 2 drives `cutHeight` from `cutFloor` to 1 over `regrowDuration` seconds (2–4 s, per-blade).
6. On phase 2 completion, `cutHeight = 1`, `regrowStart = -1`. The blade is uncut and clickable again.

For a click during phase 2 (re-cut a mid-regrowing blade): `apply_click` records the current `cutHeight` as `cutInitialHeight`, sets `cutAnimStart = globalTime`, and clears `regrowStart`. Phase 1 runs back down to the blade's `cutFloor` from wherever it was, and a new regrowth cycle is scheduled on completion.

Cut state is **per-session only**. There is no persistence in v1, and re-generating the blade list (DPI change, display hot-plug) resets all `cutHeight` to 1.0 and `regrowStart` to -1.

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
    Prng      ambientPrng;      // §8.1 — fifth independent stream
    double    nextAmbientGustTime; // §8.1 — absolute fire time
    double    monitorWidth;     // §8.1 — snapshotted at init
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

    // 2. Ambient gust scheduler (§8.1). Runs BEFORE the per-blade update so
    //    any ambient puffs that fire this tick contribute to the same
    //    decay step the cursor impulses use.
    while (sim->globalTime >= sim->nextAmbientGustTime) {
        double x         = prng_uniform(&sim->ambientPrng, 0.0, sim->monitorWidth);
        double signDir   = prng_uniform(&sim->ambientPrng, 0.0, 1.0) < 0.5 ? -1.0 : 1.0;
        double magFactor = prng_uniform(&sim->ambientPrng,
                                         AMBIENT_GUST_MAG_FACTOR_MIN,
                                         AMBIENT_GUST_MAG_FACTOR_MAX);
        apply_ambient_gust(sim, x, signDir, magFactor);

        double interval  = prng_uniform(&sim->ambientPrng,
                                         AMBIENT_GUST_INTERVAL_MIN,
                                         AMBIENT_GUST_INTERVAL_MAX);
        sim->nextAmbientGustTime += interval;
    }

    // 3. Per-blade update: gust decay + sway + cut anim + effective lean.
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
| `DEFAULT_DENSITY` | 2.25 | (unitless) | §5 |
| `BLADE_HEIGHT_MIN` | 6.0 | DIP | §4, §5 |
| `BLADE_HEIGHT_MAX` | 30.0 | DIP | §4, §5 |
| `BLADE_THICKNESS_MIN` | 1.0 | DIP | §4, §5 |
| `BLADE_THICKNESS_MAX` | 2.5 | DIP | §4, §5 |
| `STIFFNESS_MIN` | 0.6 | (unitless) | §4, §5 |
| `STIFFNESS_MAX` | 1.0 | (unitless) | §4, §5 |
| `PALETTE_SIZE` | 6 | colors | §4 |
| `BASE_SWAY_SPEED` | π / 3 ≈ 1.0471975511965976 | rad/sec | §6 |
| `BASE_AMPLITUDE` | 3.3 | DIP | §6 |
| `DECAY_RATE` | 2.5 | /sec | §6 |
| `GUST_TO_LEAN_FACTOR` | 0.75 | DIP·sec/rad | §6, §8 |
| `MAX_CURSOR_SPEED` | 4000.0 | DIP/sec | §8 |
| `IMPULSE_SCALE` | 0.003 | rad/DIP | §8 |
| `GUST_RADIUS` | 150.0 | DIP | §8 |
| `CUT_RADIUS` | 30.0 | DIP | §9 |
| `CUT_DURATION_SEC` | 0.2 | sec | §9 |
| `REGROW_DELAY_MIN` | 30.0 | sec | §4, §5, §9 |
| `REGROW_DELAY_MAX` | 90.0 | sec | §4, §5, §9 |
| `REGROW_DURATION_MIN` | 2.0 | sec | §4, §5, §9 |
| `REGROW_DURATION_MAX` | 4.0 | sec | §4, §5, §9 |
| `REGROW_PRNG_SALT` | `0xDEADBEEFCAFEBABE` | uint64 | §5 |
| `FLOWER_PROBABILITY` | 0.04 | (unitless) | §5 |
| `FLOWER_HEIGHT_BONUS_MIN` | 1.2 | (unitless) | §4, §5 |
| `FLOWER_HEIGHT_BONUS_MAX` | 1.5 | (unitless) | §4, §5 |
| `FLOWER_HEAD_RADIUS_MIN` | 1.8 | DIP | §4, §5, §7 |
| `FLOWER_HEAD_RADIUS_MAX` | 3.0 | DIP | §4, §5, §7 |
| `FLOWER_PALETTE_SIZE` | 6 | colors | §4, §5 |
| `FLOWER_PRNG_SALT` | `0xC0FFEEFACE0FFE5` | uint64 | §5 |
| `MUSHROOM_PROBABILITY` | 0.025 | (unitless) | §5 |
| `MUSHROOM_CAP_WIDTH_MIN` | 4.0 | DIP | §4, §5, §7 |
| `MUSHROOM_CAP_WIDTH_MAX` | 8.0 | DIP | §4, §5, §7 |
| `MUSHROOM_CAP_HEIGHT_MIN` | 2.5 | DIP | §4, §5, §7 |
| `MUSHROOM_CAP_HEIGHT_MAX` | 5.0 | DIP | §4, §5, §7 |
| `MUSHROOM_STEM_HEIGHT_MIN` | 4.0 | DIP | §4, §5, §7 |
| `MUSHROOM_STEM_HEIGHT_MAX` | 10.0 | DIP | §4, §5, §7 |
| `MUSHROOM_STEM_THICKNESS_MIN` | 2.0 | DIP | §4, §5, §7 |
| `MUSHROOM_STEM_THICKNESS_MAX` | 4.0 | DIP | §4, §5, §7 |
| `MUSHROOM_PALETTE_SIZE` | 6 | colors | §4, §5 |
| `MUSHROOM_PRNG_SALT` | `0xBADC0FFEE0FACE21` | uint64 | §5 |
| `MUSHROOM_STEM_COLOR` | `0xFFF5F5DC` | uint32 ARGB | §4, §7 |
| `AMBIENT_GUST_PRNG_SALT` | `0xB7EE2EE2B7EE2EE2` | uint64 | §8.1 |
| `AMBIENT_GUST_INTERVAL_MIN` | 5.0 | sec | §8.1 |
| `AMBIENT_GUST_INTERVAL_MAX` | 15.0 | sec | §8.1 |
| `AMBIENT_GUST_MAG_FACTOR_MIN` | 0.3 | (unitless) | §8.1 |
| `AMBIENT_GUST_MAG_FACTOR_MAX` | 0.6 | (unitless) | §8.1 |
| `AMBIENT_GUST_RADIUS_FACTOR` | 0.5 | (unitless) | §8.1 |
| `SCENE_DEFAULT` | `Grass` (= 0) | enum | §13 |
| `DESERT_PALETTE` | 6 ARGB; see below | uint32[] | §13 |
| `WINTER_PALETTE` | 6 ARGB; see below | uint32[] | §13 |
| `MAX_ENTITIES_PER_MONITOR` | 64 | count | §13.2 |
| `CACTUS_PROBABILITY` | 0.005 | (unitless) | §14 |
| `CACTUS_HEIGHT_MIN` | 30.0 | DIP | §14 |
| `CACTUS_HEIGHT_MAX` | 70.0 | DIP | §14 |
| `CACTUS_WIDTH_MIN` | 8.0 | DIP | §14 |
| `CACTUS_WIDTH_MAX` | 14.0 | DIP | §14 |
| `CACTUS_ARM_PROBABILITY` | 0.55 | (unitless) | §14 |
| `CACTUS_TWO_ARM_PROBABILITY` | 0.35 | (unitless) | §14 |
| `CACTUS_COLOR` | `0xFF2D7A2D` | uint32 ARGB | §14 |
| `CACTUS_PRNG_SALT` | `0xCAC75CAC75CAC75C` | uint64 | §14 |
| `TUMBLEWEED_COUNT_PER_1920DIP` | 4 | count | §14 |
| `TUMBLEWEED_SIZE_MIN` | 8.0 | DIP | §14 |
| `TUMBLEWEED_SIZE_MAX` | 18.0 | DIP | §14 |
| `TUMBLEWEED_SPEED_MIN` | 30.0 | DIP/sec | §14 |
| `TUMBLEWEED_SPEED_MAX` | 90.0 | DIP/sec | §14 |
| `TUMBLEWEED_Y_OFFSET_MIN` | 8.0 | DIP above groundY | §14 |
| `TUMBLEWEED_Y_OFFSET_MAX` | 20.0 | DIP above groundY | §14 |
| `TUMBLEWEED_COLOR` | `0xFF8A6A3D` | uint32 ARGB | §14 |
| `TUMBLEWEED_PRNG_SALT` | `0x7B0117CA7B0117CA` | uint64 | §14 |
| `SNOWFLAKE_EMIT_RATE_PER_1920DIP` | 8.0 | flakes/sec | §15 |
| `SNOWFLAKE_FALL_SPEED_MIN` | 20.0 | DIP/sec | §15 |
| `SNOWFLAKE_FALL_SPEED_MAX` | 40.0 | DIP/sec | §15 |
| `SNOWFLAKE_SIZE_MIN` | 1.5 | DIP | §15 |
| `SNOWFLAKE_SIZE_MAX` | 3.0 | DIP | §15 |
| `SNOWFLAKE_SWAY_AMPLITUDE` | 10.0 | DIP | §15 |
| `SNOWFLAKE_SWAY_FREQUENCY` | 0.6 | Hz | §15 |
| `SNOWFLAKE_LIFETIME_PADDING_SEC` | 2.0 | sec | §15 |
| `SNOWFLAKE_COLOR` | `0xFFFFFFFF` | uint32 ARGB | §15 |
| `SNOWFLAKE_PRNG_SALT` | `0xC0FFEE1CECAFEBAB` | uint64 | §15 |
| `RAINDROP_PRNG_SALT` | `0xD40F0A1DD40F0A1D` | uint64 ("rain drop" mnemonic) | §20 |
| `RAINDROP_EMIT_RATE_PER_1920DIP` | 6.0 | drops/sec | §20 |
| `RAINDROP_LENGTH_MIN` | 4.0 | DIP | §20 |
| `RAINDROP_LENGTH_MAX` | 7.0 | DIP | §20 |
| `RAINDROP_THICKNESS` | 0.9 | DIP | §20 |
| `RAINDROP_FALL_SPEED_MIN` | 240.0 | DIP/sec | §20 |
| `RAINDROP_FALL_SPEED_MAX` | 360.0 | DIP/sec | §20 |
| `RAINDROP_DRIFT_MIN` | -8.0 | DIP/sec | §20 |
| `RAINDROP_DRIFT_MAX` | 8.0 | DIP/sec | §20 |
| `RAINDROP_COLOR` | `0x88B0C4D0` | uint32 ARGB | §20 |
| `RAINDROP_LIFETIME_PADDING_SEC` | 0.3 | sec | §20 |
| `SNOW_TIP_RADIUS_FACTOR` | 1.25 | × `blade.thickness` | §15 |
| `SNOW_TIP_COLOR` | `0xFFFFFFFF` | uint32 ARGB | §15 |
| `DESERT_GRASS_HEIGHT_SCALE` | 0.5 | (unitless) | §14 |
| `WINTER_GRASS_HEIGHT_SCALE` | 0.5 | (unitless) | §15 |
| `PINE_PROBABILITY` | 0.0075 | (unitless) | §15.1 |
| `PINE_HEIGHT_MIN` | 45.0 | DIP | §15.1 |
| `PINE_HEIGHT_MAX` | 90.0 | DIP | §15.1 |
| `PINE_WIDTH_MIN` | 28.0 | DIP | §15.1 |
| `PINE_WIDTH_MAX` | 48.0 | DIP | §15.1 |
| `PINE_TIER_COUNT_MIN` | 2 | (count) | §15.1 |
| `PINE_TIER_COUNT_MAX` | 4 | (count) | §15.1 |
| `PINE_TIP_TAPER` | 0.25 | (unitless) | §15.1 |
| `PINE_TIER_OVERLAP` | 0.15 | (unitless) | §15.1 |
| `PINE_SNOW_CAP_FRACTION` | 0.30 | (unitless) | §15.1 |
| `PINE_COLOR` | `0xFF1B5E20` | uint32 ARGB | §15.1 |
| `PINE_PRNG_SALT` | `0x50494E4550494E45` | uint64 ("PINEPINE" packed) | §15.1 |
| `BIRCH_VARIANT_PROBABILITY` | 0.30 | (unitless) | §15.1 |
| `BIRCH_TRUNK_WIDTH_MIN` | 4.0 | DIP | §15.1 |
| `BIRCH_TRUNK_WIDTH_MAX` | 7.0 | DIP | §15.1 |
| `BIRCH_BARK_MARK_COUNT` | 5 | (count) | §15.1 |
| `BIRCH_BARK_MARK_LENGTH_FRAC` | 0.50 | (unitless) | §15.1 |
| `BIRCH_BRANCH_COUNT` | 6 | (count) | §15.1 |
| `BIRCH_SNOW_CAP_FRACTION` | 0.18 | (unitless) | §15.1 |
| `BIRCH_BARK_COLOR` | `0xFFEFEFE6` | uint32 ARGB | §15.1 |
| `BIRCH_MARK_COLOR` | `0xFF2A2A28` | uint32 ARGB | §15.1 |
| `CUT_STUMP_THRESHOLD` | 0.05 | (unitless) | §7 |
| `STUMP_HEIGHT` | 2.0 | DIP | §7 |
| `MUSHROOM_STUMP_HEIGHT` | 4.0 | DIP | §7 |
| `CTRL_OFFSET_FACTOR` | 0.6 | (unitless) | §7 |
| `MAX_LEAN_FRACTION` | 0.95 | (unitless) | §7 |
| `CURSOR_REINIT_GAP_SEC` | 0.25 | sec | §8 |
| `CANONICAL_TEST_SEED` | `0x6B6173746F` | uint64 | §12 |

The palette table from §4 (six ARGB values for grass blades), the Flower palette (six ARGB values for flower heads), and the Mushroom palette (six ARGB values for caps + one `MUSHROOM_STEM_COLOR` for stems) are also part of this constants set. §13 adds two further palette tables (Desert blade palette + Winter blade palette) of six ARGB values each.

### Scene blade palettes (§13)

```
DESERT_PALETTE[6] = {
    0xFFC9A26B,   // 0 dried-grass tan
    0xFFB48A56,   // 1 warm sand
    0xFFD9B57A,   // 2 light dune
    0xFF8F6E3F,   // 3 dust brown
    0xFFE6C896,   // 4 pale beige
    0xFFA67843,   // 5 burnt sienna
};

WINTER_PALETTE[6] = {
    0xFFE8EEF5,   // 0 frost white
    0xFFB7C4D2,   // 1 cool silver
    0xFFCBD8E5,   // 2 pale ice
    0xFFD7E2EE,   // 3 light snow
    0xFFA8B7C6,   // 4 winter slate
    0xFFEEF3F8,   // 5 hoarfrost
};
```

Grass blades render at the same `blade.hue` indices in every scene; only the palette table changes.

---

## 13. Scenes (infrastructure)

The simulation supports a small fixed set of visual "scenes" that share the same blade generation, sway physics, gust/cut model, and ambient-gust scheduler from §§4–10 + §8.1, but differ in render-time presentation. The four-scene cycle is **Grass / Desert / Winter / Autumn**. The infrastructure pass swaps the blade palette per scene; scene-specific sections (§14 Desert, §15 Winter, §16.5 Autumn) add entities and props on top.

### Scene enum

```
enum Scene : uint8_t {
    Grass  = 0,   // default; the original green field from §§4–10
    Desert = 1,   // dried-grass / sand palette; cacti & tumbleweeds in §14
    Winter = 2,   // frosted / snowy palette; snowflakes in §15
    Autumn = 3,   // warm orange/red/yellow palette; leaves and maples in §16.5
};
constexpr int SCENE_COUNT = 4;
```

Both impls MUST use these exact discriminant values so the cross-impl conformance tests in §12 can compare an integer scene id.

### Per-scene blade palette

Each scene defines a 6-color ARGB palette indexed by `blade.hue`. The `hue` index is still drawn from the main PRNG stream as per §5 — generation is scene-independent — but the renderer selects which palette table to look up based on `sim.currentScene`:

```c
uint32_t argb = SCENE_PALETTE[sim->currentScene][blade.hue];
```

The Grass palette is the original §4 palette (unchanged). Desert, Winter, and Autumn palettes are listed in §11 / §16.5.

### Sim state

The `Sim` carries one new field:

```
Scene currentScene = Grass;     // default at sim_init
```

`set_scene(Sim*, Scene)` is a pure state update: assign the field and return. In the infrastructure pass it has no other side effect — the renderer reads `currentScene` at draw time. Later scene sections may use this hook to (re-)generate scene-specific entities.

`sim_init` initialises `currentScene = Grass` regardless of seed.

### Tray menu

Both impls expose scene selection via the system tray icon. The menu structure:

```
DesktopGrass tray
├── Scene  ▸
│           ●  Grass
│           ○  Desert
│           ○  Winter
│           ○  Autumn
└── Quit
```

The active scene shows a radio-style mark (`MFS_CHECKED` / `MF_BYCOMMAND` in Win32; `Checked = true` in WinForms `ToolStripMenuItem`). Clicking another scene item is an atomic broadcast to every monitor window: `set_scene(sim, newScene)` runs on each `Sim` before the next frame.

### Cross-impl conformance

- The `Scene` enum values are exactly `{ Grass = 0, Desert = 1, Winter = 2, Autumn = 3 }` in both impls.
- The Desert, Winter, and Autumn blade palette tables are bit-identical between impls.
- `sim_init` sets `currentScene = Grass`.
- `set_scene` updates the field and is a no-op on the §5 generation streams: the §12 first-blade snapshot stays unchanged regardless of scene.
- Scene state is persisted across launches as part of app state (§18); missing or incompatible state still falls back to `Grass`.

### Defaults

Constants and palette tables land in §11 under "Scenes".

---

## 13.1 Scene-aware generation (amendment to §13)

§13 introduced `set_scene` as a pure state-only update. With the per-scene content sections below (§14 Desert, §15 Winter), `set_scene` additionally:

1. Clears `sim.entities` (see §13.2).
2. Calls the new scene's entity generators (`generate_cacti` is a slot-bound blade-variant operation — it touches `sim.blades` and uses `CACTUS_PRNG_SALT`; `generate_tumbleweeds` and the snowflake emitter populate `sim.entities` from `TUMBLEWEED_PRNG_SALT` / `SNOWFLAKE_PRNG_SALT`).
3. Leaves the §5 main blade stream **strictly** untouched: blade slot positions, heights, thicknesses, hues, and the flower/mushroom variant tags are bit-identical across scenes. Cacti are written **on top of** existing blade slots and only replace the `isFlower`/`isMushroom`/grass tag of slots that satisfy the cactus probability draw — they do not shift slot positions or perturb the main stream.

`sim_init` still defaults `currentScene = Grass` and, since the Grass scene has no roamer or slot-overlay generators, `sim.entities` is empty at startup. The §12 first-blade snapshot test must continue to assert the §5 outputs only — entities are a separate conformance surface (§14, §15).

## 13.2 Roaming-entity subsystem

§13 had no notion of entities that move independently of the blade slots. Desert, Winter, and Grass weather now introduce tumbleweeds, snowflakes, and raindrops, so the simulation carries an `entities` vector alongside `blades`.

### Entity model

```cpp
enum EntityKind : uint8_t {
    EntityNone       = 0,
    EntityTumbleweed = 1,
    EntitySnowflake  = 2,
    EntitySheep      = 3,
    EntityCat        = 4,
    // 5 retired (Raindrop — rain effect removed); discriminant left as a gap.
    EntityBunny      = 6,
    EntityButterfly  = 7,
    EntityFirefly    = 8,
    EntityBird       = 9,
    EntityHedgehog   = 10,
    EntityLeaf       = 11,
};

struct Entity {
    EntityKind kind;
    double x, y;             // current position (window-local DIP)
    double vx, vy;           // current velocity (DIP/sec)
    double size;             // radius (DIP)
    double rotation;         // current rotation (radians)
    double rotationSpeed;    // rad/sec
    double age;              // sec since spawn
    double lifetime;         // sec until removal; ≤ 0 means infinite (respawn-in-place)
    uint32_t seed;           // per-entity render seed (for procedural detail)
};
```

Both impls use the exact `EntityKind` discriminants above for cross-impl conformance.

### Storage and lifecycle

- `sim.entities` is a growable container of `Entity` (Native: `std::vector`; Win2D: `List<Entity>` — both pre-reserved to `MAX_ENTITIES_PER_MONITOR` at sim_init to avoid grow churn).
- Generated by `set_scene` for hard scene entities and by per-scene emitters for soft weather/ambient particles (Grass raindrops, Winter snowflakes, Autumn leaves).
- Updated every tick by `sim_tick_entities` (Native) / `TickEntities` (Win2D), called after blade physics in the §10 update order.
- Rendered after blades in the renderer (`DrawEntities` / equivalent).
- Capped at `MAX_ENTITIES_PER_MONITOR` — the snowflake emitter MUST not exceed this cap (it stops emitting and resumes when count drops).

### Tick update

```
for each entity e in sim.entities:
    e.x += e.vx * dt
    e.y += e.vy * dt
    e.rotation += e.rotationSpeed * dt
    e.age += dt
    if e.kind == Tumbleweed:
        if e.x < -e.size or e.x > monitorWidth + e.size:
            // respawn at opposite edge with new random params
            respawn_tumbleweed(e, sim.tumbleweedPrng, sim.monitorWidth, groundY)
    else if e.kind == Snowflake:
        // sway: vx wobble around its mean drift
        e.vx = baseVx + SNOWFLAKE_SWAY_AMPLITUDE * sin(e.age * 2π * SNOWFLAKE_SWAY_FREQUENCY + e.seed)

remove entities where e.lifetime > 0 and e.age >= e.lifetime
also remove snowflakes that pass below groundY
emit new snowflakes per §15 schedule, raindrops per §20 schedule, and leaves per §16.5 schedule
```

The emitter and respawn paths are the only places entities are added/removed during a tick.

### Cross-impl conformance for entities

- `EntityKind` discriminants `{None=0, Tumbleweed=1, Snowflake=2, Sheep=3, Cat=4, Bunny=6, Butterfly=7, Firefly=8, Bird=9, Hedgehog=10, Leaf=11}` match exactly (value `5`, formerly `Raindrop`, is a retired gap).
- Entity-stream PRNG salts (`TUMBLEWEED_PRNG_SALT`, `SNOWFLAKE_PRNG_SALT`, `RAINDROP_PRNG_SALT`, `CACTUS_PRNG_SALT`, `LEAF_PRNG_SALT`, `MAPLE_PRNG_SALT`) are global constants — both impls draw entity parameters from streams seeded `seed XOR salt`.
- A new conformance test class (`entity_tests.cpp` / `EntityTests.cs`) asserts the first tumbleweed for `CANONICAL_TEST_SEED + Desert + monitorWidth=1920` matches a pinned (`x`, `y`, `vx`, `size`) snapshot derivable from the spec, and the first snowflake at `t = 0.5s` (after exactly one tick at 0.5s) matches a pinned snapshot.
- Blade conformance (§12) is unchanged: `sim.blades` for any seed is bit-identical regardless of `currentScene`.

---

## 14. Desert scene

The Desert scene replaces the green grass field with a dried, dusty band of sandy-tone blades (palette swap from §13), adds tall **cacti** as slot-bound blade variants, and adds **tumbleweeds** as roaming entities that roll across the strip.

### Cacti (slot-bound)

Cacti use the existing per-blade-slot variant tag pattern from §4 (alongside `isFlower` and `isMushroom`). On `set_scene(Desert)`:

1. A new PRNG stream `cactusPrng = Prng(seed XOR CACTUS_PRNG_SALT)` is created.
2. For each blade slot `i` (in slot order, deterministic):
   - Draw `r = cactusPrng.uniform(0, 1)`. If `r < CACTUS_PROBABILITY` (≈0.5%, so ~3 cacti per 600-blade strip), promote the slot to a cactus:
     - Clear `isFlower`, `isMushroom`.
     - Set `isCactus = true`.
     - Draw `cactusHeight = cactusPrng.uniform(CACTUS_HEIGHT_MIN, CACTUS_HEIGHT_MAX)` — cacti are taller than grass (30–70 DIP vs 6–30).
     - Draw `cactusWidth = cactusPrng.uniform(CACTUS_WIDTH_MIN, CACTUS_WIDTH_MAX)`.
     - Draw `cactusArmDraw = cactusPrng.uniform(0, 1)`:
       - `cactusArmDraw < (1 - CACTUS_ARM_PROBABILITY)` → type 0 (single column, no arms)
       - else `cactusArmDraw < (1 - CACTUS_ARM_PROBABILITY) + CACTUS_TWO_ARM_PROBABILITY * CACTUS_ARM_PROBABILITY` → type 2 (saguaro, two arms)
       - else → type 1 (single arm, random side)
     - Draw `cactusArmSide = (cactusPrng.uniform(0, 1) < 0.5) ? -1 : +1` for type 1.

Cacti are **static** (no sway, no gust response). They are still cuttable: clicks within `CUT_RADIUS` cut the cactus to a stump using the same `cutHeight` animation model as grass (§9). Regrowth uses the same `regrow*` fields — a cut cactus regrows over `REGROW_DURATION_*` after `REGROW_DELAY_*`.

### Cactus rendering

Cacti render as Bezier-thick vertical strokes in `CACTUS_COLOR`:

```
type 0 (column):
    DrawLine(baseX, groundY, baseX, groundY - cactusHeight * cutHeight, cactusWidth)
    DrawEllipse rounded cap at top

type 1 (single arm):
    column as type 0
    arm: from (baseX, groundY - cactusHeight*0.4) curving out to
         (baseX + cactusArmSide * cactusWidth*1.5, groundY - cactusHeight*0.7)
         with a small vertical tip
    arm width = cactusWidth * 0.7

type 2 (saguaro):
    column as type 0
    arm on each side, mirrored, same curve as type 1 with cactusArmSide = ±1
```

All cactus rendering scales with `cutHeight` like grass. Stump short-circuit applies when `cutHeight < CUT_STUMP_THRESHOLD`: draw a short `STUMP_HEIGHT` vertical stub in `CACTUS_COLOR`.

### Tumbleweeds (roaming entities)

On `set_scene(Desert)`, generate `floor(monitorWidth / 1920.0 * TUMBLEWEED_COUNT_PER_1920DIP)` tumbleweeds (with a minimum of 1 if `monitorWidth >= 480`). For each:

```
tumbleweedPrng = Prng(seed XOR TUMBLEWEED_PRNG_SALT)
for i in 0..count-1:
    e.kind = Tumbleweed
    e.size = tumbleweedPrng.uniform(TUMBLEWEED_SIZE_MIN, TUMBLEWEED_SIZE_MAX)
    e.x    = tumbleweedPrng.uniform(0, monitorWidth)
    e.y    = groundY - tumbleweedPrng.uniform(TUMBLEWEED_Y_OFFSET_MIN, TUMBLEWEED_Y_OFFSET_MAX)
    speed  = tumbleweedPrng.uniform(TUMBLEWEED_SPEED_MIN, TUMBLEWEED_SPEED_MAX)
    e.vx   = (tumbleweedPrng.uniform(0, 1) < 0.5 ? -1 : +1) * speed
    e.vy   = 0
    e.rotation      = tumbleweedPrng.uniform(0, 2π)
    e.rotationSpeed = e.vx / e.size           // rolling-without-slipping convention
    e.age           = 0
    e.lifetime      = -1                      // infinite — respawn on edge
    e.seed          = tumbleweedPrng.next_u32()
```

When a tumbleweed rolls off-screen (`e.x < -e.size - 10` or `e.x > monitorWidth + e.size + 10`), respawn at the **opposite** edge with new random `size`, `y`, `speed`, and `vx` — `vx` keeps the entry direction (positive if entering from the left, negative if entering from the right) so the new tumbleweed visibly enters the strip.

Tumbleweeds receive ambient gust impulses (§8.1) as a transient `vx` boost in the gust direction:

```
on ambient gust at (gx, gy, signDir, magFactor):
    for each tumbleweed e where |e.x - gx| < GUST_RADIUS * AMBIENT_GUST_RADIUS_FACTOR:
        e.vx += signDir * 30.0 * magFactor  // small additive nudge
```

(This is optional — both impls MAY skip this nudge in v1; the conformance test will not assert it. Mark in code as "Phase 3.1".)

### Tumbleweed rendering

Render as a small spiky tangle: 5 concentric arc strokes at angles `e.rotation + k * (2π/5)` for `k ∈ [0, 5)`, each from inside `e.size * 0.3` to outside `e.size * 0.95`, in `TUMBLEWEED_COLOR`, stroke width `1.0` DIP.

### Tray icon

The "Scene ▸ Desert" tray item already exists (§13). Selecting Desert calls `set_scene(Desert)`, which triggers cactus + tumbleweed generation per the rules above.

### Conformance

- The first tumbleweed for `CANONICAL_TEST_SEED + monitorWidth=1920 + Desert` matches a pinned snapshot (`x`, `y`, `vx`, `size`) derivable in tests by stepping a `Prng(CANONICAL_TEST_SEED XOR TUMBLEWEED_PRNG_SALT)` through the documented draw order.
- The number of cacti for the canonical seed + 1920 DIP width is pinned (≈3, exact value asserted from the spec-derived draw count).
- Cacti never regenerate on tick (only on `set_scene`).
- Switching scenes back to Grass clears entities AND restores the original `isFlower`/`isMushroom` slot tags. Implementations MAY achieve this by storing the original slot variants in a parallel `originalVariants[]` array at sim_init, or by re-running blade generation with the main stream + flower/mushroom streams on every `set_scene` back to Grass — both yield bit-identical blade outputs because the streams are pure functions of the seed.

---

## 15. Winter scene

> **Render note (2026-06-04):** Winter renders as **snow-tipped grass** plus the pine/birch treeline, as described in this section. The sculpted snowbank/snowscape of §15.2 (accumulation band), §15.3 (carve), and §15.6 (lower bank / wind spindrift) is **no longer drawn** — it looked off and was the dominant per-frame CPU cost. The underlying Sim state (`snowDepth` accumulation, `snowCarve`, snow-drift cursor-move puffs) is still computed, persisted, and unit-tested, but the only visible Winter interaction is the **click snow puff** (§21). `DrawSnowLayer` / `SnowBankDepthAt` remain in the renderers as currently-uncalled code, and the tree base-burial offset (§15.2) is not applied at render time. The sections below document the snowbank machinery for reference and in case it is revived.

The Winter scene swaps to the frosty palette (§13), emits **snowflakes** continuously as drifting roaming entities, and adds **snow-tipped blade caps** as a pure render-time effect over the existing blade vector. The biome also shrinks ordinary blade height and suppresses mushrooms so the snow caps + pines (§15.1) read as the dominant features.

### Biome-shaped blade rendering

While `sim.currentScene == Winter`, `compute_blade_stroke` multiplies the per-blade effective length `L` by `WINTER_GRASS_HEIGHT_SCALE` (0.5) for every blade that is **not** a pine and **not** a mushroom. This mirrors the Desert convention (`DESERT_GRASS_HEIGHT_SCALE`, applied to non-cactus non-mushroom blades) so cacti / pines read as the dominant biome feature. Cut state, sway, and snapshot reproducibility are unaffected — only the rendered geometry is shorter.

### Mushroom suppression

Mushrooms do not fit a snowy, cold biome. As part of the pine generation pass invoked from `set_scene(Winter)`, every blade has `isMushroom` forced to `false` (regardless of its `originalIsMushroom`). Switching back to `Grass` restores the original mushroom flags through the universal `restore_original_variants` path.

### Snowflakes (continuous emission)

On `set_scene(Winter)`, initialize the snowflake emitter:

```
snowflakePrng = Prng(seed XOR SNOWFLAKE_PRNG_SALT)
sim.nextSnowflakeSpawnTime = sim.globalTime
                           + snowflakePrng.exponential(SNOWFLAKE_EMIT_RATE_PER_1920DIP * monitorWidth / 1920.0)
```

Where `exponential(λ)` draws from an exponential distribution with rate λ — implemented as `-ln(1 - prng.uniform(0, 1)) / λ`. This produces Poisson-distributed inter-arrival times so the snowfall feels organic without a fixed cadence.

On each tick (after blade and tumbleweed updates):

```
while sim.globalTime >= sim.nextSnowflakeSpawnTime
   and len(sim.entities) < MAX_ENTITIES_PER_MONITOR:
    e.kind = Snowflake
    e.size = snowflakePrng.uniform(SNOWFLAKE_SIZE_MIN, SNOWFLAKE_SIZE_MAX)
    e.x    = snowflakePrng.uniform(-20, monitorWidth + 20)
    e.y    = -e.size - 4
    fallSpeed = snowflakePrng.uniform(SNOWFLAKE_FALL_SPEED_MIN, SNOWFLAKE_FALL_SPEED_MAX)
    e.vx   = 0
    e.vy   = fallSpeed
    e.rotation      = snowflakePrng.uniform(0, 2π)
    e.rotationSpeed = snowflakePrng.uniform(-1.5, 1.5)
    e.age           = 0
    e.lifetime      = (groundY + e.size) / fallSpeed + SNOWFLAKE_LIFETIME_PADDING_SEC
    e.seed          = snowflakePrng.next_u32()
    sim.entities.push(e)
    sim.nextSnowflakeSpawnTime += snowflakePrng.exponential(SNOWFLAKE_EMIT_RATE_PER_1920DIP * monitorWidth / 1920.0)
```

Per-fire draw order (5 draws): `size`, `x`, `fallSpeed`, `rotation`, `rotationSpeed`. Then the `seed` draw (6th) for render variance. Then the next-spawn interval draw (7th). Both impls MUST follow this exact draw order for cross-impl bit-identity of the first-snowflake snapshot.

The emitter is capped by `MAX_ENTITIES_PER_MONITOR`: when the cap is hit, the `while` loop exits (the missed spawn slot is "lost" — `nextSnowflakeSpawnTime` is not advanced). Once capacity opens up next tick, the next scheduled spawn fires.

### Snowflake tick

Already covered by the general entity tick in §13.2. The sway formula:

```
e.vx = SNOWFLAKE_SWAY_AMPLITUDE * SNOWFLAKE_SWAY_FREQUENCY * 2π
       * cos(e.age * 2π * SNOWFLAKE_SWAY_FREQUENCY + e.seed / 4294967295.0 * 2π)
```

This gives a horizontal velocity that oscillates with amplitude proportional to `SNOWFLAKE_SWAY_AMPLITUDE * SNOWFLAKE_SWAY_FREQUENCY * 2π` ≈ 37.7 DIP/sec peak, with each flake phase-offset by its `seed`. The vertical fall speed `e.vy` stays constant per flake.

Snowflakes also receive ambient gust impulses (§8.1) as a transient `vx` offset within the gust radius — same pattern as tumbleweeds, with magnitude `signDir * 40.0 * magFactor`. (Same Phase 3.1 caveat — optional in v1.)

### Snowflake rendering

Render each snowflake as a small filled circle of radius `e.size` in `SNOWFLAKE_COLOR`. Phase 3.2 (optional) may replace the circle with a 6-line star (`size * 1.5` line length from center) for snowflake-ish detail. v1 ships circles.

### Snow-tipped blades (render-only)

When `sim.currentScene == Winter`, after drawing each non-cactus, non-mushroom blade in `DrawGrass`, additionally draw a small filled circle at the tip:

```
if sim.currentScene == Winter and !blade.isMushroom and !blade.isCactus
   and blade.cutHeight >= CUT_STUMP_THRESHOLD:
    radius = blade.thickness * SNOW_TIP_RADIUS_FACTOR
    DrawFilledCircle(tipX, tipY, radius, SNOW_TIP_COLOR)
```

The cap scales with `blade.thickness` (so thicker blades look frostier) and disappears when the blade is cut to a stump (mirrors how the flower head disappears on cut). No model changes — purely a renderer branch on `currentScene`.

Flowers in Winter render their flower head as normal (the snow tip caps on top of the head); this is acceptable v1 behavior. Mushrooms are unchanged (their domes are visually distinct enough that snow caps would muddy them).

### Conformance

- The first snowflake spawned with `CANONICAL_TEST_SEED + monitorWidth=1920 + Winter`, after stepping the sim forward one tick at `dt = 0.5s` from `t = 0`, matches a pinned `(size, x, fallSpeed, rotation, rotationSpeed)` snapshot derivable in tests from a side `Prng(CANONICAL_TEST_SEED XOR SNOWFLAKE_PRNG_SALT)`.
- `MAX_ENTITIES_PER_MONITOR` is honored: a stress test running 60 seconds of sim time with no entity removal MUST observe `len(sim.entities) ≤ MAX_ENTITIES_PER_MONITOR` at every tick.
- Switching scenes away from Winter clears snowflake entities and resets the snowflake emitter.

## 15.1 Pine trees (Winter slot-bound variant)

Pines are the Winter biome anchor — the structural counterpart to Desert cacti.
They are slot-bound (replace some grass slots), procedurally promoted on
`sim_set_scene(Winter)` from an independent PRNG stream, cuttable and
regrowable via the existing §9 model, and cleared on every non-Winter scene
transition via the same `restore_original_variants` path that clears cacti.

### Generation

Driven by `pinePrng = Prng(seed XOR PINE_PRNG_SALT)`. Iterate blade slots in
order, draw `r = pinePrng.uniform(0, 1)`. If `r >= PINE_PROBABILITY` the slot
is skipped (no further draws for that slot). Otherwise promote:

```
b.isPine        = true
b.isFlower      = false      // suppressed under tree
b.isMushroom    = false
variantDraw     = pinePrng.uniform(0, 1)
b.treeVariant   = (variantDraw < BIRCH_VARIANT_PROBABILITY) ? 1 : 0  // 0 = pine, 1 = birch
b.pineHeight    = pinePrng.uniform(PINE_HEIGHT_MIN, PINE_HEIGHT_MAX)
if b.treeVariant == 1:
    b.pineWidth = pinePrng.uniform(BIRCH_TRUNK_WIDTH_MIN, BIRCH_TRUNK_WIDTH_MAX)
else:
    b.pineWidth = pinePrng.uniform(PINE_WIDTH_MIN, PINE_WIDTH_MAX)
b.pineTierCount = floor(pinePrng.uniform(PINE_TIER_COUNT_MIN, PINE_TIER_COUNT_MAX + 1))
```

Per-fire draw order (5 draws on promotion: `r`, `variantDraw`, `pineHeight`,
`pineWidth`, `tierDraw`). Both impls MUST follow this exact order regardless
of the variant outcome — the tier-count draw happens for birch slots too even
though it is unused at render time. This preserves cross-impl bit-identity of
the per-tree PRNG state without branching the stream.

### Birch variant (treeVariant == 1)

A bare-winter birch: vertical off-white trunk (`BIRCH_BARK_COLOR`) of width
`pineWidth` ∈ [`BIRCH_TRUNK_WIDTH_MIN`, `BIRCH_TRUNK_WIDTH_MAX`), with
`BIRCH_BARK_MARK_COUNT` short centered dark dashes (`BIRCH_MARK_COLOR`,
max length `trunkW * BIRCH_BARK_MARK_LENGTH_FRAC`, individual lengths varied
by a fixed pattern for a "broken" bark look) distributed along the trunk,
and `BIRCH_BRANCH_COUNT` upward-angled branches arranged in a hand-tuned
asymmetric fan (angles 20°–60° from vertical, alternating sides at varying
heights and lengths) — each branch terminated with a small white snow puff
ellipse. A small flattened snow puff sits at the trunk apex. The intentional
asymmetry and upward angles break the cross/microphone-stand silhouette
that strict horizontal pair-branches would produce. Birches share the pine
cut/regrow + stump behavior.

### Rendering

Each pine is N stacked isoceles triangles (N = `pineTierCount`, 2..4). Tier
heights are equal (`pineHeight / N`). Tier widths taper from `pineWidth` at
the base tier to `pineWidth * PINE_TIP_TAPER` at the top tier — interpolated
linearly per tier. Adjacent tiers overlap by `PINE_TIER_OVERLAP` (fraction of
tier height) so the silhouette reads continuous.

Each tier carries a white snow cap — a smaller filled triangle covering the
top `PINE_SNOW_CAP_FRACTION` of the tier (0.30 by default). The cap inherits
the tier's base width and apex.

Cut model: total height scales by `cutHeight` (linear, same as grass and
cacti). Below `CUT_STUMP_THRESHOLD` the pine renders as a short brown stump
(see `STUMP_HEIGHT`).

### Scene clearing

`restore_original_variants(b)` is amended to also clear pine fields:
`isPine = false; pineTierCount = 0; pineHeight = 0; pineWidth = 0;`. Every
scene transition routes through this path (see §13.1 amendment), so pines
disappear on Winter→{Grass,Desert} the same way cacti disappear on
Desert→{Grass,Winter}.

### Snow-tipped blades interaction

The §15 snow-tipped-blade render branch must ALSO exclude pines (they carry
their own per-tier snow caps), so the existing predicate becomes:

```
if currentScene == Winter and !isMushroom and !isCactus and !isPine
   and cutHeight >= CUT_STUMP_THRESHOLD:
    draw snow tip
```

### Conformance

- For `CANONICAL_TEST_SEED + monitorWidth = 1920`, after
  `sim_set_scene(Winter)`, the first slot promoted to a pine has
  `(slotIndex, pineHeight, pineWidth, pineTierCount)` derivable from a side
  `Prng(CANONICAL_TEST_SEED XOR PINE_PRNG_SALT)` walking the same draw
  sequence.
- Scene→Grass (or Scene→Desert) clears `isPine` on every slot.
- Pine generation does NOT touch the §12 first-blade snapshot.

## 15.2 Snow accumulation (Winter passive layer)

Snow accumulation is Winter-only, always on, and ambient. Each monitor strip owns a single scalar `snowDepth` in DIP. It starts at `0` for fresh Winter sims, increases only while `currentScene == Winter`, and is clamped to `SNOW_DEPTH_MAX`. Switching to any non-Winter scene immediately resets the value to `0` (instant melt). Switching back to Winter starts from the loaded `state.json` value if the saved scene was Winter, otherwise `0`.

| Constant | Value | Meaning |
|---|---:|---|
| `SNOW_ACCUMULATION_RATE` | `0.012` | DIP/sec; about `0.72` DIP/min |
| `SNOW_DEPTH_MAX` | `6.0` | cap; reached in about 8 minutes |
| `SNOW_DEPTH_MIN_RENDER` | `0.3` | render threshold to avoid startup flicker |
| `SNOW_LAYER_COLOR_TOP` | `0xFFFFFFFF` | white upper band |
| `SNOW_LAYER_COLOR_BOTTOM` | `0xFFE8E8F0` | pale blue-grey lower band |
| `SNOW_LAYER_HIGHLIGHT` | `0xFFFFFFFF` | bright top-edge crest |
| `SNOW_TOP_UNDULATION_AMP` | `2.5` | DIP sinusoidal top-edge amplitude |
| `SNOW_TOP_UNDULATION_WAVELENGTH` | `90.0` | DIP top-edge wavelength |
| `SNOW_TOP_UNDULATION_PHASE_SALT` | `0x5E0A1` | per-monitor phase salt |

Tick model:

```text
if currentScene == Winter:
    snowDepth = min(snowDepth + SNOW_ACCUMULATION_RATE * dt, SNOW_DEPTH_MAX)
else:
    snowDepth = 0
```

The rendered top edge is monitor-stable and sampled at DIP x-coordinate:

```text
phase = hash(monitorKey XOR SNOW_TOP_UNDULATION_PHASE_SALT) * 2π
topYAt(x) = min(groundY,
                groundY - snowDepth
                + sin(x / SNOW_TOP_UNDULATION_WAVELENGTH * 2π + phase)
                  * SNOW_TOP_UNDULATION_AMP)
```

Winter render layering is: normal grass blades and blade ornaments → accumulated snow layer → pines/birches → scene entities (including snowflakes). The snow layer fills from the wavy top edge to `groundY` with a white-to-pale-blue-grey vertical gradient and a bright crest highlight. It covers the lower portion of grass without clipping around individual blades; cut blades read as naturally buried. Pines and birches are rendered after the layer and their base Y is raised by `max(0, snowDepth - SNOW_TOP_UNDULATION_AMP)`, capped at the max-depth-derived offset, so trunks read as partially buried rather than floating.

Snowflakes keep the existing PRNG spawn stream. In Winter, a snowflake despawns not only when it passes below `groundY`, but also when `flakeY >= topYAt(flakeX)` and `snowDepth > 0`; this makes flakes visually land on the accumulated layer without feeding per-flake counts back into accumulation.

Persistence schema bumps to v2. v2 monitor entries add `snowDepth`; v1 files load cleanly with missing snow depth defaulting to `0`, and all saves write v2.

---

## 15.3 Snow carve (Winter footprints)

Winter's tactile ground interaction, mirroring the grass cut-and-regrow loop. Clicking the snowbank presses a soft dent at the cursor that slowly settles back, so Winter has the same "leave a passing mark" feel as cut grass, desert cacti, and the autumn leaf-puff. A click both kicks up a snow puff (§21) **and** presses a dent.

State is a per-monitor transient heightfield `snowCarve` / `SnowCarve` — a fixed-length `SNOW_CARVE_BUCKETS` array of non-negative carved depths in DIP, mapped uniformly across `[0, monitorWidth]`. It is **never persisted**: it is zero-filled at sim init/regenerate and cleared on **every** scene change (including Winter→Winter re-entry).

| Constant | Value | Meaning |
|---|---:|---|
| `SNOW_CARVE_BUCKETS` | `192` | heightfield resolution across the monitor |
| `SNOW_CARVE_RADIUS_DIP` | `24.0` | dent half-width |
| `SNOW_CARVE_DEPTH_PER_CLICK` | `7.0` | depth pressed per click at the center |
| `SNOW_CARVE_MAX_DEPTH` | `11.0` | clamp so the bank never inverts |
| `SNOW_CARVE_REFILL_RATE` | `1.8` | DIP/sec settle-back (~6 s full refill) |

Apply (click-only; no hover/Move carving, to keep the scene calm and input-rate independent). Guarded to `currentScene == Winter`, finite `x`, `monitorWidth > 0`, and an initialized field:

```text
bucketWidth = monitorWidth / SNOW_CARVE_BUCKETS
for each bucket b:
    centerX = (b + 0.5) * bucketWidth
    dist    = |centerX - x|
    if dist < SNOW_CARVE_RADIUS_DIP:
        falloff   = 0.5 * (1 + cos(π * dist / SNOW_CARVE_RADIUS_DIP))   # raised cosine
        carve[b]  = min(carve[b] + SNOW_CARVE_DEPTH_PER_CLICK * falloff, SNOW_CARVE_MAX_DEPTH)
```

Decay runs in the Winter tick branch **before** per-frame input is processed, so a click delivered in the same frame lands its full dent (pinned by a cross-impl ordering test):

```text
carve[b] = max(0, carve[b] - SNOW_CARVE_REFILL_RATE * dt)   # never negative
```

Query interpolates between neighbouring bucket centers (`f = x / bucketWidth - 0.5`, clamped to the end buckets). The renderer subtracts the carve from the bank depth and clamps to `SNOW_BANK_MIN_DEPTH` so the bank never inverts; carved columns also get a cool recessed interior plus a thin raised-rim highlight along steep carve gradients so the dent reads as a scooped trench rather than just a lower ridge. The carve heightfield is input-only, so the no-input snapshot tests are unaffected.

---

## 15.4 Winter treeline depth (foreground / background pines)

Winter pines and birches are split into two depth layers so the treeline reads with dimension instead of one flat row. At generation, after the locked per-tree PRNG draws (`r` gate, variant, height, width, `tierCount`), **one final draw `depthDraw`** sets `treeBackground = depthDraw < TREE_BACKGROUND_PROBABILITY`. Because the draw is appended *last* in the locked order, all earlier per-tree attributes are unchanged and the pine snapshot tests still pass unmodified. The flag is reset to `false` in `restore_original_variants` / `RestoreOriginalVariants`, so non-winter scenes never carry it.

| Constant | Value | Meaning |
|---|---:|---|
| `TREE_BACKGROUND_PROBABILITY` | `0.45` | share of winter trees pushed to the background |
| `TREE_BG_SCALE` | `0.62` | background tree scale about its trunk base |
| `TREE_BG_OPACITY` | `0.78` | background tree haze (per-brush alpha) |

Render order becomes: blades → **background trees** → snowbank → **foreground trees** → entities. The tree pass takes `(treesOnly, backgroundTrees)`: background trees are scaled toward their trunk base (`Scale(TREE_BG_SCALE, …, center=(baseX, groundY)) * sway`) and every tree brush is dimmed to `TREE_BG_OPACITY` (reset to 1.0 before the next blade). Drawing the background pass *before* the snowbank lets the bank occlude the shorter trees' bases, selling the recession. Maples are never flagged background, so they always render in the foreground pass. All of this is render/generation-only aside from the appended PRNG draw, leaving deterministic snapshot behaviour intact.

---

## 15.5 Winter snow drift (cursor-move spindrift)

Winter's move-driven interaction, the analogue of the autumn leaf-puff hover. Brushing the cursor low and fast across the snowbank kicks up a small, gentle wisp of powder, so Winter responds to a passing cursor like grass (bend), desert (gust), and autumn (leaf-puff) — not only to clicks. It reuses the click snow-puff particle but with fewer, smaller, slower grains.

It lives in `sim_apply_move` / `ApplyCursorMove`, after the first-event guard and gust-band check, right after the cursor velocity (`capped`) is computed. It fires only when **all** of: `currentScene == Winter`; the cursor is in a low band `[groundY - SNOW_DRIFT_REACH_DIP, groundY]` near the snow surface; `|capped| >= SNOW_DRIFT_MIN_SPEED` (so a resting cursor never emits); and `globalTime >= snowDriftCooldownEnd`. Like the click puff, the full locked PRNG sequence is drawn per intended grain and only appended when capacity allows, so the stream is independent of the live entity count. The cooldown is armed on every qualifying burst (even when the entity cap was full) so a saturated pool can't spin at the OS move rate.

| Constant | Value | Meaning |
|---|---:|---|
| `SNOW_DRIFT_COUNT_MIN` / `MAX` | `4` / `8` | grains per wisp (vs 9–16 for a click) |
| `SNOW_DRIFT_REACH_DIP` | `70.0` | how near the ground the cursor must be |
| `SNOW_DRIFT_MIN_SPEED` | `90.0` | DIP/sec; only kicks up while moving |
| `SNOW_DRIFT_COOLDOWN_SEC` | `0.12` | global gate (~8 wisps/sec max) |
| `SNOW_DRIFT_SIZE_SCALE` | `0.9` | slightly smaller grains than a click burst |
| `SNOW_DRIFT_SPEED_SCALE` | `0.85` | slightly gentler upward kick |
| `SNOW_DRIFT_PRNG_SALT` | — | independent stream |

`make_snow_puff` / `MakeSnowPuff` gained optional post-draw multipliers `sizeScale`/`speedScale` (default `1.0`) applied **after** every PRNG draw, so the click puff's byte sequence is unchanged; drift passes `0.9`/`0.85`. The drift uses its own `snowDriftPrng` (salted `SNOW_DRIFT_PRNG_SALT`), so it never perturbs the click-puff or snowflake streams. `snowDriftCooldownEnd` resets to `0` on every scene change. Because it only emits from move events, the no-input snapshot tests are unaffected. The move handler now also rejects non-finite cursor coordinates up front (mirroring the click handler), preventing a stray NaN from poisoning the cursor baseline.

---

## 15.6 Winter snow visual redesign (lower bank, visible puffs, wind spindrift)

A pass to make Winter read as a calm snowscape rather than a tall white wall that buried the treeline. Three render/constant changes, no new Sim logic or PRNG draws:

1. **Lower bank.** `SNOW_BANK_BASE_DEPTH` 13→9, `SNOW_BANK_ROLL_AMP` 7→5, `SNOW_BANK_CORNICE_AMP` 11→5, and the accumulation cap `SNOW_DEPTH_MAX` 30→6. The sculpted bank (§15.4 background trees draw *before* it) now sits low enough to frame the pines instead of swallowing them. The accumulation-derived `snow_tree_base_y_offset` therefore tops out at `SNOW_DEPTH_MAX - SNOW_TOP_UNDULATION_AMP = 3.5`.
2. **Visible puffs.** Click (§21) and drift (§15.5) puffs were white ellipses on a white bank (zero contrast). Each puff now draws a cool-toned rim (the bank shadow color, scaled `SNOW_PUFF_SHADOW_SCALE`, offset down `SNOW_PUFF_SHADOW_OFFSET·r`, at `alpha·SNOW_PUFF_SHADOW_OPACITY`) behind a bright white core. Grains are also bigger (`SNOW_PUFF_SIZE` 3.5–8.0), faster (`SNOW_PUFF_BURST_SPEED` 70–150) and longer-lived (`SNOW_PUFF_LIFETIME` 1.0–1.8 s).
3. **Wind-blown spindrift (§21.3).** `SNOW_WIND_LANES` (7) faint streaks skim horizontally just above the crest, scrolling with `globalTime`, each with a stable length/height/phase from `splitmix64(0x57494E44 + lane·φ)` and a sine bob. Render-only and deterministic from time + lane index — no entities, Sim state, or PRNG-stream draws — so determinism and the no-input snapshots are untouched.

| Constant | Value | Meaning |
|---|---:|---|
| `SNOW_PUFF_SHADOW_SCALE` | `1.35` | rim disc size vs core |
| `SNOW_PUFF_SHADOW_OFFSET` | `0.45` | rim down-offset (× radius) |
| `SNOW_PUFF_SHADOW_OPACITY` | `0.55` | rim opacity (× puff alpha) |
| `SNOW_WIND_LANES` | `7` | number of spindrift streaks |
| `SNOW_WIND_SPEED` | `130.0` | DIP/sec base scroll speed |
| `SNOW_WIND_LENGTH_MIN` / `MAX` | `26.0` / `64.0` | streak length range |
| `SNOW_WIND_THICKNESS` | `2.2` | line thickness |
| `SNOW_WIND_HEIGHT_MIN` / `MAX` | `3.0` / `16.0` | DIP above crest |
| `SNOW_WIND_BOB_AMP` / `BOB_SPEED` | `3.0` / `1.1` | vertical bob |
| `SNOW_WIND_OPACITY` | `0.5` | max streak opacity |
| `SNOW_WIND_COLOR` | `0xFFFFFFFF` | white |

---

## 16.5 Autumn scene

Autumn is the fourth scene (`Scene::Autumn = 3`) and completes the Grass / Desert / Winter / Autumn cycle. It is intentionally passive and quiet: warm blade colors, falling leaves, and occasional slot-bound maple trees. There are no critters, birds, bugs, rain, snowflakes, or snow accumulation in Autumn; day-night tint still applies uniformly.

### Autumn blade palette

`AUTUMN_PALETTE` is pinned and exposed through `SCENE_PALETTES[3]`:

| Hue | ARGB | Meaning |
|---:|---|---|
| 0 | `0xFFD96B0C` | burnt orange |
| 1 | `0xFFB54D1E` | deep rust |
| 2 | `0xFFE89A3C` | warm amber |
| 3 | `0xFFC23E12` | vibrant red-orange |
| 4 | `0xFFD9A65C` | honey-gold |
| 5 | `0xFF8C2E0F` | dark maroon |

### Falling leaves

Leaves are Autumn-only transient entities (`EntityKind::Leaf = 11`) drawn from `leafPrng = Prng(seed XOR LEAF_PRNG_SALT)`, with `LEAF_PRNG_SALT = 0x1EA1DEC1D1EA1D05`. On entering Autumn, set `nextLeafSpawnTime = globalTime` so the first eligible tick emits immediately. Subsequent inter-arrival times are exponential with rate:

```text
lambda = LEAF_SPAWN_RATE_PER_SEC_1920DIP * monitorWidth / 1920.0
```

Constants:

| Constant | Value |
|---|---:|
| `LEAF_SPAWN_RATE_PER_SEC_1920DIP` | `1.4` |
| `LEAF_FALL_SPEED_MIN` / `MAX` | `14.0` / `26.0` DIP/sec |
| `LEAF_HORIZONTAL_DRIFT_AMP` | `32.0` DIP |
| `LEAF_HORIZONTAL_DRIFT_FREQ` | `1.4` rad/sec |
| `LEAF_ROTATION_SPEED_MIN` / `MAX` | `0.8` / `2.4` rad/sec |
| `LEAF_SIZE_MIN` / `MAX` | `4.0` / `7.0` DIP radius |
| `LEAF_SPAWN_Y_OFFSET` | `-10.0` DIP |
| `LEAF_COLOR_COUNT` | `6` |
| `LEAF_COLORS` | `0xFFD96B0C`, `0xFFB54D1E`, `0xFFE89A3C`, `0xFFC23E12`, `0xFFE6C849`, `0xFF8C2E0F` |

Per-leaf draw order is locked: `xFrac`, `fallSpeed`, `horizontalDriftPhase`, `rotationSpeedMagnitude`, `rotationSignBit`, `initialRotation`, `size`, `colorVariant`, then the next-spawn exponential. Motion is:

```text
y(t) = LEAF_SPAWN_Y_OFFSET + fallSpeed * age
x(t) = spawnX + LEAF_HORIZONTAL_DRIFT_AMP * sin(age * LEAF_HORIZONTAL_DRIFT_FREQ + horizontalDriftPhase)
rotation(t) = initialRotation + signedRotationSpeed * age
```

A leaf despawns when `y > groundY`. Leaves are click-through and never accumulate.

### Maple trees

Maples mirror the Winter pine/birch slot-bound pattern. On entering Autumn, `generate_maples_for_autumn` restores original blade variants, then walks blade slots with `maplePrng = Prng(seed XOR MAPLE_PRNG_SALT)`, `MAPLE_PRNG_SALT = 0xC1AA51EC1AA51E`. For each slot, draw `r = uniform(0, 1)`; if `r < MAPLE_PROBABILITY`, promote the slot to a maple, clear `isFlower` / `isMushroom`, and draw fields in this exact order: `height`, `trunkWidth`, `canopyRadius`, `canopyColorVariant`, `isBareDraw`.

| Constant | Value |
|---|---:|
| `MAPLE_PROBABILITY` | `0.0070` |
| `MAPLE_HEIGHT_MIN` / `MAX` | `50.0` / `85.0` DIP |
| `MAPLE_TRUNK_WIDTH_MIN` / `MAX` | `6.0` / `10.0` DIP |
| `MAPLE_CANOPY_RADIUS_MIN` / `MAX` | `14.0` / `24.0` DIP |
| `MAPLE_TRUNK_COLOR` / `DARK` | `0xFF4A2C18` / `0xFF2F1B0E` |
| `MAPLE_CANOPY_COLOR_COUNT` | `4` |
| `MAPLE_CANOPY_COLORS` | `0xFFD96B0C`, `0xFFE89A3C`, `0xFFC23E12`, `0xFFE6C849` |
| `MAPLE_BARE_FRACTION` | `0.20` |

Rendering: a brown trunk from `groundY` to `groundY - height`, two/three short upper branches, and (unless bare) a filled circular warm canopy plus small highlight blobs. Bare maples render only trunk/branches with a few leaf-color dots. Maples reuse the existing cut/regrowth model; when `cutHeight < CUT_STUMP_THRESHOLD`, render a short stump.

### Gating and persistence

Autumn generates no Grass critters/flyers, no rain, no Winter snowflakes, and resets snow accumulation to `0` like any non-Winter scene. Switching to Autumn clears existing raindrops so the scene remains weather-free except leaves. The existing persisted `scene` field round-trips the string `Autumn`; no schema bump is required beyond the existing v2 snow-depth schema.

---

## 12. Conformance

Because every implementation ports this spec verbatim, the unit tests in `DesktopGrass.Native.Tests` and `DesktopGrass.Win2D.Tests` can share a single snapshot fixture.

### Canonical test seed

```
CANONICAL_TEST_SEED = 0x6B6173746F  // uint64
```

Tests at minimum SHOULD assert:

1. **PRNG snapshot.** With `prng_init(seed = CANONICAL_TEST_SEED)`, the first 16 outputs of `prng_next_u64` match a fixed snapshot array embedded in each test project. Identical across both impls.

2. **Blade generation snapshot.** With `(seed = CANONICAL_TEST_SEED, monitorWidth = 1920.0, density = 1.0)`:
   - The blade count matches across impls.
   - For the first 10 and last 10 blades, every static field (`baseX`, `height`, `thickness`, `hue`, `swayPhaseOffset`, `stiffness`) matches to within `1e-12` (double precision round-trip; should be exact).
   - `baseX` is strictly increasing.
   - All `height ∈ [6, 30]`, `thickness ∈ [1.0, 2.5]`, `hue ∈ [0, 5]`, `swayPhaseOffset ∈ [0, 2π)`, `stiffness ∈ [0.6, 1.0]`.

3. **Sway determinism.** Given a fixed blade and `globalTime`, `effectiveLean` (with `gustVelocity = 0`) matches across impls to within `1e-9`. The bound is loose because `sin` may differ in last-bit precision between CRTs.

4. **Gust impulse.** Feeding a synthetic move event with a known `(prevCursorX, cursorX, dt_ev)` produces `gustVelocity` deltas on each blade matching a snapshot to within `1e-9`.

5. **Cut animation.** A click at `(clickX = 500, clickY = groundY - 40, time = 0)` followed by `tick(dt = 0.05)` calls for 5 frames produces the expected `cutHeight` series for blades within `CUT_RADIUS` (linear from 1.0 to 0.0 across 4 frames, then 0.0 thereafter), and leaves blades outside the radius at `cutHeight = 1.0`.

6. **Idempotence.** Clicking twice on the same blade within the 200 ms cut window does not change the trajectory of the first cut. Clicking on an already-cut blade is a no-op.

When any test in this list fails on a single impl, that impl has diverged from the spec — fix the impl, not the spec, unless the divergence reveals a spec ambiguity, in which case update this document first and propagate the fix to both impls.

---

## 13.3 Critter subsystem (Grass-scene ambient critters)

Critters are passive Grass-scene animals rendered on top of the bottom strip. Sheep (§16), Cat (§17), Bunny (§17.5), and Hedgehog (§17.9) share the same `CRITTER_PRNG_SALT`; Butterflies (§17.6), Fireflies (§17.7), and Birds (§17.8) are always-on/ambient Grass flyers with independent species PRNG salts. Desert and Winter currently generate zero critters/flyers. `CritterKind::None` spawns **no ground critters** — only the always-on ambient flyers (`2–4` butterflies, `3–6` fireflies) appear. The mixed ground-critter set (`2–3` sheep, `1–2` cats, `1–2` bunnies, `0–1` hedgehogs with 55% presence probability) is generated by the `CritterKind::Bunny` selector.

### Enum and salt

```
enum CritterKind { None = 0, Sheep = 1, Cat = 2, Bunny = 3 }
constexpr int         CRITTER_COUNT = 4
constexpr CritterKind CRITTER_DEFAULT  = CritterKind::None
constexpr uint64_t    CRITTER_PRNG_SALT = 0x5C8EE05C8EE05C8E
```

Discriminants are cross-impl-locked. `EntityKind::Sheep = 3`, `EntityKind::Cat = 4`, `EntityKind::Bunny = 6`, `EntityKind::Butterfly = 7`, `EntityKind::Firefly = 8`, `EntityKind::Bird = 9`, `EntityKind::Hedgehog = 10`, and `EntityKind::Leaf = 11`; existing discriminants MUST NOT be renumbered (value `5`, formerly `Raindrop`, is a retired gap and must not be reused). Bunny and Hedgehog introduce no new PRNG salt; butterflies, fireflies, birds, and leaves use independent salts documented in their sections.

### Sim state

```
Sim:
  CritterKind currentCritter = CRITTER_DEFAULT
  Prng        critterPrng                   // reseeded per generator call
  int         critterCountOverride = 0      // 0=random, 1..PET_COUNT_MAX_PER_MONITOR=fixed
```

### Generator dispatcher

`generate_critters_for_kind(sim)` is called by `sim_set_scene` as its **last** step (after biome generators) and by `sim_set_critter(sim, c)` after removing only critter entities from `sim.entities`. The dispatcher reseeds `critterPrng = Prng(entitySeed XOR CRITTER_PRNG_SALT)`. If `currentScene != Grass`, it returns without generating critters. In Grass it dispatches:

```
None  → (no ground critters; ambient flyers only)
Sheep → generate_critters_sheep(sim)        // legacy single-species selector, §16
Cat   → generate_critters_cat(sim)          // legacy single-species selector, §17
Bunny → generate all four in sheep → cat → bunny → hedgehog order
then always append generate_butterflies(sim); generate_fireflies(sim)
```

The all-species (Bunny) path ignores `critterCountOverride`; the legacy Sheep/Cat single-species paths honor it and skip that species' count draw when non-zero. Hedgehog is ambient-only (no tray toggle) and shares the critter stream. Butterflies and fireflies ignore tray critter selection and use independent side streams seeded from `entitySeed XOR BUTTERFLY_PRNG_SALT` and `entitySeed XOR FIREFLY_PRNG_SALT`.

### Ordering invariant

Inside `sim_set_scene`:

1. Remove hard scene-transition entities (finite raindrops are preserved for soft fade-out).
2. Restore default blade variants.
3. Run the scene generator (tumbleweeds, snowflakes, etc.).
4. **Run `generate_critters_for_kind(sim)` LAST.**

Step 4 must be last so that `entities[0..N-1]` for scene entities (tumbleweeds, snowflakes) is bit-identical to the snapshot tests pinned in §12 regardless of which critter mode is active. The all-species Grass entity order is locked: sheep count + sheep entities, cat count + cat entities (including `coatVariantIndex` after `nameIndex`), bunny count + bunny entities, hedgehog count-probability + hedgehog entity (if present), then butterfly count + butterfly entities and firefly count + firefly entities from their independent salts.

### `sim_set_critter` semantics

```
sim_set_critter(sim, c):
    sim.currentCritter = c
    remove every entity e where e.kind is a critter/flyer species (Sheep, Cat, Bunny, Hedgehog, Butterfly, or Firefly)
    generate_critters_for_kind(sim)
```

Scene entities (tumbleweeds, snowflakes, raindrops) are NEVER removed by `sim_set_critter`. Scene transitions remove critters/flyers and then regenerate them only if the new scene is Grass.

### Tray menu

The current tray menu offers **None**, **Sheep**, **Cat**, and **All** critter controls plus the **Pet count** picker. **None** spawns no ground critters (ambient flyers only); **All** maps to the `CritterKind::Bunny` selector and generates the mixed ground-critter set (sheep + cat + bunny + hedgehog). Hedgehog has no dedicated tray item; butterflies, fireflies, and birds are always-on ambient flyers with no user-facing setting.

### Cross-impl conformance

Given identical `(seed, monitorWidth, scene, CritterKind)`, both impls MUST produce the same critter/flyer entities with the same per-entity field values. Sheep/Cat/Bunny/Hedgehog are drawn from `Prng(seed XOR CRITTER_PRNG_SALT)` in documented species order (§16, §17, §17.5, §17.9). Butterflies and fireflies are appended after hedgehogs and are verified against their own salted side streams (§17.6, §17.7).

---

## 16. Sheep

Procedural Suffolk-style vector sheep. White wool body silhouette with a near-black face and legs (the silhouette people instantly read as "sheep" at desktop pixel scale). No PNG assets — everything is `FillEllipse` + `DrawLine` so it scales cleanly with DPI.

### Geometry

```
body : ellipse (radius SHEEP_BODY_RADIUS × SHEEP_BODY_HEIGHT)
puffs: 3 evenly-spaced top puffs at puffY = cy - bh*0.55
tail : small ellipse at the rear edge
head : ellipse pushed OUTSIDE the body silhouette (1.08 × body_radius forward)
ears : two small ellipses on top of the head
eye  : single small filled circle (closed slit when sleeping)
legs : 4 vertical line segments
```

Spawn position: `y = groundY - SHEEP_BODY_HEIGHT - SHEEP_LEG_LENGTH` so the legs touch the ground line.

### Generation (PRNG draw order — LOCKED)

```
generate_critters_sheep(sim):
    count = floor(critterPrng.uniform(SHEEP_COUNT_MIN, SHEEP_COUNT_MAX + 1))
    clamp count to [SHEEP_COUNT_MIN, SHEEP_COUNT_MAX]
    for i in 0..count-1 (subject to MAX_ENTITIES_PER_MONITOR):
        x          = critterPrng.uniform(margin, monitorWidth - margin)   // margin = body_radius + 8
        speed      = critterPrng.uniform(SHEEP_WALK_SPEED_MIN, SHEEP_WALK_SPEED_MAX)
        dirCoin    = critterPrng.uniform(0, 1)                            // <0.5 → -1, else +1
        seed       = critterPrng.next_u32()
        stateTimer = critterPrng.uniform(SHEEP_WALK_DURATION_MIN, SHEEP_WALK_DURATION_MAX)
        nameIndex  = critterPrng.index(SHEEP_NAME_POOL.size)      // one draw after stateTimer
        state      = SHEEP_STATE_WALKING
```

If `critterCountOverride` is non-zero, `count = min(critterCountOverride, PET_COUNT_MAX_PER_MONITOR)` and the count draw above is skipped. Both impls MUST follow this exact sequence per sheep for bit-identical critter PRNG state across implementations.

Per-pet name — assigned from `SHEEP_NAME_POOL = ["Bessie", "Wooly", "Clover", "Daisy", "Pippin", "Buttercup", "Mossy", "Hazel"]` at generation (one PRNG draw after `stateTimer`). Rendered as a tiny label above the pet when cursor is within `PET_NAME_HOVER_RADIUS=50` DIP; fades out over `PET_NAME_FADE_DURATION=1.5s` after cursor leaves.

### State machine (6 states)

```
SHEEP_STATE_WALKING  = 0   // moves horizontally + animated leg cycle / head bob / tail wiggle
SHEEP_STATE_GRAZING  = 1   // frozen, head pivoted down to grass + munch wiggle
SHEEP_STATE_IDLE     = 2   // frozen, head sweeps L/R
SHEEP_STATE_SLEEPING = 3   // body tucked on ground, legs hidden, eyes = horizontal slits, Z's drift up
SHEEP_STATE_HOPPING  = 4   // continues horizontal motion, renderer applies parabolic Y offset
SHEEP_STATE_GREETING = 5   // frozen, faces another sheep, gentle nuzzle head-bob
```

Greeting constants:

```
SHEEP_GREET_RADIUS          = 50.0  // DIP, center-to-center
SHEEP_GREET_DURATION_MIN    = 1.6   // sec
SHEEP_GREET_DURATION_MAX    = 2.8   // sec
SHEEP_GREET_MIN_AGE         = 1.5   // sec, must elapse in current state
SHEEP_GREET_HEAD_BOB_FREQ   = 4.5   // rad/sec
SHEEP_GREET_HEAD_BOB_AMP    = 0.7   // DIP
```

**Freeze-undo rule.** The generic entity forward pass adds `vx * dt` to `e.x` for all entities. The sheep tick undoes that addition for `{Grazing, Idle, Sleeping, Greeting}` (sheep stays planted). Hopping does NOT undo — sheep covers ground during the hop, which is what gives it the "moving but jumping" feel.

**Edge bounce.** Runs in every state (even frozen) — clamp `e.x` into `[margin, monitorWidth - margin]` where `margin = e.size + 2.0` and force `vx = abs(vx)` (left edge) / `-abs(vx)` (right edge). This ensures a sheep that spawns near the edge reflects on its first walk tick.

**Proximity greeting pass.** After the entire per-sheep transition loop completes, and before the snowflake spawner, the sim scans entity pairs in insertion order `(i, j)` with `j > i`. Both entities must be sheep in `{Walking, Grazing, Idle}` and have `age >= SHEEP_GREET_MIN_AGE`; this age gate is the natural anti-thrash cooldown after greeting exits because every state transition resets `age` to 0. If `abs(b.x - a.x) < SHEEP_GREET_RADIUS`, the pair enters Greeting. Exactly one `critterPrng.uniform(SHEEP_GREET_DURATION_MIN, SHEEP_GREET_DURATION_MAX)` is drawn per triggered pair and shared by both sheep. Their `vx` signs are set to face each other, `stateTimer` is set to the shared duration, `age` is reset, and the inner loop breaks so each sheep initiates at most one greeting per tick.

**Transition graph (deterministic from `critterPrng`):**

```
Walking expires → r = critterPrng.uniform(0,1):
  r < 0.60                              → Grazing  (duration uniform[3, 5]s)
  0.60 ≤ r < 0.85                       → Idle     (duration uniform[1.5, 3]s)
  r ≥ 0.85                              → Hopping  (duration 0.55s, no extra draw)

Idle expires → r = critterPrng.uniform(0,1), sleepProb = sheep_sleep_prob_for_local_hour(localHour):
  r < sleepProb                         → Sleeping (duration uniform[8, 16]s)
  r ≥ sleepProb                         → Walking  (duration uniform[8, 14]s)

Walking / Grazing / Idle pair in range → Greeting (shared duration uniform[1.6, 2.8]s)
Grazing / Sleeping / Hopping expire    → Walking  (duration uniform[8, 14]s)
Greeting expires                       → Walking  (duration uniform[8, 14]s, vx flipped)
```

`e.age` is reset to `0.0` on **every** state transition so animations (hop arc, sleep Z's, walk cycle, greeting bob) start at phase 0 every time. Greeting is the only timer-expiry transition that flips `vx`; Walking/Idle exits keep their current `vx` sign.

### Time-of-day sleep bias

The Idle→Sleep roll uses the same single `critterPrng.uniform(0,1)` draw as before, compared against a probability derived from the system local hour. This changes only the threshold; it adds zero PRNG draws and preserves cross-impl draw-count invariance.

| Local hour range | Sleep probability |
| --- | ---: |
| 06:00 ≤ hour < 10:00 | `SHEEP_SLEEP_PROB_MORNING = 0.10` |
| 10:00 ≤ hour < 22:00 | `SHEEP_SLEEP_PROB_DEFAULT = 0.30` |
| 22:00 ≤ hour or hour < 06:00 | `SHEEP_SLEEP_PROB_NIGHT = 0.70` |

`SHEEP_SLEEP_FROM_IDLE_PROB` remains an alias for the default 0.30 probability.

### Click startle

```
sim_apply_click after the blade-cut loop:
  for each entity e with e.kind == Sheep:
    if |e.x - clickX| >= SHEEP_STARTLE_RADIUS: skip
    awayDir = sign(e.x - clickX)
    e.vx    = min(|e.vx| * SHEEP_STARTLE_BOOST, SHEEP_WALK_SPEED_MAX * SHEEP_STARTLE_BOOST) * awayDir
    e.state = SHEEP_STATE_HOPPING
    e.stateTimer = SHEEP_HOP_DURATION
    e.age   = 0.0
```

1D distance only (the y-band gate at the top of `sim_apply_click` already restricts clicks to the strip, and the sheep stands above it). The speed cap prevents repeated-click compounding (5 clicks does not yield 10× speed).

### Render rules per state

```
hopOffsetY  = isHopping  ? -4*SHEEP_HOP_HEIGHT*t*(1-t)   : 0   where t = age/HOP_DURATION ∈ [0,1]
sleepOffsetY = isSleeping ? SHEEP_LEG_LENGTH             : 0
cy = e.y + hopOffsetY + sleepOffsetY

WALKING  : leg cycle (sin(walkPhase) * LEG_CYCLE_AMP, 4 legs in two antiphase pairs)
           head bob (sin(walkPhase*2) * HEAD_BOB_AMP), tail wiggle (sin(walkPhase*2) * TAIL_WIGGLE_AMP)
GRAZING  : head dropped to bh*0.85 (touches grass), munch (sin(age*MUNCH_FREQ) * MUNCH_AMP) on head y
IDLE     : head sweeps L/R via sin(age*IDLE_SWEEP_FREQ); facing follows sign of sweep
GREETING : frozen like Idle with no sweep; facing follows vx sign set by trigger; headY -= sin(age*GREET_HEAD_BOB_FREQ) * GREET_HEAD_BOB_AMP
SLEEPING : body sits on ground (sleepOffsetY); legs not drawn; eye → single horizontal slit
           drawn as DrawLine in sheepInkBrush; two Z glyphs drift up, grow + fade
HOPPING  : legs static (suspended look), parabolic Y offset, horizontal motion continues
```

Sleeping Z glyphs are drawn as 3 line segments (top horizontal, diagonal, bottom horizontal) in the body brush with `Opacity = 1 - t`. After drawing the Z's, the body brush opacity MUST be reset to `1.0` or all subsequent draws will be translucent.

### Cursor-curious idle render effect

Idle sheep within `SHEEP_CURIOUS_RADIUS = 80.0` DIP of the cursor notice it only when the cursor is vertically near the strip (`abs(cursorY - stripTop) <= 120 DIP`). This is render-only: no new state, no sim mutation, and zero PRNG draws. While curious, the idle L/R sweep is replaced with `headDx = clamp(cursorX - e.x, -SHEEP_CURIOUS_HEAD_TURN_MAX*SHEEP_HEAD_RADIUS, +SHEEP_CURIOUS_HEAD_TURN_MAX*SHEEP_HEAD_RADIUS)`, where `SHEEP_CURIOUS_HEAD_TURN_MAX = 0.55`.

### Defaults & conformance

All `SHEEP_*` and `CRITTER_*` constants are defined in Native `Constants.h` and Win2D `Constants.cs` with identical numeric values. The Critter tray menu is built parallel to the Scene tray menu.

For `CANONICAL_TEST_SEED + monitorWidth = 1920`, `sim_set_critter(Sheep)` produces a deterministic flock size `K ∈ [SHEEP_COUNT_MIN, SHEEP_COUNT_MAX]`. Both impls produce bit-identical `(x, vx, seed, stateTimer, nameIndex)` per sheep when walking the same `Prng(CANONICAL_TEST_SEED XOR CRITTER_PRNG_SALT)` side stream in the documented draw order, and the greeting trigger consumes exactly one additional Uniform draw per triggered pair in pair-iteration order. Both impls' test suites verify this in `critter_tests` and `sheep_greeting_tests`.

---

## 17. Cat

Procedural calm color-varied cat. Cat exists to prove the Critter framework is species-pluggable while preserving the passive desktop philosophy: cats mostly walk, sit, or sleep. They never chase cursor proximity; pounce is click-only.

### Constants

| Constant | Value | Notes |
| --- | ---: | --- |
| `CAT_COUNT_MIN` | `1` | solitary |
| `CAT_COUNT_MAX` | `2` | max per monitor |
| `CAT_WALK_SPEED_MIN` | `10.0` | DIP/sec |
| `CAT_WALK_SPEED_MAX` | `22.0` | DIP/sec |
| `CAT_POUNCE_SPEED` | `60.0` | click-only pounce |
| `CAT_BODY_RADIUS` | `11.0` | leaner than sheep |
| `CAT_BODY_HEIGHT` | `7.0` | flatter body |
| `CAT_HEAD_RADIUS` | `4.5` | smaller head |
| `CAT_LEG_LENGTH` | `5.0` | slightly shorter legs |
| `CAT_TAIL_LENGTH` | `13.0` | long curved tail |
| `CAT_TAIL_THICKNESS` | `1.6` | DIP line width |
| `CAT_EAR_HEIGHT` | `4.5` | triangle ears |
| `CAT_COAT_VARIANT_COUNT` | `6` | gray, orange, black, white, brown, cream |
| `CAT_COAT_PALETTES[6]` | see below | per-cat `(body, leg, face, ear, ink)` |
| `CAT_*_COLOR` aliases | palette `0` | backward-compatible gray tabby values |
| `CAT_WALK_PERIOD` | `0.50` | seconds |
| `CAT_LEG_CYCLE_AMP` | `1.6` | restrained gait |
| `CAT_HEAD_BOB_AMP` | `0.4` | subtle bob |
| `CAT_TAIL_SWAY_FREQ` | `1.2` | rad/sec |
| `CAT_TAIL_SWAY_AMP` | `0.35` | rad |
| `CAT_WALK_DURATION_MIN/MAX` | `6.0 / 10.0` | shorter walks |
| `CAT_IDLE_DURATION_MIN/MAX` | `4.0 / 8.0` | sits and watches |
| `CAT_SLEEP_DURATION_MIN/MAX` | `20.0 / 40.0` | long naps |
| `CAT_POUNCE_DURATION` | `0.45` | one arc |
| `CAT_IDLE_PROBABILITY` | `0.65` | Walking expiry |
| `CAT_SLEEP_PROBABILITY` | `0.30` | Walking expiry |
| `CAT_SLEEP_FROM_IDLE_PROB_MORNING` | `0.20` | 06:00-10:00 |
| `CAT_SLEEP_FROM_IDLE_PROB_DEFAULT` | `0.50` | 10:00-22:00 |
| `CAT_SLEEP_FROM_IDLE_PROB_NIGHT` | `0.85` | 22:00-06:00 |
| `CAT_POUNCE_RADIUS` | `80.0` | click x-distance |
| `CAT_POUNCE_HEIGHT` | `9.0` | parabolic rise |
| `CAT_CURIOUS_RADIUS` | `100.0` | render-only idle head turn |
| `CAT_CURIOUS_HEAD_TURN_MAX` | `0.7` | radians |

Cat coat palettes are bit-identical in Native `Constants.h` and Win2D `Constants.cs`:

| # | Short name | Body | Leg | Face | Ear | Ink | Notes |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | --- |
| 0 | Gray | `0xFF6B6259` | `0xFF3D3733` | `0xFF6B6259` | `0xFF3D3733` | `0xFF1A1614` | existing gray tabby |
| 1 | Orange | `0xFFD89A6F` | `0xFFA56B40` | `0xFFD89A6F` | `0xFFA56B40` | `0xFF2B1A0E` | warm ginger |
| 2 | Black | `0xFF2A2522` | `0xFF140F0C` | `0xFF2A2522` | `0xFF140F0C` | `0xFFD9B85B` | warm yellow eyes for contrast |
| 3 | White | `0xFFEDE9E1` | `0xFFBDB7AB` | `0xFFEDE9E1` | `0xFFBDB7AB` | `0xFF1F1817` | dark ink |
| 4 | Brown | `0xFF7A5F3C` | `0xFF4E3F26` | `0xFF7A5F3C` | `0xFF4E3F26` | `0xFF1A1108` | rich brown tabby |
| 5 | Cream | `0xFFC9B898` | `0xFF8E7F6B` | `0xFFC9B898` | `0xFF8E7F6B` | `0xFF2E251D` | tan/buff |

### State machine (reuses sheep state bytes)

Cat adds no new state bytes. It aliases the shared `SHEEP_STATE_*` byte values:

```
CAT_STATE_WALKING  = SHEEP_STATE_WALKING   // 0, moves horizontally
CAT_STATE_IDLE     = SHEEP_STATE_IDLE      // 2, sit-and-watch (no grazing)
CAT_STATE_SLEEPING = SHEEP_STATE_SLEEPING  // 3, long nap
CAT_STATE_POUNCING = SHEEP_STATE_HOPPING   // 4, click-only pounce
```

Cats do **not** use `SHEEP_STATE_GRAZING = 1` or `SHEEP_STATE_GREETING = 5`.

**Transition graph:**

```
Walking expires → r = critterPrng.uniform(0,1):
  r < 0.65                    → Idle     (duration uniform[4, 8]s)
  0.65 ≤ r < 0.95             → Sleeping (duration uniform[20, 40]s)
  r ≥ 0.95                    → Walking  (duration uniform[6, 10]s)

Idle expires → r = critterPrng.uniform(0,1), sleepProb = cat_sleep_prob_for_local_hour(localHour):
  r < sleepProb               → Sleeping (duration uniform[20, 40]s)
  r ≥ sleepProb               → Walking  (duration uniform[6, 10]s)

Sleeping / Pouncing expire     → Walking  (duration uniform[6, 10]s)
Click within radius            → Pouncing (duration 0.45s, vx toward click)
```

The same freeze-undo and edge-bounce rules as sheep apply, except only `{Idle, Sleeping}` are frozen. Pouncing continues horizontal motion and uses a renderer-side parabolic Y offset. `e.age` resets on every transition.

### Generation (PRNG draw order — LOCKED)

```
generate_critters_cat(sim):
    count = floor(critterPrng.uniform(CAT_COUNT_MIN, CAT_COUNT_MAX + 1))
    clamp count to [CAT_COUNT_MIN, CAT_COUNT_MAX]
    for i in 0..count-1 (subject to MAX_ENTITIES_PER_MONITOR):
        x          = critterPrng.uniform(margin, monitorWidth - margin)   // margin = body_radius + 8
        speed      = critterPrng.uniform(CAT_WALK_SPEED_MIN, CAT_WALK_SPEED_MAX)
        dirCoin    = critterPrng.uniform(0, 1)                            // <0.5 → -1, else +1
        seed       = critterPrng.next_u32()
        stateTimer = critterPrng.uniform(CAT_WALK_DURATION_MIN, CAT_WALK_DURATION_MAX)
        nameIndex  = critterPrng.index(CAT_NAME_POOL.size)        // one draw after stateTimer
        coatVariantIndex = critterPrng.index(CAT_COAT_VARIANT_COUNT) // NEW DRAW after nameIndex
        state      = CAT_STATE_WALKING
```

If `critterCountOverride` is non-zero, `count = min(critterCountOverride, PET_COUNT_MAX_PER_MONITOR)` and the count draw above is skipped. This matches the sheep draw sequence with one cat-only draw appended after `nameIndex`. Species share the same `CRITTER_PRNG_SALT`; no per-species salt is introduced. The `coatVariantIndex` draw is new relative to prior commits, so tests that pinned per-cat post-generation PRNG state must be re-pinned.

Per-pet name — assigned from `CAT_NAME_POOL = ["Mittens", "Whiskers", "Shadow", "Ginger", "Smokey", "Boots", "Sage", "Juno"]` at generation (one PRNG draw after `stateTimer`). Per-pet coat color is assigned immediately after the name draw. Rendered as a tiny label above the pet when cursor is within `PET_NAME_HOVER_RADIUS=50` DIP; fades out over `PET_NAME_FADE_DURATION=1.5s` after cursor leaves.

### Click pounce

```
sim_apply_click after the sheep startle loop:
  for each entity e with e.kind == Cat:
    dx = clickX - e.x
    if abs(dx) >= CAT_POUNCE_RADIUS: skip
    towardDir = sign(dx)      // Cat moves TOWARD click; sheep moves AWAY
    e.vx = towardDir * CAT_POUNCE_SPEED
    e.state = CAT_STATE_POUNCING
    e.stateTimer = CAT_POUNCE_DURATION
    e.age = 0.0
```

This is click-only. There is no cursor-proximity pounce/chase check.

### Render rules per state

```
pounceOffsetY = isPouncing ? -4*CAT_POUNCE_HEIGHT*t*(1-t) : 0  where t = age/POUNCE_DURATION ∈ [0,1]
sleepOffsetY  = isSleeping ? CAT_LEG_LENGTH                 : 0
cy = e.y + pounceOffsetY + sleepOffsetY

WALKING  : 4 thin legs with smaller CAT_LEG_CYCLE_AMP, subtle head bob, slow tail sway
IDLE     : frozen; head turns toward nearby cursor using CAT_CURIOUS_RADIUS (render-only)
SLEEPING : body drops to ground, legs hidden, tail wraps around body, closed curved eyes, two smaller Z glyphs
POUNCING : parabolic Y offset, horizontal motion toward click
```

Geometry is distinct from sheep: one long flattened body ellipse, smaller circular head on the top-front, tall triangle ears, visible ink eyes/nose on a light face, four thin legs, and a long curved tail. Renderers pre-create one brush set per `CAT_COAT_PALETTES` entry and select by `e.coatVariantIndex % CAT_COAT_VARIANT_COUNT`. Native and Win2D use vector primitives only.

### Differences from Sheep

* No grazing state.
* No greeting — cats do not greet cats or sheep.
* Longer sleeps and higher Idle→Sleep probabilities, especially at night (`0.85`).
* Click pounce moves **toward** the click; sheep startle moves **away**.
* Passive by design: no proximity-triggered chase or pounce.

### Defaults & conformance

For `CANONICAL_TEST_SEED + monitorWidth = 1920`, `sim_set_critter(Cat)` produces `K ∈ [CAT_COUNT_MIN, CAT_COUNT_MAX]`. Both impls produce bit-identical `(x, vx, seed, stateTimer, nameIndex, coatVariantIndex)` per cat when walking the same `Prng(CANONICAL_TEST_SEED XOR CRITTER_PRNG_SALT)` side stream in the documented draw order. Both impls' test suites verify this in `cat_tests` / `CatTests` and `cat_coat_tests` / `CatCoatTests`.

---

## 17.5 Bunny

Procedural calm woodland bunny. Bunnies are passive and skittish: they never chase the cursor, never greet other pets, and never create an engagement loop. Normal locomotion is hopping only; clicks within the startle radius wake/break the current pose and make the bunny hop away.

### Constants

| Constant | Value | Notes |
| --- | ---: | --- |
| `BUNNY_COUNT_MIN/MAX` | `1 / 2` | Grass ambient count |
| `BUNNY_HOP_SPEED_MIN/MAX` | `22.0 / 38.0` | DIP/sec horizontal |
| `BUNNY_BODY_RADIUS` / `HEIGHT` | `8.0 / 6.5` | small round body |
| `BUNNY_HEAD_RADIUS` | `4.2` | circular head |
| `BUNNY_EAR_HEIGHT` / `WIDTH` / `SPACING` | `9.0 / 2.2 / 3.0` | tall signature ears |
| `BUNNY_LEG_LENGTH` | `4.0` | visible when idle/grazing |
| `BUNNY_TAIL_RADIUS` | `2.4` | white puff |
| `BUNNY_BODY_COLOR` | `0xFF8A6A4A` | warm brown |
| `BUNNY_BELLY_COLOR` | `0xFFC4A98D` | lighter belly underglow |
| `BUNNY_EAR_COLOR` | `0xFF8A6A4A` | body brown |
| `BUNNY_EAR_INNER_COLOR` | `0xFFD9A0A0` | soft pink |
| `BUNNY_TAIL_COLOR` | `0xFFF7F4EB` | white puff |
| `BUNNY_EYE_COLOR` | `0xFF1A1208` | black dot |
| `BUNNY_NOSE_COLOR` | `0xFF8A4040` | pink nose |
| `BUNNY_HOP_DURATION` | `0.40` | sec per hop arc |
| `BUNNY_HOP_HEIGHT` | `8.0` | DIP peak offset |
| `BUNNY_HOP_GAP_MIN/MAX` | `0.05 / 0.20` | landing pause |
| `BUNNY_GRAZE_DURATION_MIN/MAX` | `2.5 / 4.5` | sec |
| `BUNNY_IDLE_DURATION_MIN/MAX` | `2.0 / 4.0` | sec |
| `BUNNY_SLEEP_DURATION_MIN/MAX` | `6.0 / 12.0` | sec |
| `BUNNY_GRAZE_PROBABILITY` | `0.55` | non-sleep active weight |
| `BUNNY_IDLE_PROBABILITY` | `0.30` | non-sleep active weight |
| `BUNNY_SLEEP_PROB_DAY/NIGHT` | `0.05 / 0.40` | absolute sleep probability |
| `BUNNY_STARTLE_RADIUS` | `90.0` | DIP, click center distance |
| `BUNNY_STARTLE_BOOST` | `2.0` | base speed multiplier |
| `BUNNY_STARTLE_HOP_HEIGHT` | `14.0` | boosted arc |
| `BUNNY_STARTLE_DURATION` | `3.0` | sec |
| `BUNNY_NOSE_TWITCH_FREQ` / `AMP` | `6.0 / 0.5` | idle nose animation |
| `BUNNY_EAR_WIGGLE_FREQ` / `AMP` | `1.2 / 0.20` | idle ear sway |
| `BUNNY_ZZZ_*` | sheep `ZZZ` × `0.7` where sized | smaller sleep glyphs |
| `BUNNY_NAME_POOL` | `Clover, Hazel, Thumper, Mochi, Pip, Acorn, Biscuit, Willow, Pepper, Hopper, Juniper, Snowdrop` | woodland names |

### State machine

```
BUNNY_STATE_HOPPING  = 0   // one parabolic hop; no smooth walking
BUNNY_STATE_GRAZING  = 1   // stationary, body lowered, head down nibbling
BUNNY_STATE_IDLE     = 2   // stationary, sitting up, nose twitch + ear wiggle
BUNNY_STATE_SLEEPING = 3   // lying flat, ears back, small Zzz glyphs
BUNNY_STATE_STARTLED = 4   // boosted flee-hop after click

Hopping expires  → choose Grazing / Idle / Sleeping, then draw duration
Grazing expires  → Hopping (duration = hop + random gap)
Idle expires     → Hopping (duration = hop + random gap)
Sleeping expires → Hopping (duration = hop + random gap)
Startled expires → Hopping (normal base speed, no gap)
Click in radius  → Startled from any bunny state
```

Hop render offset is `4 * peak_h * t * (1 - t)` with `t = clamp(age / BUNNY_HOP_DURATION, 0, 1)`; renderers subtract that value from `cy` so positive offset moves upward. Hopping freezes horizontal integration during the landing-gap portion (`age > BUNNY_HOP_DURATION`) so the bunny pauses on the ground between hops.

### Generation (PRNG draw order — LOCKED)

Bunnies are generated after sheep and cats in the Grass ambient stream. Species share `CRITTER_PRNG_SALT`.

```
generate_critters_bunny(sim):
    count = floor(critterPrng.uniform(BUNNY_COUNT_MIN, BUNNY_COUNT_MAX + 1))
    clamp count to [BUNNY_COUNT_MIN, BUNNY_COUNT_MAX]
    for each bunny:
        xFrac    = critterPrng.uniform(0, 1)
        vxSign   = critterPrng.next_u64() & 1       // 0 → left, 1 → right
        speed    = critterPrng.uniform(BUNNY_HOP_SPEED_MIN, BUNNY_HOP_SPEED_MAX)
        nameIndex = critterPrng.index(BUNNY_NAME_POOL.size)
```

Initial `x = margin + xFrac * (monitorWidth - 2*margin)` with `margin = BUNNY_BODY_RADIUS + 8`; `vx = sign * speed`; `rotationSpeed` stores the normal base speed for restoring after a startle. No coat variant and no PRNG seed draw are consumed in v1.

### Time-of-day sleep bias

Bunnies use a 2-band day/night helper:

| Local hour range | Sleep probability |
| --- | ---: |
| `10:00 ≤ hour < 20:00` | `BUNNY_SLEEP_PROB_DAY = 0.05` |
| `20:00 ≤ hour or hour < 10:00` | `BUNNY_SLEEP_PROB_NIGHT = 0.40` |

Sleep probability is absolute. The remaining non-sleep probability is split between Grazing and Idle using `BUNNY_GRAZE_PROBABILITY : BUNNY_IDLE_PROBABILITY` as weights, preserving the crepuscular sleep bias without adding another PRNG draw.

### Click startle

```
sim_apply_click after sheep and cat handling:
  for each entity e with e.kind == Bunny:
    if distance((e.x,e.y), (clickX,clickY)) > BUNNY_STARTLE_RADIUS: skip
    awayDir = sign(e.x - clickX)
    e.vx = awayDir * baseSpeed * BUNNY_STARTLE_BOOST
    e.state = BUNNY_STATE_STARTLED
    e.stateTimer = BUNNY_STARTLE_DURATION
    e.age = 0.0
```

Startle wakes sleeping bunnies and interrupts grazing/idle. There is no greeting, no cursor-curious behavior, and no inter-pet interaction.

### Render order

Bottom-to-top:

1. Tail puff behind body (`BUNNY_TAIL_COLOR`).
2. Body ellipse (`BUNNY_BODY_COLOR`), flattened to 0.7× Y when sleeping.
3. Belly underglow ellipse (`BUNNY_BELLY_COLOR`), offset down.
4. Two short leg ovals when idle/grazing only; legs are tucked while hopping/startled/sleeping.
5. Head circle on the leading front-top side.
6. Two tall narrow ears with pink inner strokes; while sleeping, ears render as short horizontal back dashes.
7. Eye dot; while sleeping, a short horizontal slit.
8. Nose dot; idle adds `BUNNY_NOSE_TWITCH_AMP * sin(age * BUNNY_NOSE_TWITCH_FREQ)` to nose Y.
9. Sleeping Zzz glyphs using `BUNNY_ZZZ_*`.

Facing mirrors horizontally from `vx` sign: head, ears, eye, and nose are on the leading side; tail trails.

### Scene gating and conformance

Bunnies are Grass-only. `generate_critters_for_kind` returns without bunnies in Desert and Winter. Native `bunny_tests.cpp` and Win2D `BunnyTests.cs` pin constants, scene gating, count/speed/name ranges, side-stream PRNG identity after sheep+cat draws, edge bounce, startle behavior, wake-from-sleep, hop arc bounds, transition probabilities, and day/night sleep bias.

---

## 17.6 Butterflies

Tiny daytime ambient butterflies. They are purely visual: no click handling, no cut state, no pet proximity logic, no collisions, Grass scene only, and no tray toggle.

### Constants

| Constant | Value | Notes |
| --- | ---: | --- |
| `BUTTERFLY_COUNT_MIN/MAX` | `2 / 4` | Grass ambient count |
| `BUTTERFLY_SPEED_MIN/MAX` | `18.0 / 32.0` | DIP/sec horizontal cruise |
| `BUTTERFLY_BODY_LENGTH` | `2.4` | dark vertical ellipse height |
| `BUTTERFLY_WING_RADIUS` | `3.5` | each wing oval radius |
| `BUTTERFLY_WING_OFFSET` | `2.2` | wing center X offset from body |
| `BUTTERFLY_FLUTTER_FREQ` | `16.0` | rad/sec |
| `BUTTERFLY_FLUTTER_MIN_SCALE` | `0.20` | folded wing X-scale clamp |
| `BUTTERFLY_MEANDER_FREQ_Y / AMP_Y` | `0.8 / 16.0` | vertical wander |
| `BUTTERFLY_MEANDER_FREQ_X / AMP_X` | `0.5 / 0.4` | speed multiplier, 0.6x..1.4x |
| `BUTTERFLY_ALTITUDE_MIN/MAX` | `18.0 / 70.0` | DIP above tallest grass top (`groundY - BLADE_HEIGHT_MAX`) |
| `BUTTERFLY_BODY_COLOR` | `0xFF2A2018` | dark brown |
| `BUTTERFLY_COLOR_COUNT` | `5` | monarch, swallowtail, cabbage, morpho, pink |
| `BUTTERFLY_HOUR_START/END` | `6 / 19` | full visibility window `[6,19)` |
| `BUTTERFLY_FADE_DURATION_HOUR` | `1` | dawn/dusk fades |
| `BUTTERFLY_PRNG_SALT` | `0xB07DEF1E0001` | independent butterfly stream |

Palette is `{wingColor, accentColor}` for variants: Monarch orange+black, Swallowtail yellow+black, Cabbage white+dark dots, Morpho sky-blue+deep blue, Pink soft pink+rose.

### Generation (PRNG draw order — LOCKED)

Butterflies are appended after bunnies in the Grass entity generation pipeline and use `butterflyPrng = Prng(entitySeed XOR BUTTERFLY_PRNG_SALT)`. Draw count with `floor(uniform(BUTTERFLY_COUNT_MIN, BUTTERFLY_COUNT_MAX + 1))`, clamped. Per butterfly:

```
xFrac       = uniform(0, 1)
yFrac       = uniform(0, 1) -> altitudeAnchor in [ALTITUDE_MIN, ALTITUDE_MAX)
vxSign      = next_u64() & 1       // 0 left, 1 right
baseSpeed   = uniform(SPEED_MIN, SPEED_MAX)
colorVariant = index(BUTTERFLY_COLOR_COUNT)
phaseY      = uniform(0, 2π)
phaseX      = uniform(0, 2π)
```

Initial `x = xFrac * monitorWidth`; `vx` is initialized from the motion formula at `age = 0`. `baseSpeed`, `altitudeAnchor`, `phaseY`, `phaseX`, and `colorVariant` are stored on the entity for tests/rendering.

### Motion model

No state machine. Every tick:

```
vx = baseSpeed * sign(vx) * (1 + BUTTERFLY_MEANDER_AMP_X * sin(age * BUTTERFLY_MEANDER_FREQ_X + phaseX))
y  = (groundY - BLADE_HEIGHT_MAX) - altitudeAnchor
     + BUTTERFLY_MEANDER_AMP_Y * sin(age * BUTTERFLY_MEANDER_FREQ_Y + phaseY)
```

If `x > monitorWidth + (WING_OFFSET + WING_RADIUS)`, wrap to the same negative margin; if `x < -margin`, wrap to `monitorWidth + margin`. The `altitudeAnchor` is preserved.

### Day fade function

```
butterflyFade(hour):
  [05:00,06:00) -> linear 0→1
  [06:00,19:00) -> 1
  [19:00,20:00) -> linear 1→0
  otherwise     -> 0
```

### Render order

Draw after grass as part of `DrawEntities`: left/right wing ovals first, small accent dots/tips on the wings, then the dark body ellipse. Wing X-scale is `clamp(cos(age * BUTTERFLY_FLUTTER_FREQ + phaseY), BUTTERFLY_FLUTTER_MIN_SCALE, 1.0)`. Entity opacity is multiplied by `butterflyFade(currentLocalHourFractional)`.

---

## 17.7 Fireflies

Tiny nighttime ambient fireflies. They are purely visual: no click handling, no cut state, no pet proximity logic, no collisions, Grass scene only, and no tray toggle.

### Constants

| Constant | Value | Notes |
| --- | ---: | --- |
| `FIREFLY_COUNT_MIN/MAX` | `3 / 6` | Grass ambient count |
| `FIREFLY_DRIFT_SPEED_MIN/MAX` | `4.0 / 10.0` | slow DIP/sec drift |
| `FIREFLY_BODY_RADIUS` | `1.2` | warm dot |
| `FIREFLY_GLOW_RADIUS` | `5.0` | soft halo |
| `FIREFLY_BLINK_PERIOD_MIN/MAX` | `1.4 / 2.6` | seconds |
| `FIREFLY_BLINK_DUTY` | `0.55` | fraction of cycle on |
| `FIREFLY_BLINK_FADE` | `0.30` | sec rise/fall |
| `FIREFLY_DRIFT_FREQ_X / AMP_X` | `0.4 / 0.6` | speed multiplier, 0.4x..1.6x |
| `FIREFLY_DRIFT_FREQ_Y / AMP_Y` | `0.6 / 8.0` | vertical wander |
| `FIREFLY_ALTITUDE_MIN/MAX` | `8.0 / 55.0` | DIP above tallest grass top (`groundY - BLADE_HEIGHT_MAX`) |
| `FIREFLY_BODY_COLOR` | `0xFFFFEE88` | yellow-green dot |
| `FIREFLY_GLOW_COLOR_RGB` | `0xEEDD66` | halo RGB |
| `FIREFLY_GLOW_ALPHA_MAX` | `110` | peak halo alpha |
| `FIREFLY_BODY_ALPHA_MAX` | `255` | peak body alpha |
| `FIREFLY_NIGHT_START/END_HOUR` | `20 / 6` | full visibility wraps midnight |
| `FIREFLY_FADE_DURATION_HOUR` | `1` | dusk/dawn fades |
| `FIREFLY_PRNG_SALT` | `0xF13EF1E7777` | independent firefly stream |

### Generation (PRNG draw order — LOCKED)

Fireflies are appended after butterflies in the Grass entity generation pipeline and use `fireflyPrng = Prng(entitySeed XOR FIREFLY_PRNG_SALT)`. Draw count with `floor(uniform(FIREFLY_COUNT_MIN, FIREFLY_COUNT_MAX + 1))`, clamped. Per firefly:

```
xFrac       = uniform(0, 1)
yFrac       = uniform(0, 1) -> altitudeAnchor in [ALTITUDE_MIN, ALTITUDE_MAX)
vxSign      = next_u64() & 1       // 0 left, 1 right
baseSpeed   = uniform(DRIFT_SPEED_MIN, DRIFT_SPEED_MAX)
blinkPeriod = uniform(BLINK_PERIOD_MIN, BLINK_PERIOD_MAX)
blinkPhase  = uniform(0, 1)
phaseY      = uniform(0, 2π)
phaseX      = uniform(0, 2π)
```

### Motion model

No state machine. Every tick:

```
vx = baseSpeed * sign(vx) * (1 + FIREFLY_DRIFT_AMP_X * sin(age * FIREFLY_DRIFT_FREQ_X + phaseX))
y  = (groundY - BLADE_HEIGHT_MAX) - altitudeAnchor
     + FIREFLY_DRIFT_AMP_Y * sin(age * FIREFLY_DRIFT_FREQ_Y + phaseY)
```

If `x > monitorWidth + FIREFLY_GLOW_RADIUS`, wrap to `-FIREFLY_GLOW_RADIUS`; if `x < -FIREFLY_GLOW_RADIUS`, wrap to `monitorWidth + FIREFLY_GLOW_RADIUS`. The `altitudeAnchor` is preserved.

### Blink model

```
cycleT = (age / blinkPeriod + blinkPhase) mod 1
if cycleT < BLINK_DUTY:
  brightness = 1, with smoothstep fade-in over BLINK_FADE/blinkPeriod
               and smoothstep fade-out before BLINK_DUTY
else:
  brightness = 0
```

Body and glow alpha are multiplied by `brightness * fireflyFade(currentHour)`.

### Night fade function

```
fireflyFade(hour):
  [20:00,06:00) -> 1  // wraps midnight
  [19:00,20:00) -> linear 0→1
  [06:00,07:00) -> linear 1→0
  otherwise     -> 0
```

### Render order

Draw after grass as part of `DrawEntities`: glow halo first using `GLOW_RADIUS * brightness`, then body dot using `BODY_RADIUS`. The halo opacity peaks at `FIREFLY_GLOW_ALPHA_MAX`; the body peaks at `FIREFLY_BODY_ALPHA_MAX`. Both are multiplied by the night fade. No interaction, no cut detection, no tray toggle.

---

## 17.8 Bird flybys

Rare daytime-only Grass-scene bird flybys. A flyby is a transient flock of tiny dark silhouettes that crosses the strip far above the grass and pets. Birds are pure ambient: click-through, not cuttable, no pet proximity logic, no collision, no tray toggle, and no persistence.

### Constants

| Constant | Value | Notes |
| --- | ---: | --- |
| `BIRD_FLYBY_SPAWN_RATE_PER_HOUR` | `15.0` | Poisson mean events/hour during day window (~4 min mean interval) |
| `BIRD_FLYBY_HOUR_START/END` | `7 / 19` | spawn window `[7,19)` local hour |
| `BIRD_FLOCK_SIZE_MIN/MAX` | `3 / 7` | birds per flyby |
| `BIRD_FLOCK_FORMATION_SPACING` | `9.0` | DIP between consecutive birds along the flight axis |
| `BIRD_FLOCK_V_ANGLE_DEG` | `22.0` | arm angle used for perpendicular offsets |
| `BIRD_SPEED_MIN/MAX` | `65.0 / 95.0` | DIP/sec |
| `BIRD_ALTITUDE_MIN/MAX` | `78.0 / 96.0` | leader DIP above tallest grass top |
| `BIRD_BODY_LENGTH` | `3.6` | small silhouette body length |
| `BIRD_WING_SPAN` | `5.0` | tip-to-tip span at full extension |
| `BIRD_WING_FLAP_FREQ` | `7.0` | rad/sec |
| `BIRD_WING_FLAP_PHASE_JITTER` | `0.6` | per-bird random phase in `[-jitter,+jitter]` |
| `BIRD_BODY_COLOR` | `0xFF1A1610` | dark grey-black silhouette |
| `BIRD_WING_OPEN_RATIO/FOLD_RATIO` | `1.0 / 0.30` | wing span scale bounds |
| `BIRD_FADE_IN_FRAC/OUT_FRAC` | `0.08 / 0.08` | horizontal edge fade fractions |
| `BIRD_DRIFT_AMP_Y/FREQ_Y` | `3.0 / 0.8` | gentle vertical bob |
| `BIRD_FLYBY_PRNG_SALT` | `0xB12D1F1A1B12D1A` | independent bird-flyby stream |

### Poisson spawn model

Each `Sim` owns `birdFlybyPrng` and `nextBirdFlybyAtTime` (seconds in sim time). Initialization seeds the stream with `entitySeed XOR BIRD_FLYBY_PRNG_SALT` and sets `nextBirdFlybyAtTime = globalTime + exponential(rate = BIRD_FLYBY_SPAWN_RATE_PER_HOUR / 3600)`. Each Grass-scene daytime tick (`[BIRD_FLYBY_HOUR_START, BIRD_FLYBY_HOUR_END)`) checks `globalTime >= nextBirdFlybyAtTime`; when true it spawns exactly one flock, then schedules the next interval from the same stream. Non-Grass scenes and non-day hours do not spawn; in-flight birds continue until off-screen despawn.

### Flock PRNG draw order — LOCKED

Per flyby event:

```
flockSize       = uniform integer [BIRD_FLOCK_SIZE_MIN, BIRD_FLOCK_SIZE_MAX]
direction       = next_u64() & 1      // 0 left, 1 right
leaderAltitude  = uniform(ALTITUDE_MIN, ALTITUDE_MAX)
leaderSpeed     = uniform(SPEED_MIN, SPEED_MAX)
formationStyle  = next_u64() & 1      // 0 V, 1 diagonal-line
for each bird in flock order:
  wingPhaseOffset   = uniform(-PHASE_JITTER, +PHASE_JITTER)
  verticalDriftPhase = uniform(0, 2π)
```

The scheduler's exponential draws occur outside this per-flock block: once at initialization, and once after each flock has consumed all of the event/per-bird draws above.

### Formation geometry

The leader starts at off-screen `x = -50` for rightward flight or `monitorWidth + 50` for leftward flight. For bird index `i`, `offsetAlongFlight = -i * BIRD_FLOCK_FORMATION_SPACING`; screen X is `leaderX + direction * offsetAlongFlight`. In V formation, followers alternate sides of the leader's flight line with `offsetPerpendicular = side * ((i + 1) / 2) * spacing * sin(BIRD_FLOCK_V_ANGLE_DEG)`, where side alternates left/right. In diagonal-line formation, all followers use one side with `offsetPerpendicular = i * spacing * sin(angle)`. The leader has offset `(0,0)`.

### Per-bird motion and wing flap

Each bird stores its spawn X (`x0`), altitude anchor, `vx = direction * leaderSpeed`, wing phase offset, vertical drift phase, spawn time, and formation offsets. Every tick:

```
x += vx * dt
y = grassTopY - altitudeAnchor
    + BIRD_DRIFT_AMP_Y * sin(age * BIRD_DRIFT_FREQ_Y + verticalDriftPhase)
wingScale = lerp(BIRD_WING_FOLD_RATIO, BIRD_WING_OPEN_RATIO,
                 0.5 + 0.5 * cos(age * BIRD_WING_FLAP_FREQ + wingPhaseOffset))
```

Birds despawn individually after crossing the opposite off-screen boundary (`x > monitorWidth + 50` for rightward flight, `x < -50` for leftward flight).

### Render order and interaction

Birds render in the sky layer after critters, weather, butterflies, and fireflies, and before the day-night tint. The renderer draws a tiny filled body ellipse plus two wing strokes/triangles scaled by `wingScale`, with alpha fading over the first/last `8%` of the visible cross distance. Birds are not included in cut detection or any critter interaction path.

---

## 17.9 Hedgehog

Slow, solitary Grass-scene woodland critter. Hedgehog is passive defense by design: it does not greet, pounce, chase, flee, or interact with other critters. When startled by a click, it curls into a spiky ball and waits.

### Constants

| Constant | Value | Notes |
| --- | ---: | --- |
| `HEDGEHOG_COUNT_MIN/MAX` | `0 / 1` | solitary; sometimes absent |
| `HEDGEHOG_COUNT_PROBABILITY` | `0.55` | count `1` iff count-probability draw is below this |
| `HEDGEHOG_WALK_SPEED_MIN/MAX` | `4.0 / 8.0` | DIP/sec, slowest critter |
| `HEDGEHOG_BODY_RADIUS` / `HEIGHT` | `9.0 / 5.5` | compact, low body ellipse |
| `HEDGEHOG_HEAD_RADIUS` | `3.6` | tiny head |
| `HEDGEHOG_NOSE_RADIUS` | `0.8` | black nose dot |
| `HEDGEHOG_LEG_LENGTH` | `2.5` | short stubby legs |
| `HEDGEHOG_SPIKE_COUNT` | `14` | normal pose partial-arc spikes |
| `HEDGEHOG_SPIKE_LENGTH` / `WIDTH` | `3.0 / 1.4` | DIP triangle geometry |
| `HEDGEHOG_SPIKE_ARC_START_DEG/END_DEG` | `-20 / 200` | cover top/back/bottom, leave face clear |
| `HEDGEHOG_BODY_COLOR` | `0xFF5C4633` | warm brown body/face |
| `HEDGEHOG_SPIKE_COLOR` | `0xFF3A2A1F` | darker brown spikes |
| `HEDGEHOG_SPIKE_TIP_COLOR` | `0xFF1E150E` | darkest spike tips/ZZZ ink |
| `HEDGEHOG_NOSE_COLOR` | `0xFF1A1208` | black nose |
| `HEDGEHOG_EYE_COLOR` | `0xFF1A1208` | black eye |
| `HEDGEHOG_WALK_DURATION_MIN/MAX` | `6.0 / 12.0` | sec |
| `HEDGEHOG_SNUFFLE_DURATION_MIN/MAX` | `3.0 / 6.0` | sec |
| `HEDGEHOG_IDLE_DURATION_MIN/MAX` | `1.5 / 3.0` | sec |
| `HEDGEHOG_SLEEP_DURATION_MIN/MAX` | `10.0 / 25.0` | sec, long naps |
| `HEDGEHOG_CURL_DURATION_MIN/MAX` | `3.0 / 5.5` | sec defensive curl |
| `HEDGEHOG_SNUFFLE_PROBABILITY` | `0.55` | non-sleep active weight |
| `HEDGEHOG_IDLE_PROBABILITY` | `0.30` | non-sleep active weight |
| `HEDGEHOG_SLEEP_PROB_DAY/NIGHT` | `0.50 / 0.05` | nocturnal sleep bias; day is `[06,18)` |
| `HEDGEHOG_STARTLE_RADIUS` | `70.0` | DIP click radius |
| `HEDGEHOG_SNUFFLE_HEAD_FREQ` / `AMP` | `5.0 / 0.7` | rad/sec and DIP x-offset |
| `HEDGEHOG_WADDLE_FREQ` / `AMP` | `4.0 / 0.8` | rad/sec and DIP vertical bob |
| `HEDGEHOG_ZZZ_*` | sheep `ZZZ` scaled by `0.5` rise, `0.6` size | tiny sleep puffs |
| `HEDGEHOG_NAME_POOL` | `Bristle, Quill, Mossy, Truffle, Prickles, Snuffles, Pinecone, Hazel, Bramble, Pip, Sage, Burdock` | woodland names |

### State machine

```cpp
HEDGEHOG_STATE_WALKING   = 0   // slow waddle, moves horizontally
HEDGEHOG_STATE_SNUFFLING = 1   // stationary, head x-offset sweeps left/right
HEDGEHOG_STATE_IDLE      = 2   // stationary, body shifted up 1 DIP (alert pose)
HEDGEHOG_STATE_SLEEPING  = 3   // curled ball form, no face/legs, small ZZZ puffs
HEDGEHOG_STATE_CURLED    = 4   // defensive ball after startle, no ZZZ puffs

Walking expires → choose Snuffling / Idle / Sleeping using local-hour sleep probability
Snuffling / Idle / Sleeping expire → Walking
Click within startle radius → Curled
Curled expires → previous non-sleep state; if startled from Sleeping, return to Walking
```

`e.age` is reset on every state transition. Walking uses `y_offset = HEDGEHOG_WADDLE_AMP * sin(age * HEDGEHOG_WADDLE_FREQ)`. Snuffling uses `head_x_offset = HEDGEHOG_SNUFFLE_HEAD_AMP * sin(age * HEDGEHOG_SNUFFLE_HEAD_FREQ)`. All non-Walking hedgehog states undo the generic `vx * dt` integration so the critter stays planted.

### Time-of-day sleep bias

Hedgehogs are nocturnal. `hedgehog_sleep_prob_for_local_hour(hour)` returns `HEDGEHOG_SLEEP_PROB_DAY = 0.50` for `06:00 ≤ hour < 18:00` and `HEDGEHOG_SLEEP_PROB_NIGHT = 0.05` otherwise. On Walking expiry, sleep is an absolute probability; if the hedgehog does not sleep, Snuffling vs Idle is selected from the active weights `0.55 / 0.30`.

### Generation (PRNG draw order — LOCKED)

Hedgehogs are Grass-only and generated **after bunnies** on the shared `CRITTER_PRNG_SALT` stream:

```text
hasHedgehog = critterPrng.uniform(0, 1) < HEDGEHOG_COUNT_PROBABILITY
if hasHedgehog:
    xFrac    = critterPrng.uniform(0, 1)
    vxSign   = critterPrng.next_u64() & 1
    speed    = critterPrng.uniform(HEDGEHOG_WALK_SPEED_MIN, HEDGEHOG_WALK_SPEED_MAX)
    nameIndex = critterPrng.index(HEDGEHOG_NAME_POOL.size)
```

This appends one unconditional count-probability draw plus four per-entity draws if present. It does not reorder sheep, cat, or bunny draws. Butterflies, fireflies, and birds remain on independent PRNG streams.

### Startle behavior

```text
sim_apply_click after Bunny startle:
  for each Hedgehog not already Curled:
    if distance(click, hedgehog center) > HEDGEHOG_STARTLE_RADIUS: skip
    previousState = (Sleeping ? Walking : currentState)
    previousStateTimer = (Sleeping ? 0 : currentTimer)
    state = Curled
    stateTimer = uniform(HEDGEHOG_CURL_DURATION_MIN, HEDGEHOG_CURL_DURATION_MAX)
    age = 0
```

`vx` is deliberately not flipped or boosted. The hedgehog curls in place, waits, and resumes calmly after the curl duration.

### Render order

Normal pose renders body ellipse first, then 14 triangular spikes along the partial arc, then tiny head/nose/eye and four stubby legs. Facing mirrors the head/nose/eye and local spike arc horizontally based on `vx` sign.

Sleeping/Curled render a tight ball: filled circle radius `HEDGEHOG_BODY_RADIUS * 0.85`, with `HEDGEHOG_SPIKE_COUNT * 1.5 = 21` spikes around the full 360°. Sleeping adds tiny ZZZ puffs above the ball; Curled does not. No head, eyes, nose, or legs are visible in ball form.

### Defaults & conformance

Native `hedgehog_tests.cpp` and Win2D `HedgehogTests.cs` pin constants, 55% count distribution, scene gating, speed/name ranges, sheep→cat→bunny→hedgehog PRNG identity, edge bounce, curl startle/uncurl behavior, sleep wake, transition probabilities, nocturnal sleep bias, and absence of active interaction states.

---

## 18. Persistence

DesktopGrass persists calm, invisible app state automatically. There is no user-facing save UI.

### File and schema

State lives at `%LOCALAPPDATA%\\DesktopGrass\\state.json`. Both implementations write human-readable JSON with `"version": 2` and a UTC `savedAt` timestamp. Writes are atomic: serialize to `state.json.tmp` in the same directory, then replace/rename it to `state.json` so a crash mid-write does not corrupt the previous state.

Persisted fields:

- `scene`: current `Scene` enum name (`Grass`, `Desert`, `Winter`).
- `critter`: current `CritterKind` enum name (`None`, `Sheep`, `Cat`, `Bunny`).
- `critterCount`: `0` for random, `1..6` for fixed per-monitor count.
- `autoStart`: whether DesktopGrass should start with Windows; default `false`.
- `monitors`: object keyed by monitor work-area key. Each v2 monitor has `snowDepth` plus a `cuts` array of `{ bladeIndex, cutTime }` records.

### Schema versions

- v1: `version: 1`, monitor entries contain only `cuts`.
- v2: `version: 2`, monitor entries add Winter `snowDepth` in DIP. All saves write v2. Loading v1 is supported and treats missing `snowDepth` as `0` with no migration file write required.

### Load order

On startup, load `state.json` before creating any `Sim`. Apply the loaded scene, critter, and critter count to every subsequently-created sim. After each sim has generated its blade set, find the matching monitor entry, apply `snowDepth` only when the loaded scene is Winter, then apply cuts. If the file is missing or malformed, start from defaults. If `version` is neither `1` nor `2`, log a warning and start fresh; never crash.

### Save triggers

Startup does not save by itself. Save immediately after a scene, critter, critter-count, or auto-start change is applied; save every 60 seconds while running; and save once more on the quit/exit path.

### Auto-start

`autoStart` is a boolean persisted in `state.json`; on startup it is reconciled with `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` and persisted state wins. Native writes `DesktopGrass.Native` and Win2D writes `DesktopGrass.Win2D` so both implementations can autostart side-by-side.

### Monitor matching

Monitor keys use the work-area rect from monitor enumeration:

```text
{width}x{height}@{left},{top}
```

Example: `1920x1080@0,0`. If a saved monitor key does not match any current monitor (for example, a monitor was unplugged), skip that monitor's cuts silently.

### Cut time strategy

The JSON field is named `cutTime`, but saved values are shifted relative to the sim time at save: `cutTime = originalCutTime - currentGlobalTime`. A cut made 20 seconds ago is saved as `-20.0`. Loading into a fresh sim with `globalTime = 0` applies that as `cutTime = -20.0`, preserving elapsed time and allowing regrowth to resume from the correct point without storing per-monitor global clocks.

## 19. Day-night ambient tint

DesktopGrass renders a passive, full-strip day-night ambient tint as the final draw of each frame, after blades, scene entities, and critters. It is render-only: no PRNG draws, no simulation state, and no entity behavior changes.

| Phase | Hour key | RGB | Alpha |
|---|---:|---|---:|
| Night | 0.0 | (40, 50, 90) | 36 |
| Predawn | 4.0 | (60, 70, 110) | 32 |
| Sunrise | 6.0 | (255, 180, 140) | 28 |
| Morning | 8.0 | (255, 220, 160) | 16 |
| Day | 10.0 | (255, 255, 255) | 0 |
| Late afternoon | 17.0 | (240, 170, 110) | 22 |
| Sunset | 19.0 | (220, 110, 90) | 30 |
| Dusk | 20.0 | (90, 80, 130) | 28 |
| Night | 22.0 | (40, 50, 90) | 36 |

`hourFloat = localHour + localMinute / 60.0`, using the same local clock source as critter sleep bias. Normalize into `[0, 24)`, find the bracketing hour keys (wrapping at 24), and linearly interpolate RGB and alpha with `t = (hourFloat - current.startHour) / (next.startHour - current.startHour)`, using wrap-aware span math. Bands longer than two hours are calm plateaus: Night 00-04 and Day 10-17 hold their start color/alpha, so noon is truly no-tint. `DAYTINT_MAX_ALPHA` is 36 and clamps the result so the effect remains subtle.

Current builds keep the tint enabled by default (`DAYTINT_ENABLED_DEFAULT = true`) and do not add a tray toggle; therefore day tint has no `state.json` impact.

## 20. Weather — Light rain (REMOVED)

> **Removed.** The Grass-scene light-rain effect was removed. The raindrop
> emitter, `EntityKind::Raindrop` discriminant (value `5`, now a retired gap),
> the `raindropPrng` / `nextRaindropSpawnTime` state, the per-drop renderer, and
> all `RAINDROP_*` constants no longer exist in either implementation. Scene
> transitions now clear all roaming entities outright (there is no longer any
> entity preserved for a soft fade-out). The historical specification below is
> retained only for context.

<details>
<summary>Historical specification (no longer implemented)</summary>

Grass scene weather is a passive drizzle: small muted blue-gray drops drift down through the strip and disappear by lifetime expiry after passing the bottom. The effect is intentionally calm background atmosphere, not a downpour.

### Scene gating and scheduler

Rain emits only while `currentScene == Grass`. Each `Sim` owns a dedicated `raindropPrng` seeded with `entitySeed XOR RAINDROP_PRNG_SALT` and `nextRaindropSpawnTime`, initialized to `globalTime` so Grass begins emitting immediately. The emitter uses Poisson/exponential inter-arrival timing with rate:

```text
lambda = RAINDROP_EMIT_RATE_PER_1920DIP * monitorWidth / 1920.0
```

### Constants

| Constant | Value |
| --- | ---: |
| `RAINDROP_PRNG_SALT` | `0xD40F0A1DD40F0A1D` ("rain drop" mnemonic) |
| `RAINDROP_EMIT_RATE_PER_1920DIP` | 6.0 drops/sec |
| `RAINDROP_LENGTH_MIN` / `MAX` | 4.0 / 7.0 DIP |
| `RAINDROP_THICKNESS` | 0.9 DIP |
| `RAINDROP_FALL_SPEED_MIN` / `MAX` | 240.0 / 360.0 DIP/sec |
| `RAINDROP_DRIFT_MIN` / `MAX` | -8.0 / 8.0 DIP/sec |
| `RAINDROP_COLOR` | `0x88B0C4D0` ARGB |
| `RAINDROP_LIFETIME_PADDING_SEC` | 0.3 sec |

### Spawn draw order

Per raindrop, both implementations MUST draw fields in this exact order: `size`, `x`, `fallSpeed`, `vx`, `seed`; then draw the exponential next-spawn interval. Spawned drops start at `y = -size - 2`, fall downward with positive `vy`, have zero rotation, and set `lifetime = (groundY + size) / fallSpeed + RAINDROP_LIFETIME_PADDING_SEC`.

### Rendering and scene changes

Render each raindrop as a slim line from `(x, y)` to `(x - vx * 0.03, y + size)` using `RAINDROP_COLOR` and `RAINDROP_THICKNESS`; the horizontal tail suggests subtle motion blur. Switching away from Grass stops new emission but preserves existing raindrops so they softly fade out through normal lifetime expiry rather than hard-cutting.

</details>

