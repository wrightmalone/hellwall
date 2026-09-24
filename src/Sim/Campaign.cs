using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hellwall.Sim;

public enum ObjectiveKind : byte
{
    /// <summary>Hold until the Convergence has broken on the walls (the survival win).</summary>
    Survive,
    /// <summary>Close this many Hellgates.</summary>
    CloseGates,
    /// <summary>Have this many colonists at once.</summary>
    Population,
    /// <summary>Slay this many demons.</summary>
    Slay,
}

public sealed record ObjectiveDef
{
    public ObjectiveKind Kind { get; init; }
    public int Count { get; init; }

    public string Describe() => Kind switch
    {
        ObjectiveKind.Survive => "Survive the Convergence",
        ObjectiveKind.CloseGates => Count == 1 ? "Close a Hellgate" : $"Close {Count} Hellgates",
        ObjectiveKind.Population => $"Grow to {Count} colonists",
        _ => $"Slay {Count} demons",
    };
}

/// <summary>
/// One campaign mission: a map, how hard it is, what you start with, what
/// isn't available yet, and what winning means. Data, in campaign.json; it
/// turns into WorldOptions (rules adjusted from the defaults) with Options().
/// </summary>
public sealed record ScenarioDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Briefing { get; init; } = "";

    public uint Seed { get; init; } = 11;
    public MapKind Map { get; init; } = MapKind.Plains;
    public int MapSize { get; init; } = 256;
    public Difficulty Difficulty { get; init; } = Difficulty.Normal;

    /// <summary>The Convergence lands at the end of this day (0: the rules' own).</summary>
    public int Days { get; init; }
    /// <summary>Regular waves and the Convergence are this many times their rules' size.</summary>
    public double Waves { get; init; } = 1;
    public double Convergence { get; init; } = 1;
    /// <summary>Hellgates on the map (-1: the rules' own).</summary>
    public int Hellgates { get; init; } = -1;
    /// <summary>Sleeping packs on the map (-1: the rules' own).</summary>
    public int Packs { get; init; } = -1;
    /// <summary>Starting stockpile (null: the rules' own).</summary>
    public Cost? Start { get; init; }

    /// <summary>Not available in this mission: the campaign opens the game up as it goes.</summary>
    public BuildingKind[] LockedBuildings { get; init; } = [];
    public UnitKind[] LockedUnits { get; init; } = [];
    public string[] LockedTechs { get; init; } = [];

    /// <summary>All must be done to win. Empty means Survive.</summary>
    public ObjectiveDef[] Objectives { get; init; } = [];

    /// <summary>Missions that must be won first.</summary>
    public string[] Requires { get; init; } = [];
    /// <summary>Where it sits on the campaign map, 0..1 across and down.</summary>
    public float MapX { get; init; }
    public float MapY { get; init; }

    public ObjectiveDef[] Goals => Objectives.Length > 0 ? Objectives : [new ObjectiveDef { Kind = ObjectiveKind.Survive }];

    public bool Locks(BuildingKind kind) => Array.IndexOf(LockedBuildings, kind) >= 0;
    public bool Locks(UnitKind kind) => Array.IndexOf(LockedUnits, kind) >= 0;
    public bool Locks(string tech) => Array.IndexOf(LockedTechs, tech) >= 0;

    /// <summary>The rules this mission plays under: the defaults adjusted, still at Normal (the world applies the difficulty).</summary>
    public Rules RulesFrom(Rules rules)
    {
        var r = rules.WithSurvival(s => s with
        {
            Days = Days > 0 ? Days : s.Days,
            FirstWaveSize = Math.Max(1, (int)Math.Round(s.FirstWaveSize * Waves)),
            ConvergenceSize = Math.Max(1, (int)Math.Round(s.ConvergenceSize * Convergence)),
        });
        if (Hellgates >= 0) r = r.WithHellgates(h => h with { Count = Hellgates });
        if (Packs >= 0) r = r.WithWilds(w => w with { Packs = Packs });
        if (Start != null) r = r.WithStartingResources(Start);
        return r;
    }

    public WorldOptions Options(Rules rules) =>
        new(Seed, MapSize, 0, RulesFrom(rules), Survival: true, Difficulty: Difficulty, Map: Map, Scenario: this);
}

/// <summary>A campaign: missions and the order they open in. The default ships inside the assembly (data/campaign.json).</summary>
public sealed class Campaign
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public ScenarioDef[] Scenarios { get; init; } = [];

    public ScenarioDef? Find(string id) => Scenarios.FirstOrDefault(s => s.Id == id);

    /// <summary>A mission is open once everything it requires has been won.</summary>
    public bool IsOpen(ScenarioDef s, ICollection<string> won) => s.Requires.All(won.Contains);

    static Campaign? _default;

    public static Campaign Default => _default ??= Parse(ReadEmbedded());

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Campaign Parse(string json)
    {
        var c = JsonSerializer.Deserialize<Campaign>(json, Json) ?? throw new FormatException("empty campaign");
        var ids = new HashSet<string>();
        foreach (var s in c.Scenarios)
        {
            if (!ids.Add(s.Id)) throw new FormatException($"campaign: two missions called '{s.Id}'");
            foreach (var r in s.Requires)
                if (c.Scenarios.All(o => o.Id != r)) throw new FormatException($"campaign: '{s.Id}' requires unknown '{r}'");
        }
        return c;
    }

    static string ReadEmbedded()
    {
        using var stream = typeof(Campaign).Assembly.GetManifestResourceStream("Hellwall.Sim.campaign.json")
            ?? throw new InvalidOperationException("embedded campaign.json missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// A mission's goals, checked twice a second: done stays done (a population
/// reached once counts). All done wins; still undone when the Convergence is
/// spent loses, so every mission ends.
/// </summary>
internal static class ObjectiveSystem
{
    public static void Step(World world)
    {
        if (world.Outcome != Outcome.Running || world.Tick % 10 != 0) return;
        var goals = world.Goals;
        var done = world.GoalsDone;
        if (goals.Length == 0) return;
        bool all = true;
        for (int i = 0; i < goals.Length; i++)
        {
            if (!done[i] && Met(world, goals[i]))
            {
                done[i] = true;
                world.Emit(new ObjectiveCompleted(world.Tick, i, goals[i].Kind));
            }
            all &= done[i];
        }
        if (all) world.Win();
        // The Convergence is the deadline: once it has broken on the walls, whatever is still undone never will be.
        else if (world.Survival is { ConvergenceSpent: true }) world.Lose();
    }

    static bool Met(World world, ObjectiveDef goal) => goal.Kind switch
    {
        ObjectiveKind.Survive => world.Survival is { ConvergenceSpent: true },
        ObjectiveKind.CloseGates => world.Gates.Count(g => !g.Alive) >= goal.Count,
        ObjectiveKind.Population => world.Colony.Colonists >= goal.Count,
        _ => world.Stats.DemonsKilled >= goal.Count,
    };
}
