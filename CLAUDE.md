# Working on Hellwall

Read PLAN.md first: it holds the pillars, the phase roadmap and each phase's
exit criterion. Work toward the current phase's exit criterion and nothing
beyond it.

## Rules that keep the architecture honest

- **Game rules live in `src/Sim`, never in `game/`.** If a behaviour can't be
  exercised by the headless harness, it's in the wrong place. `game/` renders
  state and turns input into `Command`s.
- **Input goes in through Commands; output comes out as SimEvents.** Nothing
  outside the sim mutates its state.
- **Determinism.** All randomness comes from `World.Rng` (or, for map
  generation, a pure hash of the seed). No clocks, threads, `System.Random`,
  `HashCode` or `string.GetHashCode`; `BannedSymbols.txt` enforces this. Don't
  let `Dictionary`/`HashSet` iteration order decide anything; iterate
  id-ordered lists instead.
- **New state goes into `StateHash`**, or the determinism checks silently stop
  covering it.
- **Every tunable goes in `Balance`** (data files from phase 2 on).
- **Horde scale is a pillar.** Unit storage is struct-of-arrays, pathing is
  flow fields, and there are no per-unit allocations in the tick.

## IP hygiene

This is headed for a commercial release. It follows *They Are Billions*'
mechanics but takes nothing else: no names, art, UI layout or exact numbers
from it.

## Checks

- `scripts/verify.sh` must be green before committing.
- Add a test or harness probe for every rule you add, the way Ashfield did:
  a claim in the README should have a measurement behind it.
