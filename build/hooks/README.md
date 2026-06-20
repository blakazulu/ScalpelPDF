# Git hooks

## `pre-push` — auto-bump version on push to `main`

When you push commits to `main`, this hook increments the **revision** (4th)
component of `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` in
`Scalpel.csproj` — e.g. `1.5.1` → `1.5.1.1` → `1.5.1.2`.

Major/minor/patch are still set by hand for real releases; the hook only nudges
the revision in between.

### One-time setup (per clone)

Git hooks aren't shared automatically. Point git at this directory:

```sh
git config core.hooksPath build/hooks
```

That setting lives in `.git/config` (not committed), so each clone runs it once.

### How it works

git chooses which commits to send *before* `pre-push` runs, so the bump can't
ride the push that triggered it. Instead the hook:

1. Bumps the three version fields in `Scalpel.csproj`.
2. Commits the change as `Bump version to X.Y.Z.N [skip-bump]`.
3. **Aborts the push.**

Run `git push` again — your work and the bump commit go up together. The second
push sees the `[skip-bump]` commit at `HEAD` and lets it through without bumping
again.

### Notes

- Fires only for pushes whose target is `refs/heads/main` (branch deletions are
  ignored).
- `pre-push` is a POSIX `sh` script — git runs hooks via `sh`, even on Windows —
  that calls `bump-version.ps1` (`pwsh`, falling back to `powershell`). The
  `.ps1` does the edit and the commit.
- Skip the bump for a one-off push with `git push --no-verify`.
