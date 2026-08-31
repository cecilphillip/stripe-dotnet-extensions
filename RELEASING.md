# Releasing

## What is enforced automatically

| Check | Enforced by | Can it be skipped? |
|---|---|---|
| Documentation samples compile | `just test` (runs in CI on every push) | No |
| Build has zero warnings | `-warnaserror` in `release-check` | No |
| Full suite passes | `release-check`, and CI | No |
| Working tree clean before tagging | `release-check` | No |
| `release-check` runs before a tag is pushed | `.githooks/pre-push` | `git push --no-verify` |
| Release-note samples compile | `verify-release-notes.yml` on release publish/edit | No |
| Publishing requires a matching tag | `publish` job in `build.yml` | No |

Install the hook once per clone:

```bash
just install-hooks
```

Everything else below is procedure, not enforcement. Follow it.

## Before tagging

Run the gate:

```bash
just release-check
```

It cleans, builds with `-warnaserror`, runs every test including the documentation sample
compiler, packs, and fails if the working tree is dirty afterwards. Do not tag until it passes.
The `pre-push` hook runs this for you when you push a tag.

## Rules

**1. Anything published as code must be compiled, not reviewed by eye.**

This includes README samples, sample-project READMEs, and **GitHub release notes**. A code fence
is not prose. Documentation samples are covered automatically by
`tests/Stripe.Extensions.Docs.Tests`. Release notes are covered by `verify-release-notes.yml`
after publication — to catch a mistake *before* it ships, compile the draft first:

```bash
just verify-notes notes.md
```

Any snippet in release notes must be copied from a file the test suite compiles (the READMEs, or
`samples/`). Never retype one from memory.

**2. Start from clean before trusting a green build.**

`just build` and `just test` reuse `obj/`, which has hidden a dropped `ProjectReference` that only
`just pack` exposed. `release-check` starts with `just clean` for this reason.

**3. Re-check `git status` after committing.**

An editor with stale buffers has written over tracked files seconds after a commit, silently
reverting a `.sln` entry and a `ProjectReference`. `release-check` fails on a dirty tree, and
`git diff <tag> HEAD` should be empty before pushing.

**4. Verify the published artifact, not just the build.**

After pushing, install the package from nuget.org into a throwaway project and compile against it.
The nuspec embeds `<repository commit="...">` — confirm it matches the tagged commit.

**5. Allow nuget.org time to propagate.**

Packages appear unevenly across endpoints; one of three visible does not mean the push failed.
Wait at least 75 seconds before re-checking:

```bash
curl -sI https://api.nuget.org/v3-flatcontainer/<id>/<version>/<id>.<version>.nupkg
```

## Steps

1. `just release-check`
2. Commit and merge to `main`
3. Draft the release notes and compile them: `just verify-notes notes.md`
4. Tag with a plain numeric version, no `v` prefix: `git tag 1.2.3`
5. Confirm MinVer agrees: `dotnet tool run minver`
6. Push branch and tag — the `pre-push` hook re-runs the gate
7. Create the GitHub release with `--notes-file` (never inline `\n` escapes)
8. Confirm `verify-release-notes` passed on the release
9. Publish: `just pack && just push-nuget`, or the `publish` workflow
10. Verify the published package per rule 4

## Documentation samples

Every fenced ` ```csharp ` block in the files listed in `MarkdownSampleLoader.DocumentedFiles` is
compiled by the test suite. Add new documentation files to that list.

A block that genuinely cannot compile must opt out in the markdown, with a reason. The comment is
invisible in rendered markdown:

```text
<!-- docs-verify: skip illustrative fragment, no surrounding type -->
```

Prefer fixing the sample over skipping it. A sample that cannot be compiled usually cannot be
copy-pasted by a reader either.
