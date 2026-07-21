# AGENTS.md

## Project
- Windows Forms app targeting `net8.0-windows`
- Solution: `src/TrayToolbar.sln`
- Project: `src/TrayToolbar/TrayToolbar.csproj`
- Tests: `src/TrayToolbar.Tests/TrayToolbar.Tests.csproj`

## Working rules
- Keep changes small and task-focused.
- Don't use powershell to edit files unless absolutely necessary. Prefer manual edits for code changes, and use powershell only for bulk updates or formatting.
- Preserve existing style and naming; avoid broad refactors unless instructed.
- Do not edit generated/build output in `bin/`, `obj/`, `publish/`, or `artifacts/`.
- Treat `*.Designer.cs` and `*.resx` as UI/resource files; change them only when needed.
- Avoid changing versioning, packaging, or release artifacts unless requested.

## Validation
- Run `dotnet format .\TrayToolbar.sln` from `src\` before committing.
- Preferred repo release build: `./build.ps1`
- If the task is narrow, use the smallest relevant validation step first.
- All unit tests must pass before merging. Run `dotnet test .\TrayToolbar.sln` from `src\`.

## Notes
- This is Windows-specific desktop code; keep fixes compatible with Windows 11.
- Contributor-facing workflow guidance now lives in `CONTRIBUTING.md` and `SECURITY.md`; keep those files in sync if validation or reporting expectations change.
- Update and launch trust-boundary guidance now lives in `docs/update-security.md`; if release packaging changes, keep that document and the update-related tests in sync.
## Fleet operations — build, worktrees, CI (pointers, not copies)

Canonical operational docs live in `dev-kodu/KADE.Tekla`:
[`docs/CI.md`](https://github.com/dev-kodu/KADE.Tekla/blob/main/docs/CI.md) (fleet CI) ·
[`docs/REMOTE-BUILD.md`](https://github.com/dev-kodu/KADE.Tekla/blob/main/docs/REMOTE-BUILD.md) (SSH builds).
Do not copy their content here — pointers cannot drift.

- **Worktrees (MANDATORY, fleet-wide):** the primary checkout `D:\Dev\<Repo>` stays parked on a
  clean default branch — ALL work (edits, reviews, audits, reading) runs in a worktree under
  `.worktrees\<leaf>`. Finish: push → PR → squash-merge via `~/.claude/tools/pr-merge.ps1`
  (never raw `gh pr merge`, never push the default branch; upstream-fork syncs use `-Method merge`).
- **CI:** PRs build on the self-hosted host (`[self-hosted, kade-build]`) through the reusable
  workflow `dev-kodu/.github/.github/workflows/kade-ci.yml` plus a ~25-line caller stub in the
  repo. Workflow steps run on Windows: **`shell: pwsh`, never `bash`**. Private siblings clone
  via the runner's read-only deploy keys — no PATs, no `KADE_SIBLING_TOKEN`.
- **Local builds:** fine in this repo. Only `KADE.Tekla` forbids local builds — there, use the
  SSH wrappers (`~/.claude/tools/kade-remote-build.ps1 -Plugin <Name> -TeklaVersion 2026`).
- **Remote/SSH build box:** `Kalev@100.68.52.24` (Tailscale) — two GitHub runners, the vcpkg
  asset/binary caches, and the Tekla toolchain live there. Read REMOTE-BUILD.md before using it.
