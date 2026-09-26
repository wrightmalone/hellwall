using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hellwall.Sim;

public enum Resource : byte
{
    Gold,
    Wood,
    Stone,
    Food,
    /// <summary>Mined from ore deposits out on the map; soldiers are made of it.</summary>
    Iron,
    /// <summary>Holy silver, from veins near the map's edge only: what the advanced soldiers are made of.</summary>
    Silver,
}

public enum UnitKind : byte
{
    Militia,
    Marksman,
    Templar,
    Crossbowman,
    /// <summary>Heals the soldiers around it; barely fights.</summary>
    Chaplain,
    /// <summary>Mounted: fast and tough, for riding out to clear the wilds.</summary>
    Outrider,
    /// <summary>The advanced tier: long-range holy fire that bursts among the horde. Costs silver.</summary>
    Exorcist,
}

/// <summary>An amount of each resource. Used for costs and for the colony's stockpile.</summary>
public sealed class Cost
{
    public double Gold { get; init; }
    public double Wood { get; init; }
    public double Stone { get; init; }
    public double Food { get; init; }
    public double Iron { get; init; }
    public double Silver { get; init; }

    public static readonly Cost None = new();

    public double this[Resource r] => r switch
    {
        Resource.Gold => Gold,
        Resource.Wood => Wood,
        Resource.Stone => Stone,
        Resource.Food => Food,
        Resource.Iron => Iron,
        _ => Silver,
    };

    public Cost Scale(double f) => new() { Gold = Gold * f, Wood = Wood * f, Stone = Stone * f, Food = Food * f, Iron = Iron * f, Silver = Silver * f };

    public override string ToString()
    {
        var parts = new List<string>();
        foreach (var r in Enum.GetValues<Resource>())
            if (this[r] > 0) parts.Add($"{this[r]:0} {r.ToString().ToLowerInvariant()}");
        return parts.Count == 0 ? "free" : string.Join(", ", parts);
    }
}

/// <summary>
/// The demons that already hold the map: sleeping packs spread across it,
/// small near the Keep and larger further out. Nothing can be built near a
/// sleeping pack, so expanding means clearing: bring soldiers close enough
/// and it wakes and fights.
/// </summary>
/// <summary>
/// The woods as a wall (when Blocks): no one walks through forest, the horde
/// hacks through it like a wall when it has no better way, and a
/// Woodcutter's crew go out as woodsmen and fell it tree by tree, so the
/// forest that shelters a colony shrinks as it's harvested.
/// </summary>
/// <summary>
/// Fog of war: the map is dark until something of the colony's has seen it.
/// Only what's in sight now shows the horde; sleeping demons, once seen, stay
/// drawn where they stand. Building needs explored ground.
/// </summary>
/// <summary>
/// Damaged buildings mend themselves once left alone: after DelaySeconds
/// without losing health, PerSecond of their full health a second, paid for
/// as they go at CostFraction of their build cost per full repair. With too
/// little in store, repair waits.
/// </summary>
public sealed record RepairRules
{
    public bool Enabled { get; init; } = true;
    public float DelaySeconds { get; init; } = 8;
    public float PerSecond { get; init; } = 0.03f;
    public double CostFraction { get; init; } = 0.4;
}

public sealed record FogRules
{
    public bool Enabled { get; init; } = true;
    /// <summary>Tiles around the Keep seen from the start.</summary>
    public int StartReveal { get; init; } = 22;
    public float UnitSight { get; init; } = 9;
    /// <summary>A building sees this far past its footprint's half-width; a tower sees its range plus TowerExtra.</summary>
    public float BuildingSight { get; init; } = 5;
    public float TowerExtra { get; init; } = 3;
    public float WoodsmanSight { get; init; } = 4;
}

