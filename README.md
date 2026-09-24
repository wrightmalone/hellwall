# Hellwall (working title)

A colony-survival RTS: a walled human settlement against demon hordes, where
one breach can cascade into losing everything. The plan, pillars and roadmap
are in [PLAN.md](./PLAN.md).

**Status: phase 2 (colony core) complete.** A walled town holds a wave that
an undefended one doesn't, on five seeds; 20,000 demons still run at 20 Hz in
~6 ms/tick with combat on.

## Layout

```
src/Sim/            pure C# simulation: no Godot reference, fixed 20 Hz, seeded
src/Sim/data/       rules.json: every building, unit and demon number
src/Sim.Headless/   console harness: replay a script, CSV samples, final hash
tests/Sim.Tests/    xUnit: RNG, placement, horde, colony, combat, units, determinism, portability
game/               Godot 4.7 .NET project: renders sim state, input -> commands
scripts/verify.sh   the full check
```

The sim is the portable half. It can't reference Godot because its project
file doesn't, and `src/Sim/BannedSymbols.txt` makes ambient nondeterminism
(clocks, `System.Random`, randomized hashing, threads, IO) a compile error.

## Requirements

- .NET SDK 10
- Godot 4.7 .NET edition (`brew install --cask godot-mono`)

## Commands

Full check (build, tests, cross-process determinism, Godot boot):

```bash
scripts/verify.sh
```

Tests only:

```bash
dotnet test tests/Sim.Tests
```

The phase 1 performance gate, headless (also run by verify.sh):

```bash
dotnet run -c Release --project src/Sim.Headless -- bench
```

Add `--trace` for a timeline, `--histogram` for where the horde ended up, and
`--snapshot=out/x.ppm` for a density image (`sips -s format png` converts it).

The phase 2 gate, headless (also run by verify.sh):

```bash
dotnet run -c Release --project src/Sim.Headless -- town
```

Add `--trace` to see the defended town's towers, crews and Keep every 5 s.

The in-engine gate (opens a window):

```bash
scripts/render-bench.sh
```

Headless harness:

```bash
dotnet run --project src/Sim.Headless -- --seed=7 --ticks=2400 --script=src/Sim.Headless/scripts/smoke.json --out=out/smoke.csv
```

Run the game:

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path game
```

Or open `game/project.godot` in the Godot editor and press F5. To watch the
probe's town hold a wave at 4x speed:

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path game -- --demo
```

## The colony

Every content number lives in [src/Sim/data/rules.json](src/Sim/data/rules.json).

- **Buildings** cost gold, wood and stone, and take time to build.
- **Colonists** live in Houses (and the Keep), pay a tithe, eat, and crew
  buildings first come first served. A building short of its crew is idle.
- **Gatherers** (Woodcutter, Quarry, Hunter) collect from matching tiles
  around them; overlapping gatherers split the tiles.
- **Holy ground is the power grid.** The Keep, Shrines and Wardstones
  consecrate a radius; everything must be built on connected ground, and
  losing a Wardstone darkens what lies beyond it. Crewed Shrines supply
  sanctity; a shortfall slows everything that draws it.
- **Demons** head for every building except walls and gates, which they path
  through at 30x cost: they walk round a short wall and break a long one.
- **Towers** (Watchtower, Bombard) and **soldiers** (Militia, Marksman,
  Templar) shoot the nearest demon. Shots are loud. Demons within four tiles
  of a soldier go for it.
- **Losing the Keep loses the game.**

Measured by `town`: on seeds 3, 7, 11, 19 and 42, a ring of walls with four
Watchtowers, two Bombards and a Barracks' garrison holds a 200-demon wave
from two sides, and the Keep with two Houses falls to the same wave in about
85 s. The breaking point is 250 to 400 depending on the map.

## The horde

Demons path with one shared **flow field**: Dijkstra from every Keep and
House tile, so 20k demons cost the same pathing as one. Walls are obstacles,
not targets; breaching comes with combat in phase 2. Crowds are held together
by pairwise **separation**, **crowd pressure** that drifts demons toward
emptier tiles, and a hard **density cap** of 8 per tile. Map demons start as
**dormant packs**, one record each whatever their size, until **noise** wakes
them. Building is loud.

Measured by `bench` (seed 7, 256 map, 20k demons, 150 s):

| Gate | Result | Budget |
|---|---|---|
| Tick time p95 | 6.2 ms (mean 5.4) | ≤ 12 ms |
| Flow field full rebuild | 2.9 ms | ≤ 5 ms |
| Densest tile | 9 | ≤ 12 |
| Stuck with room to move | 0.27% | ≤ 1% |
| Demons inside walls or rock | 0 | 0 |
| Allocations per tick | 0 (0 GCs) | — |

In-engine (`render-bench.sh`, M1 Pro, 120 Hz display): 119.6 fps average, frame
p99 9.1 ms, sim 4.8 ms/tick.

## Controls

| Input | Action |
|---|---|
| `1`–`9`, `0`, `-` | arm a building (or click the build bar); click to place |
| drag with Wall or Gate armed | lay a straight line |
| right-click or Esc | disarm, then deselect |
| click | select a soldier or building |
| drag | box-select soldiers (shift adds) |
| right-click with soldiers | attack-move; shift+right-click for a plain move |
| `H` / `Shift+S` | hold / stop |
| `Ctrl+1`–`9` / `Alt+1`–`9` | set / recall a control group |
| `Q` `E` `R` with a Barracks selected | train Militia / Marksman / Templar |
| `X` or Delete | demolish the selected building (half refund once built) |
| space | pause; building and orders still work while paused |
| WASD, arrows / wheel | pan / zoom |
| `N` / `K` / `J` | debug: noise at the cursor / a 200-demon wave / 20k assault |
