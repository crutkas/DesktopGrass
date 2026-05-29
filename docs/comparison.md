# DesktopGrass implementation comparison

> **Status (post-comparison):** the WinUI 3 and WPF implementations were **removed from `main`** after the head-to-head A/B documented below. Only Native and Win2D continue to ship. A second round of evaluation done after this document was written added a steady-state working-set measurement (Native 55 MB, Win2D 99 MB, WinUI 3 158 MB, WPF 579 MB) and confirmed the build-experience friction described here was matched by a runtime-footprint penalty. The WinUI 3 / WPF rows below are retained as the historical record that justifies the removal — do not treat them as live impls.

This is a side-by-side comparison of the four DesktopGrass implementations as they stood at the comparison point on `main`.

## TL;DR

| Impl | LoC headline | Release exe | Dependencies | Idiomatic fit | Friction level |
| --- | ---: | ---: | --- | --- | --- |
| Native (`src\DesktopGrass.Native`) | 2,221 | 39 KB | Win32 + Direct2D + DirectComposition; no vcpkg runtime deps | Best fit for a transparent, click-through, topmost desktop overlay | Low |
| Win2D-ish / Vortice (`src\DesktopGrass.Win2D`) | 1,030 track headline; 1,823 current full tree | 152 KB | .NET 8 WindowsDesktop + Vortice Direct2D/DXGI/DComp + WinForms tray | Good managed mirror of the native model while retaining HWND/DComp control | Medium |
| WinUI 3 (`src\DesktopGrass.WinUI3`) | 2,433 track headline; 2,124 current full tree | 272 KB | WinAppSDK + Microsoft.UI.Composition + Win2D geometry + H.NotifyIcon | Good when you actually want the WinUI app model; awkward for a raw overlay | High |

## Methodology

Measured/inspected:

- Source lines with a PowerShell equivalent of `git ls-files | rg 'src/DesktopGrass\.<name>/' | xargs wc -l`, plus matching test-project counts.
- Release exe sizes with `Get-ChildItem -Recurse -Filter *.exe` under each implementation output directory.
- Deploy-output payload from the current `Release` output folders.
- Package/project references from the `.vcxproj`, `.csproj`, `vcpkg.json`, and WinAppSDK target-stub files.
- Window, rendering, mouse-hook, and tray behavior from the checked-in source.
- Commit cadence from `git log --oneline --no-decorate`.
- Smoke-harness behavior from `tests\smoke\Smoke.Common.psm1` and `tests\smoke\Run-SmokeTests.ps1`.

Not formally measured in v1:

- Runtime CPU, GPU, memory, startup time, and long-run stability. The runtime notes below are smoke-run impressions only: the apps target 60 fps, run roughly 600 blades on a 1920 px monitor (after the `DEFAULT_DENSITY=2.25` density bump), and all four pass the screenshot pixel-variance gate within about three seconds of launch. Formal CPU/GPU profiling is a v2 follow-up.

Conformance signal:

- The original v1 smoke runs reported the same `11,642 unique colors` for all three implementations on `main` at the time. That is not a full pixel-perfect proof, but it is the v1 conformance signal: all four ports now use the same xorshift64 PRNG, the same canonical seed (`0x6B6173746F`), and the same blade/sway/gust/cut/regrowth math from `docs\architecture.md`, yielding a near-identical bottom-strip pixel distribution. Subsequent tunings (chord-preserving bend, softer gust, larger amplitude, higher density) have shifted the absolute count downward but the four impls remain within ~25% of each other per run.

Counting note:

- The Native headline LoC reproduces exactly from the current tree when the binary icon and vendored Catch2 header are excluded.
- The Win2D and WinUI 3 track-agent headline LoC counts use a different scope than the exact current `git ls-files` tables below. The tables below are the current-tree counts and include interop/build glue and tests where shown.

## Measured summary

