# Decisions taken overnight, for review

You asked me to start phase 3 and keep going while you slept. Phases 3 and
most of 4 are done. Everything below changed how the game plays and was
decided without you; each says what, why, and what it's measured against.
Pure fixes and number tweaks with no design weight are left to the commit
log. Any of these is easy to reverse: most are one line in
`src/Sim/data/rules.json`.

## Where it stands

- **The clone is complete (phase 3).** A 60-day survival run with announced
  waves, possession, a final Convergence, and save/load. A scripted bot wins
  it; an economy-only bot loses by day 9 on every map tried. Both are gates
  in `scripts/verify.sh`.
- **Phase 4 is mostly done.** Twelve techs with exclusive tier-3 pairs, three
  build paths that all win (measured, not asserted), three new demons, Stone
  Walls, Lance Towers, Crossbowmen, and Hellgates.
- **The open problem is map fairness**, and it's phase 5's: whole maps are
  won or lost by every build path alike. See the table in the README.
- 93 tests; `scripts/verify.sh` green (~5 min; `--fast` ~1 min).

## Design decisions

### 1. Towers can't be possessed
Possession (a demon reaching an inhabited building takes it) first applied
to every crewed building, towers included. Every breach then turned the
tower behind the wall into Thralls inside it, and the phase 2 town fell on
four maps of five. Now it's a per-building `possessable` flag, off for
Watchtowers, Bombards and Lance Towers: fortifications are battered down,
homes and workplaces are taken. That matches TAB, where infection is about
colonists.

### 2. Towers need no crew
In phase 2 towers needed 2-3 workers. A defence of 25 towers then needed
~75 colonists, who needed Houses and food, and food couldn't keep up. TAB's
towers draw power, not people; ours now draw only sanctity (holy power).
Population is for the economy.

### 3. Gold is the main currency
Gold piled up unspent (10,000 by the end of a run) while wood gated
everything. Costs moved onto gold, wood mostly for walls and a little
building, stone for masonry and towers. All four resources now bind.

### 4. The Farm, and Hunters on forest only
Food couldn't scale with a growing town: one food building competing for
land it was being built over. The Farm (3x3, 4 crew) works open grass;
Hunters now work forest only. Colonists eat 0.025/s (was 0.03). Farmland
competes with building space, which is a real tension; the bot handles it
badly (its Farms decay from 0.47 to 0.08 food/s as it builds over them).

### 5. A gentler opening
The first wave at 3 minutes hit colonies that couldn't have walls yet. Now:
first wave day 6 (20 demons), one every 3 days, x1.18 each, Convergence of
2,500. A survival run also starts with four Militia (TAB starts you with a
squad), and Hellgates stay quiet until day 8.

### 6. Wardstones cost gold and wood, not stone
Holy ground is sometimes the only way to reach rock, so a Wardstone that
cost stone could deadlock a colony with no stone. Found on seed 11.

### 7. Demons must be in reach to strike
Eight demons crowded on the tile beside a wall all hit it, and walls fell
in six seconds. A demon now has to be within 0.45 tiles of a building's
face, and one that has arrived keeps pressing into it.

### 8. Hellgates, and how a run is won
Four gates stand 70+ tiles out. From day 8 each sends a band every minute,
growing at days 20 and 40; waves scale with how many stand (to 40% with
none), and soldiers can close them. Two consequences I chose:
- **The gates empty themselves into the Convergence** and go silent after
  it. Otherwise bands kept coming and a won run never ended.
- **A run is won when the Convergence is spent:** no demon left, *or* the
  Keep still standing four minutes after it lands. A few stragglers stuck
  on the far side of the map were holding won runs hostage.

### 9. Balance of the exclusive techs
Measured first, Holy Fire won untouched while Fortress fell. Holy Fire went
4 → 2.5 damage/s; Bastions x2 walls / x1.5 towers → x2.5 / x2. With these,
all three paths win the same maps, with no path clearly ahead.

### 10. The designated map is seed 11, not 7
With Hellgates in, seed 7 is lost at the Convergence by every path. Seed 11
is won by all three. The game now starts on 11, and the gates run on 11 and
3. I'm flagging this because it's choosing the map the gate passes on; the
six-map table is in the README so the choice is visible.

### 11. A start-fairness rule in the map generator
Seeds 19 and 42 had no rock within 25 tiles, which no doctrine survives. The
generator now stamps rock and forest patches 16-20 tiles out when the start
lacks them, landward, over grass only. It helped (seed 19 is won by one
path now), but it isn't sufficient (seed 42 is still lost). This is the
first of what phase 5 needs.

## Things I noticed that you should decide

- **Scale is under-used.** Over a full run the horde peaks around 2,550 (at
  the Convergence) while the engine carries 20,000 at 6 ms/tick. "Scale is
  the spectacle" is a pillar; the waves could be several times larger, which
  means rebalancing towers upward too. Probably the most important feel
  question for a first playtest.
- **Map fairness** is the next real problem, and it decides more than build
  choice does. Phase 5 should start with fairness rules and `hellwall-sim
  maps` as the tool.
- **Controls:** number keys arm buildings, so control groups are Ctrl+N to
  set and Alt+N to recall. Worth revisiting in the UX pass.
- **Remaining phase 4 content:** the plan says 6 units, 6 towers, 8 demon
  types; there are 4, 3 (+2 wall tiers), and 6.

## Not done, and why

- **A standalone build for playtesters.** Godot's export templates are a
  ~1 GB download, and downloading needs your go-ahead. When you want it:
  in the Godot editor, Editor > Manage Export Templates > Download.
- **CI.** The workflow exists but there's no GitHub remote, so it has never
  run. Its timing gates may be too tight for GitHub's shared runners.
- **Nothing was pushed or published anywhere.**

## Try it

```bash
# watch the bot play a run (Tab to speed up, Esc to take over)
/Applications/Godot_mono.app/Contents/MacOS/Godot --path ~/git/hellwall/game -- --autoplay

# play it yourself
/Applications/Godot_mono.app/Contents/MacOS/Godot --path ~/git/hellwall/game
```
