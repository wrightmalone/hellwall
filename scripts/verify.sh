#!/usr/bin/env bash
# Full check: build (warnings are errors), unit tests, cross-process
# determinism, and a Godot boot. Run from anywhere.
#
#   scripts/verify.sh            everything (~5 min: the full-run gates play hours of game)
#   scripts/verify.sh --fast     skip the full-run gates (survival run, build paths), ~1 min
#   scripts/verify.sh --no-godot skip the Godot steps (CI, or no Godot installed)
set -euo pipefail
cd "$(dirname "$0")/.."

GODOT="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
RUN_GODOT=1
FULL_RUNS=1
for arg in "$@"; do
  case "$arg" in
    --no-godot) RUN_GODOT=0 ;;
    --fast) FULL_RUNS=0 ;;
  esac
done

step() { printf '\n==> %s\n' "$*"; }

step "build"
dotnet build Hellwall.sln -c Release --nologo -v quiet

step "unit tests"
dotnet test tests/Sim.Tests -c Release --no-build --nologo -v quiet

# The in-process tests can't see per-process randomization (string hashing,
# HashCode); two separate processes can.
step "cross-process determinism"
run() { dotnet src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll --seed="$1" --ticks=2400 --script=src/Sim.Headless/scripts/smoke.json | grep '^hash='; }
a=$(run 7); b=$(run 7); c=$(run 8)
echo "seed 7: $a | seed 7 again: $b | seed 8: $c"
[[ "$a" == "$b" ]] || { echo "FAIL: same seed, different hash"; exit 1; }
[[ "$a" != "$c" ]] || { echo "FAIL: different seeds, same hash"; exit 1; }

# Phase 1 gate: 20k demons, walled Keep. Exits nonzero on any failed gate.
step "horde bench (20k demons, headless)"
dotnet src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll bench

# Phase 2 gate: a walled town holds a wave that an undefended one doesn't, on five seeds.
step "town probe (defended holds, undefended falls)"
dotnet src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll town

if [[ $FULL_RUNS == 1 ]]; then
  # Phase 3 gate: a bot wins a full 60-day run on the designated map; a passive one loses on five.
  step "survival run (bot wins, passive loses)"
  dotnet src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll run

  # Phase 4 gate: three research paths all win, with the full demon roster.
  step "build paths (fortress, pyre, legion all win)"
  dotnet src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll paths --seeds=7,3
fi

if [[ $RUN_GODOT == 1 ]]; then
  step "godot: build C# and boot the main scene"
  mkdir -p out
  "$GODOT" --headless --path game --build-solutions --quit >out/godot-build.log 2>&1 \
    || { cat out/godot-build.log; echo "FAIL: godot build"; exit 1; }
  "$GODOT" --headless --path game --quit-after 120 >out/godot-boot.log 2>&1 \
    || { cat out/godot-boot.log; echo "FAIL: godot boot"; exit 1; }
  if grep -Ei 'error|exception' out/godot-boot.log; then echo "FAIL: errors during boot"; exit 1; fi
  grep '^hellwall: world ready' out/godot-boot.log || { cat out/godot-boot.log; echo "FAIL: Main.cs never ran"; exit 1; }
fi

step "ok"
