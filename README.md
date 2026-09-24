# Hellwall (working title)

A colony-survival RTS: a walled human settlement against demon hordes, where
one breach can cascade into losing everything. The plan, pillars and roadmap
are in [PLAN.md](./PLAN.md).

**Status: phase 0 (skeleton).**

## Layout

```
src/Sim/            pure C# simulation: no Godot reference, fixed 20 Hz, seeded
src/Sim.Headless/   console harness: replay a script, CSV samples, final hash
tests/Sim.Tests/    xUnit: RNG golden values, placement, determinism, portability
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

Headless harness:

```bash
dotnet run --project src/Sim.Headless -- --seed=7 --ticks=2400 --script=src/Sim.Headless/scripts/smoke.json --out=out/smoke.csv
```

Run the game:

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path game
```

Or open `game/project.godot` in the Godot editor and press F5.

## Controls (phase 0)

| Input | Action |
|---|---|
| `1` / `2` | arm House / Wall |
| left click | place (green ghost = valid) |
| right click | demolish (not the Keep) |
| space | pause / resume; placement still works while paused |
| WASD, arrows | pan |
| wheel | zoom |
