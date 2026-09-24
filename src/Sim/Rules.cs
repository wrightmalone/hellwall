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
}

/// <summary>An amount of each resource. Used for costs and for the colony's stockpile.</summary>
public sealed class Cost
{
    public double Gold { get; init; }
    public double Wood { get; init; }
    public double Stone { get; init; }
    public double Food { get; init; }
    public double Iron { get; init; }

    public static readonly Cost None = new();

    public double this[Resource r] => r switch
    {
        Resource.Gold => Gold,
        Resource.Wood => Wood,
        Resource.Stone => Stone,
        Resource.Food => Food,
        _ => Iron,
    };

    public Cost Scale(double f) => new() { Gold = Gold * f, Wood = Wood * f, Stone = Stone * f, Food = Food * f, Iron = Iron * f };

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
    /// <summary>A soldier this close wakes a sleeping pack.</summary>
    public float WakeRadius { get; init; } = 7;
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

    public TechDef[] Techs { get; private init; } = [];

    public TechDef Tech(string id) => Techs.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException($"no tech '{id}'");

    public BuildingDef[] Buildings { get; private init; } = [];
    public UnitDef[] Units { get; private init; } = [];
    public DemonDef[] Demons { get; private init; } = [];

    /// <summary>FNV-1a of the source text, folded into StateHash: a replay only holds under the same rules.</summary>
    public ulong Hash { get; private init; }

    public BuildingDef this[BuildingKind kind] => Buildings[(int)kind];
    public UnitDef this[UnitKind kind] => Units[(int)kind];
    public DemonDef this[DemonKind kind] => Demons[(int)kind];

    static Rules? _default;

    /// <summary>The rules shipped in src/Sim/data/rules.json.</summary>
    public static Rules Default => _default ??= Parse(ReadEmbedded());

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
        public TechDef[] Techs { get; init; } = [];
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
            Techs = file.Techs,
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
    }

    Rules Copy(Action<Builder> change)
    {
        var b = new Builder { StartingResources = StartingResources, Buildings = Buildings, Units = Units, Demons = Demons, Survival = Survival, Hellgates = Hellgates, Wilds = Wilds };
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
            Techs = Techs,
            Buildings = b.Buildings,
            Units = b.Units,
            Demons = b.Demons,
            // A modified copy must not share the original's hash.
            Hash = Hash ^ 0x9E3779B97F4A7C15UL ^ Fnv(Describe(b)),
        };
    }

    static string Describe(Builder b) =>
        JsonSerializer.Serialize(new { b.StartingResources, b.Buildings, b.Units, b.Demons, b.Survival, b.Hellgates, b.Wilds }, Options);

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
