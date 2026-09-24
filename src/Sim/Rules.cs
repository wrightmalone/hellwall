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
}

public enum UnitKind : byte
{
    Militia,
    Marksman,
    Templar,
}

/// <summary>An amount of each resource. Used for costs and for the colony's stockpile.</summary>
public sealed class Cost
{
    public double Gold { get; init; }
    public double Wood { get; init; }
    public double Stone { get; init; }
    public double Food { get; init; }

    public static readonly Cost None = new();

    public double this[Resource r] => r switch
    {
        Resource.Gold => Gold,
        Resource.Wood => Wood,
        Resource.Stone => Stone,
        _ => Food,
    };

    public Cost Scale(double f) => new() { Gold = Gold * f, Wood = Wood * f, Stone = Stone * f, Food = Food * f };

    public override string ToString()
    {
        var parts = new List<string>();
        foreach (var r in Enum.GetValues<Resource>())
            if (this[r] > 0) parts.Add($"{this[r]:0} {r.ToString().ToLowerInvariant()}");
        return parts.Count == 0 ? "free" : string.Join(", ", parts);
    }
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
}

public sealed record DemonDef
{
    public float Hp { get; init; }
    /// <summary>Tiles per second.</summary>
    public float Speed { get; init; }
    public float Damage { get; init; }
    /// <summary>Seconds between attacks.</summary>
    public float Cooldown { get; init; }
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

    public SurvivalRules Survival { get; private init; } = new();

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
        public SurvivalRules Survival { get; init; } = new();
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
            Survival = file.Survival,
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

    /// <summary>A copy in which no demon does any damage: for testing how the horde moves.</summary>
    public Rules Harmless()
    {
        var demons = Demons.Select(d => d with { Damage = 0 }).ToArray();
        return Copy(r => r.Demons = demons);
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
        public DemonDef[] Demons = [];
        public SurvivalRules Survival = new();
    }

    Rules Copy(Action<Builder> change)
    {
        var b = new Builder { StartingResources = StartingResources, Buildings = Buildings, Demons = Demons, Survival = Survival };
        change(b);
        return new Rules
        {
            StartingResources = b.StartingResources,
            ColonistGoldPerSecond = ColonistGoldPerSecond,
            ColonistFoodPerSecond = ColonistFoodPerSecond,
            RefundFraction = RefundFraction,
            PossessionSpawnSeconds = PossessionSpawnSeconds,
            Survival = b.Survival,
            Buildings = b.Buildings,
            Units = Units,
            Demons = b.Demons,
            // A modified copy must not share the original's hash.
            Hash = Hash ^ 0x9E3779B97F4A7C15UL ^ Fnv(Describe(b)),
        };
    }

    static string Describe(Builder b) =>
        JsonSerializer.Serialize(new { b.StartingResources, b.Buildings, b.Demons, b.Survival }, Options);

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
