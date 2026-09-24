#!/usr/bin/env bash
# The campaign's difficulty curve: every mission, played by every research
# plan, in parallel. Each line is mission, plan, outcome, day reached, goals.
set -euo pipefail
cd "$(dirname "$0")/.."
BIN=src/Sim.Headless/bin/Release/net10.0/hellwall-sim.dll
dotnet build src/Sim.Headless -c Release --nologo -v q >/dev/null
mkdir -p out/campaign
rm -f out/campaign/*.txt
MISSIONS=$(python3 -c "import json; print(' '.join(s['id'] for s in json.load(open('src/Sim/data/campaign.json'))['scenarios']))")
printf '%s\n' $MISSIONS | xargs -P 6 -I{} sh -c "dotnet $BIN campaign --missions={} > out/campaign/{}.txt 2>&1 || true"
for m in $MISSIONS; do cat "out/campaign/$m.txt"; done
