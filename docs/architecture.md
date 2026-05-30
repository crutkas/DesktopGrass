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
        if (b->cutHeight <= 0.0) continue;                   // stump, waiting to regrow
        if (b->cutAnimStart >= 0.0) continue;                // already animating a cut

        b->cutAnimStart     = globalTime;
        b->cutInitialHeight = b->cutHeight;
        b->regrowStart      = -1.0;   // cancel any pending regrowth — phase 1
                                      // will reschedule a fresh delay on completion.
    }
}
```

`CUT_RADIUS = 30` DIP. The "already animating" guard makes repeated clicks on an in-flight blade a no-op — the original 200 ms animation runs to completion. The `cutHeight <= 0` check makes clicks on a stump (cut, in delay) a no-op; clicks on a mid-regrowing blade (`cutHeight ∈ (0,1)`) do re-cut, and the cancellation of `regrowStart` keeps phase 2 from firing on top of phase 1.

### Advance animation (per frame)

Called from `tick()` (see §10) after `globalTime` is updated. Each blade is either idle, in phase 1 (cut animation), in the regrowth delay (idle with `regrowStart > globalTime`), or in phase 2 (regrowth animation):

```c
void advance_cut(Blade* b, double globalTime) {
    // Phase 1: cut animation.
    if (b->cutAnimStart >= 0.0) {
        double elapsed = globalTime - b->cutAnimStart;
        double t = elapsed / CUT_DURATION_SEC;       // 0..1 across 200 ms
        if (t >= 1.0) {
            b->cutHeight    = 0.0;
            b->cutAnimStart = -1.0;
            // Schedule regrowth — but only if this blade has valid jitter values.
            // (A zero-initialized Blade with regrowDelay == regrowDuration == 0
            // stays a permanent stump; this is the v1 test-fixture path.)
            if (b->regrowDelay > 0.0 && b->regrowDuration > 0.0) {
                b->regrowStart = globalTime + b->regrowDelay;
            }
        } else {
            b->cutHeight = b->cutInitialHeight * (1.0 - t);  // linear to 0
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
            b->cutHeight = t;                        // linear 0 → 1
        }
    }
}
```

`CUT_DURATION_SEC = 0.2` sec. `REGROW_DELAY_*` and `REGROW_DURATION_*` (see §11) bracket per-blade jitter sampled in §5. Both animations use linear easing — the cut is brief enough that polynomial easing doesn't pay for itself, and the regrowth is slow enough that the eye doesn't read a curve.

### Lifecycle

For a single click on an uncut blade:
1. `apply_click` sets `cutAnimStart = globalTime`, leaves `regrowStart = -1`.
2. Over `CUT_DURATION_SEC` (200 ms), phase 1 drives `cutHeight` from its current value to 0.
3. On phase 1 completion, `cutHeight = 0`, `cutAnimStart = -1`, and `regrowStart = globalTime + regrowDelay`.
4. For `regrowDelay` seconds (30–90 s, per-blade), the blade is a stump.
5. Once `globalTime >= regrowStart`, phase 2 drives `cutHeight` from 0 to 1 over `regrowDuration` seconds (2–4 s, per-blade).
6. On phase 2 completion, `cutHeight = 1`, `regrowStart = -1`. The blade is uncut and clickable again.

For a click during phase 2 (re-cut a mid-regrowing blade): `apply_click` records the current `cutHeight` as `cutInitialHeight`, sets `cutAnimStart = globalTime`, and clears `regrowStart`. Phase 1 runs back to 0 from wherever the blade was, and a new regrowth cycle is scheduled on completion.

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
| `TUMBLEWEED_SPEED_MIN` | 40.0 | DIP/sec | §14 |
| `TUMBLEWEED_SPEED_MAX` | 120.0 | DIP/sec | §14 |
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
| `SNOW_TIP_RADIUS_FACTOR` | 1.25 | × `blade.thickness` | §15 |
| `SNOW_TIP_COLOR` | `0xFFFFFFFF` | uint32 ARGB | §15 |
| `DESERT_GRASS_HEIGHT_SCALE` | 0.5 | (unitless) | §14 |
| `WINTER_GRASS_HEIGHT_SCALE` | 0.5 | (unitless) | §15 |
| `PINE_PROBABILITY` | 0.0075 | (unitless) | §15.1 |
| `PINE_HEIGHT_MIN` | 45.0 | DIP | §15.1 |
| `PINE_HEIGHT_MAX` | 90.0 | DIP | §15.1 |
| `PINE_WIDTH_MIN` | 16.0 | DIP | §15.1 |
| `PINE_WIDTH_MAX` | 28.0 | DIP | §15.1 |
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

The simulation supports a small fixed set of visual "scenes" that share the same blade generation, sway physics, gust/cut model, and ambient-gust scheduler from §§4–10 + §8.1, but differ in render-time presentation. The infrastructure pass only swaps the blade palette per scene; subsequent scene-specific sections (§14 Desert, §15 Winter) add entities and props on top.

### Scene enum

```
enum Scene : uint8_t {
    Grass  = 0,   // default; the original green field from §§4–10
    Desert = 1,   // dried-grass / sand palette; cacti & tumbleweeds in §14
    Winter = 2,   // frosted / snowy palette; snowflakes in §15
};
```

Both impls MUST use these exact discriminant values so the cross-impl conformance tests in §12 can compare an integer scene id.

### Per-scene blade palette

Each scene defines a 6-color ARGB palette indexed by `blade.hue`. The `hue` index is still drawn from the main PRNG stream as per §5 — generation is scene-independent — but the renderer selects which palette table to look up based on `sim.currentScene`:

```c
uint32_t argb = SCENE_PALETTE[sim->currentScene][blade.hue];
```

The Grass palette is the original §4 palette (unchanged). Desert and Winter palettes are listed in §11.

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
└── Quit
```

