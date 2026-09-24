# Tech tree unlocked by buildings: a proposal

Status: draft for you to decide on. Nothing here is built yet.

## Where we are

Twelve techs, all researched at one building (the Scriptorium), in three
tiers. Tier 3 is two exclusive pairs (Bastions or Holy Fire, Standing Army
or Artillery), which is what splits the three build paths (fortress, pyre,
legion). A tech costs gold plus a second resource, and takes time. Six things are
locked behind techs:

| Locked | Needs |
|---|---|
| Stone Wall | Masonry |
| Belfry | Masonry |
| Lance Tower | Ballistics |
| Crossbowman | Drill |
| Chaplain | Hallowing |
| Outrider | Husbandry |

What's weak about it:

- **One building researches everything, one tech at a time.** Research is a
  queue, not a decision about what to build.
- **Nothing you build changes what you can research.** In They Are Billions
  (TAB), what you've built opens what you can learn next, and that coupling
  is most of what makes its tech feel like progress.
- **Twelve techs can't grow.** Every new building or unit has to find a slot
  in a flat list.

## The proposal: four orders, each with its own hall

Research moves out of the Scriptorium into four halls, one per branch.
Building a hall unlocks its branch; each hall researches only its own branch, so four halls can
research at once. Higher tiers need a second building to have been built, which
is where "buildings unlock techs" comes in: the tree is gated by what
stands in your town, not only by what you've read.

| Branch | Hall (tier 1) | Tier 2 also needs | Tier 3 also needs | Feeds |
|---|---|---|---|---|
| Masonry | Stonemason's Lodge | 2 Quarries | a Keep upgrade (stone keep) | walls, gates, Bombards, Belfries, Bastions |
| Faith | Chapel | 3 Shrines | a Cathedral | holy ground, sanctity, Chaplains, Holy Fire |
| Arms | Armoury | 2 Barracks | a War College | soldiers, Drill, Outriders, Standing Army |
| Industry | Guildhall | a Mine | a Market | tithes, farms, mines, storage, trade |

Each hall is a 2x2 or 3x3 building with a crew and a sanctity cost like any other, so
opening a branch has a price: land, people and holy power, not only gold.

### What each branch holds (first pass)

Existing techs keep their effects and move to a branch; new ones are marked (new).

**Masonry**
- Tier 1:
  - Masonry (Stone Walls)
  - Gatehouses (new: gates as tough as stone walls)
- Tier 2:
  - Pitch (Bombards)
  - Ballistics (Lance Towers)
  - Bell-founding (new: Belfries, moved from Masonry)
- Tier 3:
  - Bastions, exclusive with Holy Fire (walls x2.5, towers x2)
  - Artillery, exclusive with Standing Army

**Faith**
- Tier 1:
  - Tithes (gold per colonist)
  - Consecration (new: Wardstones cheaper and faster)
- Tier 2:
  - Hallowing (reach, sanctity, Chaplains)
  - Relics (new: Shrines supply more; possession slower in holy ground)
- Tier 3:
  - Holy Fire
  - Sanctuary (new: possessed buildings purge themselves slowly on holy ground)

**Arms**
- Tier 1:
  - Fletching (damage)
  - Drill (Crossbowmen, soldier hp)
- Tier 2:
  - Husbandry for horses (Outriders; the food half moves to Industry)
  - Veterans (new: soldiers gain rank with kills)
- Tier 3:
  - Standing Army
  - Crusade (new: soldiers deal extra damage to Hellgates, making closing gates a path of its own)

**Industry**
- Tier 1:
  - Husbandry (food)
  - Prospecting (new: Mines +25%)
- Tier 2:
  - Storehouses (new: storage caps, if we add caps; see the gap list)
  - Guild levy (new: gold from buildings)
- Tier 3:
  - Market (new: trade one resource for another at a loss)

The exclusive pairs stay at tier 3 and stay across branches (Masonry against Faith,
Arms against Masonry), so the three current paths keep their identities:

| Path | Branches |
|---|---|
| Fortress | Masonry, plus Industry |
| Pyre | Faith, plus Industry |
| Legion | Arms, plus a little Masonry |

The bot plans become "which halls, in which order", and the sweep measures the result the same way it does now.

### Rules I'd keep

- Tier N needs any N-1 techs from the same branch (not a specific one), so
  there's choice inside a branch, and a branch is a commitment, not a checklist.
- A hall that is destroyed or possessed stops its research and refunds it (as the
  Scriptorium does now). What it taught stays learned.
- Exclusives are decided at research time and announced in the tooltip ("locks out
  Holy Fire"), as now.

### How the interface handles it

Selecting a hall shows its branch in the command card: the barracks pattern again, one
building, one menu. There's no global research screen to scale, because each branch
is at most about 8 techs. The build menu gets a Research tab (or the halls go under Town).
The "build" and "build advanced" split you mentioned is the fallback if Town gets crowded.

## What it costs to build

1. **Sim** (1 day):
   - `TechDef` gains `Branch`, plus "also needs building X (count n)" for tiers 2-3.
   - Research moves from `Def.Researches` to `Def.ResearchesBranch`.
   - `CheckResearch` checks the branch hall and the required buildings.
   - Tests on unlock order, parallel halls, exclusives and refunds.
2. **Content** (half a day):
   - four halls in rules.json;
   - existing techs moved to branches;
   - the (new) techs, with numbers;
   - Keep upgrade, Cathedral, War College and Market as buildings. These are new content
     worth doing anyway: the Keep upgrade is also a TAB feature (see the gap list).
3. **Bot and balance** (1 day): plans become branch orders; sweep until the three paths
   are even again.
4. **UI** (half a day): the hall menus, reusing the Scriptorium's research card.

## Decisions for you

1. Four branches, or three (fold Industry into Faith and Masonry)?
2. Research in parallel at halls (proposed), or one research at a time colony-wide
   (closer to TAB, slower)?
3. Research priced in gold and resources (as now), or a separate research
   point currency produced by the halls' crews? The currency is more to
   balance, but it makes halls feel like workplaces.
4. Keep the Scriptorium as the Faith hall under another name, or retire it?
