# Agent working agreement

## Branching

- Never commit feature work or bug fixes directly to `main`/`dev`. For every task
  (new feature, bug fix, refactor), create a dedicated branch off the default branch
  first, do the work there, and open a PR back into `main`/`dev`.
- Only trivial, low-risk one-line fixes (typo, comment, doc tweak) may be committed
  directly to `main`/`dev` without a branch/PR.
- One branch = one self-contained change. Don't mix unrelated fixes/features on the
  same branch.
- Name branches descriptively in kebab-case (e.g. `fix-zoom-timeline`,
  `add-safe-zone-camera`), not generic names like `patch-1`.

## Commits & PRs — this is where the record lives

- Do not create a new Markdown doc per feature/bug fix. The commit message and PR
  description **are** the documentation for that change.
- Commit messages: explain what changed and, more importantly, *why* (the reasoning
  behind non-obvious decisions). Avoid messages like "fix bug" or "update code".
- PR description should cover: background/motivation, the approach taken (and
  alternatives considered, if relevant), and how it was validated (tests run,
  manual verification).
- Only write a standalone design doc (e.g. under `docs/`) for decisions that are
  architecture-level and will be referenced repeatedly across multiple future
  changes — not for routine feature/bug work.
- Add code comments only where the "why" isn't obvious from the code itself.

## Before opening a PR

- Run the relevant tests (`dotnet test .\LensFlow.slnx`) and make sure they pass.
- Build the app (`dotnet build .\src\LensFlow.App\LensFlow.App.csproj -c Debug -p:Platform=x64`)
  when the change touches the WPF app.
- Verify the change actually fixes/implements what was asked before declaring it done.
