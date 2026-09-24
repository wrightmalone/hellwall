# Working title: "Hellwall" — plan

A colony-survival RTS in the lineage of *They Are Billions*: build a walled
human settlement under huge demon hordes, where one breach can cascade into
losing everything. Clone the core loop first, then mutate it into its own game.

Decisions taken (2026-09-23):

| Question | Decision |
|---|---|
| Relation to Ashfield | New repo; reuse Ashfield's architecture pattern and port proven modules |
| Engine | Godot 4, C# |
| Want more of | Replayable maps / endless survival; deeper units, enemies and tech |
| Ambition | Commercial (Steam) |
| Sacred pillars | Breach cascade; noise draws the horde; survival clock + final wave |
| Not sacred | Ironman single-save (default: normal saves, ironman as an option) |
| Setting | Demon invasion vs humans |
| Art | Asset packs through vertical slice, commission a signature look for release |
| Time | Part-time serious, ~15–25 h/week |
| Skipped | Hero missions / campaign |

---

## 1. Pillars

1. **One breach can end everything.** Anything killed by demons becomes
   demons. A leak in the wall turns into a chain reaction, not just a dent.
2. **Noise draws the horde.** Combat, construction and some buildings make
   noise. Dormant packs across the map wake and converge on it. The player's
   own success is loud.
3. **The clock is the enemy.** Survive N days. Waves are scheduled and
   telegraphed, and a final Convergence hits from every direction.
4. **Scale is the spectacle.** Thousands of demons on screen is the product.
   Performance is a design pillar, not an optimization pass.
5. **Legibility over fidelity** (carried from Ashfield). The player must be
   able to see why they lost.

## 2. What makes it its own game (the mutation)

This comes after the clone is fun, but the architecture has to allow for it now.

- **Hellgates are a living enemy network.** Ashfield's conduit tree carries
  over: gates mature, spawn sub-gates, and feed waves. Wave strength scales
  with surviving gates (Ashfield DECISIONS §1). In TAB the map's zombies are
  a finite pool you clear; here, going on offense to close gates is a real
  strategic axis instead of cleanup.
- **Possession, not infection.** Dead humans rise as thralls, and breached
  buildings become *breaches* that spawn imps until they are purged. Same
  cascade as TAB, new fiction, plus a purge verb.
- **Consecrated ground as the power grid.** Shrines and wardstones project
  holy ground. You build only on it, and it slows or burns lesser demons.
  This replaces TAB's tesla energy with something that also works in combat.
  It is a direct port of Ashfield's network flood-fill and brownout logic.
- **Endless survival with escalating corruptions.** Every few days the horde
  gains a mutation drawn from a pool (armored hides, fliers, burrowers,
  spawn-on-death), telegraphed in advance. Procedural maps make every run
  different.
- *Optional, decide after the prototype:* fold in Ashfield's creeping blight
  as hellscape that spreads from gates and degrades terrain.

## 3. Architecture

```
hellwall/
  src/Sim/            pure C# class library (net10.0), NO Godot reference
                      fixed 20 Hz tick, seeded RNG, command-in / event-out
  src/Sim.Headless/   console runner: scripts -> CSV, like Ashfield's harness
  tests/Sim.Tests/    xUnit: probes, determinism, invariants
  game/               Godot 4 .NET project; renders state, turns input into commands
  tools/              map gen previews, balance reports
```

- **Portability boundary enforced by the build.** `Sim.csproj` never
  references GodotSharp, so any Godot type inside the sim fails to compile.
  This is the C# version of Ashfield's `check-portability.mjs`.
- **Deterministic single-player.** Same seed + same command stream = same
  result on the same build. That makes replays, bug repro, headless tuning
  and determinism tests possible. No fixed-point math, since there is no
  multiplayer, but the command-based input keeps lockstep co-op possible
  later.
- **Every tunable in data.** Balance, units, buildings and waves live in
  JSON/Resource files, not code. Endless mode and mutations need this, and it
  leaves modding open.
- **Horde simulation (the hard part):**
  - Struct-of-arrays unit storage (positions, velocity, hp, type, state), no
    per-unit objects.
  - Flow fields for pathing: multi-source Dijkstra from colony targets, with
    incremental recompute when walls change. No per-unit A*.
  - Uniform spatial hash for separation, target acquisition and splash.
  - **Dormant packs are aggregates.** A sleeping pack of 300 is one record
    until noise wakes it. That is how the map holds "billions" cheaply.
  - A noise grid that decays each tick; packs wake above a threshold.
  - Rendering: `MultiMeshInstance2D` per unit type, with instance data written
    straight from the sim arrays. Interpolate between ticks.
  - **Perf gate:** 20k active demons at 20 Hz under 12 ms of sim time on the
    dev Mac, and 60 fps render.

### Ported from Ashfield (TS → C#)

