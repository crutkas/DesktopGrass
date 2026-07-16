# Agent context archive

This folder is a portable snapshot of the Copilot CLI session that built
DesktopGrass, so the same conversation can be picked up from any machine
without depending on a local `~/.copilot/session-state/` folder.

> **Archive notice:** this snapshot records the original multi-implementation
> comparison era. It is not the current support policy or roadmap. Native is the
> supported implementation and only PowerToys candidate; Managed is a
> source-available, build/unit-covered reference without an ongoing parity or
> platform-hardening commitment. Start with the root [`README`](../../README.md)
> and [`powertoys-migration.md`](../powertoys-migration.md).

## Layout

| Path | What it is |
| --- | --- |
| `plan.md` | Archived v1 comparison plan and scratch state. Read only for historical context. |
| `checkpoints/index.md` | Numbered list of all prior checkpoints with one-line summaries. |
| `checkpoints/NNN-*.md` | Historical per-milestone hand-off notes (goal, files touched, technical decisions, follow-ups). |
| `files/` | Persistent design artifacts the agent kept across checkpoints (e.g. `phase3-design.md`). |

## How to resume on a fresh machine

1. **Clone the repo**

   ```pwsh
   gh repo clone crutkas/DesktopGrass
   cd DesktopGrass
   ```

2. **Set up the dev environment** — see [`../manual-smoke.md`](../manual-smoke.md)
   for the full build-from-scratch checklist (VS 2022 + Desktop C++ workload,
   .NET SDK 10 pinned via `global.json`, Windows SDK).

3. **Start Copilot CLI in the repo root** and prime it:

   > Read the root `README.md` support policy and
   > `docs/powertoys-migration.md` first. Consult
   > `docs/agent-context/plan.md` and checkpoints only as historical context.

   That establishes the current Native-only product scope while preserving
   access to the original comparison decisions, PRNG draw order, scene
   framework, birch redesign, and other implementation history.

## What does NOT move across machines

These live outside the repo and are intentionally **not** synced as files:

- **`~/.copilot/session-state/<uuid>/events.jsonl` + `session.db`** — large,
  has a per-PID lock, SQLite WAL files, and is keyed to the CLI version on
  one machine. Don't try to sync this folder via OneDrive/Dropbox.
- **User memories** (e.g. PowerShell `.cmd` arg-mangling, WinUI test
  verification rules) — these live in the Copilot account, not on disk.
  Sign in as the same GitHub user on the new machine and they're already
  there.
- **The Native exe binary** — rebuild it (`docs/manual-smoke.md`).

## Refreshing this folder

After meaningful work on the primary machine, sync this snapshot before
switching computers:

```pwsh
$src = "$env:USERPROFILE\.copilot\session-state\<your-session-uuid>"
Copy-Item "$src\plan.md"          docs\agent-context\plan.md -Force
Copy-Item "$src\checkpoints\*.md" docs\agent-context\checkpoints\ -Force
Copy-Item "$src\files\*"          docs\agent-context\files\ -Force -ErrorAction SilentlyContinue
git add docs/agent-context
git commit -m "Sync agent context snapshot"
git push
```

(The session UUID is the folder name shown by the CLI in
`~/.copilot/session-state/`.)
