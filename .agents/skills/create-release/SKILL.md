---
name: create-release
description: Cut a versioned GitHub release of this Go project — bump the CHANGELOG, tag, and let CI build the binary + .deb/.rpm/Arch packages + tarball and publish the release (notes come from the CHANGELOG). Use when the user asks to "create a release", "release vX", "fechar a release", or "publicar a versão".
license: MIT
compatibility: Requires git + gh (GitHub CLI, authenticated). Optional: nfpm for local package builds. The version is the git tag.
---

# Releasing this Go project (GitHub)

This project lives on **GitHub** (`github.com/jniltinho/go-glpi-agent`). A release
is **CHANGELOG-driven**: write the entry, tag, push. The `release.yml` workflow
builds the binary, the `.deb`/`.rpm`/Arch `.pkg.tar.zst` packages and the tarball,
then publishes the release with notes taken from `CHANGELOG.md`. No manual
`gh release edit` needed.

> ⚠️ The version is the **git tag** (`Makefile`: `git describe --tags` → ldflags
> into `internal/version.Version`). There is **no version constant in code** — the
> tag *is* the version.

---

## Before you start

```bash
APP=$(basename "$(git rev-parse --show-toplevel)")     # go-glpi-agent
REMOTE=$(git remote get-url origin)                    # git@github.com:jniltinho/go-glpi-agent.git
LAST=$(git describe --tags --abbrev=0 2>/dev/null || echo v0.0.0)
echo "App: $APP | Last tag: $LAST"
gh auth status                                          # must be authenticated (repo scope)
git fetch origin --tags && git checkout main && git pull --ff-only
git status                                              # clean working tree
```

### Versioning

`v0.x` (pre-1.0), tags always prefixed with `v`:

- **Feature** (new user-visible capability) → minor: `v0.1.0` → `v0.2.0`
- **Fix / docs / CI / infra only** → patch: `v0.1.0` → `v0.1.1`

```bash
IFS=. read MA MI PA <<<"${LAST#v}"
NEXT="v${MA}.$((MI+1)).0"     # feature → minor   (patch: v${MA}.${MI}.$((PA+1)))
echo "Next: $NEXT"
```

---

## Release approaches

| Approach | Best for | Notes | Recommended |
|---|---|---|---|
| Tag + push (CHANGELOG entry) | everything | structured, from `CHANGELOG.md`, published by CI | **Yes** |
| Tag + push, no CHANGELOG entry | emergency hotfix | CI falls back to a one-line note | No |

---

## Process

### 1. Review changes since the last tag

```bash
git log "$LAST"..HEAD --oneline
git log "$LAST"..HEAD --stat    # more detail
```

### 2. Write the `CHANGELOG.md` entry

Repo-root `CHANGELOG.md`. Add a new section **above** the previous version and
move items out of `[Unreleased]`. Use the emoji sections (in this order, omit
empty ones) and categorize commits by prefix:

| Section | Commit prefixes | What goes here |
|---|---|---|
| `### ✨ New Features` | `feat:` | new user-visible functionality |
| `### 🔧 Improvements` | `fix:`, `perf:`, `refactor:`, `ci:`, `build:` | fixes, perf, refactors, infra |
| `### 🧹 Cleanup` | `chore:`, `cleanup:` | dead code / dependency removal |
| `### 📚 Documentation` | `docs:` | README, guides, comments |

```markdown
## [Unreleased]

—

## [0.2.0] — YYYY-MM-DD

**One- or two-line summary.**

### ✨ New Features
- feat: ...
### 🔧 Improvements
- fix: ...
### 📚 Documentation
- docs: ...
```

The text under `## [$NEXT]` becomes the GitHub release notes verbatim (the
workflow extracts it with awk and passes `--notes-file`), so write it for readers.
CI appends a `**Full Changelog**: …compare/PREV...NEXT` link automatically.
Update README/docs only if user-facing.

### 3. Verify green

