---
name: create-release
description: Cut a versioned release of this Go project on GitHub — bump the CHANGELOG, build artifacts, tag, push, and publish the release with assets via the GitHub CLI (gh). Use when the user asks to "create a release", "release vX", "fechar a release", or "publicar a versão".
license: MIT
compatibility: Requires git + gh (GitHub CLI, authenticated). Optional: nfpm for .deb/.rpm artifacts. The version comes from the git tag.
---

# Releasing this Go project (GitHub)

This project lives on **GitHub** (`github.com/jniltinho/go-fusioninventory-agent`).
A release is: bump `CHANGELOG.md`, build the artifacts, commit, tag, push, then
publish with **`gh release create`** (creating the release and uploading the assets
in one step). No Gitea, no hand-rolled API calls.

> ⚠️ The version comes from the **git tag** (`Makefile`: `git describe --tags` →
> ldflags into `internal/version.Version`). There is **no version constant in code**
> to bump — the tag *is* the version.

---

## Project facts

```bash
REMOTE=$(git remote get-url origin)           # git@github.com:jniltinho/go-fusioninventory-agent.git
OWNER="jniltinho"; REPO="go-fusioninventory-agent"
LAST=$(git describe --tags --abbrev=0 2>/dev/null || echo "v0.0.0")
echo "Last tag: $LAST"
gh auth status                                # must be authenticated for releases
```

### Versioning

Series `0.x` (pre-1.0), tags always prefixed with `v`:

- **Feature** (new user-visible capability) → bump **minor**: `v0.1.0` → `v0.2.0`
- **Fix / docs / infra only** → bump **patch**: `v0.1.0` → `v0.1.1`

```bash
IFS=. read MAJ MIN PAT <<< "${LAST#v}"
NEXT="v${MAJ}.$((MIN+1)).0"     # feature → minor   (patch: v${MAJ}.${MIN}.$((PAT+1)))
echo "Next: $NEXT"
```

### Auth

Releases use the **GitHub CLI**, not a raw token. Authenticate once per machine:

```bash
gh auth status || gh auth login        # the user runs `gh auth login` (e.g. via `! gh auth login`)
```

> If a `GH_TOKEN`/`GITHUB_TOKEN` env var is used instead, never hardcode it in files
> or commits, and remind the user to rotate a token pasted in chat.

---

## Process

### 1. Prerequisites

```bash
git fetch origin --tags
git checkout main
git pull --ff-only
git status        # clean working tree (besides the release changes you'll stage)
```

### 2. Review changes since the last tag

```bash
git log "$LAST"..HEAD --oneline
git log "$LAST"..HEAD --stat    # more detail
```

### 3. Update `CHANGELOG.md`

Repo-root `CHANGELOG.md`, **Keep a Changelog** format, with a top `## [Unreleased]`
placeholder. Insert a new section **above** the previous version, moving the relevant
items out of `[Unreleased]`. (Create `CHANGELOG.md` on the first release.)

```markdown
## [Unreleased]

—

## [0.2.0] — YYYY-MM-DD

**One- or two-line summary.**

### Added
- ...
### Changed
- ...
### Fixed
- ...
### Security
- ...
### Removed
- ...
```

Omit empty sections. Update README/docs only if user-facing and the user agrees.

### 4. Verify green, then build the artifacts

```bash
# build + tests must be green before tagging
go vet ./... && go build ./... && go test ./...

# build the release artifacts into dist/
make build-all                       # dist/fusioninventory-agent (static linux/amd64)
make package-deb package-rpm 2>/dev/null || echo "(skip .deb/.rpm — nfpm not installed)"

# versioned tarball + checksums
tar -czf "dist/fusioninventory-agent_${NEXT}_linux_amd64.tar.gz" -C dist fusioninventory-agent
( cd dist && sha256sum fusioninventory-agent_${NEXT}_linux_amd64.tar.gz *.deb *.rpm 2>/dev/null > checksums.txt )
ls -la dist/
```

### 5. Commit + tag