The active scene shows a radio-style mark (`MFS_CHECKED` / `MF_BYCOMMAND` in Win32; `Checked = true` in WinForms `ToolStripMenuItem`). Clicking another scene item is an atomic broadcast to every monitor window: `set_scene(sim, newScene)` runs on each `Sim` before the next frame.

### Cross-impl conformance

- The `Scene` enum values are exactly `{ Grass = 0, Desert = 1, Winter = 2 }` in both impls.
- The Desert and Winter blade palette tables in §11 are bit-identical between impls.
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

§13 had no notion of entities that move independently of the blade slots. The Desert and Winter scenes introduce two such types (tumbleweeds and snowflakes), so the simulation now carries an `entities` vector alongside `blades`.

### Entity model

```cpp
enum EntityKind : uint8_t {
    EntityNone       = 0,
    EntityTumbleweed = 1,
    EntitySnowflake  = 2,
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
- Generated by `set_scene` (and `sim_init` indirectly via the default `currentScene = Grass` which generates nothing).
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

remove entities where e.kind == Snowflake and (e.age >= e.lifetime or e.y > groundY)
emit new snowflakes per §15 schedule
```

The emitter and respawn paths are the only places entities are added/removed during a tick.

### Cross-impl conformance for entities

- `EntityKind` discriminants `{None=0, Tumbleweed=1, Snowflake=2}` match exactly.
- Entity-stream PRNG salts (`TUMBLEWEED_PRNG_SALT`, `SNOWFLAKE_PRNG_SALT`, `CACTUS_PRNG_SALT`) are global constants — both impls draw entity parameters from streams seeded `seed XOR salt`.
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
- Switching scenes away from Winter clears `sim.entities` and resets the snowflake emitter.

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

## 13.3 Critter subsystem (orthogonal to Scene)

