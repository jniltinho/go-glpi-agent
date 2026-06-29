---
name: create-release
description: Cut a versioned release of this Go project on Gitea — bump the CHANGELOG, tag, push (the Gitea Actions workflow builds and publishes the release), then enrich the release notes via the Gitea API. Use when the user asks to "create a release", "release vX", "fechar a release", or "publicar a versão".
license: MIT
compatibility: Requires git + curl. Release artifacts are built by the Gitea Actions workflow (.gitea/workflows/release.yml). No GitHub CLI.
---

# Releasing this Go project (Gitea)

This project lives on a **self-hosted Gitea** (`git2.criarenet.com`), not GitHub —
so there is **no `gh` CLI**. A release is: bump the `CHANGELOG.md`, commit, tag, push.
The **Gitea Actions** workflow (`.gitea/workflows/release.yml`) builds the frontend +
binaries and **creates the release with the packages** on tag push. Then you enrich the
auto-generated notes via the **Gitea API**.

> ⚠️ The version comes from the **git tag** (`Makefile`: `git describe --tags` → ldflags
> into `buildinfo.Version`). There is **no version constant in code** to bump — the tag *is*
> the version.

---

## Project facts

```bash
REMOTE=$(git remote get-url origin)          # ssh://git@git2.criarenet.com:2224/suporte/painel-antispam.git
GITEA="https://git2.criarenet.com"           # web + API base
OWNER="suporte"; REPO="painel-antispam"     # from the remote path
LAST=$(git describe --tags --abbrev=0)        # e.g. v0.23.0
echo "Last tag: $LAST"
```

### Versioning

Series `0.x` (pre-1.0), tags always prefixed with `v`:

- **Feature** (new user-visible capability) → bump **minor**: `v0.23.0` → `v0.24.0`
- **Fix / docs / infra only** → bump **patch**: `v0.23.0` → `v0.23.1`

```bash
# minor bump helper
IFS=. read MAJ MIN PAT <<< "${LAST#v}"
NEXT="v${MAJ}.$((MIN+1)).0"     # feature → minor
echo "Next: $NEXT"
```

### Gitea API token

The API steps (poll CI, edit notes) need a token. **Never hardcode it** in files or
commits — pass it via env. Get one in Gitea → *Settings → Applications → Generate Token*
(scope: repo). The user provides it for the session:

```bash
export GITEA_TOKEN="<token>"     # the user types this, e.g. via `! export GITEA_TOKEN=...`
API="$GITEA/api/v1"
```

> If the user pasted a token in chat, remind them to **rotate it** afterward (it lands in
> the transcript).

---

## Process

### 1. Prerequisites

```bash
git fetch origin
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

Repo-root `CHANGELOG.md`, **Keep a Changelog** format in **pt-BR**, with a top
`## [Não lançado]` placeholder. Insert a new section **above** the previous version,
moving the relevant items out of `[Não lançado]`:

```markdown
## [Não lançado]

—

## [0.24.0] — YYYY-MM-DD

**Resumo de 1–2 linhas.**

### Adicionado
- ...
### Alterado
- ...
### Corrigido
- ...
### Segurança
- ...
### Removido
- ...
```

Omit empty sections. Update README/docs only if user-facing and the user agrees.

### 4. Verify, then commit + tag

```bash
# build + tests must be green before tagging
(cd frontend && npm run build)   # vue-tsc
go vet ./... && go build ./... && go test ./...
cd ..

# stage ONLY the release changes — never skills/dist/screenshots/secrets
git add -A
git diff --cached --name-only | grep -iE '\.agent/skills|\.claude/skills|skills-lock|web/dist|screencast|\.swp' \
  && echo "⚠ remove junk from stage" || echo "stage OK"

git commit -m "feat(painel): <resumo> — $NEXT"   # or fix:/docs: per the change
git tag -a "$NEXT" -m "Release $NEXT — <resumo>"
```

End commit messages with the project's co-author trailer if used elsewhere.

### 5. Push main + tag (triggers the Gitea CI)

```bash
git push origin main
git push origin "$NEXT"     # tag push → .gitea/workflows/release.yml runs
```

