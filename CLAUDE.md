# Opening Bell (working title)

First-person day-trading life sim in Unity 6000.6.2f1. Source of truth: `PROJECT_SPEC.md` (read only relevant sections). Decisions: `ARCHITECTURE.md`. Status: `TODO.md`.

## Rules
- Stay inside the current phase (see TODO.md). No placeholder code for future systems.
- Extend existing systems before adding new ones. Prefer targeted edits to rewrites.
- Market/Trading/Core are pure C# (`noEngineReferences`). Keep UnityEngine out of them.
- Money is `decimal`. Simulation randomness only via `SeededRandomService` streams.
- Comments explain reasoning, formulas and constraints, not syntax.
- Run the smallest relevant tests first: `./run-tests.ps1 -Filter <Class>`. The full suite runs at milestones; UI changes also need `./run-tests.ps1 -Platform PlayMode`, then check `TestResults/terminal-trading.png`. Close the Unity Editor before running tests headlessly.
- Edit files with the Edit/Write tools, not PowerShell Get-Content/Set-Content (that corrupts UTF-8).
- Reports: what changed, key files, test results, open issues, recommended next task. Stop when the task is done.
