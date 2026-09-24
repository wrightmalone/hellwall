# Hellwall (working title)

A colony-survival RTS: a walled human settlement against demon hordes, where
one breach can cascade into losing everything. The plan, pillars and roadmap
are in [PLAN.md](./PLAN.md).

**Status: phase 4 (depth) mostly done.** The clone is complete (phase 3):
a 60-day survival run with announced waves, possession and a final
Convergence is winnable and losable. Phase 4 adds a tech tree with three
build paths, three new demons, Stone Walls, Lance Towers, Crossbowmen, and
Hellgates, and the roster the plan asks for: 6 units, 6 towers and 8 demon
types. Since then, the expansion loop: waves are about four times
larger, demons sleep in packs across the whole map and must be cleared
before you can build near them, and every soldier costs iron, which is mined
from deposits out in the wilds. The three paths are balanced against each other, but
whole maps are won or lost by every path alike, so map fairness is the open
problem (phase 5). 20,000 demons still run at 20 Hz in ~6 ms/tick.

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

60 one-minute days, on the designated map (seed 11) by default. From day 6 a wave lands every three days, each larger
than the last and from more sides as the run goes on; each is announced a
minute ahead with its size and direction, pinned to the edge of the screen.
The first wave is 30 demons, each wave after is x1.24, and the last regular
wave is about 1,330. At the end of day 60 the Convergence (9,000) comes from every side at once.
Survive it and the run is won; lose the Keep and it's over.

**The wilds.** 160 packs sleep across the map, none within 20 tiles of the
Keep. Near packs hold about a dozen demons and far packs about ninety.
You can't build within 10 tiles of a sleeping pack, so growing means going
out and clearing it. A pack wakes when a soldier comes within 7 tiles, or
when it hears noise. Building is quiet enough that it never wakes a pack; gunfire does. The
first iron deposit and the home rock are always left unguarded, so the
opening never depends on a fight.

**Hellgates** stand far out on the map (four of them). From day 8 each sends
a small band at the colony every minute, bigger from day 20 and again from
day 40, and every wave is scaled by how many still stand (down to 40% with
none). Soldiers attack a gate in reach when no demon is nearer; closing
gates is how offense pays. At the Convergence the gates empty themselves
into it and fall silent. The run is won when the Convergence is spent: no
demon left, or the Keep still standing four minutes after it lands.

**Demons:** Imps; fast Hounds; Thralls (colonists and soldiers the horde has
taken); Gargoyles, which fly over walls (from wave 5); Bloaters, which burst
against buildings and when killed (from wave 7); Howlers (from wave 5), which howl
once they're in sight of the colony and wake the sleeping packs near it;
Broodmothers (from wave 8), which burst into five Imps when killed; and siege
Brutes (from wave 10).

**Research** happens at a Scriptorium. Twelve techs in three tiers; tier 3
is two exclusive pairs (Bastions or Holy Fire, Standing Army or Artillery),
so paths part for good. Holy Fire makes consecrated ground burn demons.

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
- **Iron** is the army's resource. Every soldier costs iron (Militia 5, Marksman
  10, Crossbowman 15, Templar 20) and a little gold upkeep. A Mine (2x2, 3 crew)
  works ore tiles. There is one small deposit 22 to 28 tiles out and richer
  ones beyond 34 tiles, so a bigger army means holding more of the map.
- **Colonists** live in Houses (and the Keep), pay a tithe, eat, and crew
  buildings first come first served. A building short of its crew is idle.
- **Gatherers** collect from matching tiles around them: Woodcutter from
  forest, Quarry from rock, Hunter from forest, Farm from open grass, Mine
  from ore.
  Overlapping gatherers split the tiles, and tiles under buildings yield
  nothing, so farmland competes with building space.
- **Holy ground is the power grid.** The Keep, Shrines and Wardstones
  consecrate a radius; everything must be built on connected ground, and
  losing a Wardstone darkens what lies beyond it. Crewed Shrines supply
  sanctity; a shortfall slows everything that draws it.
- **Demons** head for every building except walls and gates, which they path
  through at 30x cost: they walk round a short wall and break a long one.
- **Towers** draw sanctity but no crew:
  - Watchtower, the all-rounder.
  - Bombard, with splash. It is loud.
  - Lance Tower, long range (needs Ballistics).
  - Censer, cheap and quiet, with a short range and rapid splash for the crowd at the wall.
  - Skyspire, which shoots only fliers.
  - Belfry, which has no weapon but slows every demon within 7 tiles to 55% speed (needs Masonry).
- **Soldiers** train at a Barracks:
  - Militia.
  - Marksman.
  - Templar.
  - Crossbowman (needs Drill).
  - Chaplain, which heals soldiers around it at 5 hp/s (needs Hallowing).
  - Outrider, fast mounted melee for riding out to clear the wilds (needs Husbandry).

  Towers and soldiers shoot the nearest demon. Shots are loud. Demons within four tiles
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
| `Q` `E` `R` with a Barracks selected | train Militia / Marksman / Templar (Crossbowmen by button) |
| `9`, `=`, `G`, `B`, `U` | Stone Wall, Lance Tower, Gate, Barracks, Scriptorium |
| `X` or Delete | demolish the selected building (half refund once built) |
| F1 | controls overlay |
| minimap (bottom left) | click or drag to move the camera |
| space / Tab | pause (building and orders still work) / cycle 1x, 2x, 4x |
| F5 / F9 | quicksave / quickload |
| WASD, arrows / wheel | pan / zoom |
| `N` / `K` / `J` | debug: noise at the cursor / a 200-demon wave / 20k assault |

## Bot results

`hellwall-sim run` plays the bot (fortress plan) on the designated map (seed
11) and seed 3 and requires wins, and the passive bot on five maps and
requires losses. `hellwall-sim paths` plays all three research plans. Both
are in verify.sh. `scripts/sweep.sh` plays 8 maps x 3 paths in parallel
(about 2 minutes). With the full phase 4 roster (6 units, 6 towers, 8 demons):

| Seed | Fortress | Pyre | Legion |
|---|---|---|---|
| 3 | won | won | won |
| 5 | won | won | lost day 62 |
| 7 | lost day 41 | lost day 23 | lost day 36 |
| 11 | won | won | won |
| 13 | lost day 62 | won | lost day 35 |
| 19 | won | lost day 62 | lost day 62 |
| 23 | lost day 63 | lost day 63 | lost day 51 |
| 42 | lost day 38 | lost day 38 | lost day 38 |

Fortress and pyre win 4 of 8 each, legion 2. Many losses come at the
Convergence (days 61-63), so these runs are close, and one change to the bot
moves a map either way. Seeds 7 and 42 collapse early whatever the path: on
7 the colony grows along a lake shore and never reaches its iron. The map
generator already guarantees rock and forest 16 to 20 tiles out on the landward
side, but making every generated map fair is phase 5's job.

`town --extra=<Kind>` measures what one more building on each attacked side
adds. At a 650-demon wave (5 maps):

| Extra | Maps held |
|---|---|
| none | 0 |
| Watchtower | 1 |
| Censer | 2 |
| Belfry | 3 |
| Bombard | 5 (holds 800 as well) |

The bot is a lower bound, not a target: it packs towers into blocks, builds
over its own farmland, doesn't plan chokepoints, and only the legion plan
raids gates. Short of stone for a Bombard, it builds a Watchtower, or a Censer when gold piles up. It adds a Skyspire and a Belfry per eight towers. It clears packs only with 8+ soldiers and 4+ towers, leaves
a home guard of 4, and takes on packs up to 1.5x its own army.
