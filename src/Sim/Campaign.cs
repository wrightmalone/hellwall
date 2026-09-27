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
    /// <summary>Take the loot from this many ruins.</summary>
    LootRuins,
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
        ObjectiveKind.LootRuins => Count == 1 ? "Loot a ruin" : $"Loot {Count} ruins",
        _ => $"Slay {Count} demons",
    };
}

/// <summary>
/// A scripted moment in a mission: when it fires (at the start of Day, or
/// once goal AfterGoal is met), and what happens: a message, demons at a map
/// edge, resources given. Each fires once.
/// </summary>
public sealed record TriggerDef
{
    /// <summary>Fires when this day begins (0: not on a day).</summary>
    public int Day { get; init; }
    /// <summary>Fires once this goal (an index into the mission's goals) is met (-1: not on a goal).</summary>
    public int AfterGoal { get; init; } = -1;
    public string Say { get; init; } = "";
    public DemonKind SpawnKind { get; init; } = DemonKind.Imp;
    public int SpawnCount { get; init; }
    public Side SpawnSide { get; init; } = Side.North;
    public Cost? Give { get; init; }
    /// <summary>Who says it: a speaker's id from the campaign (empty: the campaign's first speaker).</summary>
    public string Speaker { get; init; } = "";
}

/// <summary>A sleeping pack put down by hand: its centre tile, its size and kind.</summary>
public sealed record PlacedPack(int X, int Y, int Count, DemonKind Kind = DemonKind.Imp);

/// <summary>A Hellgate put down by hand: its top-left tile (a gate is Hellgate.Size square).</summary>
public sealed record PlacedGate(int X, int Y);

