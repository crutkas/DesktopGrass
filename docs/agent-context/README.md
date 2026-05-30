# Agent context — resume on any machine

This folder is a portable snapshot of the Copilot CLI session that built
DesktopGrass, so the same conversation can be picked up from any machine
without depending on a local `~/.copilot/session-state/` folder.

## Layout

| Path | What it is |
| --- | --- |
| `plan.md` | Current high-level plan, open questions, scratch state. Read this first. |
| `checkpoints/index.md` | Numbered list of all prior checkpoints with one-line summaries. |
| `checkpoints/NNN-*.md` | Detailed per-milestone hand-off notes (goal, files touched, technical decisions, follow-ups). Read the most recent few for live context, older ones on demand. |
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

   > Read `docs/agent-context/plan.md` and the last 2–3 checkpoints in
   > `docs/agent-context/checkpoints/`, then continue from where we left off.

   That re-hydrates the same working context — the agent will know about the
   Native + Win2D split, the locked PRNG draw order, the scene framework,
   the birch redesign, etc.

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
