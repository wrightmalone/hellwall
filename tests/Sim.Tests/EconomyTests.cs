using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

/// <summary>The player's economy sliders (Rules.WithEconomy): each resource's income, and nothing else.</summary>
public class EconomyTests
{
    static double[] Only(Resource r, double m)
    {
        var e = Enumerable.Repeat(1.0, Enum.GetValues<Resource>().Length).ToArray();
        e[(int)r] = m;
        return e;
    }

    [Fact]
    public void AtAHundredPercentNothingChanges()
    {
        Assert.Same(Rules.Default, Rules.Default.WithEconomy(null));
        Assert.Same(Rules.Default, Rules.Default.WithEconomy(Only(Resource.Iron, 1)));
        Assert.False(World.Create(new WorldOptions(7, 128, 0, Rules.Default, Economy: Only(Resource.Gold, 1))).CustomEconomy);
    }

    [Fact]
    public void EachSliderScalesItsOwnResourceOnly()
    {
        var d = Rules.Default;
        var iron = d.WithEconomy(Only(Resource.Iron, 2));
        Assert.Equal(d[BuildingKind.Mine].PerTile * 2, iron[BuildingKind.Mine].PerTile, 6);
        Assert.Equal(d.Mining.IronPerHp * 2, iron.Mining.IronPerHp, 4);
        Assert.Equal(d.Mining.StonePerHp, iron.Mining.StonePerHp);
        Assert.Equal(d[BuildingKind.Quarry].PerTile, iron[BuildingKind.Quarry].PerTile);
        Assert.Equal(d.ColonistGoldPerSecond, iron.ColonistGoldPerSecond);
        Assert.NotEqual(d.Hash, iron.Hash);

        var gold = d.WithEconomy(Only(Resource.Gold, 0.5));
        Assert.Equal(d.ColonistGoldPerSecond * 0.5, gold.ColonistGoldPerSecond, 6);
        Assert.Equal(d[BuildingKind.Keep].Gold * 0.5, gold[BuildingKind.Keep].Gold, 6);

        var wood = d.WithEconomy(Only(Resource.Wood, 3));
        Assert.Equal(d[BuildingKind.Woodcutter].PerTile * 3, wood[BuildingKind.Woodcutter].PerTile, 6);
        Assert.Equal(d.Woods.WoodPerHp * 3, wood.Woods.WoodPerHp, 4);
        Assert.Equal(d[BuildingKind.House].Cost.Wood, wood[BuildingKind.House].Cost.Wood); // costs stay
    }

    [Fact]
    public void ASaveKeepsTheEconomyItWasMadeWith()
    {
        var economy = Only(Resource.Iron, 2.5);
        var mission = Campaign.Default.Find("first-night")!;
        var world = World.Create(mission.Options(Rules.Default.WithEconomy(economy), ["hearthstone"]) with { Economy = economy });
        Assert.True(world.CustomEconomy);
        RunSeconds(world, 2);
        var loaded = World.Load(world.Save(), Rules.Default);
        Assert.Equal(economy, loaded.Economy);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }
}
