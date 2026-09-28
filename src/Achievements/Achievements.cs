using System.Text.Json;
using System.Text.Json.Serialization;
using Hellwall.Sim;

namespace Hellwall.Achievements;

/// <summary>What earns an achievement: each a rule in AchievementTracker (docs/plans/steam.md).</summary>
public enum AchievementKind
{
    MissionWon, RelicWon, RelicHallowed, AllHallowed, FinaleHallowed,
    SurvivalWon, LongSurvival, EndlessDay,
    NoBuildingsLost, WildsCleared, EarlyGate, AllGates, HolyGround, Colonists, RuinsLooted, AftermathCleared,
    DemonsSlain,
    MapMade, Published, WellReceived,
}

/// <summary>One achievement: its id (the API name in Steamworks), what it says, and its rule's parameters.</summary>
public sealed record AchievementDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public AchievementKind Kind { get; init; }
    public string Mission { get; init; } = "";
    public Difficulty Difficulty { get; init; }
    public long Count { get; init; }
}

/// <summary>What a run is, beyond its world: whether it counts at all, and what kind it is.</summary>
/// <param name="Eligible">Nothing counts with the bot playing, in the menu's backdrop, or once a debug key is used.</param>
/// <param name="HandMade">A hand-made map: only the map-maker's achievement (a custom map mustn't farm the rest).</param>
/// <param name="Campaign">One of the campaign's own missions.</param>
public readonly record struct RunInfo(bool Eligible, bool HandMade, bool Campaign);

/// <summary>What the campaign holds, for its achievements: relics won and hallowed, and those taken into this mission.</summary>
public readonly record struct CampaignFacts(int RelicsOwned, int RelicsHallowed, int RelicsInCampaign, int HallowedTaken);

/// <summary>
/// Which achievements a player has earned, and the rules for earning more: pure C#, so the tests can
/// play them against scripted worlds, and the game (Achievements.cs in game/) keeps them, shows
/// them, and hands them to Steam. Watch() during a run (about once a second), Ended() when it's won
/// or lost, Grant() for what happens outside a run (a relic won, a map made, a mission published).
/// Each returns what it newly earned.
/// </summary>
public sealed class AchievementTracker
{
    public IReadOnlyList<AchievementDef> Defs { get; }
    public HashSet<string> Earned { get; } = new();
    /// <summary>Demons slain over every counting run (the Steam stat demons_slain).</summary>
    public long DemonsSlain { get; set; }

    // The run being watched: its kills so far (for the total), and whether it had wilds to clear.
    World? _run;
    int _killsCounted;
    bool _hadSleepers;

    public AchievementTracker(IReadOnlyList<AchievementDef> defs) => Defs = defs;

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>The list from achievements.json's text.</summary>
    public static List<AchievementDef> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = doc.RootElement.GetProperty("achievements").Deserialize<List<AchievementDef>>(Json) ?? [];
        var ids = new HashSet<string>();
        foreach (var a in list)
            if (!ids.Add(a.Id) || a.Id.Length == 0 || a.Id.Any(c => !(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_')))
                throw new FormatException($"achievements: id '{a.Id}' is repeated or not A-Z, 0-9 and _");
        return list;
    }

    List<AchievementDef> Earn(Func<AchievementDef, bool> met)
    {
        var fresh = new List<AchievementDef>();
        foreach (var a in Defs)
            if (!Earned.Contains(a.Id) && met(a) && Earned.Add(a.Id)) fresh.Add(a);
        return fresh;
    }

    /// <summary>During a run: what can be earned mid-run (colonists, holy ground, gates, the wilds cleared, Endless days, the kill total).</summary>
    public List<AchievementDef> Watch(World world, RunInfo run)
    {
        if (!run.Eligible || run.HandMade) return [];
        if (!ReferenceEquals(_run, world))
        {
            _run = world;
            _killsCounted = world.Stats.DemonsKilled; // a loaded save's kills were counted when they happened
            _hadSleepers = world.Sleeping > 0;
        }
        DemonsSlain += world.Stats.DemonsKilled - _killsCounted;
        _killsCounted = world.Stats.DemonsKilled;
        var s = world.Survival;
        int holy = world.Colony.Consecrated.Count(c => c);
        bool gatesClosed = world.Gates.Count > 0 && world.Gates.All(g => !g.Alive);
        return Earn(a => a.Kind switch
        {
            AchievementKind.DemonsSlain => DemonsSlain >= a.Count,
            AchievementKind.Colonists => world.Colony.Colonists >= a.Count,
            AchievementKind.HolyGround => holy >= a.Count,
            AchievementKind.RuinsLooted => world.Ruins.Count(r => r.Looted) >= a.Count,
            AchievementKind.EarlyGate => world.Day < a.Count && world.Gates.Any(g => !g.Alive),
            AchievementKind.AllGates => gatesClosed,
            AchievementKind.EndlessDay => s is { Endless: true } && world.Day >= a.Count,
            AchievementKind.WildsCleared => _hadSleepers && world.Sleeping == 0 && s is { Endless: false, FinalLanded: false },
            AchievementKind.AftermathCleared => world.Aftermath && world.Horde.Count == 0 && world.Sleeping == 0,
            _ => false,
        });
    }

    /// <summary>A run won or lost: what winning it earns (the campaign's facts, if it's a campaign mission).</summary>
    public List<AchievementDef> Ended(World world, RunInfo run, CampaignFacts? campaign = null)
    {
        if (!run.Eligible || run.HandMade) return [];
        var fresh = Watch(world, run);
        if (world.Outcome != Outcome.Won) return fresh;
        var s = world.Survival;
        bool survival = !run.Campaign && s is { Endless: false };
        fresh.AddRange(Earn(a => a.Kind switch
        {
            AchievementKind.MissionWon => run.Campaign && world.Scenario?.Id == a.Mission,
            AchievementKind.SurvivalWon => survival && world.Rules.Difficulty >= a.Difficulty, // a harder win earns the easier ones too
            AchievementKind.LongSurvival => survival && world.Rules.Survival.Days >= a.Count,
            AchievementKind.NoBuildingsLost => world.Stats.StructuresLost == 0,
            AchievementKind.FinaleHallowed => run.Campaign && world.Scenario?.Id == "hellwall" && campaign is { HallowedTaken: >= 3 },
            _ => false,
        }));
        if (campaign is { } c) fresh.AddRange(Campaign(c));
        return fresh;
    }

    /// <summary>The campaign's relics, however they came (a win, or checked at launch).</summary>
    public List<AchievementDef> Campaign(CampaignFacts c) => Earn(a => a.Kind switch
    {
        AchievementKind.RelicWon => c.RelicsOwned > 0,
        AchievementKind.RelicHallowed => c.RelicsHallowed > 0,
        AchievementKind.AllHallowed => c.RelicsInCampaign > 0 && c.RelicsHallowed >= c.RelicsInCampaign,
        _ => false,
    });

    /// <summary>Something outside a run: a map of one's own played, a mission published, its thumbs up (count).</summary>
    public List<AchievementDef> Grant(AchievementKind kind, long count = 0) => Earn(a => a.Kind == kind && count >= a.Count);
}