```bash
go vet ./... && go build ./... && go test ./...
# optional local package smoke-test (CI does this anyway):
#   make build-all && make packages   # .deb + .rpm + Arch .pkg.tar.zst
```

### 4. Commit, tag, push

```bash
# stage ONLY the release changes (never skills/dist/VM-state/secrets)
git add CHANGELOG.md   # plus any agreed doc/code changes
git commit -m "release: $NEXT — <summary>"
git tag -a "$NEXT" -m "Release $NEXT — <summary>"
git push origin main
git push origin "$NEXT"          # the tag push triggers release.yml
```

End commit messages with the project's `Co-Authored-By` trailer if used elsewhere.

### 5. Monitor CI

```bash
RID=$(gh run list --workflow=release.yml --limit 1 --json databaseId -q '.[0].databaseId')
gh run watch "$RID" --exit-status --interval 15
```

### 6. Verify the release

```bash
gh release view "$NEXT" --json tagName,url,body,assets \
  -q '"\(.tagName)  \(.url)\nassets: \([.assets[].name] | join(", "))"'
```

Check:
- Title is `$NEXT`; notes match the CHANGELOG entry (not an auto commit dump).
- Five assets: `.deb`, `.rpm`, `.pkg.tar.zst`, `.tar.gz`, `checksums.txt`.

> If CI ran but the notes are wrong (e.g. the CHANGELOG entry was missing), fix
> the entry and re-publish notes only: `gh release edit "$NEXT" --notes-file notes.md`.

### 7. (Optional) OpenSpec

If the release closes an OpenSpec change, run `/opsx:archive <change>` to sync the
main specs and archive it, then commit + push that separately.

---

## Quick reference

```bash
LAST=$(git describe --tags --abbrev=0 2>/dev/null || echo v0.0.0); IFS=. read MA MI PA <<<"${LAST#v}"; NEXT="v${MA}.$((MI+1)).0"
# 1) write the ## [$NEXT] section in CHANGELOG.md   2) go vet/build/test green
git add CHANGELOG.md && git commit -m "release: $NEXT — ..."
git tag -a "$NEXT" -m "Release $NEXT" && git push origin main && git push origin "$NEXT"
RID=$(gh run list --workflow=release.yml --limit 1 --json databaseId -q '.[0].databaseId'); gh run watch "$RID" --exit-status
gh release view "$NEXT"
```

---

## Workflow capabilities (`.github/workflows/release.yml`)

| Artifact / step | Status |
|---|---|
| `linux/amd64` static binary | ✅ |
| `.deb` (systemd units + config under `/opt/go-glpi-agent`) | ✅ nfpm |
| `.rpm` (same layout) | ✅ nfpm |
| Arch `.pkg.tar.zst` | ✅ nfpm |
| `.tar.gz` + `checksums.txt` | ✅ |
| Release notes from `CHANGELOG.md` | ✅ `--notes-file` |
| Publish | ✅ native `gh` CLI (no Node actions) |
| Multi-arch (arm64) | ❌ not yet |

---

## Adapting to another Go project

| Item | This project | Your project |
|---|---|---|
| Repo | `github.com/jniltinho/go-glpi-agent` | your repo |
| Version scheme | `v0.x.y` | `v1.0.x` / semver |
| Packages | nfpm `.deb`/`.rpm`/`.pkg.tar.zst` | whatever CI builds |
| Notes source | `CHANGELOG.md` via CI | CHANGELOG or `gh release edit` |
| Install prefix | `/opt/go-glpi-agent` | yours |

## Guardrails

- **Never** push a tag before `go vet`, `go build`, and `go test` are green.
- **Never** stage `.agents/skills`, `.claude/skills`, `dist/`, Vagrant VM state
  (`test/vagrant/.vagrant/`), `test/glpi/.env`, or local settings into a commit.
- **Never** hardcode a GitHub token; use `gh auth`, and remind the user to rotate
  a token pasted in chat.
- The tag is the version — no code constant to bump.
- Reuse an existing tag only by deleting it first (risky if already pulled);
  prefer a new patch version instead.
```
