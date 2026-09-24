#!/usr/bin/env bash
# Standalone builds for playtesters: macOS (universal) and Windows (x86_64),
# zipped with a short note, in out/build/. Needs Godot's export templates
# (Editor > Manage Export Templates, or see README).
#
#   scripts/export.sh            both platforms
#   scripts/export.sh macos      one
set -euo pipefail
cd "$(dirname "$0")/.."

GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
VERSION=$(sed -n 's/^application\/short_version="\(.*\)"/\1/p' game/export_presets.cfg | head -1)
TARGETS=("${@:-macos windows}")
[[ $# -eq 0 ]] && TARGETS=(macos windows)
mkdir -p out/build

note() {
  cat <<NOTE
Hellwall $VERSION - playtest build

A colony-survival RTS: hold a walled town against demon hordes. The first
screen picks the mode (60-day survival or endless), difficulty, map and seed.
F1 shows the controls. F5 saves, F9 loads.

$1

Please send back: how far you got, what killed you, and anything that
confused you. If it crashes, send the newest file in its log folder:
  macOS:   ~/Library/Application Support/Godot/app_userdata/Hellwall/logs/
  Windows: %APPDATA%\\Godot\\app_userdata\\Hellwall\\logs\\
Thank you for playing.
NOTE
}

for target in "${TARGETS[@]}"; do
  case "$target" in
    macos)
      rm -rf out/build/macos && mkdir -p out/build/macos
      "$GODOT" --headless --path game --export-release "macOS" ../out/build/macos/Hellwall.zip >out/export-macos.log 2>&1 \
        || { cat out/export-macos.log; echo "FAIL: macOS export"; exit 1; }
      if grep -q ERROR out/export-macos.log; then grep -A1 ERROR out/export-macos.log; echo "FAIL: macOS export had errors"; exit 1; fi
      (cd out/build/macos && unzip -q Hellwall.zip && rm Hellwall.zip)
      # Signed here, ad hoc, rather than by Godot: the app Godot 4.7.2 signs is
      # killed at launch (exit 137) on this machine, while the same app signed
      # with codesign runs.
      codesign -s - -f --deep -o runtime --entitlements game/entitlements.plist out/build/macos/Hellwall.app
      codesign --verify --deep --strict out/build/macos/Hellwall.app
      out/build/macos/Hellwall.app/Contents/MacOS/Hellwall --headless --quit-after 120 2>&1 | grep '^hellwall: world ready' \
        || { echo "FAIL: the exported app doesn't boot"; exit 1; }
      note "macOS: the app isn't notarized, so the first time, right-click Hellwall.app
and choose Open (or run: xattr -dr com.apple.quarantine Hellwall.app)." > out/build/macos/READ-ME.txt
      rm -f "out/build/Hellwall-$VERSION-macos.zip"
      (cd out/build/macos && ditto -c -k --keepParent Hellwall.app "../Hellwall-$VERSION-macos.zip" && zip -qj "../Hellwall-$VERSION-macos.zip" READ-ME.txt)
      echo "built out/build/Hellwall-$VERSION-macos.zip"
      ;;
    windows)
      rm -rf out/build/windows && mkdir -p out/build/windows
      "$GODOT" --headless --path game --export-release "Windows" ../out/build/windows/Hellwall.exe >out/export-windows.log 2>&1 \
        || { cat out/export-windows.log; echo "FAIL: Windows export"; exit 1; }
      if grep -q ERROR out/export-windows.log; then grep -A1 ERROR out/export-windows.log; echo "FAIL: Windows export had errors"; exit 1; fi
      [[ -f out/build/windows/Hellwall.exe ]] || { echo "FAIL: no Hellwall.exe"; exit 1; }
      note "Windows: the exe isn't signed, so SmartScreen may warn: More info, then Run anyway.
Keep Hellwall.exe next to its data_Hellwall folder." > out/build/windows/READ-ME.txt
      rm -f "out/build/Hellwall-$VERSION-windows.zip"
      (cd out/build/windows && zip -qr "../Hellwall-$VERSION-windows.zip" .)
      echo "built out/build/Hellwall-$VERSION-windows.zip (not run: this is a Mac)"
      ;;
    *) echo "unknown target $target (macos, windows)"; exit 2 ;;
  esac
done
ls -la out/build/*.zip