public sealed record WoodsRules
{
    public bool Blocks { get; init; }
    public float TreeHp { get; init; } = 300;
    /// <summary>Route cost through a tree for the horde, as a multiple of open ground (walls are 30).</summary>
    public int TreeCost { get; init; } = 20;
    /// <summary>Tree hit points a woodsman takes off a second, and the wood each point yields.</summary>
    public float ChopDps { get; init; } = 5.5f;
    public float WoodPerHp { get; init; } = 0.1f;
    /// <summary>Wood a woodsman carries home at a time.</summary>
    public float Carry { get; init; } = 10;
    public float Speed { get; init; } = 2.2f;
    /// <summary>Steps from his lodge's door a woodsman will walk to find a tree.</summary>
    public int Reach { get; init; } = 14;
}

public sealed record MiningRules
{
    /// <summary>Quarries and Mines send out miners who wear rock and ore away, rather than gathering from a radius forever.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Hit points of a rock tile and of an iron-ore tile: how long a deposit lasts.</summary>
    public float RockHp { get; init; } = 360;
    public float OreHp { get; init; } = 720;
    /// <summary>Hit points a miner takes off a second, and what each point yields.</summary>
    public float MineDps { get; init; } = 1;
    public float StonePerHp { get; init; } = 0.16f;
    public float IronPerHp { get; init; } = 0.13f;
}

public sealed record WildsRules
{
    /// <summary>Packs on a survival map (tests and probes ask for their own number).</summary>
    public int Packs { get; init; } = 160;
    public int MinDistance { get; init; } = 20;
    /// <summary>Tiles between pack centres, at the least.</summary>
    public int Spacing { get; init; } = 8;
    /// <summary>Pack size at MinDistance, and at the far edge of the map; in between it grows with distance.</summary>
    public int NearCount { get; init; } = 12;
    public int FarCount { get; init; } = 90;
    public double HoundChance { get; init; } = 0.15;
    /// <summary>No building within this many tiles of a sleeping pack.</summary>
    public float ClearRadius { get; init; } = 10;
    /// <summary>A soldier within WakeRadius plus WakeSpread of the pack's spread (its crowd's radius) of the centre wakes it.</summary>
    public float WakeRadius { get; init; } = 7;
    public float WakeSpread { get; init; } = 0.4f;
    /// <summary>How many sleeping demons stand per tile of a pack's disc: low, so a pack is a loose crowd, not a ball.</summary>
    public float SleepDensity { get; init; } = 0.45f;
    /// <summary>Stragglers: little groups of 1..StrayMax scattered over the whole map beyond MinDistance.</summary>
    public int Strays { get; init; } = 0;
    public int StrayMax { get; init; } = 4;
    /// <summary>Strays keep this far from the Keep: close ones wake to the first hammering and pick at a town with no walls yet.</summary>
    public int StrayMinDistance { get; init; } = 34;
    /// <summary>Old settlements to loot (on a 256 map; distances scale with the map), each guarded by about RuinGuards Thralls.</summary>
    public int Ruins { get; init; } = 0;
    public int RuinGuards { get; init; } = 50;
    public int RuinMinDistance { get; init; } = 40;
    public int RuinMaxDistance { get; init; } = 95;
    /// <summary>What a near ruin holds; the furthest hold twice as much.</summary>
    public Cost RuinLoot { get; init; } = Cost.None;
}

public enum Difficulty : byte
{
    Easy,
    Normal,
    Hard,
    Nightmare,
}

/// <summary>
/// Multipliers a difficulty applies to the Normal rules (rules.json is
/// Normal). Pure scaling of how many demons come and what you start with:
/// the rules of play are the same at every level.
/// </summary>
public sealed record DifficultyDef
{
    /// <summary>Every regular wave's size.</summary>
    public double Waves { get; init; } = 1;
    public double Convergence { get; init; } = 1;
    /// <summary>Sleeping packs' sizes.</summary>
    public double Packs { get; init; } = 1;
    /// <summary>How many sleeping packs there are, and stray handfuls: a harder map is fuller, not only its packs bigger.</summary>
    public double PackCount { get; init; } = 1;
    public double Strays { get; init; } = 1;
    /// <summary>Hellgate bands.</summary>
    public double Gates { get; init; } = 1;
    /// <summary>Starting resources.</summary>
    public double Start { get; init; } = 1;
}

