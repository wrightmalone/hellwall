using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

/// <summary>Blocking woods (woods.blocks): no one walks through forest, the horde hacks through it, and woodsmen fell it.</summary>
public class WoodsTests
{
    static Rules Woods(Rules? rules = null) => (rules ?? Rules.Default).WithWoods(w => w with { Blocks = true });

    [Fact]
    public void ForestBlocksOnlyWhenTheRuleIsOn()
    {
        var open = Rich();
        open.PlantForest(C + 8, C, C + 8, C);
        Assert.True(open.IsWalkable(C + 8, C));
        Assert.True(open.IsHumanWalkable(C + 8, C));

        var blocked = Rich(Woods());
        blocked.PlantForest(C + 8, C, C + 8, C);
        Assert.False(blocked.IsWalkable(C + 8, C));
        Assert.False(blocked.IsHumanWalkable(C + 8, C));
        Assert.True(blocked.IsTree(C + 8, C));
    }

    /// <summary>A lodge beside a stand of trees: its crew go out, bring wood home, and bring trees down.</summary>
    static (World World, Building Lodge) Lodge()
    {
        var world = Rich(Woods());
        var lodge = Built(world, BuildingKind.Woodcutter, C + 5, C - 1);
        world.PlantForest(C + 9, C - 3, C + 12, C + 3);
        return (world, lodge);
    }

    [Fact]
    public void WoodsmenCarryWoodHomeAndFellTrees()
    {
        var (world, lodge) = Lodge();
        double before = world.Colony[Resource.Wood];
        var events = RunSeconds(world, 120);
        Assert.Equal(lodge.Def.Workers, world.Woodsmen.Count);
        Assert.True(world.Colony[Resource.Wood] > before + 50, $"wood {before:0} -> {world.Colony[Resource.Wood]:0}");
        Assert.True(lodge.Rate > 0.5f, $"measured rate {lodge.Rate:0.00}");
        Assert.True(world.TreesFelled > 0);
        var felled = events.OfType<TreeFelled>().First();
        Assert.Equal(Tile.Grass, world.Terrain.Get(felled.X, felled.Y));
        Assert.True(world.IsWalkable(felled.X, felled.Y));
    }

    [Fact]
    public void TheCrewGoesWithTheLodge()
    {
        var (world, lodge) = Lodge();
        RunSeconds(world, 10);
        Assert.NotEmpty(world.Woodsmen);
        Run(world, new Demolish(lodge.Id));
        RunSeconds(world, 1);
        Assert.Empty(world.Woodsmen);
    }

    /// <summary>A Keep sealed in by a ring of trees: the horde can't walk in, so it cuts its way in.</summary>
    [Fact]
    public void TheHordeHacksThroughASealingRing()
    {
        var world = Rich(Woods());
        world.PlantForest(C - 12, C - 12, C + 12, C - 11);
        world.PlantForest(C - 12, C + 11, C + 12, C + 12);
        world.PlantForest(C - 12, C - 12, C - 11, C + 12);
        world.PlantForest(C + 11, C - 12, C + 12, C + 12);
        Run(world, new SpawnDemons(DemonKind.Imp, C + 24, C, 80));
        RunSeconds(world, 90);
        Assert.True(world.TreesFelled > 0, "no tree came down");
    }

    [Fact]
    public void WoodsSurviveASaveMidWalk()
    {
        var (world, _) = Lodge();
        // Until one is on his way home with a load, between tiles.
        for (int t = 0; t < 200 * Balance.TickHz && !world.Woodsmen.Any(m => m.State == WoodsmanState.Back && m.Carry > 0 && m.X % 1 != 0.5f); t++) world.Step();
        Assert.Contains(world.Woodsmen, m => m.State == WoodsmanState.Back && m.Carry > 0);
        var loaded = World.Load(world.Save(), world.Rules);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
        RunSeconds(world, 30);
        RunSeconds(loaded, 30);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }
}
