# Agent working agreement

## Branching

- Never commit feature work or bug fixes directly to `main`/`dev`. For every task
  (new feature, bug fix, refactor), create a dedicated branch off the default branch
  first, do the work there, and open a PR back into `main`/`dev`.
- Only trivial, low-risk one-line fixes (typo, comment, doc tweak) may be committed
  directly to `main`/`dev` without a branch/PR.
- One branch = one self-contained change. Don't mix unrelated fixes/features on the
  same branch.
- Branch names must start with a type prefix — `feat-` for new features,
  `fix-` for bug fixes, `docs-`/`refactor-`/`chore-` where those fit — followed
  by a descriptive kebab-case summary (e.g. `feat-zoom-timeline`, `fix-zoom`).
  Never generic names like `patch-1`.
- Do not include usernames, emails, or other personal identifiers in the
  branch name — the git author/committer fields already record who made
  the change.
- This applies to auto-generated names too. Some tooling seeds a branch named
  after the current user (e.g. `<user>-<org>-...`). Rename it to a compliant
  name *before* the first push; if the rename helper refuses because the branch
  was already renamed once, fall back to `git branch -m`, push the new name, and
  delete the old remote branch.

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

## Building & running

- Build/test into the standard `bin`/`obj` output only:
  - Tests: `dotnet test .\LensFlow.slnx`
  - App: `dotnet build .\src\LensFlow.App\LensFlow.App.csproj -c Debug -p:Platform=x64`
- Do **not** `dotnet publish` into ad-hoc folders like `artifacts\<feature-name>`
  to get a runnable copy for manual verification. That pattern has produced a
  dozen+ stale, undocumented folders under `artifacts\` that nobody cleans up.
  Instead, run the app straight out of the Debug build output:
  `src\LensFlow.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\LensFlow.App.dll`.
  If a genuinely standalone/distributable build is needed for a specific reason,
  publish into a temp directory outside the repo (e.g. `$env:TEMP\...`) and
  delete it when done — never commit it, and don't leave it under `artifacts\`.
- On an ARM64 machine without the x64 desktop runtime, install it once with
  `winget install Microsoft.DotNet.DesktopRuntime.10 --architecture x64`, then
  launch the built DLL with that runtime's `dotnet.exe` explicitly, e.g.
  `& "C:\Program Files\dotnet\x64\dotnet.exe" "src\LensFlow.App\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\LensFlow.App.dll"`
  (the default ARM64 `dotnet` on PATH cannot host an x64-targeted WPF app).

## Before opening a PR

- Run the relevant tests (`dotnet test .\LensFlow.slnx`) and make sure they pass.
- Build the app (`dotnet build .\src\LensFlow.App\LensFlow.App.csproj -c Debug -p:Platform=x64`)
  when the change touches the WPF app.
- Verify the change actually fixes/implements what was asked before declaring it done.
