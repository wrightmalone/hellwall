#!/usr/bin/env bash
# Balance sweep: every build path on many maps, in parallel, then a summary
# of win rate and average day reached per path. Slow (several minutes) but
# far less noisy than a gate on two maps.
#
#   scripts/sweep.sh                  seeds 3 5 7 11 13 19 23 42
#   scripts/sweep.sh 3 5 7            chosen seeds
set -euo pipefail
cd "$(dirname "$0")/.."
SEEDS=("${@:-3 5 7 11 13 19 23 42}")
[[ $# -eq 0 ]] && SEEDS=(3 5 7 11 13 19 23 42)
BIN=src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll
dotnet build src/Sim.Headless -c Release --nologo -v q >/dev/null
mkdir -p out/sweep
rm -f out/sweep/*.txt
printf '%s\n' "${SEEDS[@]}" | xargs -P 6 -I{} sh -c "dotnet $BIN paths --seeds={} > out/sweep/{}.txt 2>&1 || true"
cat out/sweep/*.txt | grep '^seed' | awk '
  { path=$4; won=($5=="Won"); day=$7; n[path]++; w[path]+=won; d[path]+=day; row[$2]=row[$2] sprintf("  %-8s %-5s d%-2s", path, $5, day) }
  END {
    for (s in row) print "seed " s ":" row[s] | "sort -n -k2";
    close("sort -n -k2");
    for (p in n) printf "%-9s won %d/%d   average day %.1f\n", p, w[p], n[p], d[p]/n[p];
  }'