Critters are user-pickable pets that wander on top of the bottom strip *independent of which biome is active*. The user toggles a critter from a dedicated tray submenu and it persists across scene changes — selecting **Winter** does not clear the active critter, selecting **Sheep** does not clear snowflakes, and selecting **Cat** proves the framework is species-pluggable.

### Enum and salt

```
enum CritterKind { None = 0, Sheep = 1, Cat = 2 }
constexpr int         CRITTER_COUNT = 3
constexpr CritterKind CRITTER_DEFAULT  = CritterKind::None
constexpr uint64_t    CRITTER_PRNG_SALT = 0x5C8EE05C8EE05C8E
```

Discriminants are cross-impl-locked. `CritterKind::Sheep` maps to `EntityKind::Sheep = 3`; `CritterKind::Cat` maps to `EntityKind::Cat = 4`. The `EntityKind` discriminants for existing kinds MUST NOT be renumbered.

### Sim state

```
Sim:
  CritterKind currentCritter = CRITTER_DEFAULT
  Prng        critterPrng                   // reseeded per generator call
  int         critterCountOverride = 0      // 0=random, 1..PET_COUNT_MAX_PER_MONITOR=fixed
```

### Generator dispatcher

`generate_critters_for_kind(sim)` is called by `sim_set_scene` as its **last** step (after biome generators) and by `sim_set_critter(sim, c)` after removing only critter entities from `sim.entities`. The dispatcher reseeds `critterPrng = Prng(entitySeed XOR CRITTER_PRNG_SALT)` and then dispatches:

```
None  → no-op
Sheep → generate_critters_sheep(sim)        // §16
Cat   → generate_critters_cat(sim)          // §17
```

### Ordering invariant

Inside `sim_set_scene`:

1. `entities.clear()`
2. Restore default blade variants.
3. Run the scene generator (tumbleweeds, snowflakes, etc.).
4. **Run `generate_critters_for_kind(sim)` LAST.**

Step 4 must be last so that `entities[0..N-1]` for scene entities (tumbleweeds, snowflakes) is bit-identical to the snapshot tests pinned in §12 regardless of which critter is active.

### `sim_set_critter` semantics

```
sim_set_critter(sim, c):
    sim.currentCritter = c
    remove every entity e where e.kind is a critter species (Sheep or Cat)
    generate_critters_for_kind(sim)
```

Scene entities (tumbleweeds, snowflakes) are NEVER removed by `sim_set_critter`. Critter entities are NEVER removed by `sim_set_scene` (the dispatcher just regenerates them).

Count override — Sim may carry a `critterCountOverride` (1-`PET_COUNT_MAX_PER_MONITOR` or 0 for random). When non-zero, the count PRNG draw in the species generator is skipped. Changing the override calls `sim_set_critter_count`, removes current critter entities, and regenerates the active species.

### Tray menu

A `Critter` submenu (radio-style) under the tray icon offers **None**, **Sheep**, and **Cat**, plus a **Pet count** picker (**Random**, **1**–**6**) that applies to the active species. Selection broadcasts to all monitor windows and calls `sim_set_critter(currentCritter)` or `sim_set_critter_count(n)` on each window's Sim.

### Cross-impl conformance

Given identical `(seed, monitorWidth, CritterKind)`, both impls MUST produce the same number of critter entities with the same per-entity field values, drawn from `Prng(seed XOR CRITTER_PRNG_SALT)` in the species-specific draw order (§16 for sheep, §17 for cat).

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

Procedural calm tabby cat. Cat exists to prove the Critter framework is species-pluggable while preserving the passive desktop philosophy: cats mostly walk, sit, or sleep. They never chase cursor proximity; pounce is click-only.

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
| `CAT_BODY_COLOR` / `CAT_FACE_COLOR` | `0xFF6B6259` | muted warm gray |
| `CAT_LEG_COLOR` / `CAT_EAR_COLOR` | `0xFF3D3733` | darker gray |
| `CAT_INK_COLOR` | `0xFF1A1614` | eyes/nose |
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
        state      = CAT_STATE_WALKING
