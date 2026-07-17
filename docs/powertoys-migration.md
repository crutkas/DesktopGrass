# PowerToys migration backlog

Status date: 2026-07-16

## Decisions already made

| Decision | Outcome |
| --- | --- |
| Repository support | Native is the supported standalone implementation and only PowerToys candidate. |
| Implementation | Port the Native C++/Direct2D/DirectComposition implementation. |
| Proposed hosting | Start with an in-process native PowerToys module, subject to explicit maintainer approval in PT-006. |
| Managed implementation | Keep the C#/Vortice source and a commented restore/build/test recipe only as a reproducible comparison/reference. It is outside required product CI; an optional advisory smoke does not change that status. Do not import it or its graphics dependencies. |
| Standalone shell | Do not port the tray icon, Run-key auto-start, standalone entry point, private updater/installer behavior, or duplicate enable/disable lifecycle. |
| License | DesktopGrass is MIT licensed. Native tests use Microsoft's C++ Unit Test Framework; no third-party test framework is vendored or needs to be imported. |
| Feature scope | Port existing behavior first. Do not add scenes, critters, or other features until the PowerToys module meets its quality gates. |

Managed feature parity, release packaging, smoke hardening, and platform
behavior are outside this backlog. Managed-only hardening work is not a blocker
for Native standalone releases or PowerToys readiness. Required CI is scoped to
the supported Native product so a frozen reference cannot block product work.

## Toolchain position

