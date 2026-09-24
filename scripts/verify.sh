#!/usr/bin/env bash
# Full check: build (warnings are errors), unit tests, cross-process
# determinism, and a Godot boot. Run from anywhere.
#
#   scripts/verify.sh            everything (~6 min: the full-run gates play days of game)
#   scripts/verify.sh --fast     skip the full-run gates (survival run, sweep, endless), ~1 min
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
# The p95 is a tail, and a busy machine moves tails: one retry before failing.
step "horde bench (20k demons, headless)"
dotnet src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll bench \
  || { echo "(over budget: once more, in case the machine was busy)"; dotnet src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll bench; }

# Phase 2 gate: a walled town holds a wave that an undefended one doesn't, on five seeds.
step "town probe (defended holds, undefended falls)"
dotnet src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll town

if [[ $FULL_RUNS == 1 ]]; then
  # Phase 3 gate, losable half: an economy with no defense falls on five maps.
  step "survival run (passive loses)"
  dotnet src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll run --win-seeds=

  # Phases 3-5: runs are winnable, and no research path is dead or dominant, over 8 fair maps.
  step "balance sweep (8 maps x 3 paths)"
  GATE=1 scripts/sweep.sh

  # Phase 5 gate: endless runs vary by seed, in how long they last and in what the horde becomes.
  step "endless (runs vary by seed)"
  printf '%s\n' 3 11 42 | xargs -P 3 -I{} dotnet src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll endless --seeds={} --max-days=150 > out/endless.txt
  cat out/endless.txt
  [[ $(awk '{print $7}' out/endless.txt | sort -u | wc -l) -ge 2 ]] || { echo "FAIL: every endless run ended the same day"; exit 1; }
  [[ $(sed 's/.*\[//' out/endless.txt | sort -u | wc -l) -ge 3 ]] || { echo "FAIL: endless runs drew the same corruptions"; exit 1; }

  # The campaign's difficulty curve: the opening missions are won by every path, the last is not a walkover.
  step "campaign (the curve rises)"
  scripts/campaign.sh > out/campaign.txt
  grep -E '^mission (first-night|iron-hills|hellwall) ' out/campaign.txt
  [[ $(grep -cE '^mission (first-night|iron-hills) .* Won ' out/campaign.txt) -eq 6 ]] || { echo "FAIL: an opening mission was lost"; exit 1; }
  grep -qE '^mission hellwall .* Lost ' out/campaign.txt || { echo "FAIL: every path won the final mission: the curve is too flat"; exit 1; }
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
  # The attack-move controls, through Godot's own input.
  "$GODOT" --headless --path game -- --selftest=controls >out/godot-selftest.log 2>&1 || true
  grep -q 'hellwall-selftest: PASS controls' out/godot-selftest.log || { cat out/godot-selftest.log; echo "FAIL: controls self-test"; exit 1; }
  # The new-game menu, which a headless boot otherwise skips.
  "$GODOT" --headless --path game --quit-after 60 -- --menu >out/godot-menu.log 2>&1 \
    || { cat out/godot-menu.log; echo "FAIL: godot boot to the menu"; exit 1; }
  if grep -Ei 'error|exception' out/godot-menu.log; then echo "FAIL: errors on the new-game menu"; exit 1; fi
fi

step "ok"
