# Gaps against They Are Billions: what to port, change, or skip

Status: draft for you to choose from. "TAB" is They Are Billions as I remember
it. Details are from memory, so check anything that decides a design before
leaning on it; the claims here are about kinds of systems, not exact numbers.

**Done since (2026-09-24, unattended; details in DECISIONS.md "The long session"):**
housing tiers (House, Cottage, Manor), fishing (the Fishery), mayors as patron saints, Keep
upgrades (Curtain Wall, Citadel), repair, the stone gate, fog of war, veterancy, patrol, ruins
(the "villages"), a ranged demon (the Spitter), custom survival settings with a score, the
weekly challenge, synthesized music, fuller tooltips, and a noise view. **Still open:** storage
caps, a market, a wider tower roster, an advanced production building, special units, death
animations, ironman. The tech tree is still waiting on your answers in `tech-tree.md`.

Each row gives my recommendation:
- **Port**: it's core to why TAB works.
- **Change**: we have the idea, but ours should differ.
- **Skip**: it doesn't fit our game.
- **Later**: worth it, but not before the playtest says so.

## Economy and colony

| TAB has | We have | Recommendation |
|---|---|---|
| **Housing tiers**: tents upgraded in place to cottages, then stone houses, for more colonists and gold per tile | one House | **Port.** Upgrade-in-place is a good land-versus-cost decision, and the obvious sink for late gold. House, then Stone House, then Manor, each costlier and denser. |
| **Storage caps** (Warehouses raise them) | unlimited stockpiles | **Change.** Caps make you spend and give raids a reason (lose a store, lose its goods). I'd add caps with a Storehouse, but loose ones: a cap nobody hits is noise. Worth a playtest before committing. |
| **Energy grid** (power plants and relay towers), with everything needing power | holy ground and sanctity (Shrines, Wardstones) | **Keep ours.** It's the same idea, plus it burns demons with Holy Fire. |
| **Workers separate from colonists** (every building takes workers from a pool) | colonists crew buildings; the Keep and Houses house them | **Keep ours.** It's the same model. |
| **Market or Bank** (gold boosts, trade) | none | **Later.** It's the Industry tier-3 tech in the tech-tree plan. |
| **Food from fishing** as well as hunting and farming | Farms and Hunters | **Port (small).** A Fishery on the shore: it makes water worth building next to, and Lakes maps worth playing. |
| **Oil** as a fifth resource | iron | **Keep iron.** It's our fifth resource, and ours makes you expand. |
| **Mayors**: at population milestones you pick one of a few bonuses (1.0 update, as I recall) | none | **Port, changed.** "Patrons" (saints) chosen at population milestones: a pick-one-of-three bonus. It's a cheap source of run-to-run variety and fits the holy theme. |
| **Keep upgrades** (the Command Center has levels) | a fixed Keep | **Port.** Two upgrades, for range, income and tier-3 research (see the tech-tree plan). |

## Buildings and defence

| TAB has | We have | Recommendation |
|---|---|---|
| **Repair**: damaged buildings are repaired, at a cost | no repair; damage is permanent until rebuilt | **Port, high priority.** Without it, every wave ratchets the colony down. Auto-repair for a fee, out of combat, with a toggle. |
| **Wall and gate tiers** | timber and stone walls, one gate | **Port the stone gate.** It's a small change. |
| **A wide tower roster** (ballistae, shock and area towers, a late super-tower) | 6 towers | **Enough for now.** Revisit after the tech tree lands. |
| **Lookout towers and radar** (vision) | no fog of war | **See the fog of war row below.** |
| **Buildings that are dangerous when lost** (infected buildings spawn infected) | possession | **Keep ours.** It's the same pillar, done well. |
| **Destroy and deconstruct refunds** | demolish refunds half | **Keep ours.** |

## Military

| TAB has | We have | Recommendation |
|---|---|---|
| **Veterancy** (units rank up with kills) | none | **Port.** It's the Arms tier-2 tech in the plan, and makes losing a veteran hurt. |
| **Two production buildings** (a basic and an advanced) | one Barracks | **Change.** The Armoury and War College in the tech plan are the advanced half. |
| **Unit upkeep** in gold and food | gold upkeep | **Keep ours.** |
| **A few special units** (a slow super-unit, a flamer) | 6 soldier types | **Later.** A late "Paladin" or siege unit once Arms tier 3 exists. |
| **Control groups, attack-move, patrol** | groups, attack-move, move, hold, stop | **Port patrol.** It's small, and good for guarding a wall line. |
| **Formations** | none | **Skip.** |

## Enemies

| TAB has | We have | Recommendation |
|---|---|---|
| **Villages of infected** (towns to clear) | sleeping packs | **Change: ruins.** A few old settlements full of Thralls and loot (resources, a relic), worth clearing on purpose. |
| **Specials**: fast runners, fliers (harpies), ranged spitters, giants | Hounds, Gargoyles, Brutes, Bloaters, Howlers, Broodmothers | **Port a ranged demon.** A spitter that outranges walls and forces soldiers out. It's the biggest missing enemy role. |
| **Waves that grow and a final horde** | the same, plus Hellgates and corruptions | **Keep ours.** |
| **Noise drawing zombies** | noise wakes packs and draws Howlers | **Keep ours.** Consider making noise visible (a noise meter, or rings when towers fire). |

## Map and modes

| TAB has | We have | Recommendation |
|---|---|---|
| **Fog of war** and exploration | the whole map is visible | **Port, probably.** It's much of TAB's tension: you can't see the horde massing until it's close, and scouting matters. It costs a visibility grid in the sim (cheap), fogged rendering, and minimap fog, and every probe must still see all. I'd do it right after repair. |
| **Custom survival settings** (map size, zombie density, days, with a score multiplier) | map kind, difficulty, mode, seed | **Port the score.** A score from days survived, difficulty, map and kills, shown at the end, so friends can compare. Map size (192, 256 or 320) is cheap to add. |
| **A campaign** (a territory map of missions, research earned between missions) | none | **Port, changed.** This is the next job (scaffolding below); I'd skip the hero missions, as you said. |
| **Weekly challenge** (a seeded shared run) | seeds | **Later.** A "weekly seed" is trivial once there's somewhere to post scores. |
| **Ironman saves** | free saves | **Skip**, as you decided. Maybe an optional ironman flag later for scores. |

## Presentation and feel

| TAB has | We have | Recommendation |
|---|---|---|
| **Pause-and-command** | yes | Keep. |
| **Tooltips with full stats** | text tooltips | **Change.** Rich tooltip cards, in the UI polish pass. |
| **Audio**: music, and an alarm when a wave is sighted | synthesized cues | **Later**, with commissioned audio. |
| **Death animations**, and corpses fading | demons vanish | **Port (cheap).** The baked sheets can include a death row. It adds a lot of weight to a fight. |

## My suggested order

1. **Repair** (small, and big for feel).
2. **Tech tree by buildings** (the plan next to this file), with the Keep upgrades and housing tiers.
3. **Fog of war and vision.**
4. **A ranged demon, and veterancy.**
5. **Score and map size.**
6. **Patrons** (the pick-one bonuses).
7. **Storage caps**, if the playtest says gold and wood pile up.
