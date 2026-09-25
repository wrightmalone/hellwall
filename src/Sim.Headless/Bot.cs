using Hellwall.Sim;

namespace Hellwall.Headless;

/// <summary>
/// A scripted player, for proving a full survival run can be won (and lost)
/// under the default rules with default resources. It plays a doctrine a new
/// player could follow, not a clever one: its wins are a lower bound on what
/// a person can do, and its losses show what neglect costs.
///
/// Every second it looks at the world and issues at most a few commands, in
/// priority order. It only acts through Commands, like any player.
/// </summary>
public sealed class Bot
{
    public enum Style
    {
        /// <summary>Economy, walls, towers, soldiers, and moving the garrison to meet each wave.</summary>
        Full,
        /// <summary>Economy only: no walls, towers or soldiers.</summary>
        Passive,
    }

    /// <summary>Named research orders: three build paths that part ways at the exclusive tier-3 pairs.</summary>
    public static readonly Dictionary<string, string[]> Plans = new()
    {
        ["fortress"] = ["tithes", "masonry", "fletching", "husbandry", "pitch", "bastions", "ballistics", "artillery"],
        ["pyre"] = ["tithes", "hallowing", "holyfire", "fletching", "pitch", "artillery", "husbandry"],
        ["legion"] = ["tithes", "husbandry", "drill", "standingarmy", "fletching", "pitch", "masonry", "ballistics"],
    };

    readonly World _world;
    readonly Style _style;
    readonly string[] _plan;

    /// <summary>Only the army path goes out after Hellgates; turtles lose too much when raiders die far from home.</summary>
    readonly bool _raids;
    readonly int _c;
    readonly List<Side> _incoming = new();

    /// <summary>Print each decision, for working out why a run went wrong.</summary>
    public bool Verbose;
    int _lastWaveMoved = -1;
    int? _raidGate;
    /// <summary>The ruin an expedition is out to loot, while one is.</summary>
    int? _lootRuin;

    public Bot(World world, Style style, string plan = "fortress")
    {
        _world = world;
        _style = style;
        _plan = Plans[plan];
        // Legion raids gates by doctrine; any plan does when closing them is the mission.
        _raids = plan == "legion" || world.Goals.Any(g => g.Kind == ObjectiveKind.CloseGates);
        _c = world.Terrain.Width / 2;
    }

    double Seconds => _world.Tick / (double)Balance.TickHz;
    Colony Colony => _world.Colony;
    Rules Rules => _world.Rules;

    public void See(IEnumerable<SimEvent> events)
    {
        foreach (var e in events)
            if (e is WaveAnnounced w)
            {
                _incoming.Clear();
                _incoming.AddRange(w.Sides);
            }
    }

