# Publishing / updating RimDoctor (Steam Workshop + GitHub)

A quick, repeatable checklist. The **repo is the source of truth** — everything that
ships is generated from it.

- Workshop item: **3748829922** — https://steamcommunity.com/sharedfiles/filedetails/?id=3748829922
- Author: itygr · packageId `tyler.rimdoctor` · Requires Harmony · RimWorld 1.6 · Mac + Windows

## Paths
```
REPO="/Users/tylergrieve/Documents/Documents - Tyler Macbook Pro/App Dev NEWEST/Rimworld Mod"
WORKSHOP="$HOME/Library/Application Support/Steam/steamapps/workshop/content/294100/3748829922"
```

## 0. Bump the version + edit notes (do this first)
The version lives in **two** spots and they must match:
- [ ] `Source/RimDoctor/Core/RimDoctorMod.cs` → `public const string Version = "X.Y.Z";`
- [ ] `About/About.xml` → description first line `RimDoctor vX.Y.Z — …`
- [ ] Add a line to the `Changelog` block in `About/About.xml`.
- [ ] Preview only needs regenerating if the art/text changed (the version is intentionally
      NOT on the card):
      ```
      swiftc -O tools/make_preview.swift -o /tmp/mkprev && /tmp/mkprev "About/Preview.png"
      ```

## 1. Build -> stage -> push into the live mod folder (so you test the real bytes)
```
cd "$REPO"
./build.sh                                          # compile DLL (carries the new Version)
./tools/make_release.sh                             # clean copy -> release/RimDoctor
rsync -a --delete "release/RimDoctor/" "$WORKSHOP/" # this is what the game loads
```

## 2. Test in-game — this exact build
- [ ] Launch RimWorld with the small test list (Core + 5 DLCs + Harmony + Loading Progress + RimDoctor).
- [ ] Bottom bar shows the **RimDoctor** tab; the tab title reads the new version.
- [ ] Audio works (main-menu music + in-game SFX) — no "could not resolve any grains" flood.
- [ ] Load Order tab is **read-only** (no apply button, "suggestions only" note); other tabs open clean.

## 3. Publish to the Workshop (from inside the game)
1. Options -> enable **Development mode**.
2. Main menu -> **Mods** -> select **RimDoctor**.
3. Bottom bar -> **Update mod on Steam Workshop**.
4. Paste the **change notes** when prompted, confirm, wait for "upload complete."

First upload only: a browser opens the Steam Workshop Legal Agreement — accept it, then set
the item **Public** with tags **Mod** + **1.6**.

## 4. Refresh the Steam page text (only if it changed)
- The page description uses **BBCode** — paste the formatted block.
- The in-game `About.xml` description is the same copy with BBCode stripped. Keep them in sync.

## 5. Save it — git + GitHub
```
cd "$REPO"
git add -A
git commit -F - <<'MSG'
vX.Y.Z: <one-line summary>

- <what changed>
MSG
git push origin main

# release zip + GitHub release (Latest)
( cd release && zip -rq "../RimDoctor-X.Y.Z.zip" RimDoctor -x '*.DS_Store' )
gh release create vX.Y.Z "RimDoctor-X.Y.Z.zip" \
  --title "vX.Y.Z — <title>" --notes-file <notes.md>
```
**Never** add `Co-Authored-By` or "Generated with …" trailers to commits or PR bodies.

## Good to know
- The game loads RimDoctor from the **subscribed Workshop copy** (`$WORKSHOP`), not a local
  `Mods/` folder. Step 1's rsync lets you test the exact bytes you'll ship; the in-game
  **Update** then re-uploads that folder.
- `About/PublishedFileId.txt` (= 3748829922) ties the folder to the Workshop item — keep it so
  uploads *update* the existing item instead of creating a duplicate.
- Build artifacts (`release/`, `RimDoctor-*.zip`, `Source/**/obj/`, the `.app`) are git-ignored
  on purpose; the zip is attached to the GitHub release, not committed.

## Alternate: upload as a local mod (avoids touching Steam's folder)
If you'd rather not write into the Steam-managed Workshop folder, symlink a local copy and
upload that instead (RimWorld treats it as the same item via `PublishedFileId.txt`):
```
MODS="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods"
ln -sfn "$REPO/release/RimDoctor" "$MODS/RimDoctor"   # upload this in-game, then:
ln -sfn "$REPO" "$MODS/RimDoctor"                     # restore dev symlink afterward
```
