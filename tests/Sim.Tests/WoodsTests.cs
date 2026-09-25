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

    /// <summary>The same ring with a way through on the far side: they go round to it, and leave the trees standing.</summary>
    [Fact]
    public void TheHordeGoesRoundTheWoodsToAGap()
    {
        var world = Rich(Woods());
        world.PlantForest(C - 12, C - 12, C + 12, C - 11);
        world.PlantForest(C - 12, C + 11, C + 12, C + 12);
        world.PlantForest(C - 12, C - 12, C - 11, C + 12);
        world.PlantForest(C + 11, C - 12, C + 12, C + 12);
        world.PlantForest(C, C - 12, C, C - 11, Tile.Grass); // a gap in the north side, a long way round from the east
        Run(world, new SpawnDemons(DemonKind.Imp, C + 24, C, 40));
        RunSeconds(world, 60);
        Assert.Equal(0, world.TreesFelled);
        Assert.True(world.Stats.BuildingsLost > 0 || world.Buildings.First(b => b.Kind == BuildingKind.Keep).Hp < world.Rules[BuildingKind.Keep].Hp, "they got in, by the gap");
    }

    /// <summary>A tree costs the horde's route what a wall does: it breaks through a strip of trees no sooner than a wall.</summary>
    [Fact]
    public void ATreeIsPricedLikeAWall() => Assert.Equal(Balance.WallCostMultiplier, Rules.Default.Woods.TreeCost);

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

public class GapTests
{
    static Terrain Band(int thickness)
    {
        // Open ground west and east of a north-south band of forest, the whole height of the window and more.
        var t = new Terrain(40, 40);
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 40; x++)
                t.Set(x, y, x >= 20 && x < 20 + thickness ? Tile.Forest : Tile.Grass);
        return t;
    }

    [Fact]
    public void TheLastTreeOfABandOpensAGap()
    {
        var t = Band(1);
        Assert.True(WoodsSystem.OpensAGap(t, 20, 20));
    }

    [Fact]
    public void ATreeWithForestBehindItDoesNot()
    {
        var t = Band(3);
        Assert.False(WoodsSystem.OpensAGap(t, 20, 20)); // the edge of a thick band: still forest behind
    }

    [Fact]
    public void AGapNearbyMeansThisIsNoNewOpening()
    {
        var t = Band(1);
        t.Set(20, 22, Tile.Grass); // already a gap two tiles south
        Assert.False(WoodsSystem.OpensAGap(t, 20, 20));
    }
}

public class HoldTests
{
    [Fact]
    public void ALodgeOnHoldCallsItsWoodsmenHomeAndFreesItsCrew()
    {
        var world = TestWorlds.Rich(Rules.Default.WithWoods(w => w with { Blocks = true }));
        var lodge = TestWorlds.Built(world, BuildingKind.Woodcutter, TestWorlds.C + 5, TestWorlds.C - 1);
        world.PlantForest(TestWorlds.C + 9, TestWorlds.C - 3, TestWorlds.C + 12, TestWorlds.C + 3);
        TestWorlds.RunSeconds(world, 15);
        Assert.Contains(world.Woodsmen, m => m.State != WoodsmanState.Home);
        int used = world.Colony.WorkersUsed;
        TestWorlds.Run(world, new SetPaused(lodge.Id, true));
        TestWorlds.RunSeconds(world, 15);
        Assert.All(world.Woodsmen, m => Assert.Equal(WoodsmanState.Home, m.State));
        Assert.Equal(used - lodge.Def.Workers, world.Colony.WorkersUsed);
        int felled = world.TreesFelled;
        TestWorlds.RunSeconds(world, 30);
        Assert.Equal(felled, world.TreesFelled);
        TestWorlds.Run(world, new SetPaused(lodge.Id, false));
        TestWorlds.RunSeconds(world, 15);
        Assert.Contains(world.Woodsmen, m => m.State != WoodsmanState.Home);
        var loaded = World.Load(world.Save(), world.Rules);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }
}
