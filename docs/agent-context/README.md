# Agent context archive

This folder is a portable snapshot of the Copilot CLI session that built
DesktopGrass, so the same conversation can be picked up from any machine
without depending on a local `~/.copilot/session-state/` folder.

> **Archive notice:** this snapshot records the original multi-implementation
> comparison era. It is not the current support policy or roadmap. Native is the
> supported implementation and only PowerToys candidate; Managed is a
> source-available reference with a manual build/unit recipe, no required
> product CI, and no ongoing parity or platform-hardening commitment. An
> optional advisory smoke may exercise Managed without making it a gate. Start
> with the root
> [`README`](../../README.md) and
> [`powertoys-migration.md`](../powertoys-migration.md).

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
   for the full build-from-scratch checklist (Visual Studio 2026 + MSVC `v145`,
   a Windows SDK, and a .NET SDK accepted by `global.json`).

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

This is a curated archive, not a byte-for-byte session-state mirror. Do not copy
live session files into it wholesale. When preserving a useful checkpoint:

1. Keep the archive notice and current support decision above.
2. Remove credentials, personal machine paths, process IDs, and machine-local
   state.
3. Treat commands, output paths, counts, and tool versions as historical unless
   they are revalidated against the current checkout.
4. Link current build and test guidance to the root
   [`README`](../../README.md) instead of duplicating a new roadmap here.