| Ashfield | New role |
|---|---|
| `rng.ts` (mulberry32, injected) | RNG |
| `commands.ts` / `events.ts` queues | Command/event boundary |
| `data/balance.ts` single-source tunables | Data files |
| `network.ts` flood-fill + `economy.ts` brownout | Consecrated-ground grid |
| `units.ts` flow-field navigation | Starting point for the horde flow fields |
| `nests.ts` conduit tree + maturation | Hellgates |
| `waves.ts` schedule + telegraph | Waves and Convergence |
| headless harness, neglect scripts, probes, determinism check | Same, in xUnit + console |

Ideas carry over and code gets rewritten. The value is ~100 commits of tuning
lessons (FINDINGS.md), not the TypeScript itself.

## 4. Clone-phase feature set ("clone-complete")

Own names from day one. Mechanics can't be copyrighted, but names, art, UI
trade dress and exact numbers can be infringing, so none are taken from TAB.

- **Colony:** Keep (the Command Center analog, whose loss = defeat), housing →
  population → gold tithe, workers auto-assigned.
- **Resources:** food, wood, stone, iron, gold, plus holy power (the grid).
  Producers need adjacency to their resource tiles, as in TAB.
- **Building:** grid placement on consecrated ground, walls (3 tiers), gates,
  towers (2 in clone phase), pause-and-build.
- **Army:** 3 human units (militia ranged, crossbow sniper, heavy), box
  select, move / attack-move / hold, control groups.
- **Demons:** imp (swarm), hound (fast), gargoyle (flier, ignores walls),
  bloater (bursts, damages walls), brute (siege). Thralls rise from dead
  humans; breached buildings spawn imps.
- **Survival:** day counter, telegraphed waves from named directions, final
  Convergence, win screen.
- **Map:** one handmade map with forest, rock, water, chokepoints and dormant
  packs.
- **Saves:** normal save/load, with ironman as an option.

## 5. Roadmap

Paced for ~20 h/week, with Claude writing most code and you directing and
playtesting. Every phase ends with a standalone desktop build (Godot C#
can't export to the web), so playtesters get a file.

| Phase | Goal | Exit criterion | Est. |
|---|---|---|---|
| **0. Skeleton** | Repo, Sim lib, headless runner, Godot shell, CI | `dotnet test` green; Godot renders an empty map from Sim state; determinism test passes | 1–2 wk |
| **1. Horde spike** *(riskiest first)* | Flow fields, spatial hash, dormant packs, noise wake, MultiMesh render | The perf gate above; 20k demons stream around walls into a target | 3–4 wk |
| **2. Colony core** | Placement, resources, housing, holy grid, walls/gates, 2 towers, 3 units, control | Build a walled town and hold a scripted wave | 5–6 wk |
| **3. Survival loop** | Possession cascade, waves, day clock, Convergence, one map, save/load | **Clone-complete:** a full 60–90 min run is winnable and losable. Playtest round 1 | 3–4 wk |
| **4. Depth** | Tech tree (3 workshops), 6 units, 6 towers, 8 demon types, Hellgates as network | Harness shows ≥3 viable build paths; no dominant strategy | 5–6 wk |
| **5. The mutation** | Procedural maps, endless mode, horde corruptions, difficulty settings | Endless runs vary meaningfully by seed; playtest round 2 | 5–6 wk |
| **6. Vertical slice** | Asset-pack art, audio, UI/UX pass, onboarding, Steam page | Steam page live; demo build ready for Next Fest | 6–8 wk |
| **7. Early Access** | Commissioned art, content, polish, localization basics | EA launch | TBD after 6 |

About 7–9 months to a public demo. The dates are guesses; the exit
criteria are the real gates.

## 6. Risks

1. **Horde perf in C#.** Mitigated by making phase 1 the spike. Fallbacks:
   run the sim on a worker thread, use more aggregation, drop the tick rate
   for distant units.
2. **Pathing at scale around player-built walls.** Flow fields plus
   incremental recompute. Budget the work per tick and never recompute the
   whole map at once.
3. **The cascade isn't fun, just punishing.** Tune with the headless harness
   and "neglect" scripts, as Ashfield did (economy / defense / offense / tech).
4. **Scope.** Endless mode and depth can grow forever. Phase exit criteria
   are hard gates.
5. **Asset-pack look reads as generic.** Fine through the slice. Budget
   commissioned art before EA.
6. **Trade dress too close to TAB.** Own names, own UI layout, and the
   demon fiction from day one.

## 7. Open decisions (defaults in bold)

- Camera: **top-down 2D** (cheaper assets, cleaner reads) vs isometric like TAB.
  Asset-pack availability decides it.
- Meta-progression between endless runs: **none until phase 5 proves the
  run loop**, then decide.
- Map size for the clone map: **256×256 tiles**.
- Working title: **"Hellwall" (placeholder)**.
- Blight/hellscape terrain spread: **decide after clone-complete**.
