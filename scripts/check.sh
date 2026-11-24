#!/bin/bash
# The gate every phase must pass. Only this output counts, never a lane's own claim.
set -e
cd "$(dirname "$0")/.."

# Only existing paths: grep exits 2 (not 0) when a path is missing, even if it also found a match.
dash="$(printf '\342\200\224\|\342\200\223')"
paths=()
for p in src tests scripts docs profiles CLAUDE.md README.md DEVLOG.md ASSET-LICENSES.md; do
  [ -e "$p" ] && paths+=("$p")
done
if grep -rn --include=*.cs --include=*.js --include=*.html --include=*.css --include=*.md --include=*.json --include=*.sh \
    --exclude-dir=bin --exclude-dir=obj "$dash" "${paths[@]}"; then
  echo "dash found"
  exit 1
fi

dotnet build --nologo -v q
dotnet test --nologo -v q --no-build