/// <summary>A voice of the campaign: a name and a portrait (a unit or building kind's picture, until there's art for faces).</summary>
public sealed record SpeakerDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Portrait { get; init; } = "Keep";
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
    /// <summary>Straggler groups (-1: the rules' own).</summary>
    public int Strays { get; init; } = -1;
    /// <summary>Ruins to loot (-1: the rules' own).</summary>
    public int Ruins { get; init; } = -1;
    /// <summary>Forest is a wall and woodsmen fell it (woods.blocks).</summary>
    public bool LivingWoods { get; init; }
    public bool Fog { get; init; } = true;
    /// <summary>A hand-made map: every tile, one byte each (a Tile), row by row, base64. Empty: generated from Seed and Map.</summary>
    public string Tiles { get; init; } = "";
    /// <summary>Endless: no Convergence, no win; the score is the day (skirmish only).</summary>
    public bool Endless { get; init; }
    /// <summary>Hand-placed sleeping packs (a map editor's), besides any scattered at random (Packs).</summary>
    public PlacedPack[] PlacedPacks { get; init; } = [];
    /// <summary>The sides waves may come from in this mission (empty: whatever its kind of map allows).</summary>
    public Side[] WaveSides { get; init; } = [];

    /// <summary>Hand-placed Hellgates: when there are any, they're the map's gates and none are placed at random.</summary>
    public PlacedGate[] PlacedGates { get; init; } = [];

    /// <summary>Not available in this mission: the campaign opens the game up as it goes.</summary>
    public BuildingKind[] LockedBuildings { get; init; } = [];
    public UnitKind[] LockedUnits { get; init; } = [];
    public string[] LockedTechs { get; init; } = [];

    /// <summary>All must be done to win. Empty means Survive.</summary>
    public ObjectiveDef[] Objectives { get; init; } = [];

    /// <summary>Scripted moments: messages, surprise attacks, relief.</summary>
    public TriggerDef[] Triggers { get; init; } = [];

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
        if (Strays >= 0) r = r.WithWilds(w => w with { Strays = Strays });
        if (Ruins >= 0) r = r.WithWilds(w => w with { Ruins = Ruins });
        if (LivingWoods) r = r.WithWoods(w => w with { Blocks = true });
        if (!Fog) r = r.WithFog(f => f with { Enabled = false });
        return r;
    }

    public WorldOptions Options(Rules rules) =>
        new(Seed, MapSize, 0, RulesFrom(rules), Survival: true, Difficulty: Difficulty, Endless: Endless, Map: Map, Scenario: this);

    /// <summary>The hand-made map's tiles, or null for a generated one.</summary>
    public Tile[]? DecodeTiles()
    {
        if (Tiles.Length == 0) return null;
        var bytes = Convert.FromBase64String(Tiles);
        if (bytes.Length != MapSize * MapSize) throw new FormatException($"map '{Id}' has {bytes.Length} tiles, not {MapSize}x{MapSize}");
        return Array.ConvertAll(bytes, b => (Tile)b);
    }

    public static string EncodeTiles(Tile[] tiles) => Convert.ToBase64String(Array.ConvertAll(tiles, t => (byte)t));

    /// <summary>
    /// Everything that will come at you, said before you begin: where the waves come from, the
    /// Hellgates, when and how big the Convergence is, and every raid the mission holds. No
    /// mission hides a spawn you can only learn about by losing to it.
    /// </summary>
    public IEnumerable<string> Threats(Rules rules)
    {
        var r = RulesFrom(rules).ForDifficulty(Difficulty);
        var sides = WaveSides.Length > 0 ? WaveSides : MapGen.WaveSides(Map);
        string Names(IEnumerable<Side> s) => string.Join(" and ", s.Select(x => x.ToString().ToLowerInvariant()));
        yield return sides.Length == 4 ? "Waves from any side, one or two at a time, each told a minute ahead" : $"Waves only from the {Names(sides)}, each told a minute ahead";
        if (r.Hellgates.Count > 0) yield return $"{r.Hellgates.Count} Hellgate{(r.Hellgates.Count == 1 ? "" : "s")} feeding the waves (close them and the waves shrink)";
        if (!Endless)
        {
            int size = (int)Math.Round(r.Survival.ConvergenceSize / 100.0) * 100;
            yield return $"The Convergence on day {r.Survival.Days}: about {size:N0}, most from one side, told ten minutes ahead";
        }
        foreach (var t in Triggers.Where(t => t.SpawnCount > 0))
        {
            string when = t.Day > 0 ? $"on day {t.Day}" : t.AfterGoal >= 0 && t.AfterGoal < Goals.Length ? $"once you've done this: {Goals[t.AfterGoal].Describe().ToLowerInvariant()}" : "during the mission";
            yield return $"{t.SpawnCount} {t.SpawnKind}s from the {t.SpawnSide.ToString().ToLowerInvariant()} {when}, told {Campaign.RaidLeadSeconds:0} seconds ahead";
        }
    }

    /// <summary>Map sizes the game makes: a shared map must be one of them.</summary>
    public static readonly int[] MapSizes = [128, 192, 256, 320];

    /// <summary>
    /// What's wrong with a map someone else made (an imported file), or null if it's fit to play:
    /// a known size, its tiles all there and all real tiles, and nothing out of all proportion
    /// (a doctored file mustn't be able to put a million demons on the map or run for ever).
    /// </summary>
    public string? Problem()
    {
        if (Name.Length > 60 || Briefing.Length > 4000) return "its name or briefing is too long";
        if (Array.IndexOf(MapSizes, MapSize) < 0) return $"it's {MapSize} tiles across, which isn't a size the game makes";
        if (!Enum.IsDefined(Map) || !Enum.IsDefined(Difficulty) || WaveSides.Any(s => !Enum.IsDefined(s))) return "it names a kind of map, difficulty or side this build doesn't have";
        if (Days is < 0 or > 400 || Waves is < 0 or > 20 || Convergence is < 0 or > 20) return "its days or wave sizes are out of range";
        if (Hellgates is < -1 or > 16 || Packs is < -1 or > 5000 || Strays is < -1 or > 5000 || Ruins is < -1 or > 64) return "it asks for too many gates, packs, strays or ruins";
        if (Start != null && Enum.GetValues<Resource>().Any(r => Start[r] is < 0 or > 1_000_000 || double.IsNaN(Start[r]))) return "its starting stock is out of range";
        if (PlacedPacks.Length > 2000 || PlacedPacks.Any(p => p.Count is < 1 or > 1000 || !Enum.IsDefined(p.Kind) || p.X < 0 || p.Y < 0 || p.X >= MapSize || p.Y >= MapSize)) return "its hand-placed packs are out of range";
        if (PlacedGates.Length > 16 || PlacedGates.Any(g => g.X < 0 || g.Y < 0 || g.X >= MapSize || g.Y >= MapSize)) return "its hand-placed Hellgates are out of range";
        if (Tiles.Length > 0)
        {
            Tile[]? tiles;
            try { tiles = DecodeTiles(); }
            catch (FormatException e) { return e.Message; }
            if (tiles!.Any(t => !Enum.IsDefined(t))) return "it has tiles this build doesn't know";
        }
        return null;
    }

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this, Campaign.Json);
    public static ScenarioDef FromJson(string json) => System.Text.Json.JsonSerializer.Deserialize<ScenarioDef>(json, Campaign.Json) ?? throw new FormatException("empty scenario");
}

