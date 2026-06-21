# RimDoctor

A smart in-game RimWorld 1.6 mod manager + load/texture/log doctor.

> **Steam Workshop:** https://steamcommunity.com/sharedfiles/filedetails/?id=3748829922
> **Author:** itygr · **packageId:** `tyler.rimdoctor`

- **Load-order sorter** — dependency rules + community rules DB → topo-sort → writes `ModsConfig.xml` → Apply & Restart.
- **Content health scanner** — finds missing textures, bad def paths, incomplete Steam downloads.
- **Runtime texture fallback** *(headline)* — substitutes a generated placeholder for any texture that fails to load, so a missing texture never freezes/crashes the game.
- **Log Doctor** — reads the live error log and explains each error in plain language: what it means, the likely culprit mod, and a suggested fix.
- **Performance analytics** — live TPS (actual vs target), FPS, ms/tick, tick-budget %, sparkline, and memory/GC, plus **per-mod tick attribution** (which mods eat your ticks) and a compact draggable on-screen HUD.
- **Analytics tab** — startup load weight (defs/assemblies per mod) and save/world bloat (things & world-pawns by mod, save size).
- **Repair tiers** — report-only → safe auto-fix → maximum, all emitted into a generated local override mod (never edits Workshop files).

RimDoctor **fails safe**: if any feature errors, it disables that feature and logs it — it never blocks the game from loading.

## Building (macOS, Mono)

No `dotnet` SDK needed — builds with Mono's `msbuild`.

```bash
./build.sh          # compile -> 1.6/Assemblies/RimDoctor.dll
./build.sh link     # compile, then symlink this folder into RimWorld's Mods/
```

The symlink (`RimWorldMac.app/Mods/RimDoctor`) means a rebuild is picked up next time you launch the game — no copying.

## Paths

Build paths are in [`RimDoctor.paths.props`](RimDoctor.paths.props), detected on this machine:

- **Managed DLLs**: `…/Steam/steamapps/common/RimWorld/RimWorldMac.app/Contents/Resources/Data/Managed`
- **Harmony**: referenced from the `brrainz.harmony` Workshop mod (never bundled)
- **Game version**: RimWorld 1.6.4850

Game and Harmony DLLs are **referenced, never committed or copied** into the mod.

## Dependencies

Requires [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) (`brrainz.harmony`), declared in `About/About.xml` and loaded before RimDoctor.

## Companion tooling (`tools/`)

- **Bisection harness** (`tools/bisect/rimdoctor_bisect.sh`) — launches RimWorld headless
  with `-quicktest` and binary-searches a mod list to pin conflicting/broken mods. Modes:
  `--check`, `--build` (resumable additive rebuild), `--pair`, `--conflict`.
- **RimDoctor Repair.app** (`tools/app`) — a native SwiftUI front-end over the harness
  (clickable actions, live progress log, one-click apply/restore). Build: `tools/app/build_app.sh`.
- **Release + publish** — `tools/make_release.sh` stages a clean Workshop-shippable copy;
  see `tools/PUBLISH.md` for the Steam upload flow.

## Status

- [x] **Foundation** — scaffold, fail-safe Harmony bootstrap, settings, RimDoctor tab, clean build
- [x] **M1** — Load-order sorter + manager UI (diff, warnings, Apply & Restart, import/export)
- [x] **M3** — Runtime missing-texture fallback (headline anti-crash)
- [x] **M4** — Log Doctor (capture, interpret, attribute, copy report)
- [x] **M2** — Content health scanner (missing textures + incomplete-download detection)
- [x] **M5** — Repair tiers (report / safe / maximum) via generated override mod, backups + undo
- [x] **M6** — In-game diagnostics + deep reporter
- [x] **Performance** — live TPS/FPS/ms + per-mod tick & memory analytics + compact HUD
- [x] **Analytics** — startup load-weight + save/world bloat
- [x] **Published** to the Steam Workshop

All features are individually Harmony-patched and try/catch-guarded — a failure in
any one disables only that feature and never blocks game startup.
