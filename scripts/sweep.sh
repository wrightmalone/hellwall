#!/usr/bin/env bash
# Balance sweep: every build path on many maps, in parallel, then a summary
# of win rate and average day reached per path. Slow (several minutes) but
# far less noisy than a gate on two maps.
#
#   scripts/sweep.sh                  seeds 3 5 7 11 13 19 23 42
#   scripts/sweep.sh 3 5 7            chosen seeds
#   DIFFICULTY=hard scripts/sweep.sh  at another difficulty (default normal)
#   MAP=lakes scripts/sweep.sh        on another kind of map (default plains)
#   WOODS=1 scripts/sweep.sh          with blocking woods and woodsmen (woods.blocks)
#   GATE=1 scripts/sweep.sh           and fail unless the balance gate holds (see below)
set -euo pipefail
cd "$(dirname "$0")/.."
SEEDS=("${@:-3 5 7 11 13 19 23 42}")
[[ $# -eq 0 ]] && SEEDS=(3 5 7 11 13 19 23 42)
BIN=src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll
dotnet build src/Sim.Headless -c Release --nologo -v q >/dev/null
mkdir -p out/sweep
rm -f out/sweep/*.txt
printf '%s\n' "${SEEDS[@]}" | xargs -P 6 -I{} sh -c "dotnet $BIN paths --seeds={} --difficulty=${DIFFICULTY:-normal} --map=${MAP:-plains} ${WOODS:+--woods} > out/sweep/{}.txt 2>&1 || true"
cat out/sweep/*.txt | grep '^seed' | awk '
  { path=$4; won=($5=="Won"); day=$7; n[path]++; w[path]+=won; d[path]+=day; row[$2]=row[$2] sprintf("  %-8s %-5s d%-2s", path, $5, day) }
  END {
    for (s in row) print "seed " s ":" row[s] | "sort -n -k2";
    close("sort -n -k2");
    for (p in n) printf "%-9s won %d/%d   average day %.1f\n", p, w[p], n[p], d[p]/n[p];
  }' | tee out/sweep/summary.txt

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
    }' out/sweep/summary.txt
fi
