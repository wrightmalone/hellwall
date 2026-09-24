# Hellwall (working title)

A colony-survival RTS: a walled human settlement against demon hordes, where
one breach can cascade into losing everything. The plan, pillars and roadmap
are in [PLAN.md](./PLAN.md).

**Status: phase 3 (survival loop) complete: the clone is complete.** A
60-day survival run with announced waves, possession, and a final
Convergence is winnable (a scripted bot wins it on the designated map and a
second one) and losable (an economy-only bot falls by day 9 on every map
tried). 20,000 demons still run at 20 Hz in ~6 ms/tick.

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

The phase 3 gate, headless (also run by verify.sh, about a minute):

```bash
dotnet run -c Release --project src/Sim.Headless -- run
```

`--trace` prints the bot's decisions and the colony every five days;
`--snapshot-at=<seconds>` writes a picture of the map; `--seeds=11,19`
plays the bot on other maps without gating.

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
bot play a whole run (Tab to speed up, Esc to take over):

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path game -- --autoplay
```

To watch the probe's town hold a wave at 4x speed:

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path game -- --demo
```

## A survival run

60 one-minute days. From day 6 a wave lands every three days, each larger
than the last and from more sides as the run goes on; each is announced a
minute ahead with its size and direction, pinned to the edge of the screen.
At the end of day 60 the Convergence (2,500) comes from every side at once.
Survive it and the run is won; lose the Keep and it's over. Forty dormant
packs (about 10,000 demons) sleep across the map until noise wakes them.

**Possession is the cascade.** A demon reaching an inhabited building
(a House, or a workplace with its crew in) takes it: its people come out as
Thralls one a second, and it falls when it's empty. Losing them can leave
other buildings short of crew. Demolishing a possessed building purges it.
Soldiers killed by demons rise as Thralls. Towers and walls are only ever
battered down.

F5 / F9 quicksave and quickload. Saves are full snapshots, checked by a test
that plays a loaded world against the original tick for tick.

## The colony

Every content number lives in [src/Sim/data/rules.json](src/Sim/data/rules.json).

- **Buildings** cost gold, wood and stone, and take time to build.
- **Colonists** live in Houses (and the Keep), pay a tithe, eat, and crew
  buildings first come first served. A building short of its crew is idle.
- **Gatherers** collect from matching tiles around them: Woodcutter from
  forest, Quarry from rock, Hunter from forest, Farm from open grass.
  Overlapping gatherers split the tiles, and tiles under buildings yield
  nothing, so farmland competes with building space.
- **Holy ground is the power grid.** The Keep, Shrines and Wardstones
  consecrate a radius; everything must be built on connected ground, and
  losing a Wardstone darkens what lies beyond it. Crewed Shrines supply
  sanctity; a shortfall slows everything that draws it.
- **Demons** head for every building except walls and gates, which they path
  through at 30x cost: they walk round a short wall and break a long one.
- **Towers** (Watchtower, Bombard) draw sanctity but no crew; **soldiers**
  (Militia, Marksman, Templar) train at a Barracks. Both shoot the nearest demon. Shots are loud. Demons within four tiles
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
| `1`–`9`, `0`, `-`, `=` | arm a building (or click the build bar); click to place |
| drag with Wall or Gate armed | lay a straight line |
| right-click or Esc | disarm, then deselect |
| click | select a soldier or building |
| drag | box-select soldiers (shift adds) |
| right-click with soldiers | attack-move; shift+right-click for a plain move |
| `H` / `Shift+S` | hold / stop |
| `Ctrl+1`–`9` / `Alt+1`–`9` | set / recall a control group |
| `Q` `E` `R` with a Barracks selected | train Militia / Marksman / Templar |
| `X` or Delete | demolish the selected building (half refund once built) |
| space / Tab | pause (building and orders still work) / cycle 1x, 2x, 4x |
| F5 / F9 | quicksave / quickload |
| WASD, arrows / wheel | pan / zoom |
| `N` / `K` / `J` | debug: noise at the cursor / a 200-demon wave / 20k assault |

## Bot results

`hellwall-sim run` plays the bot on the designated map (seed 7) and seed 3 and
requires wins; other maps are informational. At the time of writing:

| Seed | Full bot | Passive bot |
|---|---|---|
| 7 | won, day 63, 4,663 demons killed | lost day 9 |
| 3 | won, day 62, 4,594 killed | lost day 8 |
| 11 | lost at the Convergence, day 62 | lost day 8 |
| 19 | lost day 50 | lost day 8 |
| 42 | lost day 49 | lost day 8 |

The bot is a lower bound, not a target: it packs towers into blocks, builds
over its own farmland, and doesn't plan chokepoints.
