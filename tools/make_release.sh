#!/bin/bash
# Builds a CLEAN, Workshop-shippable copy of RimDoctor into ./release/RimDoctor —
# only the files a player needs. Excludes .git, tools/, build artifacts, the Mac app.
# Run ./build.sh first so 1.6/Assemblies/RimDoctor.dll is current.
set -euo pipefail
cd "$(dirname "$0")/.."        # repo root
REPO="$(pwd)"
OUT="$REPO/release/RimDoctor"

echo "==> Staging clean release at: $OUT"
rm -rf "$OUT"
mkdir -p "$OUT"

# ---- ship list (only what the game loads) ----
cp -R "About"            "$OUT/"            # About.xml, Preview.png, PublishedFileId.txt
cp -R "1.6"             "$OUT/"             # Assemblies (+ Defs if any)
[ -d "Data" ]        && cp -R "Data"        "$OUT/"
[ -f "loadFolders.xml" ] && cp "loadFolders.xml" "$OUT/"
[ -f "LICENSE" ]     && cp "LICENSE"        "$OUT/"
[ -f "README.md" ]   && cp "README.md"      "$OUT/"

# Optional: include C# source (common + welcomed on Workshop). Comment out to omit.
if [ -d "Source" ]; then
  mkdir -p "$OUT/Source"
  # copy source but NOT build artifacts
  (cd "Source" && find . -type f -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*" \
      | while read -r f; do mkdir -p "$OUT/Source/$(dirname "$f")"; cp "$f" "$OUT/Source/$f"; done)
  [ -f "Source/RimDoctor/RimDoctor.csproj" ] && { mkdir -p "$OUT/Source/RimDoctor"; cp "Source/RimDoctor/RimDoctor.csproj" "$OUT/Source/RimDoctor/"; }
fi

# strip macOS metadata
find "$OUT" -name ".DS_Store" -delete 2>/dev/null || true
xattr -cr "$OUT" 2>/dev/null || true

echo "==> Clean release contents:"
( cd "$OUT/.." && du -sh RimDoctor && find RimDoctor -maxdepth 2 -type d | sort )
echo "==> Size:"; du -sh "$OUT" | awk '{print "    "$1}'
echo "==> Done. Upload this folder (see tools/PUBLISH.md)."