    /// <summary>One decision step; call about once a second.</summary>
    public void Act()
    {
        // A patron's blessing on offer: take the first (the offer is a seeded draw, so this is a fair pick).
        if (_world.PatronOffer.Length > 0) Do(new ChoosePatron(_world.PatronOffer[0]));
        foreach (var b in _world.Buildings.Where(b => b.Possessed).ToList())
            Do(new Demolish(b.Id)); // purge at once: every second it stands is another Thrall

        int free = Colony.Colonists - Colony.WorkersUsed;
        double net(Resource r) => Colony.NetPerSecond[(int)r];
        bool anyIdle = _world.Buildings.Any(b => b.Complete && b.OnGround && b.NeedsCrew && !b.Staffed && !b.Possessed);

        // A Quarry or Mine with nothing left in reach only ties up its crew: take it down, and the want below builds a new one on fresh ground.
        if (_world.Buildings.FirstOrDefault(b => b.Exhausted && b.Def.Miners && b.Complete && !b.Possessed) is { } spent)
        {
            Do(new Demolish(spent.Id));
            return;
        }

        // 1. People first: an idle building is wasted, and nothing crewed gets built without hands for it.
        if ((anyIdle || free < 3) && !Underway(BuildingKind.House))
            if (Place(BuildingKind.House, NearKeep)) return;

        // 2. Holy power ahead of demand.
        if (Colony.SanctityDemand + 6 > Colony.SanctitySupply && !Underway(BuildingKind.Shrine) && free >= Rules[BuildingKind.Shrine].Workers)
            if (Place(BuildingKind.Shrine, NearKeep)) return;

        // 3. Whichever resource is furthest behind its target gets a gatherer, or holy ground toward it.
        // Soldiers eat too, as they're trained: once there's a Barracks, food for a steady trickle of recruits.
        double foodTarget = 0.1 + Colony.Colonists * 0.004 + (Count(BuildingKind.Barracks) > 0 ? 0.3 : 0);
        double woodTarget = Seconds < 300 ? 0.8 : Seconds < 1200 ? 1.6 : 2.4;
        double stoneTarget = Seconds < 150 ? 0 : Seconds < 1200 ? 0.8 : 1.4;
        double ironTarget = Seconds < 120 ? 0 : Seconds < 1200 ? 0.5 : 1.2;
        var wants = new List<(double Gap, BuildingKind Kind)>
        {
            (ironTarget - net(Resource.Iron), BuildingKind.Mine),
            (foodTarget - net(Resource.Food), BuildingKind.Farm),
            (foodTarget - net(Resource.Food) - 0.01, BuildingKind.Hunter),
            (woodTarget - net(Resource.Wood), BuildingKind.Woodcutter),
            (stoneTarget - net(Resource.Stone), BuildingKind.Quarry),
        };
        foreach (var (gap, kind) in wants.Where(w => w.Gap > 0).OrderByDescending(w => w.Gap))
        {
            if (Underway(kind)) continue;
            // A gatherer needs hands; reaching its ground doesn't, so holy ground moves toward it while Houses fill.
            double worthIt = kind == BuildingKind.Mine && Count(BuildingKind.Mine) == 0 ? 0.15 : 0.25; // the first Mine is worth a poor spot
            if (free >= Rules[kind].Workers && Place(kind, s => GatherScore(kind, s), minScore: worthIt)) return;
            if (!HasSpot(kind, worthIt) && Expand(Rules[kind].Gathers)) return;
        }

        if (_style == Style.Passive) return;

        Defend();
    }

    /// <summary>
    /// Go out and close a Hellgate when there's time: a big enough army, a
    /// gate standing, and the next wave well off. Everyone but a home guard
    /// attack-moves to the nearest gate; the next announced wave calls them back.
    /// </summary>
    void Raid()
    {
        if (!_raids) return;
        const int homeGuard = 8;
        var next = _world.Survival?.Next;
        double untilWave = next == null ? double.MaxValue : (next.LandsAtTick - _world.Tick) / (double)Balance.TickHz;
        if (_raidGate is { } current && _world.Gates.FirstOrDefault(g => g.Id == current) is { Alive: true }) return; // under way
        _raidGate = null;
        // When closing gates is the mission, go with a smaller force: waiting for a big one runs out the clock.
        int force = _world.Goals.Any(g => g.Kind == ObjectiveKind.CloseGates) ? 8 : 12;
        if (_world.Units.Count < homeGuard + force || untilWave < 150 || next is { Announced: true }) return;
        var gate = _world.Gates.Where(g => g.Alive).OrderBy(g => MathF.Abs(g.CentreX - _c) + MathF.Abs(g.CentreY - _c)).FirstOrDefault();
        if (gate == null) return;
        var raiders = _world.Units.OrderByDescending(u => u.Hp).Skip(homeGuard).Select(u => u.Id).ToArray();
        _raidGate = gate.Id;
        Do(new OrderUnits(raiders, OrderKind.AttackMove, (int)gate.CentreX, gate.Y + Hellgate.Size + 1));
    }

    /// <summary>
    /// Loot ruins, when a mission asks for it: with an army to spare and no
    /// wave due soon, everyone but a home guard attack-moves to the nearest
    /// unlooted ruin, and stays until it's taken (the guards fight them there).
    /// </summary>
    void Loot()
    {
        if (!_world.Goals.Any(g => g.Kind == ObjectiveKind.LootRuins) || _raidGate != null) return;
        if (_lootRuin is { } current && _world.Ruins.FirstOrDefault(r => r.Id == current) is { Looted: false }) return; // under way
        _lootRuin = null;
        const int homeGuard = 6, force = 10;
        var next = _world.Survival?.Next;
        double untilWave = next == null ? double.MaxValue : (next.LandsAtTick - _world.Tick) / (double)Balance.TickHz;
        if (_world.Units.Count < homeGuard + force || untilWave < 120 || next is { Announced: true }) return;
        var ruin = _world.Ruins.Where(r => !r.Looted).OrderBy(r => MathF.Abs(r.X - _c) + MathF.Abs(r.Y - _c)).FirstOrDefault();
        if (ruin == null) return;
        var party = _world.Units.OrderByDescending(u => u.Hp).Skip(homeGuard).Select(u => u.Id).ToArray();
        _lootRuin = ruin.Id;
        Do(new OrderUnits(party, OrderKind.AttackMove, ruin.X, ruin.Y));
    }

