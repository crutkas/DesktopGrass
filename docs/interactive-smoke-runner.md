# Interactive smoke runner

The `Interactive smoke` GitHub Actions workflow runs the screenshot-based
Native and managed-reference harness on a dedicated self-hosted Windows desktop.
It intentionally cannot select a GitHub-hosted runner.

## Runner contract

Register an x64 Windows runner with all four labels:

- `self-hosted`
- `Windows`
- `X64`
- `desktopgrass-interactive`

The runner must be dedicated to this repository or another equally trusted
workload. Run the Actions runner as the logged-in test user from that user's
interactive session, not as a Windows service. The session must remain active
and unlocked with a real or supported virtual display attached. A runner
service executes in session 0 and is rejected.

Set the non-secret machine or runner-user environment variable
`DESKTOPGRASS_INTERACTIVE_RUNNER=1` before starting the Actions runner. The
workflow does not set this marker: it proves the selected machine was
deliberately provisioned for interactive smoke rather than merely relabeled.

After the runner is online and verified, set the repository Actions variable
`DESKTOPGRASS_INTERACTIVE_SMOKE_ENABLED=true`. Until that variable is exactly
`true`, automatic workflow runs complete on a hosted configuration job, clearly
report **not provisioned**, and do not queue a self-hosted job or claim UI
coverage.

Install these prerequisites for the runner user:

- PowerShell 7 (`pwsh`)
- Visual Studio 2026 C++ build tools with MSBuild, toolset `v145`, and a Windows
  SDK
- .NET SDK compatible with `global.json` (currently .NET 10.0.300)
- Network access to GitHub Actions and NuGet

Do not store credentials in the repository or inject desktop-login credentials
into the workflow. Machine provisioning, login, display attachment, runner
registration, and keeping the session unlocked are external lab operations.

## Coverage and failure behavior

Native smoke is the required product gate. It builds the Release x64 app,
asserts the click-through styles and monitor geometry, and verifies rendered
pixels from the actual desktop framebuffer.

The managed Win2D implementation is a frozen comparison/reference rather than a
supported release target. Its build and smoke still run after the Native step,
including when Native fails, but the step is advisory (`continue-on-error`) so
reference drift cannot block Native work. Its result and diagnostics remain
visible in the workflow and artifact.

Before building, the entry script rejects:

- GitHub-hosted Actions environments
- session 0
- disconnected, locked, or otherwise inactive WTS sessions
- desktops from which it cannot save a real screenshot

In Actions, the entry script also requires the runner-owned
`DESKTOPGRASS_INTERACTIVE_RUNNER=1` marker. A GitHub-hosted runner or an
accidentally relabeled self-hosted runner fails before build or launch.

Each target writes a transcript and all diagnostics available at the point of
failure under `artifacts\interactive-smoke`. A completed smoke invocation also
writes its build log, smoke log, structured JSON result, and target screenshot.
The workflow uploads the directory on every outcome and retains it for 14 days.

Each target runs with isolated `LOCALAPPDATA`, `APPDATA`, `TEMP`, and `TMP`
directories. `finally` cleanup restores the environment, removes that state,
and stops only DesktopGrass processes launched from the current workspace. A
separate `always()` cleanup step repeats process and sandbox cleanup if a target
step fails.

Pull requests from forks are deliberately skipped because self-hosted runners
must not execute untrusted fork code. After reviewing a fork commit, a
maintainer can run `workflow_dispatch` from a trusted repository branch. The
separate `Interactive smoke gate` hosted job fails when the trusted interactive
job is skipped or fails, so a fork PR cannot look covered merely because its
self-hosted job was skipped.

Only after provisioning and enabling the repository variable should branch
protection require `Interactive smoke gate`. Before that point, the workflow's
hosted `Interactive smoke availability (no coverage)` check is informational
and must not be interpreted as smoke coverage.

## External owner action

Repository-side automation is complete, but it cannot provision the lab.
Before treating issue #26 as complete, a repository owner must register the
dedicated runner with the labels above, set
`DESKTOPGRASS_INTERACTIVE_SMOKE_ENABLED=true`, trigger `Interactive smoke`, and
retain one successful run showing the required Native step and the attempted
managed reference step. Keep the issue open until that end-to-end run exists.
