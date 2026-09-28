#!/usr/bin/env bash
# Builds SpaceAnalyzer.app for macOS.
#
#   ./build/macos-app.sh                      # this Mac's architecture, single file (needs the .NET 10 runtime)
#   ./build/macos-app.sh --aot                # native (Native AOT): nothing else to install
#   ./build/macos-app.sh --aot --universal    # native, Apple Silicon + Intel in one binary (for releases)
#   ./build/macos-app.sh osx-x64              # a specific architecture
#
# Result: artifacts/SpaceAnalyzer.app (ad-hoc signed; the icon is rendered by the app itself).
set -euo pipefail
cd "$(dirname "$0")/.."

AOT=false
UNIVERSAL=false
ARCH="$(uname -m)"
RID="osx-${ARCH/x86_64/x64}"
for arg in "$@"; do
  case "$arg" in
    --aot) AOT=true ;;
    --universal) UNIVERSAL=true ;;
    osx-*) RID="$arg" ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done
if $UNIVERSAL && ! $AOT; then
  echo "--universal needs --aot (a single-file bundle cannot be merged with lipo)" >&2
  exit 2
fi

OUT=artifacts
APP="$OUT/SpaceAnalyzer.app"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"

publish() { # rid -> output folder
  local extra=()
  $AOT && extra=(-p:PublishAot=true)
  dotnet publish src/SpaceAnalyzer -c Release -r "$1" -o "$OUT/$1" --nologo -v quiet "${extra[@]}"
}

if $UNIVERSAL; then
  publish osx-arm64
  publish osx-x64
  BIN="$OUT/SpaceAnalyzer-universal"
  lipo -create -output "$BIN" "$OUT/osx-arm64/SpaceAnalyzer" "$OUT/osx-x64/SpaceAnalyzer"
else
  publish "$RID"
  BIN="$OUT/$RID/SpaceAnalyzer"
fi

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