public sealed record StartingUnit
{
    public UnitKind Kind { get; init; }
    public int Count { get; init; }
}

public sealed record WeaponDef
{
    /// <summary>Tiles.</summary>
    public float Range { get; init; }
    public float Damage { get; init; }
    /// <summary>Seconds between shots.</summary>
    public float Cooldown { get; init; }
    /// <summary>Radius in tiles hit around the target; 0 for single-target.</summary>
    public float Splash { get; init; }
    /// <summary>Radius in tiles of the noise each shot makes.</summary>
    public float Noise { get; init; }
    /// <summary>Shoots only fliers.</summary>
    public bool AirOnly { get; init; }
}

public sealed record BuildingDef
{
    public int[] Size { get; init; } = [1, 1];
    public float Hp { get; init; } = 100;
    public float BuildSeconds { get; init; }
    public Cost Cost { get; init; } = Cost.None;

    /// <summary>Colonists this building houses once complete.</summary>
    public int Housing { get; init; }
    /// <summary>Colonists it needs to operate. Short-staffed buildings are idle, not slower.</summary>
    public int Workers { get; init; }
    /// <summary>Base gold per second (the Keep's own income).</summary>
    public double Gold { get; init; }

    public float SanctitySupply { get; init; }
    public float SanctityUse { get; init; }
    /// <summary>Radius of consecrated ground it projects, if it's a node of the holy grid.</summary>
    public float ConsecrateRadius { get; init; }

    public Resource? Produces { get; init; }
    /// <summary>Per second, per matching tile within GatherRadius that no other gatherer has claimed.</summary>
    public double PerTile { get; init; }
    public int GatherRadius { get; init; }
    public Tile[] Gathers { get; init; } = [];

    public WeaponDef? Weapon { get; init; }
    public UnitKind[] Trains { get; init; } = [];

    /// <summary>
    /// A demon reaching it with people inside takes it, rather than damaging
    /// it. True for homes and workplaces; fortifications (towers) are only
    /// ever battered down, so a breach costs the tower, not a tower's worth
    /// of Thralls behind the wall.
    /// </summary>
    public bool Possessable { get; init; } = true;

    /// <summary>Can't be built until this tech is researched.</summary>
    public string? RequiresTech { get; init; }

    /// <summary>Researches techs (the Scriptorium).</summary>
    public bool Researches { get; init; }

    /// <summary>What it can be rebuilt as, in place (same footprint), paying that building's cost.</summary>
    public BuildingKind? UpgradesTo { get; init; }
    /// <summary>Only reached by upgrading: can't be placed.</summary>
    public bool UpgradeOnly { get; init; }

    /// <summary>When the woods block (WoodsRules.Blocks), its crew go out and fell trees rather than gathering from a radius.</summary>
    public bool Woodsmen { get; init; }

    /// <summary>When mining is on (MiningRules.Enabled), its crew go out as miners and wear away the rock or ore they work.</summary>
    public bool Miners { get; init; }

    /// <summary>
    /// While active, every demon within SlowRadius tiles moves at SlowFactor
    /// of its speed (the Belfry). Overlapping fields don't stack: the
    /// strongest applies.
    /// </summary>
    public float SlowRadius { get; init; }
    public float SlowFactor { get; init; } = 1;

    public int W => Size[0];
    public int H => Size[1];
}

public sealed record UnitDef
{
    public float Hp { get; init; }
    public float Speed { get; init; }
    public float TrainSeconds { get; init; }
    public Cost Cost { get; init; } = Cost.None;
    public double UpkeepGold { get; init; }
    public WeaponDef Weapon { get; init; } = new();

    /// <summary>Hit points a second restored to every other soldier within HealRadius tiles (the Chaplain).</summary>
    public float HealPerSecond { get; init; }
    public float HealRadius { get; init; }

    /// <summary>Can't be trained until this tech is researched.</summary>
    public string? RequiresTech { get; init; }
}

public sealed record DemonDef
{
    public float Hp { get; init; }
    /// <summary>Tiles per second.</summary>
    public float Speed { get; init; }
    public float Damage { get; init; }
    /// <summary>Seconds between attacks.</summary>
    public float Cooldown { get; init; }