    /// <summary>
    /// Clear the wilds: with no wave due soon, send the army at the nearest
    /// sleeping pack it can beat, so the ground around it can be built on.
    /// The next announced wave calls it home, like a raid.
    /// </summary>
    void Clear()
    {
        if (_raidGate != null || _lootRuin != null) return;
        var next = _world.Survival?.Next;
        double untilWave = next == null ? double.MaxValue : (next.LandsAtTick - _world.Tick) / (double)Balance.TickHz;
        int towersUp = _world.Buildings.Count(b => b.Def.Weapon != null && b.Complete);
        if (untilWave < 90 || next is { Announced: true } || _world.Units.Count < 8 || towersUp < 4) return;
        if (_clearing is { } current && _world.Packs.FirstOrDefault(p => p.Id == current) is { Awake: false }) return; // under way
        _clearing = null;
        const int homeGuard = 4;
        int army = _world.Units.Count - homeGuard;
        var pack = _world.Packs
            .Where(p => !p.Awake && p.Count <= army * 1.5)
            .Select(p => (Pack: p, D: MathF.Sqrt((p.X - _c) * (p.X - _c) + (p.Y - _c) * (p.Y - _c))))
            .Where(p => p.D < 45)
            .OrderBy(p => p.D)
            .Select(p => p.Pack)
            .FirstOrDefault();
        if (pack == null)
        {
            if (Verbose && (int)Seconds % 60 == 0)
            {
                var near = _world.Packs.Where(p => !p.Awake).OrderBy(p => (p.X - _c) * (p.X - _c) + (p.Y - _c) * (p.Y - _c)).Take(3)
                    .Select(p => $"{p.Count}@{MathF.Sqrt((p.X - _c) * (p.X - _c) + (p.Y - _c) * (p.Y - _c)):F0}");
                Console.WriteLine($"      t={Seconds,5:F0}s  no pack to clear with {army} soldiers; nearest: {string.Join(", ", near)}");
            }
            return;
        }
        if (Verbose) Console.WriteLine($"      t={Seconds,5:F0}s  clearing a pack of {pack.Count} with {army}");
        _clearing = pack.Id;
        Do(new OrderUnits(_world.Units.OrderByDescending(u => u.Hp).Skip(homeGuard).Select(u => u.Id).ToArray(), OrderKind.AttackMove, pack.X, pack.Y));
    }

    int? _clearing;

    /// <summary>What defense may spend: keep enough back that a House and a Shrine are always affordable.</summary>
    bool CanSpend(Cost cost) =>
        Colony[Resource.Wood] - cost.Wood >= Rules[BuildingKind.House].Cost.Wood + 10
        && Colony[Resource.Stone] - cost.Stone >= Rules[BuildingKind.Shrine].Cost.Stone
        && Colony.CanAfford(cost);

