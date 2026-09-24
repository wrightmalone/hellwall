#!/usr/bin/env bash
# The in-engine half of the phase 1 gate: opens a window, runs the 20k
# assault for 25s, prints frame and sim timings, saves a screenshot.
# Not part of verify.sh because it needs a display.
set -euo pipefail
cd "$(dirname "$0")/.."
GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
mkdir -p out
"$GODOT" --path game --build-solutions --quit >/dev/null 2>&1
"$GODOT" --path game -- --bench=25 --screenshot="$PWD/out/render-bench.png" 2>&1 | grep '^hellwall-bench:'
echo "screenshot: out/render-bench.png"
