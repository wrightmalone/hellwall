using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

/// <summary>Miners (mining.enabled): a Quarry's and a Mine's crew go out and wear the rock and ore away.</summary>
public class MiningTests
{
    static Rules Fast(float hp = 20) => Rules.Default.WithMining(m => m with { RockHp = hp, OreHp = hp * 2 });

    [Fact]
    public void MinersCarryStoneHomeAndWearTheRockAway()
    {
        var world = Rich(Fast());
        var quarry = Built(world, BuildingKind.Quarry, C + 5, C - 1);
        world.PlantForest(C + 9, C - 1, C + 10, C + 1, Tile.Rock);
        double before = world.Colony[Resource.Stone];
        var events = RunSeconds(world, 120);
        Assert.Equal(quarry.Def.Workers, world.Woodsmen.Count(m => m.HomeId == quarry.Id));
        Assert.True(world.Colony[Resource.Stone] > before + 10, $"stone {before:0} -> {world.Colony[Resource.Stone]:0}");
        var worn = events.OfType<DepositWorn>().First();
        Assert.Equal(Tile.Rock, worn.Was);
        Assert.Equal(Tile.Grass, world.Terrain.Get(worn.X, worn.Y));
        Assert.True(world.IsWalkable(worn.X, worn.Y), "worn-away rock is open ground");
        Assert.True(world.DepositsWorn > 0);
    }

    [Fact]
    public void IronLastsLongerThanRock()
    {
        Assert.True(Rules.Default.Mining.OreHp > Rules.Default.Mining.RockHp);
        Assert.True(Rules.Default.Mining.RockHp > Rules.Default.Woods.TreeHp * 2, "rock is much slower than trees");
    }

    [Fact]
    public void AMineWithNothingLeftSaysSoUntilThereIsMore()
    {
        var world = Rich(Fast(4));
        var mine = Built(world, BuildingKind.Mine, C + 5, C - 1);
        world.PlantForest(C + 9, C, C + 9, C, Tile.Ore);
        double before = world.Colony[Resource.Iron];
        RunSeconds(world, 90);
        Assert.Equal(Tile.Grass, world.Terrain.Get(C + 9, C));
        Assert.True(world.Colony[Resource.Iron] > before, "the ore it had came home");
        Assert.True(mine.Exhausted, "a mine with no ore in reach is worked out");
        world.PlantForest(C + 9, C + 2, C + 9, C + 2, Tile.Ore);
        RunSeconds(world, 6); // a worked-out crew looks again every 5 seconds
        Assert.False(mine.Exhausted, "fresh ore in reach puts it back to work");
    }

    [Fact]
    public void OffMeansTheOldRadiusGathering()
    {
        var world = Rich(Rules.Default.WithMining(m => m with { Enabled = false }));
        var quarry = Built(world, BuildingKind.Quarry, C + 5, C - 1);
        world.PlantForest(C + 8, C - 1, C + 9, C + 1, Tile.Rock);
        RunSeconds(world, 30);
        Assert.Empty(world.Woodsmen);
        Assert.True(quarry.Rate > 0);
        Assert.Equal(0, world.DepositsWorn);
    }

    [Fact]
    public void AHalfMinedRockSurvivesASave()
    {
        var world = Rich(Rules.Default);
        Built(world, BuildingKind.Quarry, C + 5, C - 1);
        world.PlantForest(C + 9, C - 1, C + 10, C + 1, Tile.Rock);
        RunSeconds(world, 60);
        var copy = World.Load(world.Save(), world.Rules);
        Assert.Equal(StateHash.Hex(world), StateHash.Hex(copy));
    }
}

public class RunOptionSaveTests
{
    /// <summary>Living woods and miners, each on or off, on a campaign mission and in survival: the save loads under the rules it was made with.</summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void EitherWayRoundTheSaveLoads(bool woods, bool mining, bool mission)
    {
        var rules = Rules.Default;
        if (woods != rules.Woods.Blocks) rules = rules.WithWoods(w => w with { Blocks = woods });
        if (mining != rules.Mining.Enabled) rules = rules.WithMining(m => m with { Enabled = mining });
        var world = mission
            ? World.Create(Campaign.Default.Find("first-night")!.Options(rules))
            : World.Create(new WorldOptions(7, 128, 0, rules, Survival: true));
        for (int t = 0; t < 40; t++) world.Step();
        var copy = World.Load(world.Save(), Rules.Default);
        Assert.Equal(woods, copy.Rules.Woods.Blocks);
        Assert.Equal(mining, copy.Rules.Mining.Enabled);
        Assert.Equal(StateHash.Hex(world), StateHash.Hex(copy));
    }
}

