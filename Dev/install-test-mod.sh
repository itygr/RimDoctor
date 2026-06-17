#!/usr/bin/env bash
#
# Symlinks the deliberately-broken test mod into RimWorld's Mods folder so you
# can enable it in-game and verify RimDoctor. Does NOT edit ModsConfig.xml —
# enable "RimDoctor TEST — Broken Content" from the in-game mod list yourself.
#
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MODS_DIR="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods"
LINK="$MODS_DIR/RimDoctorTestBroken"

if [[ -e "$LINK" || -L "$LINK" ]]; then
  echo "==> $LINK already exists — leaving as-is"
else
  ln -s "$HERE/TestBrokenMod" "$LINK"
  echo "==> Symlinked $LINK -> $HERE/TestBrokenMod"
fi
echo "Now: launch RimWorld, enable 'RimDoctor TEST — Broken Content' (after RimDoctor), restart."
