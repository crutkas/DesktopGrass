# DesktopGrass implementation comparison

> **Current support decision:** [`DesktopGrass.Native`](../src/DesktopGrass.Native)
> is the supported standalone product and the only PowerToys candidate.
> [`DesktopGrass.Win2D`](../src/DesktopGrass.Win2D) is retained only as a
> source-available managed comparison/reference. It has no required product CI,
> feature-parity, platform-hardening, release, or downloadable-artifact
> commitment. WinUI 3 and WPF were removed after the historical comparison.

This document separates the **current checkout** from the **historical
comparison snapshot** that led to the Native decision. Historical measurements
remain useful selection evidence, but they are not current product claims.

## Current checkout

### Support and toolchain

| Concern | Supported Native | Managed reference |
| --- | --- | --- |
| Project | `src\DesktopGrass.Native\DesktopGrass.Native.vcxproj` | `src\DesktopGrass.Win2D\DesktopGrass.Win2D.csproj` |
| Role | Supported product; required CI; release artifacts; PowerToys candidate | Reproducible reference only; optional advisory smoke |
| Stack | C++17, Win32, D3D11, DXGI, Direct2D, DirectComposition | C# 12, Win32 interop, D3D11, DXGI, Direct2D, DirectComposition |
| Build target | MSVC PlatformToolset `v145`; Windows SDK `10.0`; x64 and ARM64 | .NET SDK requested by `global.json`: 10.0.300 with compatible feature-band roll-forward; x64 and ARM64 |
| App target framework | Native Win32 executable | `net10.0-windows10.0.19041.0`; minimum Windows version `10.0.17763.0` |
| Graphics packages | No vcpkg-managed runtime dependencies | `Vortice.Direct2D1`, `Vortice.Direct3D11`, `Vortice.DXGI`, and `Vortice.DirectComposition`, all 3.6.2 |
| Test framework | Microsoft Visual Studio C++ Unit Test Framework | xUnit on `net10.0` |
| Verified x64 Release tests | **368 passed** | **299 passed** |

The verified test counts are runner-reported test cases at this revision, not
source-method counts. Parameterized managed theories can produce more cases than
the number of `[Fact]`/`[Theory]` methods.

The default [`DesktopGrass.slnx`](../DesktopGrass.slnx) intentionally contains
only Native and its tests. The required CI workflow builds/tests Native and
publishes x64 and ARM64 Native artifacts. Its managed build/test recipe is
commented for manual reproduction. The local interactive smoke harness may run
Managed as a non-blocking advisory comparison.

### Output paths and payload shape

| Output | Repository-root path | Notes |
| --- | --- | --- |
| Native app | `src\DesktopGrass.Native\out\<Platform>\<Configuration>\DesktopGrass.Native.exe` | Release uses the static CRT (`/MT`); no VC++ redistributable or vcpkg runtime payload |
| Native tests | `tests\DesktopGrass.Native.Tests\out\<Platform>\<Configuration>\DesktopGrass.Native.Tests.dll` | Run through `tests\DesktopGrass.Native.Tests\Run-Tests.ps1` and Visual Studio VSTest |
| Managed app | `src\DesktopGrass.Win2D\bin\<Platform>\<Configuration>\net10.0-windows10.0.19041.0\DesktopGrass.Win2D.exe` | Framework-dependent reference output with .NET/Vortice companion DLLs |
| Managed tests | `tests\DesktopGrass.Win2D.Tests\bin\<Configuration>\net10.0\DesktopGrass.Win2D.Tests.dll` | Plain `net10.0` test assembly |

For scale only, the verified 2026-07-17 x64 Release build produced a
1,347,072-byte Native executable. The managed build folder contained 15 files
and 28,582,704 bytes including its PDB; the managed apphost itself was 533,504
bytes. These values depend on the installed toolchain and restored packages and
are not release-size guarantees. The paths and dependency shapes above are the
stable contract.

### Cadence defaults

Both projects default `targetFps` to **24**, accept configured values from 5
through 144, and default blade density to **2.53125**. Native additionally
applies production runtime caps:

- 12 FPS on battery or short-term/UPS power;
- 5 FPS under Battery Saver or a dimmed display; and
- zero rendering while suspended, locked/disconnected, display-off,
  fullscreen-covered, or fully occluded.

The Native `--benchmark` mode also defaults to 24 FPS. It does not exercise
those production suppression policies.

## Current verification boundaries

### Smoke

[`tests/smoke/Run-SmokeTests.ps1`](../tests/smoke/Run-SmokeTests.ps1)
resolves the architecture-qualified output paths above. Native is the product
gate; Managed is optional. The harness checks required extended window styles,
Native per-monitor window count and work-area/DPI geometry, and screenshot pixel
variance with a 50-color minimum.

