#!/usr/bin/env bash
# Adds a released version to the vcpkg registry: the branch vcpkg-registry of this repository
# (ports/fullint + versions/, the layout of a vcpkg git registry).
#   bash vcpkg/update-registry.sh <tag> [<registry worktree dir>]
# The port is vcpkg/port with the version and the SHA-512 of the tag's source archive filled in;
# the version becomes the registry's baseline. Commits to the worktree's branch, does not push.
set -euo pipefail
TAG=$1
V=${TAG#v}
SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SRC/.." && pwd)"
WT="${2:-$REPO/out/vcpkg-registry}"
URL="https://github.com/denis-king/FullInt/archive/$TAG.tar.gz"

sha=""
for i in 1 2 3 4 5; do
    sha=$(curl -fsSL "$URL" | sha512sum | cut -d' ' -f1) && break
    sleep 10
done
[ -n "$sha" ] || { echo "cannot download $URL"; exit 1; }
echo "$TAG: SHA512 $sha"

# the worktree of the branch (an orphan branch the first time)
if [ ! -d "$WT/.git" ] && [ ! -f "$WT/.git" ]; then
    if git -C "$REPO" rev-parse --verify -q vcpkg-registry > /dev/null \
       || git -C "$REPO" fetch -q origin vcpkg-registry:vcpkg-registry 2> /dev/null; then
        git -C "$REPO" worktree add -q "$WT" vcpkg-registry
    else
        git -C "$REPO" worktree add -q --orphan -b vcpkg-registry "$WT"
        printf '# FullInt vcpkg registry\n\nThe vcpkg port of FullInt (https://github.com/denis-king/FullInt),\nupdated by its publish workflow; usage: docs/CPP_INSTALL.md there.\n' > "$WT/README.md"
    fi
fi
cd "$WT"

# 1. the port
mkdir -p ports/fullint
sed "s/@SHA512@/$sha/" "$SRC/port/portfile.cmake" > ports/fullint/portfile.cmake
sed "s/@VERSION@/$V/" "$SRC/port/vcpkg.json" > ports/fullint/vcpkg.json
cp "$SRC/port/usage" ports/fullint/usage
git add -A
git -c user.name="${GIT_NAME:-Denys Omelianenko}" -c user.email="${GIT_EMAIL:-denis-king@ukr.net}" \
    commit -q -m "fullint $V: the port" || echo "the port is unchanged"

# 2. the versions: the port's git tree for this version, the baseline
tree=$(git rev-parse HEAD:ports/fullint)
mkdir -p versions/f-
PY=python3; $PY -c '' 2> /dev/null || PY=python
$PY - "$V" "$tree" <<'EOF'
import json, os, sys
v, tree = sys.argv[1], sys.argv[2]
p = 'versions/f-/fullint.json'
d = json.load(open(p)) if os.path.exists(p) else {'versions': []}
d['versions'] = [e for e in d['versions'] if e.get('version-semver') != v]
d['versions'].insert(0, {'git-tree': tree, 'version-semver': v, 'port-version': 0})
json.dump(d, open(p, 'w'), indent=2); open(p, 'a').write('\n')
b = {'default': {'fullint': {'baseline': v, 'port-version': 0}}}
json.dump(b, open('versions/baseline.json', 'w'), indent=2); open('versions/baseline.json', 'a').write('\n')
EOF
git add -A
git -c user.name="${GIT_NAME:-Denys Omelianenko}" -c user.email="${GIT_EMAIL:-denis-king@ukr.net}" \
    commit -q -m "fullint $V: versions, baseline" || echo "the versions are unchanged"
git log --oneline -3
echo "registry commit: $(git rev-parse HEAD)"
