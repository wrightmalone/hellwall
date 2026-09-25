namespace Hellwall.Sim;

/// <summary>
/// One change a tech makes. It targets a building kind, a unit kind, or a
/// group, and multiplies then adds to one stat. A modifier with no target is
/// colony-wide ("colonistGold", "holyGroundDps").
///
/// Groups: "towers" (anything with a weapon), "walls", "gatherers", "food",
/// "units". Building stats: hp, damage, range, cooldown, splash, rate,
/// consecrateRadius, sanctitySupply, buildSeconds. Unit stats: hp, damage,
/// range, cooldown, trainSeconds, speed, upkeep.
/// </summary>
public sealed record TechModifier
{
    public BuildingKind? Building { get; init; }
    public UnitKind? Unit { get; init; }
    public string? Group { get; init; }
    public string Stat { get; init; } = "";
    public double Mul { get; init; } = 1;
    public double Add { get; init; }
}

public sealed record TechDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int Tier { get; init; } = 1;
    public Cost Cost { get; init; } = Cost.None;
    public float Seconds { get; init; } = 30;

    /// <summary>All of these must be researched first.</summary>
    public string[] Requires { get; init; } = [];

    /// <summary>Researching either one of an exclusive pair locks the other out for the run.</summary>
    public string? ExclusiveWith { get; init; }

    public BuildingKind[] UnlocksBuildings { get; init; } = [];
    public UnitKind[] UnlocksUnits { get; init; } = [];
    public TechModifier[] Modifiers { get; init; } = [];
    /// <summary>A patron saint's blessing: never researched, only chosen when the colony reaches a milestone (PatronSystem).</summary>
    public bool Patron { get; init; }
}

/// <summary>
/// What the colony has learned, and what that does to every building and
/// unit definition. The sim reads effective definitions (World.Def), never
/// the base rules, so a researched modifier reaches everything built before
/// and after it.
/// </summary>
public sealed class TechState
{
    /// <summary>In the order they finished.</summary>
    public readonly List<string> Researched = new();

    public BuildingDef[] Buildings = [];
    public UnitDef[] Units = [];
    public double ColonistGoldMultiplier = 1;
    public float HolyGroundDps;

    public bool Has(string id) => Researched.Contains(id);

    /// <summary>Recompute effective definitions from the base rules and everything researched, in order.</summary>
    public void Recompute(Rules rules)
    {
        var b = (BuildingDef[])rules.Buildings.Clone();
        var u = (UnitDef[])rules.Units.Clone();
        double gold = 1;
        float fire = 0;
        foreach (var id in Researched)
        {
            var tech = rules.Tech(id);
            foreach (var m in tech.Modifiers)
            {
                if (m.Building == null && m.Unit == null && m.Group == null)
                {
                    if (m.Stat == "colonistGold") gold = gold * m.Mul + m.Add;
                    else if (m.Stat == "holyGroundDps") fire = (float)(fire * m.Mul + m.Add);
                    continue;
                }
                for (int k = 0; k < b.Length; k++)
                    if (Matches(m, (BuildingKind)k, b[k])) b[k] = Apply(b[k], m);
                for (int k = 0; k < u.Length; k++)
                    if (m.Unit == (UnitKind)k || m.Group == "units") u[k] = Apply(u[k], m);
            }
        }
        Buildings = b;
        Units = u;
        ColonistGoldMultiplier = gold;
        HolyGroundDps = fire;
    }

    static bool Matches(TechModifier m, BuildingKind kind, BuildingDef def) =>
        m.Building == kind || m.Group switch
        {
            "towers" => def.Weapon != null,
            "walls" => kind is BuildingKind.Wall or BuildingKind.Gate or BuildingKind.StoneWall or BuildingKind.StoneGate,
            "gatherers" => def.Produces != null,
            "food" => def.Produces == Resource.Food,
            _ => false,
        };

    static float F(float v, TechModifier m) => (float)(v * m.Mul + m.Add);

    static BuildingDef Apply(BuildingDef d, TechModifier m) => m.Stat switch
    {
        "hp" => d with { Hp = F(d.Hp, m) },
        "buildSeconds" => d with { BuildSeconds = F(d.BuildSeconds, m) },
        "rate" => d with { PerTile = d.PerTile * m.Mul + m.Add },
        "consecrateRadius" => d with { ConsecrateRadius = F(d.ConsecrateRadius, m) },
        "sanctitySupply" => d with { SanctitySupply = F(d.SanctitySupply, m) },
        "damage" or "range" or "cooldown" or "splash" when d.Weapon != null => d with { Weapon = Apply(d.Weapon, m) },
        _ => d,
    };

    static UnitDef Apply(UnitDef d, TechModifier m) => m.Stat switch
    {
        "hp" => d with { Hp = F(d.Hp, m) },
        "speed" => d with { Speed = F(d.Speed, m) },
        "trainSeconds" => d with { TrainSeconds = F(d.TrainSeconds, m) },
        "upkeep" => d with { UpkeepGold = d.UpkeepGold * m.Mul + m.Add },
        "damage" or "range" or "cooldown" or "splash" => d with { Weapon = Apply(d.Weapon, m) },
        _ => d,
    };

    static WeaponDef Apply(WeaponDef w, TechModifier m) => m.Stat switch
    {
        "damage" => w with { Damage = F(w.Damage, m) },
        "range" => w with { Range = F(w.Range, m) },
        "cooldown" => w with { Cooldown = F(w.Cooldown, m) },
        "splash" => w with { Splash = F(w.Splash, m) },
        _ => w,
    };
}
