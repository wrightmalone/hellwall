#!/usr/bin/env bash
# Balance sweep: every build path on many maps, in parallel, then a summary
# of win rate and average day reached per path. Every game is its own
# process, as many at once as the machine has cores.
#
#   scripts/sweep.sh                  seeds 3 5 7 11 13 19 23 42 (~3 min a map)
#   QUICK=1 scripts/sweep.sh          seeds 3 7 11 42 only (~1-2 min): for which way a change
#                                     moves things while iterating; noisy (+-2-3 wins of 12)
#   scripts/sweep.sh 3 5 7            chosen seeds
#   DIFFICULTY=hard scripts/sweep.sh  at another difficulty (default normal)
#   MAP=lakes scripts/sweep.sh        on another kind of map (default plains)
#   WOODS=1 scripts/sweep.sh          force blocking woods (they block by default now; --no-woods in EXTRA for walkable)
#   EXTRA='--tree-hp=150' scripts/sweep.sh   any other probe flags
#   JOBS=4 scripts/sweep.sh           games at once (default: the machine's cores)
#   GATE=1 scripts/sweep.sh           and fail unless the balance gate holds (see below)
#   scripts/ab.sh '--no-mining' plains lakes   a quick A/B of some flags, side by side
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ $# -gt 0 ]]; then SEEDS=("$@")
elif [[ "${QUICK:-0}" == 1 ]]; then SEEDS=(3 7 11 42)
else SEEDS=(3 5 7 11 13 19 23 42); fi
JOBS=${JOBS:-$(sysctl -n hw.ncpu 2>/dev/null || nproc 2>/dev/null || echo 6)}
OUT=${OUT:-out/sweep}
BIN=src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll
dotnet build src/Sim.Headless -c Release --nologo -v q >/dev/null
mkdir -p "$OUT"
rm -f "$OUT"/*.txt
for s in "${SEEDS[@]}"; do for p in fortress pyre legion; do echo "$s $p"; done; done |
  xargs -P "$JOBS" -n 2 sh -c "dotnet $BIN paths --seeds=\$0 --plans=\$1 --difficulty=${DIFFICULTY:-normal} --map=${MAP:-plains} ${WOODS:+--woods} ${EXTRA:-} > $OUT/\$0-\$1.txt 2>&1 || true"
cat "$OUT"/*.txt | grep '^seed' | awk '
  { path=$4; won=($5=="Won"); day=$7; n[path]++; w[path]+=won; d[path]+=day; row[$2]=row[$2] sprintf("  %-8s %-5s d%-2s", path, $5, day) }
  END {
    for (s in row) print "seed " s ":" row[s] | "sort -n -k2";
    close("sort -n -k2");
    for (p in n) printf "%-9s won %d/%d   average day %.1f\n", p, w[p], n[p], d[p]/n[p];
  }' | tee "$OUT"/summary.txt
# The balance gate (phases 3-5): runs are winnable and losable, and no build
# path is dead or dominant. Gated on rates over many maps, not wins on named
# ones: a single run near the Convergence is a coin flip, and a gate on two
# seeds flipped whenever the maps or the bot changed.
if [[ "${GATE:-0}" == 1 ]]; then
  awk '/ won / {
      split($3, f, "/"); won=f[1]; day=$6; total+=won; runs+=f[2]
      if (won < 2) { print "FAIL: " $1 " won only " $3; bad=1 }
      if (day < 55) { print "FAIL: " $1 " averages day " day; bad=1 }
      if (min == "" || won < min) min=won; if (won > max) max=won
    }
    END {
      if (total * 24 < 9 * runs) { print "FAIL: only " total "/" runs " runs won"; bad=1 }
      if (max - min > 4) { print "FAIL: one path wins " max " where another wins " min; bad=1 }
      exit bad
    }' "$OUT"/summary.txt
fi
