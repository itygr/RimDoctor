#!/usr/bin/env bash
#
# RimDoctor build script — compiles the C# assembly into 1.6/Assemblies/RimDoctor.dll
# using Mono's msbuild (no dotnet SDK required).
#
# Usage:
#   ./build.sh           # build (Release)
#   ./build.sh link      # build, then symlink the mod into RimWorld's Mods folder
#
set -euo pipefail

MOD_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CSPROJ="$MOD_ROOT/Source/RimDoctor/RimDoctor.csproj"
MODS_DIR="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods"
LINK="$MODS_DIR/RimDoctor"

echo "==> Building RimDoctor (Mono msbuild)"
msbuild "$CSPROJ" /p:Configuration=Release /v:minimal /nologo

DLL="$MOD_ROOT/1.6/Assemblies/RimDoctor.dll"
if [[ -f "$DLL" ]]; then
  echo "==> Built: $DLL"
else
  echo "!! Build reported success but DLL not found at $DLL" >&2
  exit 1
fi

if [[ "${1:-}" == "link" ]]; then
  if [[ -e "$LINK" || -L "$LINK" ]]; then
    echo "==> Mods/RimDoctor already exists — leaving it as-is"
  else
    ln -s "$MOD_ROOT" "$LINK"
    echo "==> Symlinked $LINK -> $MOD_ROOT"
  fi
fi

echo "==> Done."