    void Defend()
    {
        if (Seconds > 60) BuildRing();

        int free = Colony.Colonists - Colony.WorkersUsed;
        // Towers once there's a direction to face: the first wave's announcement, then steadily.
        int towers = Count(BuildingKind.Watchtower) + Count(BuildingKind.Bombard) + Count(BuildingKind.LanceTower);
        bool warned = (_world.Survival?.Waves.Any(w => w.Announced) ?? true) || _world.Horde.Count > 0;
        int wantTowers = !warned ? 0 : 3 + (int)(Seconds / 60);
        // Rich and fed: turn surplus gold into towers. Not while hungry, or the new crews starve the colony.
        bool fed = Colony.NetPerSecond[(int)Resource.Food] > 0.05 && Colony[Resource.Food] > 100;
        // All in for the end: once the Convergence is near, spend everything, fed or not.
        var survival = _world.Survival;
        if (survival != null && (_world.Day >= survival.Rules.Days - 5 || survival.Waves[^1].LandsAtTick - _world.Tick <= Math.Min(10, survival.Rules.Days / 6.0) * survival.Rules.DaySeconds * Balance.TickHz)) fed = true;
        if (warned && fed && Colony[Resource.Gold] > 800) wantTowers = int.MaxValue;
        var towerKind = Seconds > 900 && Count(BuildingKind.Bombard) * 3 < Count(BuildingKind.Watchtower) ? BuildingKind.Bombard : BuildingKind.Watchtower;
        if (towerKind == BuildingKind.Watchtower && _world.Tech.Has("ballistics") && Count(BuildingKind.LanceTower) * 2 < Count(BuildingKind.Watchtower))
            towerKind = BuildingKind.LanceTower;
        // Short of stone for the tower it wants, it builds what it can afford rather than nothing.
        // (A Censer only when gold is piling up: it's a stopgap, not the opening's tower.)
        var fallback = Colony[Resource.Gold] > 500 ? new[] { towerKind, BuildingKind.Watchtower, BuildingKind.Censer } : new[] { towerKind, BuildingKind.Watchtower };
        foreach (var kind in fallback.Distinct())
        {
            var towerDef = _world.Def(kind);
            if (!(towers < wantTowers && free >= towerDef.Workers && CanSpend(towerDef.Cost)
                && Colony.SanctitySupply - Colony.SanctityDemand >= towerDef.SanctityUse)) continue;
            if (Place(kind, TowerScore)) return;
            break;
        }

        // Support, on top of the quota: gargoyles come from wave 5, so a Skyspire per eight towers by then; a Belfry per eight once Masonry allows.
        var support = Seconds > 900 && Count(BuildingKind.Skyspire) * 8 < towers ? BuildingKind.Skyspire
            : towers >= 8 && _world.Tech.Has("masonry") && Count(BuildingKind.Belfry) * 8 < towers ? BuildingKind.Belfry
            : (BuildingKind?)null;
        if (support is { } sk && towers >= wantTowers / 2 && CanSpend(_world.Def(sk).Cost) && Colony[Resource.Gold] > 300
            && Colony.SanctitySupply - Colony.SanctityDemand >= _world.Def(sk).SanctityUse)
            if (Place(sk, TowerScore)) return;

        if (Research()) return;

        var barracksDef = Rules[BuildingKind.Barracks];
        if (Seconds > 360 && Count(BuildingKind.Barracks) == 0 && free >= barracksDef.Workers && CanSpend(barracksDef.Cost))
            if (Place(BuildingKind.Barracks, NearKeep)) return;

        var barracks = _world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Barracks && b.Active);
        int wantUnits = 8 + (int)(Seconds / 60);
        if (fed && Colony[Resource.Gold] > 1000) wantUnits = 80; // rich and fed: turn gold into soldiers
        bool affordArmy = Colony.NetPerSecond[(int)Resource.Gold] > 0.5 || Colony[Resource.Gold] > 1500; // upkeep mustn't sink the colony
        if (barracks != null && barracks.Queue.Count < 2 && _world.Units.Count + barracks.Queue.Count < wantUnits && Colony[Resource.Gold] > 120 && affordArmy)
        {
            var ranged = _world.Tech.Has("drill") ? UnitKind.Crossbowman : UnitKind.Militia;
            var melee = _world.Tech.Has("husbandry") && _world.Units.Count % 12 == 5 ? UnitKind.Outrider : UnitKind.Templar;
            int slot = _world.Units.Count % 6;
            var kind = slot == 5 && _world.Tech.Has("hallowing") ? UnitKind.Chaplain : slot is 2 or 5 ? melee : ranged;
            Do(new TrainUnit(barracks.Id, kind));
        }

        Raid();
        Loot();
        Clear();