| Metric | Native | Win2D-ish / Vortice | WinUI 3 |
| --- | ---: | ---: | ---: |
| Track-reported source LoC | 2,221 | 1,030 | 2,433 (1,637 app + 796 tests) |
| Current `git ls-files` source tree | 1,511 app + 710 tests | 1,229 app + 594 tests | 1,412 app + 712 tests |
| Release exe size | 39,936 B | 152,576 B | 272,384 B |
| Unit tests | 34 cases / 58,266 assertions | 38 cases | 42 cases |
| Unit test runtime | ms-scale | 27 ms | 25 ms |
| Smoke unique colors | 11,642 | 11,642 | 11,642 |
| Build time | 4.5 s app / 7.0 s tests | not captured | not captured |

## LoC breakdown

These are file-level counts from the current checkout. Mixed-purpose files are assigned to their primary role; Native tray code lives in `App.cpp`/`App.h`, so it is included under lifecycle rather than counted twice.

### Native

| Category | Files | Lines |
| --- | --- | ---: |
| Window/lifecycle | `src\App.cpp` (268), `src\App.h` (50), `src\GrassWindow.cpp` (128), `src\GrassWindow.h` (43), `src\main.cpp` (27) | 516 |
| Renderer | `src\Renderer.cpp` (271), `src\Renderer.h` (75) | 346 |
| Simulation core | `src\Constants.h` (50), `src\Sim.cpp` (213), `src\Sim.h` (112) | 375 |
| Mouse hook | `src\MouseHook.cpp` (60), `src\MouseHook.h` (58) | 118 |
| Tray | `Shell_NotifyIconW` code is in `src\App.cpp`/`src\App.h` | included above |
| Interop/glue/build | `DesktopGrass.Native.vcxproj` (108), `DesktopGrass.Native.rc` (8), `resource.h` (4), `app.manifest` (30), `vcpkg.json` (6) | 156 |
| Tests | test `.vcxproj` (85), `snapshot_gen.cpp` (46), `blade_gen_tests.cpp` (98), `cut_tests.cpp` (138), `gust_tests.cpp` (112), test `main.cpp` (6), `prng_tests.cpp` (69), `snapshot_data.h` (70), `sway_tests.cpp` (80), test `vcpkg.json` (6) | 710 |
| **Total** | Excludes `res\icon.ico`, `third_party\catch2\catch.hpp`, and the Catch2 README | **2,221** |

### Win2D-ish / Vortice

| Category | Files | Lines |
| --- | --- | ---: |
| Window/lifecycle | `App.cs` (244), `Program.cs` (23) | 267 |
| Renderer | `GrassWindow.cs` (244) | 244 |
| Simulation core | `Constants.cs` (52), `Sim.cs` (244) | 296 |
| Mouse hook | `MouseHook.cs` (98) | 98 |
| Tray | `TrayIcon.cs` (86) | 86 |
| Interop/glue/build | `Interop\User32.cs` (178), `DesktopGrass.Win2D.csproj` (37), `app.manifest` (23) | 238 |
| Tests | test `.csproj` (34), `InternalsVisible.cs` (7), `BladeGenTests.cs` (94), `CutTests.cs` (165), `GustTests.cs` (117), `PrngTests.cs` (87), `SwayTests.cs` (90) | 594 |
| **Total current tree** | Source + tests | **1,823** |

### WinUI 3

| Category | Files | Lines |
| --- | --- | ---: |
| Window/lifecycle | `App.xaml` (8), `App.xaml.cs` (55), `MainWindow.xaml` (10), `MainWindow.xaml.cs` (120), `MonitorEnumerator.cs` (25) | 218 |
| Renderer | `GrassRenderer.cs` (109) | 109 |
| Simulation core | `Constants.cs` (62), `Sim.cs` (328) | 390 |
| Mouse hook | `MouseHook.cs` (87) | 87 |
| Tray | `TrayHost.cs` (64) | 64 |
| Interop/glue/build | `InternalsVisibleTo.cs` (8), `Interop\Types.cs` (36), `Interop\User32.cs` (56), `WindowAttacher.cs` (71), `DesktopGrass.WinUI3.csproj` (54), `WinAppSdkTaskStubs.targets` (299), `app.manifest` (20) | 544 |
| Tests | test `.csproj` (38), `BladeGenTests.cs` (125), `CutTests.cs` (129), `GustTests.cs` (131), `PrngTests.cs` (96), `StrokeTests.cs` (94), `SwayTests.cs` (99) | 712 |
| **Total current tree** | Source + tests | **2,124** |