/// <summary>A campaign: missions and the order they open in. The default ships inside the assembly (data/campaign.json).</summary>
public sealed class Campaign
{
    /// <summary>A mission's raid is told this long before it comes: never a surprise you only learn by losing to it.</summary>
    public const float RaidLeadSeconds = 45;

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public ScenarioDef[] Scenarios { get; init; } = [];
    public SpeakerDef[] Speakers { get; init; } = [];

    public ScenarioDef? Find(string id) => Scenarios.FirstOrDefault(s => s.Id == id);

    /// <summary>One of this campaign's own missions (not a skirmish or a hand-made map that happens to share an id).</summary>
    public bool Contains(ScenarioDef? s) => s != null && ReferenceEquals(Find(s.Id), s);

    /// <summary>A speaker by id; the first speaker (the narrator) for an empty or unknown id.</summary>
    public SpeakerDef Speaker(string id) => Speakers.FirstOrDefault(s => s.Id == id) ?? Speakers.FirstOrDefault() ?? new SpeakerDef { Name = "" };

    /// <summary>A mission is open once everything it requires has been won.</summary>
    public bool IsOpen(ScenarioDef s, ICollection<string> won) => s.Requires.All(won.Contains);

    static readonly Lazy<Campaign> _default = new(() => Parse(ReadEmbedded())); // thread-safe, as Rules.Default

    public static Campaign Default => _default.Value;

    internal static readonly JsonSerializerOptions Json = new()
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
            foreach (var t in s.Triggers)
                if (t.Speaker.Length > 0 && c.Speakers.All(p => p.Id != t.Speaker)) throw new FormatException($"campaign: '{s.Id}' has a line for unknown speaker '{t.Speaker}'");
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
        Fire(world);
        if (all) world.Win();
        // The Convergence is the deadline: once it has broken on the walls, whatever is still undone never will be.
        else if (world.Survival is { ConvergenceSpent: true }) world.Lose();
    }

    /// <summary>A mission's triggers whose moment has come, each once; and the raids they told of, when theirs has.</summary>
    static void Fire(World world)
    {
        if (world.Scenario is not { } s) return;
        var fired = world.TriggersFired;
        for (int i = 0; i < s.Triggers.Length; i++)
        {
            var t = s.Triggers[i];
            if (world.RaidDue[i] > 0 && world.Tick >= world.RaidDue[i])
            {
                world.RaidDue[i] = -1;
                world.SpawnColumn = Horde.ColumnOf(1000 + i, t.SpawnSide);
                int spawned = world.SpawnAtEdge(t.SpawnSide, t.SpawnKind, t.SpawnCount);
                world.SpawnColumn = 0;
                world.Emit(new RaidLanded(world.Tick, i, spawned, t.SpawnSide, t.SpawnKind));
            }
            if (fired[i]) continue;
            bool due = (t.Day > 0 && world.Day >= t.Day) || (t.AfterGoal >= 0 && t.AfterGoal < world.GoalsDone.Length && world.GoalsDone[t.AfterGoal]);
            if (!due) continue;
            fired[i] = true;
            if (t.SpawnCount > 0) world.RaidDue[i] = world.Tick + (int)(Campaign.RaidLeadSeconds * Balance.TickHz);
            if (t.Give != null) world.Colony.Refund(t.Give, 1);
            world.Emit(new ScenarioMessage(world.Tick, i, t.Say, t.SpawnCount, t.SpawnSide, t.Speaker, t.SpawnKind));
        }
    }

    static bool Met(World world, ObjectiveDef goal) => goal.Kind switch
    {
        ObjectiveKind.Survive => world.Survival is { ConvergenceSpent: true },
        ObjectiveKind.CloseGates => world.Gates.Count(g => !g.Alive) >= goal.Count,
        ObjectiveKind.Population => world.Colony.Colonists >= goal.Count,
        ObjectiveKind.LootRuins => world.Ruins.Count(r => r.Looted) >= goal.Count,
        _ => world.Stats.DemonsKilled >= goal.Count,
    };
}
