# Publishing RimDoctor to Steam Workshop (macOS)

RimWorld has a built-in Workshop uploader. The catch: your `Mods/RimDoctor` is a
**symlink to the whole dev repo**, so uploading as-is would ship `.git`, `tools/`,
and the macOS app. We upload a **clean release folder** instead.

## One-time facts
- Upload requires **RimWorld launched through Steam** (logged in).
- The mod must be a **local** mod (in `…/RimWorldMac.app/Mods/`), not a subscribed copy.
- `About/Preview.png` is the thumbnail (yours: ✓, 25 KB — keep it < 1 MB).
- First upload creates `About/PublishedFileId.txt` (the Workshop ID). **Keep it** — it
  links the folder to the Workshop item so future uploads *update* instead of duplicating.

## Paths
```
REPO="/Users/tylergrieve/Documents/Documents - Tyler Macbook Pro/App Dev NEWEST/Rimworld Mod"
MODS="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods"
```

## Publish steps

### 1. Build + stage the clean release
```
cd "$REPO"
./build.sh                 # compile the current DLL
./tools/make_release.sh    # stage clean copy at release/RimDoctor
```

### 2. Point the Steam Mods entry at the clean release (for upload only)
```
ln -sfn "$REPO/release/RimDoctor" "$MODS/RimDoctor"
```

### 3. Upload from inside RimWorld
1. Launch **RimWorld via Steam**.
2. (Recommended) Options → enable **Development mode**.
3. Main menu → **Mods**.
4. Select **RimDoctor** in the list (it's a local mod).
5. Bottom bar → **Upload to Steam Workshop** (the Steam icon button).
6. **First time only:** a browser opens with the Steam Workshop Legal Agreement —
   **accept it**, or the item stays hidden.
7. Wait for the "upload complete" confirmation.

### 4. Preserve the Workshop ID + restore your dev setup
```
# copy the new ID back into the repo so future builds keep updating the same item
cp "$REPO/release/RimDoctor/About/PublishedFileId.txt" "$REPO/About/PublishedFileId.txt" 2>/dev/null

# restore the dev symlink so ./build.sh keeps deploying live
ln -sfn "$REPO" "$MODS/RimDoctor"
```

### 5. Finish on the Steam page
- Open Steam → your profile → **Workshop Items** → RimDoctor.
- Set **visibility to Public** (new items often default to hidden).
- Add tags: **Mod**, **1.6**. Confirm the description/preview look right.

## Updating later
Repeat steps 1–4 (the `PublishedFileId.txt` now in `About/` makes it an update, not a
new item). On step 3 the button reads **Update**. Bump notes via the Steam page or a
`News`/changelog as you like.
