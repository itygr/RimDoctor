#!/bin/bash
# Builds "RimDoctor Repair.app" — compiles the SwiftUI front-end, generates an
# app icon, and assembles a double-clickable .app bundle with the bisection
# engine embedded in Resources.
set -euo pipefail
cd "$(dirname "$0")"

APP="RimDoctor Repair.app"
BIN="RimDoctorRepair"
ENGINE="../bisect/rimdoctor_bisect.sh"

echo "== generating app icon =="
swiftc -O -o make_icon make_icon.swift -framework AppKit
./make_icon icon_1024.png >/dev/null
ICONSET="AppIcon.iconset"
rm -rf "$ICONSET"; mkdir "$ICONSET"
sips -z 16   16   icon_1024.png --out "$ICONSET/icon_16x16.png"      >/dev/null
sips -z 32   32   icon_1024.png --out "$ICONSET/icon_16x16@2x.png"   >/dev/null
sips -z 32   32   icon_1024.png --out "$ICONSET/icon_32x32.png"      >/dev/null
sips -z 64   64   icon_1024.png --out "$ICONSET/icon_32x32@2x.png"   >/dev/null
sips -z 128  128  icon_1024.png --out "$ICONSET/icon_128x128.png"    >/dev/null
sips -z 256  256  icon_1024.png --out "$ICONSET/icon_128x128@2x.png" >/dev/null
sips -z 256  256  icon_1024.png --out "$ICONSET/icon_256x256.png"    >/dev/null
sips -z 512  512  icon_1024.png --out "$ICONSET/icon_256x256@2x.png" >/dev/null
sips -z 512  512  icon_1024.png --out "$ICONSET/icon_512x512.png"    >/dev/null
cp icon_1024.png "$ICONSET/icon_512x512@2x.png"
iconutil -c icns "$ICONSET" -o AppIcon.icns

echo "== compiling SwiftUI app =="
swiftc -O -parse-as-library -o "$BIN" RimDoctorRepair.swift \
  -framework SwiftUI -framework AppKit

echo "== assembling bundle: $APP =="
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
mv "$BIN" "$APP/Contents/MacOS/$BIN"
cp "$ENGINE" "$APP/Contents/Resources/rimdoctor_bisect.sh"
chmod +x "$APP/Contents/Resources/rimdoctor_bisect.sh"
cp AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>RimDoctor Repair</string>
  <key>CFBundleDisplayName</key><string>RimDoctor Repair</string>
  <key>CFBundleIdentifier</key><string>tyler.rimdoctor.repair</string>
  <key>CFBundleVersion</key><string>1.1</string>
  <key>CFBundleShortVersionString</key><string>1.1</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>$BIN</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
</dict>
</plist>
PLIST

# clean up intermediates
rm -rf "$ICONSET" make_icon

# strip extended attributes (codesign rejects resource-fork/Finder detritus)
xattr -cr "$APP" 2>/dev/null || true
# ad-hoc codesign so Gatekeeper lets it run locally
codesign --force --sign - "$APP/Contents/MacOS/$BIN" 2>/dev/null || true
codesign --force --sign - "$APP" 2>/dev/null && echo "signed (ad-hoc)" || echo "(codesign skipped — app still runs)"
touch "$APP"

echo "== done =="
echo "Built: $(pwd)/$APP"
echo "Run with:  open \"$(pwd)/$APP\""