        // Meet each wave: once it's announced, stand the garrison just inside the ring on its side.
        var next = _world.Survival?.Next;
        if (next is { Announced: true } && next.Number != _lastWaveMoved && _world.Units.Count > 0)
        {
            _lastWaveMoved = next.Number;
            _raidGate = null; // any raid is called home
            _clearing = null; // and any clearing: pick it up again after the wave
            _lootRuin = null; // and any expedition
            var units = _world.Units.Select(u => u.Id).ToArray();
            if (next.Final)
            {
                // From every side at once: hold the middle, where they can turn to wherever the walls break.
                Do(new OrderUnits(units, OrderKind.AttackMove, _c, _c + 3));
                return;
            }
            var sides = next.Sides;
            for (int i = 0; i < sides.Length; i++)
            {
                var share = units.Where((_, k) => k % sides.Length == i).ToArray();
                if (share.Length == 0) continue;
                var (x, y) = InsideRing(sides[i]);
                Do(new OrderUnits(share, OrderKind.AttackMove, x, y));
            }
        }
    }

    /// <summary>
    /// The edge of holy ground: consecrated tiles with open, unconsecrated
    /// ground beside them. Everything is built on holy ground, so walling its
    /// edge encloses the whole colony; as Wardstones push the ground out, the
    /// old walls become inner walls. Edge tiles that can't take a wall (forest)
    /// are holes, which is where towers matter most.
    /// </summary>
    List<(int X, int Y, bool Hole)> Perimeter()
    {
        var t = _world.Terrain;
        var holy = Colony.Consecrated;
        var edge = new List<(int, int, bool)>();
        for (int y = 1; y < t.Height - 1; y++)
            for (int x = 1; x < t.Width - 1; x++)
            {
                int i = t.Index(x, y);
                if (!holy[i]) continue;
                bool outside =
                    Open(x + 1, y) || Open(x - 1, y) || Open(x, y + 1) || Open(x, y - 1);
                if (!outside) continue;
                int id = _world.BuildingIdAt(x, y);
                bool hole = id == 0 && !Terrain.IsBuildable(t.Get(x, y)) && Terrain.IsWalkable(t.Get(x, y));
                if (id != 0 || Terrain.IsBuildable(t.Get(x, y)) || hole) edge.Add((x, y, hole));
            }
        return edge;

        bool Open(int x, int y) => !holy[t.Index(x, y)] && _world.IsWalkable(x, y);
    }

    /// <summary>A Scriptorium once the colony is established, then the plan's next available tech whenever it's idle.</summary>
    bool Research()
    {
        int free = Colony.Colonists - Colony.WorkersUsed;
        var labDef = _world.Def(BuildingKind.Scriptorium);
        if (Seconds > 420 && Count(BuildingKind.Scriptorium) == 0 && free >= labDef.Workers && CanSpend(labDef.Cost))
            return Place(BuildingKind.Scriptorium, NearKeep);
        var lab = _world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Scriptorium && b.Active && b.Researching == null);
        if (lab == null) return false;
        var next = _plan.FirstOrDefault(id => _world.CheckResearch(id) == null);
        if (next == null || !CanSpend(Rules.Tech(next).Cost)) return false;
        Do(new Research(lab.Id, next));
        return true;
    }

    /// <summary>A point inside the colony toward one side: where the garrison meets a wave.</summary>
    (int X, int Y) InsideRing(Side side)
    {
        const int reach = 7;
        return side switch
        {
            Side.North => (_c, _c - reach),
            Side.South => (_c, _c + reach),
            Side.West => (_c - reach, _c),
            _ => (_c + reach, _c),
        };
    }

    /// <summary>Wall the edge of holy ground, a few tiles a second, the incoming side first. Stone once it's known.</summary>
    void BuildRing()
    {
        var wall = _world.Tech.Has("masonry") ? BuildingKind.StoneWall : BuildingKind.Wall;
        var cost = _world.Def(wall).Cost;
        // Walls never eat what the next planned tech needs.
        var nextTech = _plan.FirstOrDefault(id => !_world.Tech.Has(id) && _world.CheckResearch(id) is null or "already being researched");
        double techStone = nextTech == null ? 0 : Rules.Tech(nextTech).Cost.Stone;
        double techWood = nextTech == null ? 0 : Rules.Tech(nextTech).Cost.Wood;
        double spare = wall == BuildingKind.StoneWall
            ? (Colony[Resource.Stone] - Rules[BuildingKind.Shrine].Cost.Stone - techStone - 10) / cost.Stone
            : (Colony[Resource.Wood] - Rules[BuildingKind.House].Cost.Wood - techWood - 10) / cost.Wood;
        int budget = (int)Math.Min(12, spare);
        if (budget <= 0) return;
        // Never wall over the ground a Mine or Quarry needs: a lost one has to be rebuilt.
        var tiles = Perimeter().Where(p => !p.Hole && !NearOre(p.X, p.Y, 1) && _world.CheckPlacement(wall, p.X, p.Y) == null).Select(p => (p.X, p.Y));
        if (_incoming.Count > 0)
        {
            var (tx, ty) = InsideRing(_incoming[0]);
            tiles = tiles.OrderBy(t => Math.Abs(t.X - tx) + Math.Abs(t.Y - ty));
        }
        // Every tenth tile of the ring is a Gate, where there's open ground on both sides of it: soldiers
        // have to get out to clear the wilds, and a gate onto the trees (which block them) is only a wall.
        foreach (var (x, y) in tiles.Take(budget).ToList())
            Do(new PlaceBuilding((x + y) % 10 == 0 && OpensBothWays(x, y) && _world.CheckPlacement(BuildingKind.Gate, x, y) == null ? BuildingKind.Gate : wall, x, y));
    }

    bool OpensBothWays(int x, int y)
    {
        bool Open(int tx, int ty) => _world.Terrain.InBounds(tx, ty) && Terrain.IsWalkable(_world.Terrain.Get(tx, ty)) && !(_world.ForestBlocks && _world.Terrain.Get(tx, ty) == Tile.Forest) && _world.BuildingIdAt(tx, ty) == 0;
        return (Open(x - 1, y) && Open(x + 1, y)) || (Open(x, y - 1) && Open(x, y + 1));
    }

    List<(int X, int Y, bool Hole)>? _perimeterCache;
    int _perimeterTick = -1;

    /// <summary>
    /// Towers go where the edge is least covered: each edge tile counts for
    /// less the more towers already reach it, holes count triple, and the
    /// incoming side double.
    /// </summary>
    double TowerScore((int X, int Y) s)
    {
        if (_perimeterTick != _world.Tick)
        {
            _perimeterCache = Perimeter();
            _perimeterTick = _world.Tick;
        }
        float cx = s.X + 1, cy = s.Y + 1;
        var towers = _world.Buildings.Where(b => b.Def.Weapon != null).ToList();
        double score = 0;
        foreach (var (x, y, hole) in _perimeterCache!)
        {
            const float r = 6.5f;
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            if (dx * dx + dy * dy > r * r) continue;
            int covering = towers.Count(t => (x + 0.5f - t.CentreX) * (x + 0.5f - t.CentreX) + (y + 0.5f - t.CentreY) * (y + 0.5f - t.CentreY) <= t.Def.Weapon!.Range * t.Def.Weapon.Range);
            double w = (hole ? 3.0 : 1.0) / (1 + covering * covering);
            if (_incoming.Count > 0 && Toward(x, y, _incoming)) w *= 2;
            score += w;
        }
        return score;
    }

    bool Toward(int x, int y, List<Side> sides) =>
        sides.Any(side => side switch
        {
            Side.North => y < _c - Math.Abs(x - _c),
            Side.South => y > _c + Math.Abs(x - _c),
            Side.West => x < _c - Math.Abs(y - _c),
            _ => x > _c + Math.Abs(y - _c),
        });

    /// <summary>
    /// No good gathering spot on holy ground: put a Wardstone at the edge of it,
    /// where it would consecrate the most tiles of the resource we're short of.
    /// </summary>
    bool Expand(Tile[] wanted)
    {
        if (Underway(BuildingKind.Wardstone)) return false;
        if (Count(BuildingKind.Wardstone) >= 4 + (int)(Seconds / 120)) return false; // expansion at a pace, not a reflex
        var t = _world.Terrain;
        float r = Rules[BuildingKind.Wardstone].ConsecrateRadius;
        int Gain((int X, int Y) s)
        {
            int gain = 0;
            for (int y = (int)(s.Y - r - 4); y <= s.Y + r + 4; y++)
                for (int x = (int)(s.X - r - 4); x <= s.X + r + 4; x++)
                {
                    if (!t.InBounds(x, y) || Array.IndexOf(wanted, t.Get(x, y)) < 0 || _world.Colony.Consecrated[t.Index(x, y)]) continue;
                    float dx = x - s.X, dy = y - s.Y;
                    if (dx * dx + dy * dy <= (r + 4) * (r + 4)) gain++;
                }
            return gain;
        }
        if (Place(BuildingKind.Wardstone, s => Gain(s), minScore: 8)) return true;

        // Nothing within one Wardstone's reach: step toward the nearest of it instead, and go on next time.
        var targets = new List<(int X, int Y)>();
        for (int y = 0; y < t.Height; y += 2)
            for (int x = 0; x < t.Width; x += 2)
                if (Array.IndexOf(wanted, t.Get(x, y)) >= 0 && !_world.Colony.Consecrated[t.Index(x, y)]
                    && Math.Abs(x - _c) + Math.Abs(y - _c) < 90)
                    targets.Add((x, y));
        if (targets.Count == 0) return false;
        return Place(BuildingKind.Wardstone, s => -targets.Min(p => Math.Abs(p.X - s.X) + Math.Abs(p.Y - s.Y)));
    }

    /// <summary>
    /// Some legal spot on holy ground where this gatherer would score over
    /// `minScore`, scored exactly as Place scores it: a spot that passed here
    /// but failed there once stopped a colony both building and expanding.
    /// </summary>
    bool HasSpot(BuildingKind kind, double minScore)
    {
        var t = _world.Terrain;
        for (int y = 0; y < t.Height; y += 2)
            for (int x = 0; x < t.Width; x += 2)
                if (Colony.Consecrated[t.Index(x, y)] && _world.CheckPlacement(kind, x, y) is null or "not enough gold" or "not enough wood"
                    && GatherScore(kind, (x, y)) > minScore) return true;
        return false;
    }

    bool NearOre(int x, int y, int r)
    {
        var t = _world.Terrain;
        for (int ty = y - r; ty <= y + r; ty++)
            for (int tx = x - r; tx <= x + r; tx++)
                if (t.InBounds(tx, ty) && t.Get(tx, ty) is Tile.Ore or Tile.Rock) return true;
        return false;
    }

    double NearKeep((int X, int Y) s) => -Dist(s);

    double Dist((int X, int Y) s) => MathF.Sqrt((s.X - _c) * (s.X - _c) + (s.Y - _c) * (s.Y - _c));

    double Gather(BuildingKind kind, (int X, int Y) s) => _world.EstimateGathering(kind, s.X, s.Y);

    double GatherScore(BuildingKind kind, (int X, int Y) s) => Gather(kind, s) - Dist(s) * 0.002;

    /// <summary>
    /// Place a building at the best-scoring legal spot on holy ground. Only
    /// every other tile is tried, which is plenty and keeps the search cheap.
    /// </summary>
    bool Place(BuildingKind kind, Func<(int X, int Y), double> score, double minScore = double.MinValue)
    {
        if (!Colony.CanAfford(Rules[kind].Cost)) return false;
        var t = _world.Terrain;
        (int X, int Y)? best = null;
        double bestScore = minScore;
        for (int y = 0; y < t.Height; y += 2)
            for (int x = 0; x < t.Width; x += 2)
            {
                if (!Colony.Consecrated[t.Index(x, y)]) continue;
                if (_world.CheckPlacement(kind, x, y) != null) continue;
                double sc = score((x, y));
                if (sc > bestScore) { bestScore = sc; best = (x, y); }
            }
        if (best == null)
        {
            if (Verbose && _lastMiss != (kind, (int)Seconds / 30)) Console.WriteLine($"      t={Seconds,5:F0}s  no spot for {kind} scoring over {minScore:0.##}");
            _lastMiss = (kind, (int)Seconds / 30);
            return false;
        }
        Do(new PlaceBuilding(kind, best.Value.X, best.Value.Y));
        return true;
    }

    (BuildingKind, int) _lastMiss;

    int Count(BuildingKind kind, bool underway = false) => _world.Buildings.Count(b => b.Kind == kind && (!underway || b.Complete));

    bool Underway(BuildingKind kind) => _world.Buildings.Any(b => b.Kind == kind && !b.Complete);

    void Do(Command c)
    {
        if (Verbose && c is not PlaceBuilding { Kind: BuildingKind.Wall }) Console.WriteLine($"      t={Seconds,5:F0}s  {c}");
        _world.Enqueue(c);
        _world.FlushCommands(); // so the next decision sees this one's footprint and cost
    }
}