```

If `critterCountOverride` is non-zero, `count = min(critterCountOverride, PET_COUNT_MAX_PER_MONITOR)` and the count draw above is skipped. This matches the sheep draw sequence exactly with cat constants substituted. Species share the same `CRITTER_PRNG_SALT`; no per-species salt is introduced.

Per-pet name — assigned from `CAT_NAME_POOL = ["Mittens", "Whiskers", "Shadow", "Ginger", "Smokey", "Boots", "Sage", "Juno"]` at generation (one PRNG draw after `stateTimer`). Rendered as a tiny label above the pet when cursor is within `PET_NAME_HOVER_RADIUS=50` DIP; fades out over `PET_NAME_FADE_DURATION=1.5s` after cursor leaves.

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

Geometry is distinct from sheep: one long flattened body ellipse, smaller circular head on the top-front, tall triangle ears, visible ink eyes/nose on a light face, four thin legs, and a long curved tail. Native and Win2D use vector primitives only.

### Differences from Sheep

* No grazing state.
* No greeting — cats do not greet cats or sheep.
* Longer sleeps and higher Idle→Sleep probabilities, especially at night (`0.85`).
* Click pounce moves **toward** the click; sheep startle moves **away**.
* Passive by design: no proximity-triggered chase or pounce.

### Defaults & conformance

For `CANONICAL_TEST_SEED + monitorWidth = 1920`, `sim_set_critter(Cat)` produces `K ∈ [CAT_COUNT_MIN, CAT_COUNT_MAX]`. Both impls produce bit-identical `(x, vx, seed, stateTimer, nameIndex)` per cat when walking the same `Prng(CANONICAL_TEST_SEED XOR CRITTER_PRNG_SALT)` side stream in the documented draw order. Both impls' test suites verify this in `cat_tests` / `CatTests`.

---

## 18. Persistence

DesktopGrass persists calm, invisible app state automatically. There is no user-facing save UI.

### File and schema

State lives at `%LOCALAPPDATA%\\DesktopGrass\\state.json`. Both implementations write human-readable JSON with `"version": 1` and a UTC `savedAt` timestamp. Writes are atomic: serialize to `state.json.tmp` in the same directory, then replace/rename it to `state.json` so a crash mid-write does not corrupt the previous state.

Persisted fields:

- `scene`: current `Scene` enum name (`Grass`, `Desert`, `Winter`).
- `critter`: current `CritterKind` enum name (`None`, `Sheep`, `Cat`).
- `critterCount`: `0` for random, `1..6` for fixed per-monitor count.
- `autoStart`: bool placeholder for the upcoming startup feature; default `false`.
- `monitors`: object keyed by monitor work-area key, each with a `cuts` array of `{ bladeIndex, cutTime }` records.

### Load order

On startup, load `state.json` before creating any `Sim`. Apply the loaded scene, critter, and critter count to every subsequently-created sim. After each sim has generated its blade set, find the matching monitor entry and apply its cuts. If the file is missing or malformed, start from defaults. If `version` is not `1`, log a warning and start fresh; never crash.

### Save triggers

Startup does not save by itself. Save immediately after a scene, critter, or critter-count change is applied; save every 60 seconds while running; and save once more on the quit/exit path.

### Monitor matching

Monitor keys use the work-area rect from monitor enumeration:

```text
{width}x{height}@{left},{top}
```

Example: `1920x1080@0,0`. If a saved monitor key does not match any current monitor (for example, a monitor was unplugged), skip that monitor's cuts silently.

### Cut time strategy

The JSON field is named `cutTime`, but saved values are shifted relative to the sim time at save: `cutTime = originalCutTime - currentGlobalTime`. A cut made 20 seconds ago is saved as `-20.0`. Loading into a fresh sim with `globalTime = 0` applies that as `cutTime = -20.0`, preserving elapsed time and allowing regrowth to resume from the correct point without storing per-monitor global clocks.