public class ConvergenceShapeTests
{
    [Fact]
    public void TheConvergenceLeansOnOneSideAndIsKnownEarly()
    {
        var rules = Rules.Default.Survival;
        var final = new PlannedWave { Number = 20, LandsAtTick = 100_000, Size = 7000, Final = true, Sides = [Side.East, Side.North, Side.South, Side.West] };
        int main = final.ShareOf(0, rules);
        Assert.Equal((int)Math.Round(7000 * rules.ConvergenceLean), main);
        Assert.Equal(7000, Enumerable.Range(0, 4).Sum(i => final.ShareOf(i, rules)));
        Assert.True(main > 3 * final.ShareOf(1, rules) / 2, "most come from the main side");
        Assert.True(final.LandsAtTick - final.AnnounceTick(rules) >= rules.ConvergenceWarnSeconds * Balance.TickHz, "known well ahead");

        var wave = new PlannedWave { Number = 3, LandsAtTick = 50_000, Size = 101, Sides = [Side.East, Side.West] };
        Assert.Equal(51, wave.ShareOf(0, rules));
        Assert.Equal(50, wave.ShareOf(1, rules));
        Assert.Equal(wave.LandsAtTick - (int)(rules.TelegraphSeconds * Balance.TickHz), wave.AnnounceTick(rules));
    }
}

public class FlightTests
{
    static Rules Still() => TestWorlds.Dummies(); // demons that stand still and don't hit

    [Fact]
    public void AFarmsCrewRunFromADemonWithNothingBetweenAndItGathersNothing()
    {
        var world = TestWorlds.Rich(Still());
        var farm = TestWorlds.Built(world, BuildingKind.Farm, TestWorlds.C + 6, TestWorlds.C - 1);
        TestWorlds.RunSeconds(world, 2);
        double before = world.Colony.NetPerSecond[(int)Resource.Food];
        TestWorlds.Run(world, new SpawnDemons(DemonKind.Imp, TestWorlds.C + 12, TestWorlds.C, 1));
        TestWorlds.RunSeconds(world, 2);
        Assert.True(farm.Fleeing, "the crew should have run");
        Assert.True(world.Colony.NetPerSecond[(int)Resource.Food] < before - farm.Rate * 0.5, "and the farm gathers nothing while they're gone");
    }

    [Fact]
    public void AWallBetweenLetsThemKeepWorking()
    {
        var world = TestWorlds.Rich(Still());
        var farm = TestWorlds.Built(world, BuildingKind.Farm, TestWorlds.C + 6, TestWorlds.C - 1);
        for (int y = TestWorlds.C - 8; y <= TestWorlds.C + 8; y++) TestWorlds.Run(world, new PlaceBuilding(BuildingKind.Wall, TestWorlds.C + 10, y));
        TestWorlds.RunSeconds(world, 5);
        TestWorlds.Run(world, new SpawnDemons(DemonKind.Imp, TestWorlds.C + 12, TestWorlds.C, 1));
        TestWorlds.RunSeconds(world, 2);
        Assert.False(farm.Fleeing, "the wall is between them and the demon");
    }

    [Fact]
    public void MinersRunHomeAndStayWhileTheyAreThreatened()
    {
        var world = TestWorlds.Rich(Still());
        var quarry = TestWorlds.Built(world, BuildingKind.Quarry, TestWorlds.C + 5, TestWorlds.C - 1);
        world.PlantForest(TestWorlds.C + 9, TestWorlds.C - 1, TestWorlds.C + 10, TestWorlds.C + 1, Tile.Rock);
        TestWorlds.RunSeconds(world, 15);
        Assert.Contains(world.Woodsmen, m => m.HomeId == quarry.Id && m.State != WoodsmanState.Home);
        TestWorlds.Run(world, new SpawnDemons(DemonKind.Imp, TestWorlds.C + 12, TestWorlds.C + 3, 1));
        TestWorlds.RunSeconds(world, 6);
        Assert.True(quarry.Fleeing);
        Assert.All(world.Woodsmen.Where(m => m.HomeId == quarry.Id), m => Assert.True(m.State is WoodsmanState.Home or WoodsmanState.Back, $"a miner still {m.State}"));
    }
}

public class SteadyingTests
{
    [Fact]
    public void ASoldierByTheFieldsKeepsTheCrewAtWorkAgainstAStrayButNotAPack()
    {
        var world = TestWorlds.Rich(TestWorlds.Dummies());
        var farm = TestWorlds.Built(world, BuildingKind.Farm, TestWorlds.C + 6, TestWorlds.C - 1);
        world.TrySpawnUnit(UnitKind.Militia, world.Buildings.First(b => b.Kind == BuildingKind.Keep));
        var guard = world.Units[0];
        guard.X = guard.PrevX = TestWorlds.C + 9.5f;
        guard.Y = guard.PrevY = TestWorlds.C + 0.5f;
        TestWorlds.Run(world, new OrderUnits([guard.Id], OrderKind.Hold, 0, 0));
        TestWorlds.Run(world, new SpawnDemons(DemonKind.Imp, TestWorlds.C + 13, TestWorlds.C, 1));
        TestWorlds.RunSeconds(world, 2);
        Assert.False(farm.Fleeing, "one soldier steadies them against one demon");
        Assert.True(farm.Steadied);
        TestWorlds.Run(world, new SpawnDemons(DemonKind.Imp, TestWorlds.C + 13, TestWorlds.C + 1, 12));
        TestWorlds.RunSeconds(world, 2);
        Assert.True(farm.Fleeing, "but not against a pack");
    }
}
