#!/usr/bin/env bash
# scripts/make-appbundle.sh <arch> <version>
# Builds a macOS .app bundle and DMG from the osx-{arch} self-contained publish output.
# Must be run from the repository root. Requires publish/osx-{arch} to exist.
set -euo pipefail

ARCH="${1:?Usage: make-appbundle.sh <arch> <version>}"
VERSION="${2:?Usage: make-appbundle.sh <arch> <version>}"
RID="osx-${ARCH}"
PUBLISH_DIR="publish/${RID}"
APP_NAME="BookDB.app"
APP_DIR="${APP_NAME}/Contents"
STAGING="dmg-staging-${ARCH}"
ICONSET="book.iconset"
trap 'rm -rf "${APP_NAME}" "${STAGING}" "${ICONSET}"' EXIT

# Verify publish output exists and contains the required binary
if [[ ! -f "${PUBLISH_DIR}/BookDB.Desktop" ]]; then
    echo "Error: publish directory '${PUBLISH_DIR}' is missing or does not contain BookDB.Desktop" >&2
    exit 1
fi

# Create .app bundle structure
mkdir -p "${APP_DIR}/MacOS" "${APP_DIR}/Resources"
cp -r "${PUBLISH_DIR}/." "${APP_DIR}/MacOS/"
chmod +x "${APP_DIR}/MacOS/BookDB.Desktop"

# Generate .icns from book.png using macOS built-in tools (sips + iconutil)
mkdir -p "${ICONSET}"
for s in 16 32 128 256 512; do
    sips -z $s $s src/BookDB.Desktop/Assets/book.png --out "${ICONSET}/icon_${s}x${s}.png" 2>/dev/null
    sips -z $((s*2)) $((s*2)) src/BookDB.Desktop/Assets/book.png --out "${ICONSET}/icon_${s}x${s}@2x.png" 2>/dev/null
done
iconutil -c icns "${ICONSET}" -o "${APP_DIR}/Resources/book.icns"
rm -rf "${ICONSET}"

# Write Info.plist (no single-quote on PLIST delimiter so ${VERSION} expands)
cat > "${APP_DIR}/Info.plist" << PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleExecutable</key><string>BookDB.Desktop</string>
    <key>CFBundleIdentifier</key><string>com.cadwal.bookdb</string>
    <key>CFBundleVersion</key><string>${VERSION}</string>
    <key>CFBundleShortVersionString</key><string>${VERSION}</string>
    <key>CFBundleName</key><string>BookDB</string>
    <key>CFBundleDisplayName</key><string>BookDB</string>
    <key>CFBundleIconFile</key><string>book</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>LSMinimumSystemVersion</key><string>12.0</string>
</dict>
</plist>
PLIST

# Stage .app bundle and create DMG
mkdir -p "${STAGING}"
cp -r "${APP_NAME}" "${STAGING}/"
hdiutil create \
    -volname "BookDB ${VERSION}" \
    -srcfolder "${STAGING}" \
    -ov \
    -format UDZO \
    "BookDB-v${VERSION}-osx-${ARCH}.dmg"

echo "DMG created: BookDB-v${VERSION}-osx-${ARCH}.dmg"