    /// <summary>Crosses walls and terrain in a straight line, heading for the nearest building.</summary>
    public bool Flies { get; init; }

    /// <summary>
    /// Bursts, rather than striking, when it reaches a building, and also bursts
    /// when killed: ExplodeDamage to every building and soldier within
    /// ExplodeRadius tiles. Kill it before it reaches the wall, and not in the
    /// middle of your own soldiers.
    /// </summary>
    public float ExplodeDamage { get; init; }
    public float ExplodeRadius { get; init; }

    /// <summary>
    /// Every HowlSeconds, once it's within HowlSight tiles of a building, it
    /// howls: noise of HowlRadius tiles where it stands. Sleeping packs near
    /// the colony wake and join it, so ground left uncleared beside the
    /// walls is a debt that a Howler calls in.
    /// </summary>
    public float HowlRadius { get; init; }
    public float HowlSeconds { get; init; }
    public int HowlSight { get; init; } = 12;

    /// <summary>On death, BroodCount demons of BroodKind crawl out where it fell.</summary>
    public DemonKind BroodKind { get; init; }
    public int BroodCount { get; init; }

    /// <summary>Ranged: from this far it spits SpitDamage at a soldier, or else the nearest building that isn't wall (0: melee only).</summary>
    public float SpitRange { get; init; }
    public float SpitDamage { get; init; }
}

/// <summary>
/// Every content number: buildings, units, demons, economy. Parsed from JSON
/// so balance can be tuned (and, later, modded) without recompiling. The sim
/// does no IO: the default is embedded in this assembly, and a host that
/// wants different rules reads the file itself and passes the text to Parse.
///
/// Engine constants that aren't content (tick rate, crowd physics, flow
/// costs) stay in Balance.
/// </summary>
public sealed class Rules
{
    public Cost StartingResources { get; private init; } = Cost.None;
    public double ColonistGoldPerSecond { get; private init; }
    public double ColonistFoodPerSecond { get; private init; }
    public double RefundFraction { get; private init; }

    /// <summary>Seconds between Thralls leaving a possessed building.</summary>
    public float PossessionSpawnSeconds { get; private init; }

    /// <summary>The garrison a colony starts with, beside the Keep.</summary>
    public StartingUnit[] StartingUnits { get; private init; } = [];

    public SurvivalRules Survival { get; private init; } = new();

    public HellgateRules Hellgates { get; private init; } = new();

    public WildsRules Wilds { get; private init; } = new();

    public WoodsRules Woods { get; private init; } = new();
    public MiningRules Mining { get; private init; } = new();
    public FogRules Fog { get; private init; } = new();
    public RepairRules Repair { get; private init; } = new();

    public TechDef[] Techs { get; private init; } = [];

    public Dictionary<Difficulty, DifficultyDef> Difficulties { get; private init; } = new();

    /// <summary>Endless mode's pool of horde mutations.</summary>
    public CorruptionDef[] Corruptions { get; private init; } = [];

    public CorruptionDef Corruption(string id) => Corruptions.FirstOrDefault(c => c.Id == id) ?? throw new KeyNotFoundException($"no corruption '{id}'");

    /// <summary>The level these rules were scaled to (Normal for rules straight from the file).</summary>
    public Difficulty Difficulty { get; private init; } = Difficulty.Normal;

