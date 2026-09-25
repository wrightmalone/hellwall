#!/usr/bin/env bash
# A quick A/B while iterating: a QUICK sweep (4 seeds x 3 paths) of each map
# as the rules stand and again with some probe flags, wins side by side.
# Noisy by design (12 games a side): it tells you which way a change moves
# things, not the final number. Settle it with a full sweep before committing.
#
#   scripts/ab.sh '--no-mining'                      on plains
#   scripts/ab.sh '--wave-scale=1.3' plains lakes    on each map named
#   FULL=1 scripts/ab.sh '--no-woods' wildwood       8 seeds a side instead of 4
set -euo pipefail
cd "$(dirname "$0")/.."
FLAGS=${1:?usage: scripts/ab.sh '<probe flags>' [map ...]}
shift
MAPS=("${@:-plains}")
[[ $# -eq 0 ]] && MAPS=(plains)
QUICK=$([[ "${FULL:-0}" == 1 ]] && echo 0 || echo 1)
wins() { awk '/ won / { split($3, f, "/"); w += f[1]; n += f[2]; per = per sprintf(" %s %s", substr($1, 1, 1), $3) } END { printf "%2d/%-2d (%s )", w, n, per }' "$1"; }
printf '%-10s %-28s %-28s\n' map "as is" "$FLAGS"
for map in "${MAPS[@]}"; do
  QUICK=$QUICK MAP=$map OUT=out/ab/$map-base scripts/sweep.sh >/dev/null
  QUICK=$QUICK MAP=$map OUT=out/ab/$map-with EXTRA="$FLAGS" scripts/sweep.sh >/dev/null
  printf '%-10s %-28s %-28s\n' "$map" "$(wins out/ab/$map-base/summary.txt)" "$(wins out/ab/$map-with/summary.txt)"
done
