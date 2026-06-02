#!/usr/bin/env bash
# scripts/make-homebrew-cask.sh <version> <arm64-dmg-path> <x64-dmg-path>
# Generates ready-to-commit Casks/bookdb.rb content for the cadwal/homebrew-bookdb tap.
# Pipe output: bash scripts/make-homebrew-cask.sh X.Y.Z BookDB-v...arm64.dmg BookDB-v...x64.dmg > Casks/bookdb.rb
# Must be run on macOS (shasum is a macOS built-in).
set -euo pipefail

VERSION="${1:?Usage: make-homebrew-cask.sh <version> <arm64-dmg-path> <x64-dmg-path>}"
ARM64_DMG="${2:?Usage: make-homebrew-cask.sh <version> <arm64-dmg-path> <x64-dmg-path>}"
X64_DMG="${3:?Usage: make-homebrew-cask.sh <version> <arm64-dmg-path> <x64-dmg-path>}"

# Verify DMG files exist
if [[ ! -f "${ARM64_DMG}" ]]; then
    echo "Error: arm64 DMG not found: ${ARM64_DMG}" >&2
    exit 1
fi
if [[ ! -f "${X64_DMG}" ]]; then
    echo "Error: x64 DMG not found: ${X64_DMG}" >&2
    exit 1
fi

# Compute SHA256 (extract hash only, strip filename from shasum output)
SHA256_ARM64=$(shasum -a 256 "${ARM64_DMG}" | awk '{print $1}')
SHA256_X64=$(shasum -a 256 "${X64_DMG}" | awk '{print $1}')

# Print ready-to-commit cask content
# Note: #{version} is Ruby interpolation (Homebrew) — bash does not expand #{}
cat << CASK
cask "bookdb" do
  arch arm: "arm64", intel: "x64"

  version "${VERSION}"

  on_arm do
    sha256 "${SHA256_ARM64}"
    url "https://github.com/cadwal/BookDB/releases/download/v#{version}/BookDB-v#{version}-osx-arm64.dmg"
  end

  on_intel do
    sha256 "${SHA256_X64}"
    url "https://github.com/cadwal/BookDB/releases/download/v#{version}/BookDB-v#{version}-osx-x64.dmg"
  end

  name "BookDB"
  desc "Personal book catalog"
  homepage "https://github.com/cadwal/BookDB"

  app "BookDB.app"
end
CASK