    /// <summary>These rules at another difficulty. Normal is these rules unchanged; scaling is always from Normal.</summary>
    public Rules ForDifficulty(Difficulty level)
    {
        if (level == Difficulty) return this;
        if (Difficulty != Difficulty.Normal) throw new InvalidOperationException($"rules already scaled to {Difficulty}");
        var d = Difficulties.GetValueOrDefault(level) ?? new DifficultyDef();
        int Round(double v) => Math.Max(1, (int)Math.Round(v));
        var survival = Survival with { FirstWaveSize = Round(Survival.FirstWaveSize * d.Waves), ConvergenceSize = Round(Survival.ConvergenceSize * d.Convergence) };
        var wilds = Wilds with
        {
            NearCount = Round(Wilds.NearCount * d.Packs), FarCount = Round(Wilds.FarCount * d.Packs),
            // Counts may be none (a hand-made map that wants only its own packs): a zero stays a zero.
            Packs = (int)Math.Round(Wilds.Packs * d.PackCount), Strays = (int)Math.Round(Wilds.Strays * d.Strays),
        };
        var gates = Hellgates with { BandSize = Round(Hellgates.BandSize * d.Gates) };
        var start = StartingResources.Scale(d.Start);
        var scaled = Copy(r => { r.Survival = survival; r.Wilds = wilds; r.Hellgates = gates; r.StartingResources = start; });
        return new Rules
        {
            StartingResources = scaled.StartingResources, ColonistGoldPerSecond = scaled.ColonistGoldPerSecond, ColonistFoodPerSecond = scaled.ColonistFoodPerSecond,
            RefundFraction = scaled.RefundFraction, PossessionSpawnSeconds = scaled.PossessionSpawnSeconds, StartingUnits = scaled.StartingUnits,
            Survival = scaled.Survival, Hellgates = scaled.Hellgates, Wilds = scaled.Wilds, Woods = scaled.Woods, Mining = scaled.Mining, Fog = scaled.Fog, Repair = scaled.Repair, Techs = scaled.Techs, Difficulties = Difficulties, Corruptions = Corruptions,
            Buildings = scaled.Buildings, Units = scaled.Units, Demons = scaled.Demons,
            Difficulty = level,
            Hash = scaled.Hash ^ ((ulong)level + 1) * 0x100000001B3UL,
        };
    }

    /// <summary>A tech by id. A loop, not LINQ: research looks its tech up every tick, and a closure a tick adds up.</summary>
    public TechDef Tech(string id)
    {
        foreach (var t in Techs) if (t.Id == id) return t;
        throw new KeyNotFoundException($"no tech '{id}'");
    }

    public BuildingDef[] Buildings { get; private init; } = [];
    public UnitDef[] Units { get; private init; } = [];
    public DemonDef[] Demons { get; private init; } = [];

    /// <summary>FNV-1a of the source text, folded into StateHash: a replay only holds under the same rules.</summary>
    public ulong Hash { get; private init; }

    public BuildingDef this[BuildingKind kind] => Buildings[(int)kind];
    public UnitDef this[UnitKind kind] => Units[(int)kind];
    public DemonDef this[DemonKind kind] => Demons[(int)kind];

    static readonly Lazy<Rules> _default = new(() => Parse(ReadEmbedded())); // thread-safe: tests build worlds in parallel

    /// <summary>The rules shipped in src/Sim/data/rules.json.</summary>
    public static Rules Default => _default.Value;

    sealed class File
    {
        public Cost StartingResources { get; init; } = Cost.None;
        public double ColonistGoldPerSecond { get; init; }
        public double ColonistFoodPerSecond { get; init; }
        public double RefundFraction { get; init; }
        public float PossessionSpawnSeconds { get; init; }
        public StartingUnit[] StartingUnits { get; init; } = [];
        public SurvivalRules Survival { get; init; } = new();
        public HellgateRules Hellgates { get; init; } = new();
        public WildsRules Wilds { get; init; } = new();
        public WoodsRules Woods { get; init; } = new();
        public MiningRules Mining { get; init; } = new();
        public FogRules Fog { get; init; } = new();
        public RepairRules Repair { get; init; } = new();
        public TechDef[] Techs { get; init; } = [];
        public Dictionary<Difficulty, DifficultyDef> Difficulties { get; init; } = new();
        public CorruptionDef[] Corruptions { get; init; } = [];
        public Dictionary<BuildingKind, BuildingDef> Buildings { get; init; } = new();
        public Dictionary<UnitKind, UnitDef> Units { get; init; } = new();
        public Dictionary<DemonKind, DemonDef> Demons { get; init; } = new();
    }

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Rules Parse(string json)
    {
        var file = JsonSerializer.Deserialize<File>(json, Options) ?? throw new FormatException("empty rules");
        return new Rules
        {
            StartingResources = file.StartingResources,
            ColonistGoldPerSecond = file.ColonistGoldPerSecond,
            ColonistFoodPerSecond = file.ColonistFoodPerSecond,
            RefundFraction = file.RefundFraction,
            PossessionSpawnSeconds = file.PossessionSpawnSeconds,
            StartingUnits = file.StartingUnits,
            Survival = file.Survival,
            Hellgates = file.Hellgates,
            Wilds = file.Wilds,
            Woods = file.Woods,
            Mining = file.Mining,
            Fog = file.Fog,
            Repair = file.Repair,
            Techs = file.Techs,
            Difficulties = file.Difficulties,
            Corruptions = file.Corruptions,
            Buildings = Dense(file.Buildings, "building"),
            Units = Dense(file.Units, "unit"),
            Demons = Dense(file.Demons, "demon"),
            Hash = Fnv(json),
        };
    }