| Build | Effective toolset | Evidence | Migration rule |
| --- | --- | --- | --- |
| DesktopGrass Native app | Hard-pinned `v145` in all x64/ARM64 Debug/Release configurations | [`DesktopGrass.Native.vcxproj`](../src/DesktopGrass.Native/DesktopGrass.Native.vcxproj) | Do not copy the pin into PowerToys. |
| DesktopGrass Native tests | Hard-pinned `v145` in all x64/ARM64 Debug/Release configurations | [`DesktopGrass.Native.Tests.vcxproj`](../tests/DesktopGrass.Native.Tests/DesktopGrass.Native.Tests.vcxproj) | The suite already uses PowerToys' Microsoft C++ Unit Test Framework conventions; inherit PowerToys central props when importing it. |
| DesktopGrass CI | VS 2026 runner; therefore `v145` | [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) | This validates `v145`, not `v143`. |
| PowerToys C++ default | `v143`, overridden to `v145` when `VisualStudioVersion == 18.0` | [`Cpp.Build.props`](https://github.com/microsoft/PowerToys/blob/5c9c93d56d0c63fcc485f65bebba5315775c919b/Cpp.Build.props) | Inherit `Directory.Build.props`/`Cpp.Build.props`; do not set `PlatformToolset` in the new module. |
| PowerToys CI and release | Current default agents are `SHINE-VS18-Latest`, so official builds effectively use `v145`; preview also uses VS 18 | [`ci.yml`](https://github.com/microsoft/PowerToys/blob/5c9c93d56d0c63fcc485f65bebba5315775c919b/.pipelines/v2/ci.yml), [`pipeline-ci-build.yml`](https://github.com/microsoft/PowerToys/blob/5c9c93d56d0c63fcc485f65bebba5315775c919b/.pipelines/v2/templates/pipeline-ci-build.yml), [`release.yml`](https://github.com/microsoft/PowerToys/blob/5c9c93d56d0c63fcc485f65bebba5315775c919b/.pipelines/v2/release.yml) | The module must pass the official `v145` path without breaking the supported `v143` fallback. |

DesktopGrass's checked-in projects and CI use `v145` for x64 and ARM64. A
`v143` source-compatibility build is a PowerToys migration gate, not a supported
DesktopGrass project configuration.

## Status definitions

| Status | Meaning |
| --- | --- |
| Done | Evidence and acceptance criteria are complete. |
| Ready | Can start in DesktopGrass or as proposal work now. |
| Blocked | Must wait for the listed dependency or PowerToys approval. |
| Not started | Approved work that has not begun. |

## P0: approval and measurable gates

| ID | Status | Work item | Depends on | Done when |
| --- | --- | --- | --- | --- |
| PT-001 | Done | License DesktopGrass for contribution | None | Root [`LICENSE`](../LICENSE) contains the MIT license and repository documentation links to it. |
| PT-002 | Ready | Obtain PowerToys product approval | None | A PowerToys issue/discussion and utility specification are accepted by the maintainers, with an agreed name, scope, owner, and ship criteria. |
| PT-003 | Ready | Approve the runtime behavior matrix | PT-002 for final approval | The specification defines behavior for fullscreen apps/games, session lock, display-off, Remote Desktop, monitor sleep, battery saver, AC versus battery power, occlusion, and PowerToys disable/shutdown. |
| PT-004 | Ready | Set resource budgets and measurement method | PT-003 | The [Native benchmark and energy evidence workflow](../tools/benchmark/README.md) records CPU, GPU engines, working set, a context-switch wakeup proxy, optional hardware/battery energy, machine/power context, and provisional same-machine pass/fail calculations. Maintainer approval and final visible-versus-suppressed evidence remain required. |
| PT-005 | Ready | Complete source and third-party provenance | PT-002 | Every imported file is mapped to this MIT repository; no Vortice packages or third-party test framework are imported; native tests use PowerToys' Microsoft C++ Unit Test Framework. |
| PT-006 | Ready | Approve the hosting and fault-isolation model | PT-002 for final approval | Maintainers accept either in-process rendering or a worker process after reviewing GPU/device-loss blast radius, Runner stability, shutdown behavior, memory cost, and recovery. The selected model is recorded before projects are scaffolded. |

## P0: native port foundation

| ID | Status | Work item | Depends on | Done when |
| --- | --- | --- | --- | --- |
| PT-101 | Ready | Prove PowerToys toolchain compatibility | Access to VS 17/`v143` locally or in CI | The native production sources build as x64 and ARM64 with PowerToys central props on VS 18/`v145`, and also compile through the central VS 17/`v143` fallback. No module project hardcodes `PlatformToolset`, SDK version, CRT mode, warning level, or output paths. |
| PT-102 | Blocked | Create the PowerToys module projects | PT-002, PT-006, PT-101 | Native module and test projects are under `src/modules/<module>`, are in `PowerToys.slnx`, inherit repository props, and build in the standard x64/ARM64 configurations. |
| PT-103 | Blocked | Separate renderer/simulation from the standalone host | PT-102 | Simulation, renderer, pacing, and window management can be initialized and stopped by a host-owned lifecycle; no production module code depends on `wWinMain`, tray menus, the Run key, or standalone paths. |
| PT-104 | Blocked | Implement `PowertoyModuleIface` | PT-102, PT-103 | The DLL exports `powertoy_create`; `enable`, `disable`, `is_enabled`, `destroy`, `get_config`, and `set_config` are correct, idempotent, thread-safe, and return only after owned windows/hooks/threads are stopped. |
| PT-105 | Blocked | Adopt PowerToys logging and failure handling | PT-102 | Startup, display rebuild, device loss, settings rejection, and shutdown failures use shared logging; no private log location or silent success fallback remains. |

## P0: runtime and platform behavior

| ID | Status | Work item | Depends on | Done when |
| --- | --- | --- | --- | --- |
| PT-201 | Blocked | Implement power-aware rendering | PT-003, PT-103 | Rendering pauses or throttles exactly as approved for battery saver, battery power, display-off, session lock, and monitor sleep, then resumes without resetting simulation or leaking resources. |
| PT-202 | Blocked | Implement fullscreen and occlusion policy | PT-003, PT-103 | Fullscreen/game detection and occlusion behavior match the approved matrix on each monitor and avoid continuous invisible rendering. |
| PT-203 | Blocked | Harden display-topology and DPI changes | PT-103 | Add/remove/reorder, resolution, orientation, taskbar/work-area, primary-monitor, and mixed-DPI changes rebuild only affected surfaces and preserve valid state. |
| PT-204 | Blocked | Add deterministic graphics recovery tests | PT-103 | Tests can force D3D/D2D/DComp device removal and recreation; repeated failures retry with bounded cadence and disable cleanly when the host requests shutdown. |
| PT-205 | Blocked | Bound input observation and hook lifetime | PT-103, PT-104 | Mouse observation is active only while enabled, captures only data needed for local effects, logs no coordinates/content, and is always removed on disable, shutdown, and partial startup failure. |

## P1: PowerToys product integration

| ID | Status | Work item | Depends on | Done when |
| --- | --- | --- | --- | --- |
| PT-301 | Blocked | Define and migrate settings | PT-002, PT-104 | PowerToys owns defaults, validation, serialization, and live updates for scene, target FPS, density, sway, and critter counts. The spec explicitly decides whether standalone `%LOCALAPPDATA%\DesktopGrass` state is imported once or ignored. |
| PT-302 | Blocked | Build the Settings UI | PT-301 | Settings use PowerToys controls and navigation, update live where safe, expose reset/default behavior, and require no tray menu or manual JSON editing. |
| PT-303 | Blocked | Add enterprise policy | PT-301 | A GPO can force-enable or force-disable the utility using PowerToys conventions, and policy overrides are reflected correctly in Settings and Runner behavior. |
| PT-304 | Blocked | Add localization resources | PT-302 | Utility name, description, settings, errors, and accessibility strings use PowerToys resource infrastructure; no user-facing string is hardcoded. |
| PT-305 | Blocked | Define privacy-safe telemetry | PT-002, PT-105, PT-301 | The approved events cover enable/disable, settings shape, recovery, and performance diagnostics without cursor coordinates, critter names, monitor content, or other user data. No event is added without product approval. |
| PT-306 | Blocked | Remove standalone integration surfaces | PT-104, PT-301 | The imported module contains no tray icon, private auto-start, standalone config/state ownership, custom installer, updater, or signing path. Runner enablement and PowerToys packaging are the only product lifecycle. |
| PT-307 | Blocked | Register the utility in Settings and Runner | PT-302, PT-303 | Module type, enabled-modules model, dashboard card, navigation, policy state, default-enabled state, deep links, and the OOBE inclusion decision are wired using current PowerToys conventions. |

## P1: verification and ship readiness

| ID | Status | Work item | Depends on | Done when |
| --- | --- | --- | --- | --- |
| PT-401 | Blocked | Port deterministic unit coverage | PT-005, PT-102, PT-103 | Existing simulation/configuration regression coverage is moved into the PowerToys module test project and runs with the already-adopted Microsoft C++ Unit Test Framework; required snapshots are preserved. |
| PT-402 | Blocked | Add lifecycle and Settings integration tests | PT-104, PT-203, PT-204, PT-301, PT-303 | Automated tests cover enable/disable/re-enable, malformed settings, live updates, Runner shutdown, partial startup failure, GPO override, and repeated display/device rebuilds. |
| PT-403 | Blocked | Validate real display configurations | PT-201, PT-202, PT-203 | Manual or lab coverage passes on x64 and ARM64, one and multiple monitors, mixed DPI, every taskbar edge, portrait/landscape, monitor hot-plug, Remote Desktop, and sleep/resume. |
| PT-404 | Blocked | Complete accessibility review | PT-302, PT-304 | Settings pass keyboard, Narrator, scaling, high-contrast, and localization checks; decorative overlay windows expose no actionable UI and do not trap input or focus. |
| PT-405 | Blocked | Complete security and privacy review | PT-205, PT-305 | Review covers global hook lifetime, settings parsing, window privileges, logging/telemetry data, file access, and DLL/module boundaries with no unresolved high-severity finding. |
| PT-406 | Blocked | Meet performance and energy budgets | PT-004, PT-201, PT-202, PT-203 | Long-running measurements meet every approved CPU/GPU/memory/wakeup/energy limit across all scenes on AC, battery, battery saver, fullscreen, occluded, locked, and display-off paths. |
| PT-407 | Blocked | Pass soak and fault testing | PT-104, PT-201, PT-203, PT-204 | A multi-hour run with scene changes, sleep/resume, monitor churn, device loss, and repeated enable/disable has no crash, hang, unbounded growth, stale window, or leaked hook/thread. |

## P1: repository and release wiring

| ID | Status | Work item | Depends on | Done when |
| --- | --- | --- | --- | --- |
| PT-501 | Blocked | Wire build, packaging, and assets | PT-102, PT-304, PT-307, PT-401 | Projects, module DLL, icons, localization, Settings assets, tests, installer payload, signing, and CI are included through standard PowerToys infrastructure for x64 and ARM64. |
| PT-502 | Blocked | Add user and maintainer documentation | PT-301, PT-406 | PowerToys docs explain purpose, controls, power behavior, limitations, privacy, and troubleshooting; maintainers have architecture and test instructions. |
| PT-503 | Blocked | Run staged rollout and exit review | PT-401..PT-407, PT-501, PT-502 | Maintainers approve final performance, accessibility, security, telemetry, localization, and reliability evidence; rollout and rollback criteria are documented. |

## Critical path

| Track | Sequence |
| --- | --- |
| Product and power policy | `PT-002 -> PT-003 -> PT-004 -> PT-201/PT-202 -> PT-406` |
| Hosting decision | `PT-002 -> PT-006 -> PT-102` |
| Toolchain and module | `PT-101 -> PT-102 -> PT-103 -> PT-104` |
| Settings and policy | `PT-104 -> PT-301 -> PT-302/PT-303 -> PT-307` |
| Quality gates | `PT-103/PT-104 -> PT-401..PT-407` |
| Release | `PT-304/PT-307/PT-401 -> PT-501 -> PT-502 -> PT-503` |