## Binary size and dependencies

| Impl | Release exe | Current deploy/output payload | Packages and notable DLLs | Runtime requirements |
| --- | ---: | ---: | --- | --- |
| Native | 39,936 B | 39,936 B exe-only; `out\Release` is 1,920,000 B including PDB | `vcpkg.json` has no runtime deps. Tests vendor single-header Catch2. Links Win32, D3D11, DXGI, D2D1, DComp, Shell32, Shcore. | Windows with Direct2D/DirectComposition/D3D11; MSVC runtime because Release uses `/MD`. |
| Win2D-ish / Vortice | 152,576 B | 27,316,872 B framework-dependent `bin\Release\net8.0-windows10.0.19041.0` output, 15 files | `Vortice.Direct2D1`, `Vortice.Direct3D11`, `Vortice.DXGI`, `Vortice.DirectComposition` 3.6.2; SharpGen runtime DLLs; `Microsoft.Windows.SDK.NET.dll`; tests use xUnit. | .NET 8 WindowsDesktop/WinForms runtime; Windows 10 1809+ APIs. |
| WinUI 3 | 272,384 B | 168,842,065 B self-contained `bin\Release\net8.0-windows10.0.19041.0\win-x64` output, 447 files | `Microsoft.WindowsAppSDK` 1.6.250108002, `Microsoft.Windows.SDK.BuildTools`, `Microsoft.Graphics.Win2D`, `H.NotifyIcon.WinUI`; output carries WinUI/XAML and .NET runtime payload. | Current project is `WindowsPackageType=None`, `SelfContained=true`, `WindowsAppSDKSelfContained=true`. A framework-dependent packaged variant would require the WinAppSDK runtime, 1.5+ class / 1.6 for this package line. |
| WPF | (not measured) | (not measured) framework-dependent `bin\Release\net10.0-windows10.0.19041.0` output | No package references; uses WindowsDesktop WPF plus WindowsForms `NotifyIcon`; tests use xUnit. | .NET 10 WindowsDesktop/WPF runtime; Windows 10 1809+ APIs. |

## Build experience

| Impl | Build path | Commit cadence on `main` | Observations |
| --- | --- | --- | --- |
| Native | `msbuild` over the `.vcxproj`/solution, x64 Release, toolset `v145` | 8 native-specific commits from bootstrap through smoke registration | Most direct build. The project is conventional C++/MSBuild, uses Windows SDK libraries, and keeps Catch2 vendored for offline tests. Track agents measured 4.5 s app build and 7.0 s tests. |
| Win2D-ish / Vortice | `dotnet build` on `DesktopGrass.Win2D.csproj` | 7 Win2D-specific commits: bootstrap, sim, tests, Vortice switch, interop, hook/tray, renderer/window | The project name stayed "Win2D" as the comparison track label, but the renderer uses Vortice Direct2D/DXGI/DComp. The win2d-track summary explicitly called **"Vortice is the path of least resistance"** and described the alternative as bridging Microsoft Win2D `CanvasSwapChain` through WinRT/CsWinRT. The checked-in `.csproj` repeats that this avoids the WinRT/CsWinRT bridge needed to feed a Vortice swap chain into a Win2D `CanvasSwapChain`. |
| WinUI 3 | `dotnet build` with explicit SDK imports plus `WinAppSdkTaskStubs.targets` | 2 WinUI implementation/test commits plus 1 smoke-harness commit | This was the noisiest build. `DesktopGrass.WinUI3.csproj` uses explicit `Sdk.props`/`Sdk.targets` imports so `WinAppSdkTaskStubs.targets` can be imported after WindowsAppSDK targets. The stubs override `MrtCore.PriGen.targets` paths that point at Visual Studio-only Appx/Pri task assemblies. That is exactly the kind of non-product-code friction this comparison was meant to reveal. |

