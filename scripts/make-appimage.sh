#!/usr/bin/env bash
# scripts/make-appimage.sh <version>
# Builds an AppImage from the linux-x64 self-contained publish output.
# Must be run from the repository root. Requires publish/linux-x64 to exist.
set -euo pipefail

VERSION="${1:?Usage: make-appimage.sh <version>}"
PUBLISH_DIR="publish/linux-x64"
APPDIR="BookDB.AppDir"
trap 'rm -rf "$APPDIR"' EXIT

# Create AppDir structure
mkdir -p "$APPDIR/usr/bin"
cp -r "$PUBLISH_DIR/." "$APPDIR/usr/bin/"

# Desktop entry (Icon=book must match the .png filename without extension)
cat > "$APPDIR/BookDB.desktop" << 'DESKTOP'
[Desktop Entry]
Name=BookDB
Exec=BookDB.Desktop
Icon=book
Type=Application
Categories=Office;Database;
DESKTOP

# Icon: book.png exists at src/BookDB.Desktop/Assets/book.png (verified)
cp src/BookDB.Desktop/Assets/book.png "$APPDIR/book.png"

# AppRun launcher script
cat > "$APPDIR/AppRun" << 'APPRUN'
#!/bin/bash
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/BookDB.Desktop" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

# Download appimagetool if not cached (pinned to release 13, SHA-256 verified)
APPIMAGETOOL_VERSION="1.9.1"
APPIMAGETOOL_SHA256="ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"
if [[ ! -f appimagetool-x86_64.AppImage ]]; then
    wget -q "https://github.com/AppImage/appimagetool/releases/download/${APPIMAGETOOL_VERSION}/appimagetool-x86_64.AppImage"
    echo "${APPIMAGETOOL_SHA256}  appimagetool-x86_64.AppImage" | sha256sum -c -
    chmod +x appimagetool-x86_64.AppImage
fi

# --appimage-extract-and-run is REQUIRED on GitHub Actions (no FUSE on ubuntu-latest)
ARCH=x86_64 VERSION="$VERSION" \
    ./appimagetool-x86_64.AppImage --appimage-extract-and-run \
    "$APPDIR" "BookDB-v${VERSION}-linux-x64.AppImage"

echo "AppImage created: BookDB-v${VERSION}-linux-x64.AppImage"