```bash
# stage ONLY the release changes — never skills/dist/VM-state/secrets
git add CHANGELOG.md   # plus any agreed doc/code changes
git status --short

git commit -m "release: $NEXT — <summary>"   # or feat:/fix: per the change
git tag -a "$NEXT" -m "Release $NEXT — <summary>"
```

End commit messages with the project's `Co-Authored-By` trailer if used elsewhere.

### 6. Push main + tag

```bash
git push origin main
git push origin "$NEXT"
```

### 7. Publish the release with assets (`gh`)

`gh release create` creates the release **and** uploads the assets in one call.
Use a notes file mirroring the CHANGELOG entry (or `--generate-notes` for an
auto-generated diff of merged PRs/commits).

```bash
cat > /tmp/notes.md <<EOF
# $NEXT — <title>

## ✨ Added
- ...
## 🔧 Changed
- ...
## 🐛 Fixed
- ...

**Artifacts:** linux/amd64 (.tar.gz)$( ls dist/*.deb >/dev/null 2>&1 && echo ', .deb, .rpm' ), checksums.txt.

**Changelog:** https://github.com/$OWNER/$REPO/compare/$LAST...$NEXT
EOF

gh release create "$NEXT" \
  --title "$NEXT — <title>" \
  --notes-file /tmp/notes.md \
  dist/fusioninventory-agent_${NEXT}_linux_amd64.tar.gz \
  dist/checksums.txt \
  $(ls dist/*.deb dist/*.rpm 2>/dev/null)
```

> Prefer this CLI path. If the repo has a release workflow (see below), pushing the
> tag triggers it instead — in that case skip `gh release create` and only enrich the
> notes with `gh release edit "$NEXT" --notes-file /tmp/notes.md`.

### 8. Verify

```bash
gh release view "$NEXT" --json tagName,assets,url \
  -q '"\(.tagName) | \(.url)\nassets: \([.assets[].name] | join(", "))"'
```

### 9. (Optional) OpenSpec

If the release closes an OpenSpec change, run `/opsx:archive <change>` to sync the
main specs and archive it, then commit + push that separately.

---

## Quick reference

```bash
LAST=$(git describe --tags --abbrev=0 2>/dev/null || echo v0.0.0); IFS=. read MA MI PA <<<"${LAST#v}"; NEXT="v${MA}.$((MI+1)).0"
# 1) edit CHANGELOG.md   2) go vet/build/test green   3) make build-all + packages + tarball + checksums
git add CHANGELOG.md && git commit -m "release: $NEXT — ..."
git tag -a "$NEXT" -m "Release $NEXT" && git push origin main && git push origin "$NEXT"
gh release create "$NEXT" --title "$NEXT — ..." --notes-file /tmp/notes.md dist/*.tar.gz dist/checksums.txt $(ls dist/*.deb dist/*.rpm 2>/dev/null)
```

---

## Optional CI workflow (`.github/workflows/release.yml`)

If you prefer CI to build and publish on tag push, a minimal workflow is:

```yaml
name: release
on:
  push:
    tags: ['v*']
permissions:
  contents: write
jobs:
  release:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-go@v5
        with: { go-version: '1.26' }
      - run: make build-all
      - run: tar -czf dist/fusioninventory-agent_${GITHUB_REF_NAME}_linux_amd64.tar.gz -C dist fusioninventory-agent
      - uses: softprops/action-gh-release@v2
        with:
          files: dist/fusioninventory-agent_*_linux_amd64.tar.gz
          generate_release_notes: true
```

With this in place, the tag push creates the release automatically; the skill then
only enriches the notes (`gh release edit "$NEXT" --notes-file ...`).

## Guardrails

- **Never** push a tag before `go vet`, `go build`, and `go test` are green.
- **Never** stage `.agents/skills`, `.claude/skills`, `dist/`, Vagrant VM state
  (`test/vagrant/.vagrant/`), `test/glpi/.env`, or local settings into a release commit.
- **Never** hardcode a GitHub token; use `gh auth` and remind the user to rotate a
  pasted token.
- The tag is the version — no code constant to bump.
- Reuse an existing tag only by deleting it first (risky if already pulled); prefer a
  new patch version instead.
```