The workflow builds `make frontend` + binaries and **creates the Gitea release** with
assets: `antispam-painel_<ver>_linux_amd64.tar.gz`, `..._windows_amd64.zip`, `checksums.txt`.

### 6. Wait for the CI (Gitea Actions API)

The release object only appears **after** the workflow finishes (runs ~1–2 min). Poll:

```bash
for i in $(seq 1 18); do
  st=$(curl -s -k -H "Authorization: token $GITEA_TOKEN" \
        "$API/repos/$OWNER/$REPO/actions/tasks?limit=5" \
       | python3 -c "import sys,json;
wr=json.load(sys.stdin).get('workflow_runs',[])
print(next((r['status'] for r in wr if r.get('head_branch')=='$NEXT'),'?'))")
  echo "[$i] $NEXT = $st"; [ "$st" != "running" ] && [ -n "$st" ] && break; sleep 15
done
```

### 7. Verify the release + assets

```bash
curl -s -k -H "Authorization: token $GITEA_TOKEN" \
  "$API/repos/$OWNER/$REPO/releases/tags/$NEXT" \
  | python3 -c "import sys,json; r=json.load(sys.stdin);
print('id', r['id'], '| draft', r['draft'], '| assets', [a['name'] for a in r['assets']])"
```

### 8. Enrich the release notes (Gitea API)

The CI writes minimal notes. Replace with structured markdown (mirror the CHANGELOG
entry). Get the release `id` from step 7, then `PATCH`:

```bash
RID=$(curl -s -k -H "Authorization: token $GITEA_TOKEN" \
  "$API/repos/$OWNER/$REPO/releases/tags/$NEXT" \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['id'])")

cat > /tmp/notes.md <<'EOF'
# vX.Y.Z — <título>

## ✨ Adicionado
- ...
## 🔧 Alterado
- ...
## 🐛 Corrigido
- ...

**Pacotes:** linux/amd64 (.tar.gz), windows/amd64 (.zip), checksums.txt.

**Changelog:** https://git2.criarenet.com/suporte/painel-antispam/compare/PREV...NEXT
EOF

python3 -c "import json;print(json.dumps({'name':'vX.Y.Z — <título>','body':open('/tmp/notes.md').read()}))" > /tmp/patch.json
curl -s -k -X PATCH -H "Authorization: token $GITEA_TOKEN" -H "Content-Type: application/json" \
  --data @/tmp/patch.json "$API/repos/$OWNER/$REPO/releases/$RID" \
  | python3 -c "import sys,json;r=json.load(sys.stdin);print('ok:',r['name'],'|',r['html_url'])"
```

### 9. (Optional) OpenSpec

If the release closes an OpenSpec change, run `/opsx:archive <change>` to sync the main
specs and archive it, then commit + push that separately.

---

## Quick reference

```bash
LAST=$(git describe --tags --abbrev=0); IFS=. read MA MI PA <<<"${LAST#v}"; NEXT="v${MA}.$((MI+1)).0"
# 1) edit CHANGELOG.md  2) build+test green
git add -A && git commit -m "feat: ... — $NEXT"
git tag -a "$NEXT" -m "Release $NEXT" && git push origin main && git push origin "$NEXT"
# 3) poll CI (step 6)  4) get RID (step 8)  5) PATCH notes (step 8)
```

---

## CI workflow (`.gitea/workflows/release.yml`)

| Artefato | Status |
|---|---|
| `linux/amd64` (.tar.gz) | ✅ |
| `windows/amd64` (.zip) | ✅ |
| `checksums.txt` | ✅ |
| Release criado no tag push | ✅ |
| Notas automáticas | ⚠️ mínimas — enriquecidas via API (passo 8) |

## Guardrails

- **Never** push a tag before build + tests are green.
- **Never** stage `.claude/skills`, `.agent/skills`, `skills-lock.json`, `web/dist`,
  `docs/screencast-validar/`, or `.swp` files into a release commit.
- **Never** hardcode the Gitea token; use `$GITEA_TOKEN` and remind the user to rotate a
  pasted token.
- The tag is the version — no code constant to bump.
- Reuse an existing tag only by deleting it first (risky if already pulled); prefer a new
  patch version instead.