The smoke harness also changed for WinUI 3: `Smoke.Common.psm1` gained `-TitleMatch` support by enumerating all top-level windows for a process and regex-matching titles; `Invoke-AppSmoke` gained a `BeforeLaunch` hook; `Run-SmokeTests.ps1` gained RID-aware .NET exe resolution so WinUI 3's `bin\Release\<TFM>\win-x64` layout is found.

## Window model: the actually interesting part

### Native

The native implementation is the clean baseline. `GrassWindow::Create` calls `CreateWindowExW` directly with:

- `WS_EX_LAYERED`
- `WS_EX_TRANSPARENT`
- `WS_EX_TOPMOST`
- `WS_EX_TOOLWINDOW`
- `WS_EX_NOACTIVATE`

It then initializes the renderer against the HWND. `Renderer.cpp` creates a D3D11 device, DXGI composition swap chain with premultiplied alpha, D2D device context, DirectComposition target, and visual root. The swap chain is assigned as the DComp visual content and the visual is set as the HWND target root. This is the cleanest mapping from product requirement to Windows primitives.

### Win2D-ish / Vortice

The C# implementation follows the same model through hand-written P/Invoke. `App.cs` registers a custom window class, keeps the managed WNDPROC delegate alive in a field, and calls `CreateWindowExW` with the same overlay styles plus `WS_EX_NOREDIRECTIONBITMAP`. `GrassWindow.cs` then builds the D3D11/DXGI/D2D/DComp chain through Vortice and sets the swap chain as DComp visual content. It is more glue than C++, but still maps directly to the native overlay design.

### WinUI 3

WinUI 3 creates the HWND for `Microsoft.UI.Xaml.Window`; the app does not own the class name. `WindowAttacher.cs` retroactively replaces the style with `WS_POPUP | WS_VISIBLE`, ORs in the click-through/topmost extended styles, calls `SetLayeredWindowAttributes(hwnd, alpha=255, LWA_ALPHA)`, and forces `SetWindowPos(... SWP_FRAMECHANGED ...)`. The fully opaque layered-window alpha lets the compositor's per-pixel transparency take over.

The fixed framework class name (`WinUIDesktopWin32WindowClass`) also affected tests. `MainWindow.xaml.cs` sets `Title = "DesktopGrass.WinUI3.Window"`; the smoke harness matches `^DesktopGrass\.WinUI3\.Window$` with `-TitleMatch` instead of relying on a unique class name.

## Rendering

| Impl | How blades reach the screen |
| --- | --- |
| Native | Direct2D path geometry per blade. `Renderer::DrawGrass` creates an `ID2D1PathGeometry`, adds a quadratic Bezier, and calls `DrawGeometry` with a palette brush. |
| Win2D-ish / Vortice | Vortice Direct2D on a DXGI composition swap chain. The current code computes the same quadratic Bezier stroke but tessellates it into six `DrawLine` segments instead of creating a path geometry per blade per frame; the comment calls this cheaper than constructing a path every frame. |
| WinUI 3 | Microsoft.UI.Composition `ShapeVisual` with one `CompositionSpriteShape` / `CompositionPathGeometry` per blade. `GrassRenderer.cs` uses Win2D `CanvasPathBuilder` and `CanvasGeometry.CreatePath` because Composition requires an `IGeometrySource2D` and WinUI 3 alone does not ship a practical path-builder implementation. |

The WinUI 3 renderer is the clearest "WinUI 3 alone is not enough" data point: even without XAML controls, the path geometry source comes from Win2D.

## Mouse hook

All four implementations use the same observe-only pattern:

- Install `SetWindowsHookExW(WH_MOUSE_LL, ...)`.
- Handle `WM_MOUSEMOVE` and `WM_LBUTTONDOWN`.
- Queue or dispatch lightweight events to the render/UI thread.
- Always return `CallNextHookEx` and never consume input.

The C# variants both keep the hook delegate in a field so the GC cannot collect it while installed. Win2D queues through a bounded `Channel<HookEvent>`; WinUI 3 invokes an `Action<InputEvent>` sink that enqueues into each window's mailbox; Native uses a lock-free SPSC queue drained by the render loop.

## Tray icon

| Impl | Tray approach |
| --- | --- |
| Native | `Shell_NotifyIconW` from `App.cpp`, with a message-only window and a Quit menu. |
| Win2D-ish / Vortice | WinForms `NotifyIcon` on a dedicated STA message-loop thread with a hidden form and a Quit action. |
| WinUI 3 | `H.NotifyIcon.Core.TrayIcon`, not the WinUI XAML `TaskbarIcon` wrapper. `TrayHost.cs` documents why: the XAML control is designed to be created by loading a `FrameworkElement`, which does not fit a transparent click-through grass-strip window. |

## Testing

Native uses Catch2; both managed implementations use xUnit. That distinction did not matter much because the shared spec made the pure simulation tests straightforward: same canonical seed, same PRNG sequence, same blade vector, same sway/gust/cut behavior.

The smoke tests are intentionally screenshot-based. `Smoke.Common.psm1` asserts the required click-through/topmost ExStyles, waits 1.5 seconds for rendering, screenshots the bottom strip of the primary monitor, samples pixels every four pixels, and requires enough unique colors to prove something meaningful drew. The original v1 measurement was `11,642` for all three impls; the current four-impl baseline after later visual tuning sits roughly in the 1,600–3,500 range per impl (still well above the 50-color minimum gate).

## Where each stack genuinely shines

**Native** is the right choice when the product is fundamentally a Windows desktop primitive: transparent layered HWNDs, DirectComposition, D2D, mouse hooks, and tray integration. It has the smallest exe, fewest runtime dependencies, and the least impedance mismatch. The trade-off is C++ ownership/COM complexity and less managed test ergonomics.

**Win2D-ish / Vortice** is the pragmatic managed option. It keeps the useful part of C# -- fast iteration, xUnit, safe orchestration code -- while still using the same Direct2D/DXGI/DComp model as Native. You would pick it when you want native overlay behavior without writing the whole app in C++.

**WinUI 3** shines when the app needs WinAppSDK, XAML, app-window integration, and a broader Windows app model. DesktopGrass deliberately asked it to do something it is not designed around: a transparent, click-through, topmost overlay. The friction here is not an indictment of WinUI 3; it is evidence that this specific product shape sits below the layer WinUI 3 wants to own.

## What v2 should change

- Add formal CPU/GPU/startup/memory measurement, ideally with repeatable ETW or Windows Performance Recorder profiles.
- Standardize the LoC counting scope before quoting headline numbers: app only, app+tests, interop included/excluded, vendored code excluded.
- Make the smoke harness a small `WinAppRuntime`-aware launcher that understands class-name matching, title-regex matching, `BeforeLaunch`, packaged app activation, and RID-specific .NET output paths.
- Add a click-through probe window under the grass instead of only asserting ExStyle bits.
- Add multi-monitor smoke and DPI-change smoke; all four codebases already have monitor-aware architecture, but v1 only gates primary-monitor rendering.
- Standardize on Vortice for managed low-level render comparisons; it provided the most direct mapping to the native Direct2D/DComp model.
- If WinUI remains in the comparison, decide whether the target is packaged/framework-dependent or unpackaged self-contained, then measure that deployment shape explicitly.
- Consider adding Avalonia or Uno as a cross-stack comparison only if the goal is UI-framework ergonomics; for raw overlays, they should be compared against the same HWND/DComp requirements.
- Share snapshot fixtures across implementations so the canonical seed, blade vector, and stroke geometry cannot drift silently.