    /// <summary>A copy with different starting resources, for probes and scenarios.</summary>
    public Rules WithStartingResources(Cost start) => Copy(r => r.StartingResources = start);

    /// <summary>A copy with one building's definition replaced, for probes (an unkillable Keep for the bench).</summary>
    public Rules WithBuilding(BuildingKind kind, Func<BuildingDef, BuildingDef> change)
    {
        var buildings = (BuildingDef[])Buildings.Clone();
        buildings[(int)kind] = change(buildings[(int)kind]);
        return Copy(r => r.Buildings = buildings);
    }

    /// <summary>A copy with one demon's definition replaced (harmless demons for movement tests).</summary>
    public Rules WithDemon(DemonKind kind, Func<DemonDef, DemonDef> change)
    {
        var demons = (DemonDef[])Demons.Clone();
        demons[(int)kind] = change(demons[(int)kind]);
        return Copy(r => r.Demons = demons);
    }

    /// <summary>A copy with a different corruption pool (one corruption, to test what it does).</summary>
    public Rules WithCorruptions(CorruptionDef[] pool)
    {
        var copy = Copy(_ => { });
        return new Rules
        {
            StartingResources = copy.StartingResources, ColonistGoldPerSecond = copy.ColonistGoldPerSecond, ColonistFoodPerSecond = copy.ColonistFoodPerSecond,
            RefundFraction = copy.RefundFraction, PossessionSpawnSeconds = copy.PossessionSpawnSeconds, StartingUnits = copy.StartingUnits,
            Survival = copy.Survival, Hellgates = copy.Hellgates, Wilds = copy.Wilds, Woods = copy.Woods, Mining = copy.Mining, Fog = copy.Fog, Repair = copy.Repair, Techs = copy.Techs, Difficulties = Difficulties, Corruptions = pool,
            Buildings = copy.Buildings, Units = copy.Units, Demons = copy.Demons, Difficulty = Difficulty,
            Hash = copy.Hash ^ Fnv(JsonSerializer.Serialize(pool, Options)),
        };
    }

    /// <summary>A copy with one unit's definition replaced (no tech requirement, for tests).</summary>
    public Rules WithUnit(UnitKind kind, Func<UnitDef, UnitDef> change)
    {
        var units = (UnitDef[])Units.Clone();
        units[(int)kind] = change(units[(int)kind]);
        return Copy(r => r.Units = units);
    }

    /// <summary>A copy in which no demon does any damage: for testing how the horde moves.</summary>
    public Rules Harmless()
    {
        var demons = Demons.Select(d => d with { Damage = 0, ExplodeDamage = 0 }).ToArray();
        return Copy(r => r.Demons = demons);
    }

    /// <summary>A copy with different Hellgate rules (none, for runs about something else).</summary>
    public Rules WithHellgates(Func<HellgateRules, HellgateRules> change)
    {
        var gates = change(Hellgates);
        return Copy(r => r.Hellgates = gates);
    }

