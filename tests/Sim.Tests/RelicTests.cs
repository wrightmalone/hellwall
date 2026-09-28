using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class RelicTests
{
    static readonly Campaign C = Campaign.Default;

    [Fact]
    public void EveryRelicAppliesPlainAndHallowed()
    {
        Assert.NotEmpty(C.Relics);
        foreach (var relic in C.Relics)
        {
            Assert.NotNull(C.Find(relic.From));
            Assert.NotEqual("", relic.Effect.Text);
            Assert.NotEqual("", relic.Hallowed.Text);
            Assert.NotNull(relic.Bonus);
            Relics.Apply(Rules.Default, C, [relic.Id]);
            Relics.Apply(Rules.Default, C, [relic.Id + "+"]);
        }
    }

    [Fact]
    public void ARelicBendsTheMissionsRules()
    {
        var rules = Relics.Apply(Rules.Default, C, ["hearthstone", "tongs+", "key", "vial"]);
        Assert.Equal(Rules.Default[BuildingKind.Keep].ConsecrateRadius + 2, rules[BuildingKind.Keep].ConsecrateRadius, 3);
        Assert.Equal(Rules.Default[UnitKind.Militia].TrainSeconds * 0.5f, rules[UnitKind.Militia].TrainSeconds, 3);
        Assert.True(rules.Hellgates.BandSize < Rules.Default.Hellgates.BandSize);
        Assert.Equal(Rules.Default.StartingResources.Silver + 60, rules.StartingResources.Silver);
        Assert.NotEqual(Rules.Default.Hash, rules.Hash);
    }

    [Fact]
    public void ASaveKeepsTheRelicsTakenAndTheBonusSoFar()
    {
        var mission = C.Find("iron-hills")!;
        var world = World.Create(mission.Options(Rules.Default, ["hearthstone", "casket+"]));
        RunSeconds(world, 3);
        var loaded = World.Load(world.Save(), Rules.Default);
        Assert.Equal(world.Relics, loaded.Relics);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
        Assert.NotNull(loaded.Bonus);
        Assert.Equal(ObjectiveKind.Population, loaded.Bonus!.Kind);
    }

    [Fact]
    public void AKeptBonusGoalBreaksWhenABuildingFalls()
    {
        var world = World.Create(C.Find("first-night")!.Options(Rules.Default.Harmless()));
        Assert.Equal(ObjectiveKind.LoseNoBuildings, world.Bonus!.Kind);
        Assert.True(world.BonusDone, "kept until broken");
        RunSeconds(world, 1); // past the mission's own first tick
        var house = Place(world, BuildingKind.House, world.Terrain.Width / 2 + 4, world.Terrain.Height / 2 + 4);
        house.Hp = 0;
        RunSeconds(world, 1);
        Assert.False(world.BonusDone);
        Assert.True(world.BonusBroken);
    }

    [Fact]
    public void OnlyTheCampaignsOwnMissionsHaveABonus()
    {
        var copy = C.Find("first-night")! with { Name = "a copy" };
        Assert.Null(World.Create(copy.Options(Rules.Default)).Bonus);
    }
}
