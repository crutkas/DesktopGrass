# DesktopGrass — Shared Algorithm Specification

This document is the **single source of truth** for the grass simulation. The four v1 implementations — `DesktopGrass.Native` (Win32 + Direct2D, C++), `DesktopGrass.Win2D` (C# + Win2D), `DesktopGrass.WinUI3` (packaged WinUI 3, C#), and `DesktopGrass.WPF` (vanilla .NET WPF, C#) — each port the algorithms below into their own language. There is no shared library; the spec **is** the contract.

For the product goals, window model, input model, and project layout, see [`plan.md`](../plan.md). This document covers only the math/state machine that every implementation must reproduce.

---

## 1. Overview

DesktopGrass paints a strip of procedurally generated grass along the bottom of every monitor, on top of all windows, fully click-through. The strip sways gently on its own, reacts to cursor motion with localized gusts, and reacts to left-clicks by cutting blades.

The v1 plan calls for **four independent implementations** of the same feature set, so they can be compared side-by-side on LoC, CPU/GPU cost, startup time, and binary size. To make that comparison honest, all four must produce **pixel-equivalent output** for a given `(seed, monitorWidth, density)` and an identical event stream. This spec fixes every constant, function, and ordering decision needed to make that true.

Pseudocode is given in a C-like form chosen to port cleanly to C++ (Native), C# (Win2D, WinUI 3, WPF), and any future Rust/Go port. Where a port idiom differs (e.g., `Math.Sin` vs `std::sin`), the spec uses the mathematical name (`sin`, `exp`, `sqrt`, `clamp`).

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
        // sequence across all four implementations for a given seed.
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
        // fixed and identical across all four implementations.
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

**Field-draw order is fixed, per stream.** From the main stream `p`, implementations MUST draw the six static fields in this exact order: `step`, `height`, `thickness`, `hue`, `swayPhaseOffset`, `stiffness`. From the regrowth stream `pr`, the order is `regrowDelay`, then `regrowDuration`. From the flower stream `pf`, the order is `isFlower` decision (always one draw), and **only if** `isFlower == true`, then `flowerHeadColorIdx`, `flowerHeadRadius`, `heightBonus`. From the mushroom stream `pm`, the order is `isMushroom` decision (always one draw), and **only if** `isMushroom == true`, then `mushroomCapColorIdx`, `mushroomCapWidth`, `mushroomCapHeight`, `mushroomStemHeight`, `mushroomStemThickness`. Reordering or interleaving the four streams changes the per-blade values for a given seed and breaks the snapshot tests. The four streams are completely independent — the main stream's draw count per blade does not depend on whether regrowth, flowers, or mushrooms are enabled.

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

The head is suppressed once the stem has been cut down past `CUT_STUMP_THRESHOLD` (the same threshold that switches the stem itself to the stump short-circuit). This keeps a cut flower from leaving a colored dot floating just above the ground. No outline / no anti-aliasing toggle is required — implementations may use whatever filled-ellipse primitive their renderer provides (`FillEllipse` on D2D, `FillCircle` on Win2D canvas, `DrawingContext.DrawEllipse` with `pen = null` on WPF). All four implementations MUST place the head **at the same tip point** the chord-preserving stroke math computed.

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

Implementations are free to use whatever line-stroke and filled-ellipse primitives their renderer provides (D2D `DrawLine` + `FillEllipse` on Native, Vortice `DrawLine` + `FillEllipse` on Win2D, Composition `CompositionLineGeometry`/`SpriteShape` + `CompositionEllipseGeometry` on WinUI 3, WPF `DrawingContext.DrawLine` + `DrawEllipse`). The stem thickness uses no caps requirement — round caps are fine, butt caps are fine. The cap MUST be filled, not stroked.

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
| `CUT_STUMP_THRESHOLD` | 0.05 | (unitless) | §7 |
| `STUMP_HEIGHT` | 2.0 | DIP | §7 |
| `MUSHROOM_STUMP_HEIGHT` | 4.0 | DIP | §7 |
| `CTRL_OFFSET_FACTOR` | 0.6 | (unitless) | §7 |
| `MAX_LEAN_FRACTION` | 0.95 | (unitless) | §7 |
| `CURSOR_REINIT_GAP_SEC` | 0.25 | sec | §8 |
| `CANONICAL_TEST_SEED` | `0x6B6173746F` | uint64 | §12 |

The palette table from §4 (six ARGB values for grass blades), the Flower palette (six ARGB values for flower heads), and the Mushroom palette (six ARGB values for caps + one `MUSHROOM_STEM_COLOR` for stems) are also part of this constants set.

---

## 12. Conformance

Because every implementation ports this spec verbatim, the unit tests in `DesktopGrass.Native.Tests`, `DesktopGrass.Win2D.Tests`, `DesktopGrass.WinUI3.Tests`, and `DesktopGrass.WPF.Tests` can share a single snapshot fixture. WPF joins as the fourth conformant implementation.

### Canonical test seed

```
CANONICAL_TEST_SEED = 0x6B6173746F  // uint64
```

Tests at minimum SHOULD assert:

1. **PRNG snapshot.** With `prng_init(seed = CANONICAL_TEST_SEED)`, the first 16 outputs of `prng_next_u64` match a fixed snapshot array embedded in each test project. Identical across all four impls.

2. **Blade generation snapshot.** With `(seed = CANONICAL_TEST_SEED, monitorWidth = 1920.0, density = 1.0)`:
   - The blade count matches across impls.
   - For the first 10 and last 10 blades, every static field (`baseX`, `height`, `thickness`, `hue`, `swayPhaseOffset`, `stiffness`) matches to within `1e-12` (double precision round-trip; should be exact).
   - `baseX` is strictly increasing.
   - All `height ∈ [6, 30]`, `thickness ∈ [1.0, 2.5]`, `hue ∈ [0, 5]`, `swayPhaseOffset ∈ [0, 2π)`, `stiffness ∈ [0.6, 1.0]`.

3. **Sway determinism.** Given a fixed blade and `globalTime`, `effectiveLean` (with `gustVelocity = 0`) matches across impls to within `1e-9`. The bound is loose because `sin` may differ in last-bit precision between CRTs.

4. **Gust impulse.** Feeding a synthetic move event with a known `(prevCursorX, cursorX, dt_ev)` produces `gustVelocity` deltas on each blade matching a snapshot to within `1e-9`.

5. **Cut animation.** A click at `(clickX = 500, clickY = groundY - 40, time = 0)` followed by `tick(dt = 0.05)` calls for 5 frames produces the expected `cutHeight` series for blades within `CUT_RADIUS` (linear from 1.0 to 0.0 across 4 frames, then 0.0 thereafter), and leaves blades outside the radius at `cutHeight = 1.0`.

6. **Idempotence.** Clicking twice on the same blade within the 200 ms cut window does not change the trajectory of the first cut. Clicking on an already-cut blade is a no-op.

When any test in this list fails on a single impl, that impl has diverged from the spec — fix the impl, not the spec, unless the divergence reveals a spec ambiguity, in which case update this document first and propagate the fix to all four impls.