    /// <summary>A copy with different wilds (sleeping packs).</summary>
    /// <summary>A copy with different woods (forest that blocks, and woodsmen).</summary>
    public Rules WithRepair(Func<RepairRules, RepairRules> change)
    {
        var repair = change(Repair);
        return Copy(r => r.Repair = repair);
    }

    public Rules WithFog(Func<FogRules, FogRules> change)
    {
        var fog = change(Fog);
        return Copy(r => r.Fog = fog);
    }

    public Rules WithWoods(Func<WoodsRules, WoodsRules> change)
    {
        var woods = change(Woods);
        return Copy(r => r.Woods = woods);
    }

    public Rules WithMining(Func<MiningRules, MiningRules> change)
    {
        var mining = change(Mining);
        return Copy(r => r.Mining = mining);
    }

    public Rules WithWilds(Func<WildsRules, WildsRules> change)
    {
        var wilds = change(Wilds);
        return Copy(r => r.Wilds = wilds);
    }

    /// <summary>A copy with a different survival schedule (short runs for tests, tuning sweeps).</summary>
    public Rules WithSurvival(Func<SurvivalRules, SurvivalRules> change)
    {
        var survival = change(Survival);
        return Copy(r => r.Survival = survival);
    }

    sealed class Builder
    {
        public Cost StartingResources = Cost.None;
        public BuildingDef[] Buildings = [];
        public UnitDef[] Units = [];
        public DemonDef[] Demons = [];
        public SurvivalRules Survival = new();
        public HellgateRules Hellgates = new();
        public WildsRules Wilds = new();
        public WoodsRules Woods = new();
        public MiningRules Mining = new();
        public FogRules Fog = new();
        public RepairRules Repair = new();
    }

    Rules Copy(Action<Builder> change)
    {
        var b = new Builder { StartingResources = StartingResources, Buildings = Buildings, Units = Units, Demons = Demons, Survival = Survival, Hellgates = Hellgates, Wilds = Wilds, Woods = Woods, Mining = Mining, Fog = Fog, Repair = Repair };
        change(b);
        return new Rules
        {
            StartingResources = b.StartingResources,
            ColonistGoldPerSecond = ColonistGoldPerSecond,
            ColonistFoodPerSecond = ColonistFoodPerSecond,
            RefundFraction = RefundFraction,
            PossessionSpawnSeconds = PossessionSpawnSeconds,
            StartingUnits = StartingUnits,
            Survival = b.Survival,
            Hellgates = b.Hellgates,
            Wilds = b.Wilds,
            Woods = b.Woods,
            Mining = b.Mining,
            Fog = b.Fog,
            Repair = b.Repair,
            Techs = Techs,
            Difficulties = Difficulties,
            Corruptions = Corruptions,
            Difficulty = Difficulty,
            Buildings = b.Buildings,
            Units = b.Units,
            Demons = b.Demons,
            // A modified copy must not share the original's hash.
            Hash = Hash ^ 0x9E3779B97F4A7C15UL ^ Fnv(Describe(b)),
        };
    }

    static string Describe(Builder b) =>
        JsonSerializer.Serialize(new { b.StartingResources, b.Buildings, b.Units, b.Demons, b.Survival, b.Hellgates, b.Wilds, b.Woods, b.Mining, b.Fog, b.Repair }, Options);

    static T[] Dense<TKey, T>(Dictionary<TKey, T> map, string what) where TKey : struct, Enum
    {
        var keys = Enum.GetValues<TKey>();
        var dense = new T[keys.Length];
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var def)) throw new FormatException($"rules: no {what} definition for {key}");
            dense[Convert.ToInt32(key)] = def;
        }
        return dense;
    }

    static string ReadEmbedded()
    {
        using var stream = typeof(Rules).Assembly.GetManifestResourceStream("Hellwall.Sim.rules.json")
            ?? throw new InvalidOperationException("embedded rules.json missing");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    static ulong Fnv(string text)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in text)
        {
            unchecked
            {
                h = (h ^ (byte)c) * 1099511628211UL;
                h = (h ^ (byte)(c >> 8)) * 1099511628211UL;
            }
        }
        return h;
    }
}
