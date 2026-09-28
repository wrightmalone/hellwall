using Hellwall.Achievements;
using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class AchievementTests
{
    /// <summary>The game's own list (game/data/achievements.json), found from the test's folder up.</summary>
    static readonly List<AchievementDef> Defs = AchievementTracker.Parse(File.ReadAllText(FindList()));

    static string FindList()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "game", "data", "achievements.json");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("game/data/achievements.json");
    }

    static readonly RunInfo Counts = new(Eligible: true, HandMade: false, Campaign: false);

    static World Survival(Difficulty difficulty = Difficulty.Normal) =>
        World.Create(new WorldOptions(7, 128, 0, Rules.Default.Harmless(), Survival: true, Difficulty: difficulty));

    [Fact]
    public void TheListParsesAndCoversEveryMission()
    {
        Assert.True(Defs.Count >= 30);
        foreach (var mission in Campaign.Default.Scenarios)
            Assert.Contains(Defs, a => a.Kind == AchievementKind.MissionWon && a.Mission == mission.Id);
        Assert.All(Defs, a => Assert.NotEqual("", a.Description));
    }

    [Fact]
    public void ASurvivalWinEarnsItsDifficultyAndEveryEasierOne()
    {
        var t = new AchievementTracker(Defs);
        var world = Survival(Difficulty.Hard);
        world.Win();
        var earned = t.Ended(world, Counts).Select(a => a.Id).ToList();
        Assert.Contains("ENDURE_EASY", earned);
        Assert.Contains("ENDURE_NORMAL", earned);
        Assert.Contains("ENDURE_HARD", earned);
        Assert.DoesNotContain("ENDURE_NIGHTMARE", earned);
        Assert.Contains("NOT_ONE_STONE", earned);
        Assert.Empty(t.Ended(world, Counts)); // earned once
    }

    [Fact]
    public void NothingCountsWithTheBotOrCheatsOrOnAHandMadeMap()
    {
        var world = Survival();
        world.Win();
        Assert.Empty(new AchievementTracker(Defs).Ended(world, Counts with { Eligible = false }));
        Assert.Empty(new AchievementTracker(Defs).Ended(world, Counts with { HandMade = true }));
        var t = new AchievementTracker(Defs);
        Assert.Contains(t.Grant(AchievementKind.MapMade), a => a.Id == "CARTOGRAPHER");
    }

    [Fact]
    public void FeatsAreEarnedMidRun()
    {
        var t = new AchievementTracker(Defs);
        var world = Survival();
        world.Colony.Colonists = 1000;
        var earned = t.Watch(world, Counts).Select(a => a.Id).ToList();
        Assert.Contains("THOUSAND_SOULS", earned);
        Assert.DoesNotContain("NO_GATE_STANDS", earned);
        foreach (var g in world.Gates) g.Hp = 0;
        earned = t.Watch(world, Counts).Select(a => a.Id).ToList();
        Assert.Contains("NO_GATE_STANDS", earned);
        Assert.Contains("GATECRASHER", earned); // day 1
    }

    [Fact]
    public void KillsAddUpOverRunsButALoadedSavesDontCountTwice()
    {
        var t = new AchievementTracker(Defs);
        var first = Survival();
        t.Watch(first, Counts);
        first.Stats.DemonsKilled = 6000;
        t.Watch(first, Counts);
        var second = Survival();
        second.Stats.DemonsKilled = 99999; // a save loaded with kills already counted
        t.Watch(second, Counts);
        second.Stats.DemonsKilled += 4000;
        var earned = t.Watch(second, Counts).Select(a => a.Id).ToList();
        Assert.Equal(10000, t.DemonsSlain);
        Assert.Contains("SLAYER", earned);
    }

    [Fact]
    public void TheCampaignsRelicsAndMissions()
    {
        var t = new AchievementTracker(Defs);
        var mission = Campaign.Default.Find("first-night")!;
        var world = World.Create(mission.Options(Rules.Default.Harmless()));
        world.Win();
        var earned = t.Ended(world, Counts with { Campaign = true }, new CampaignFacts(1, 1, 11, 0)).Select(a => a.Id).ToList();
        Assert.Contains("MISSION_FIRST_NIGHT", earned);
        Assert.Contains("RELIC_BEARER", earned);
        Assert.Contains("HALLOWED", earned);
        Assert.DoesNotContain("FULL_RELIQUARY", earned);
        Assert.DoesNotContain("ENDURE_EASY", earned); // a campaign win isn't a survival run
        Assert.Contains(t.Campaign(new CampaignFacts(11, 11, 11, 0)), a => a.Id == "FULL_RELIQUARY");
    }
}