The harness's `DurationMs` covers the entire smoke operation: optional
pre-launch work, process launch, window discovery, assertions, screenshot
polling, and shutdown/cleanup. It is **not** a startup or
launch-to-first-frame metric.

### Benchmark

[`tools/benchmark`](../tools/benchmark/README.md) collects Native-only
performance and energy evidence.

- `--benchmark` uses one primary-monitor window and bypasses the tray, mouse
  hook, persistence, multi-monitor lifecycle, and production runtime policy.
- Its requested duration and reported `duration_s` begin after window/renderer
  setup and end before teardown.
- The external driver's first counter interval is priming because it spans
  startup; it is excluded from measured samples.
- `WallSec` includes startup, rendering, teardown, and scheduling overhead and
  is diagnostic process wall time.
- No field isolates startup, launch-to-window, or launch-to-first-frame
  duration. Subtracting render duration from `WallSec` is not a valid startup
  measurement.
- The production-runtime qualification path exercises the unmodified Native
  app, but display-dim, lock/display-off, and suspend/resume transitions still
  require separately controlled manual evidence.

Consequently, neither the smoke harness nor the benchmark can support a current
startup-performance claim or a current Native-versus-managed runtime comparison.

## Historical comparison snapshot

The repository originally explored four independent implementations for the
same transparent, click-through, topmost overlay:

1. Native Win32 + Direct2D/DirectComposition.
2. Managed C# using Vortice over the same low-level graphics stack.
3. WinUI 3 using Windows App SDK/XAML/Composition.
4. WPF.

The comparison-era measurements below are preserved as historical evidence.
They describe the implementation state at the time and must not be combined
with the current counts, binaries, or toolchains above.

| Historical metric | Native | Managed/Vortice | WinUI 3 | WPF |
| --- | ---: | ---: | ---: | ---: |
| Track-reported source LoC headline | 2,221 | 1,030 | 2,433 | Not standardized |
| Release executable | 39,936 B | 152,576 B | 272,384 B | Not measured |
| Unit-test headline | 34 cases / 58,266 assertions | 38 cases | 42 cases | Not retained |
| Initial smoke unique colors | 11,642 | 11,642 | 11,642 | Not part of that run |
| Later steady-state working set | 55 MB | 99 MB | 158 MB | 579 MB |

The original source-count scopes were not fully standardized. Native excluded
the icon and then-vendored Catch2 header; managed-track reports did not always
use the same app/test/interop boundaries. The exact values are useful only as
the archived comparison headline.

The 11,642-color result was a screenshot conformance signal, not pixel-perfect
proof. The ports used the same xorshift64 PRNG, canonical seed
`0x6B6173746F`, and then-current blade/sway/gust/cut math. Later visual tuning
changed the absolute color counts.

Runtime CPU, GPU, energy, startup, and long-run stability were **not formally
measured in the original comparison**. The historical apps targeted 60 FPS and
typically crossed the smoke rendering threshold within a few seconds, but that
end-to-end observation was never a startup benchmark.

## Why Native was selected

### Direct fit for the window model

Native directly owns the popup HWND and applies:

- `WS_EX_LAYERED`
- `WS_EX_TRANSPARENT`
- `WS_EX_TOPMOST`
- `WS_EX_TOOLWINDOW`
- `WS_EX_NOACTIVATE`

It creates the D3D11 device, DXGI composition swap chain, Direct2D device
context, and DirectComposition visual/target without a framework-owned window
or XAML layer. That maps closely to the product requirement.

The managed reference proved that C# plus Vortice can mirror the same design,
but it needs hand-written P/Invoke, managed delegate lifetime handling, a .NET
runtime, and the Vortice/SharpGen payload. It remains useful as a readable
comparison and frozen deterministic baseline, not as a second product.

WinUI 3 required framework-window style retrofits, Composition geometry glue,
and a much larger deployment shape for an app that does not need XAML controls
or the broader Windows App SDK model. WPF had the largest measured working set.
Those results reflect DesktopGrass's unusually low-level overlay shape, not a
general judgment on either UI framework.

### Product ownership

Native now owns product behavior, runtime suppression, monitor/DPI hardening,
accessibility validation, smoke/soak/fault qualification, release artifacts,
and the PowerToys migration path. Native behavior may intentionally advance
beyond the managed freeze point without a backport.

Managed may receive narrowly scoped build or test maintenance needed to keep
the reference reproducible. Such maintenance does not change its support
status.

## Reproducing the managed reference

From the repository root:

```powershell
dotnet build src\DesktopGrass.Win2D\DesktopGrass.Win2D.csproj -c Release -p:Platform=x64
dotnet test tests\DesktopGrass.Win2D.Tests\DesktopGrass.Win2D.Tests.csproj -c Release --nologo
```

These commands validate source availability and the frozen test baseline. They
do not produce a supported release or a product gate.
