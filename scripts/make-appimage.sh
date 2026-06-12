#!/usr/bin/env bash
# scripts/make-appimage.sh <version> [arch]
# Builds an AppImage from a self-contained Linux publish output.
#   arch = x86_64 (default) -> publish/linux-x64   -> BookDB-v<version>-x86_64.AppImage
#   arch = aarch64          -> publish/linux-arm64 -> BookDB-v<version>-aarch64.AppImage
# AppImage filenames use the canonical AppImage arch token (x86_64/aarch64) — the same value
# passed to appimagetool — which is what AppImage tooling and the AM catalog expect.
# Must be run from the repository root. Requires the matching publish/<rid> to exist.
set -euo pipefail

VERSION="${1:?Usage: make-appimage.sh <version> [arch]}"
ARCH="${2:-x86_64}"

# appimagetool releases ship a per-arch binary; pinned to 1.9.1, SHA-256 verified below.
case "$ARCH" in
    x86_64)  RID="linux-x64";   APPIMAGETOOL_SHA256="ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0" ;;
    aarch64) RID="linux-arm64"; APPIMAGETOOL_SHA256="f0837e7448a0c1e4e650a93bb3e85802546e60654ef287576f46c71c126a9158" ;;
    *) echo "Unsupported arch: $ARCH (expected x86_64 or aarch64)" >&2; exit 1 ;;
esac

PUBLISH_DIR="publish/${RID}"
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

# Download appimagetool for this arch if not cached (pinned release, SHA-256 verified)
APPIMAGETOOL_VERSION="1.9.1"
APPIMAGETOOL="appimagetool-${ARCH}.AppImage"
if [[ ! -f "$APPIMAGETOOL" ]]; then
    wget -q "https://github.com/AppImage/appimagetool/releases/download/${APPIMAGETOOL_VERSION}/${APPIMAGETOOL}"
    echo "${APPIMAGETOOL_SHA256}  ${APPIMAGETOOL}" | sha256sum -c -
    chmod +x "$APPIMAGETOOL"
fi

# --appimage-extract-and-run is REQUIRED on GitHub Actions (no FUSE on the runners)
ARCH=$ARCH VERSION="$VERSION" \
    "./$APPIMAGETOOL" --appimage-extract-and-run \
    "$APPDIR" "BookDB-v${VERSION}-${ARCH}.AppImage"

echo "AppImage created: BookDB-v${VERSION}-${ARCH}.AppImage"
