#!/usr/bin/env bash
# Builds SpaceAnalyzer.app for macOS from the single-file executable.
#
#   ./build/macos-app.sh              # for this Mac's architecture
#   ./build/macos-app.sh osx-x64      # or osx-arm64
#
# Result: artifacts/SpaceAnalyzer.app (ad-hoc signed; the icon is rendered by the app itself).
set -euo pipefail
cd "$(dirname "$0")/.."

ARCH="$(uname -m)"
RID="${1:-osx-${ARCH/x86_64/x64}}"
OUT=artifacts
APP="$OUT/SpaceAnalyzer.app"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"

dotnet publish src/SpaceAnalyzer -c Release -r "$RID" -o "$OUT/$RID" --nologo -v quiet
BIN="$OUT/$RID/SpaceAnalyzer"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/SpaceAnalyzer"

ICONSET="$OUT/AppIcon.iconset"
rm -rf "$ICONSET"
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  "$BIN" --snapshot "$ICONSET/icon_${s}x${s}.png" --screen icon --size "${s}x${s}" > /dev/null
  "$BIN" --snapshot "$ICONSET/icon_${s}x${s}@2x.png" --screen icon --size "$((s * 2))x$((s * 2))" > /dev/null
done
iconutil --convert icns --output "$APP/Contents/Resources/AppIcon.icns" "$ICONSET"
rm -rf "$ICONSET"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>SpaceAnalyzer</string>
  <key>CFBundleDisplayName</key><string>SpaceAnalyzer</string>
  <key>CFBundleIdentifier</key><string>io.github.aurgo.spaceanalyzer</string>
  <key>CFBundleExecutable</key><string>SpaceAnalyzer</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSSupportsAutomaticGraphicsSwitching</key><true/>
</dict>
</plist>
EOF

codesign --force --sign - "$APP" > /dev/null 2>&1
echo "Built $APP ($(du -sh "$APP" | cut -f1))"
