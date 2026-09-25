using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class UnitTests
{
    static Unit Train(World world, Building barracks, UnitKind kind)
    {
        var events = Run(world, new TrainUnit(barracks.Id, kind));
        Assert.Empty(events.OfType<CommandRejected>());
        var trained = RunSeconds(world, world.Rules[kind].TrainSeconds + 0.5).OfType<UnitTrained>().Single();
        return world.UnitById(trained.UnitId)!;
    }

    [Fact]
    public void ABarracksTrainsWhatItIsPaidFor()
    {
        var world = Rich();
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        double gold = world.Colony[Resource.Gold];
        Run(world, new TrainUnit(barracks.Id, UnitKind.Militia));
        Assert.True(world.Colony[Resource.Gold] < gold - world.Rules[UnitKind.Militia].Cost.Gold + 1);

        RunSeconds(world, world.Rules[UnitKind.Militia].TrainSeconds - 1);
        Assert.Empty(world.Units);
        var events = RunSeconds(world, 1.5);
        Assert.Single(events.OfType<UnitTrained>());
        Assert.Single(world.Units);
    }

    [Fact]
    public void TrainingIsRejectedAtTheWrongBuildingOrAnUnfinishedOne()
    {
        var world = Rich();
        var house = Built(world, BuildingKind.House, 68, 62);
        Assert.Equal("House can't train Militia",
            Assert.Single(Run(world, new TrainUnit(house.Id, UnitKind.Militia)).OfType<CommandRejected>()).Reason);

        var barracks = Place(world, BuildingKind.Barracks, 59, 66);
        Assert.Equal("still under construction",
            Assert.Single(Run(world, new TrainUnit(barracks.Id, UnitKind.Militia)).OfType<CommandRejected>()).Reason);
    }

    [Fact]
    public void SoldiersLeaveARingByTheGateAndNeverCrossAWall()
    {
        var world = Rich();
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        // Ring 8 out with a gate on the east side.
        foreach (var c in Scenarios.WallRing(world, 8, Side.East)) world.Enqueue(c);
        world.Enqueue(new PlaceBuilding(BuildingKind.Gate, C + 8, C));
        world.Enqueue(new PlaceBuilding(BuildingKind.Wall, C + 8, C - 1));
        world.Enqueue(new PlaceBuilding(BuildingKind.Wall, C + 8, C + 1));
        RunSeconds(world, 5);
        var soldier = Train(world, barracks, UnitKind.Militia);

        // Somewhere outside, west: the short way is through the west wall, the only way is round by the east gate.
        Run(world, new OrderUnits([soldier.Id], OrderKind.Move, C - 14, C));
        bool usedGate = false;
        for (int t = 0; t < 90 * Balance.TickHz; t++)
        {
            world.Step();
            int id = world.BuildingIdAt((int)soldier.X, (int)soldier.Y);
            if (id != 0)
            {
                var b = world.BuildingById(id)!;
                Assert.Equal(BuildingKind.Gate, b.Kind);
                usedGate = true;
            }
            if (soldier.Order == OrderKind.Idle) break;
        }
        Assert.True(usedGate, "the soldier never went through the gate");
        Assert.Equal(OrderKind.Idle, soldier.Order);
        Assert.InRange(soldier.X, C - 15, C - 13);
    }

    [Fact]
    public void DemonsCannotUseGates()
    {
        var world = Rich(Rules.Default.Harmless());
        Built(world, BuildingKind.Gate, C, C - 8);
        // For demons a gate is a wall: stepping onto it costs a wall's worth.
        int onto = world.Flow.DistAt(C, C - 8) - world.Flow.DistAt(C, C - 7);
        Assert.True(onto >= Balance.CostStraight * Balance.WallCostMultiplier);
        Assert.False(world.IsWalkable(C, C - 8));
        Assert.True(world.IsHumanWalkable(C, C - 8));
    }

    [Fact]
    public void AHoldingSoldierStandsItsGroundAndShoots()
    {
        var world = Rich(Dummies());
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        var soldier = Train(world, barracks, UnitKind.Militia);
        Run(world, new OrderUnits([soldier.Id], OrderKind.Hold, 0, 0));
        float x = soldier.X, y = soldier.Y;
        // A dummy inside aggro reach but outside weapon range: an Idle soldier would close in; a holding one mustn't.
        Run(world, new SpawnDemons(DemonKind.Imp, (int)x + 7, (int)y, 1));
        RunSeconds(world, 3);
        Assert.Equal(x, soldier.X, 3);
        Assert.Equal(y, soldier.Y, 3);
        Assert.Equal(1, world.Horde.Count);

        // Within range it fires.
        Run(world, new SpawnDemons(DemonKind.Imp, (int)x + 3, (int)y, 1));
        var events = RunSeconds(world, 3);
        Assert.Contains(events, e => e is ShotFired { FromUnit: true });
    }

    [Fact]
    public void DemonsChaseASoldierAndOverwhelmIt()
    {
        var world = Rich();
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        var soldier = Train(world, barracks, UnitKind.Militia);
        Run(world, new OrderUnits([soldier.Id], OrderKind.Hold, 0, 0));
        // Just inside chase radius, well away from any building: they come for the soldier, not the town.
        Run(world, new SpawnDemons(DemonKind.Imp, (int)soldier.X, (int)soldier.Y + 4, 30));

        var events = RunSeconds(world, 20);
        // 30 imps out-damage one militiaman many times over; he gets shots off, then dies.
        Assert.Contains(events, e => e is ShotFired { FromUnit: true });
        Assert.Contains(events, e => e is UnitDied d && d.UnitId == soldier.Id);
    }

    [Fact]
    public void ATemplarWinsAFairFight()
    {
        var world = Rich();
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        var templar = Train(world, barracks, UnitKind.Templar);
        Run(world, new OrderUnits([templar.Id], OrderKind.Hold, 0, 0));
        Run(world, new SpawnDemons(DemonKind.Imp, (int)templar.X, (int)templar.Y + 3, 3));

        RunSeconds(world, 15);
        Assert.Equal(0, world.Horde.Count);
        Assert.Contains(templar, world.Units);
        Assert.True(templar.Hp < templar.Def.Hp, "the imps should have landed some blows");
    }

    [Fact]
    public void SoldiersTrainedInARowDoNotStack()
    {
        var world = Rich();
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        for (int i = 0; i < 4; i++) world.Enqueue(new TrainUnit(barracks.Id, UnitKind.Militia));
        RunSeconds(world, 4 * world.Rules[UnitKind.Militia].TrainSeconds + 2);
        Assert.Equal(4, world.Units.Count);
        foreach (var a in world.Units)
            foreach (var b in world.Units)
                if (a != b) Assert.True(MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y) > 0.4f, $"units {a.Id} and {b.Id} are standing on each other");
    }
}

public class VeterancyTests
{
    [Fact]
    public void KillsEarnRanksAndRanksHitHarder()
    {
        var world = TestWorlds.Rich(TestWorlds.Dummies());
        // A lone Marksman beside a crowd of harmless demons: he shoots until he runs out of targets.
        var barracks = TestWorlds.Built(world, BuildingKind.Barracks, TestWorlds.C + 4, TestWorlds.C + 4);
        TestWorlds.Run(world, new TrainUnit(barracks.Id, UnitKind.Marksman));
        TestWorlds.RunSeconds(world, 12);
        var marksman = world.Units.Single(x => x.Kind == UnitKind.Marksman);
        TestWorlds.Run(world, new SpawnDemons(DemonKind.Imp, (int)marksman.X + 5, (int)marksman.Y, 40));
        var events = TestWorlds.RunSeconds(world, 120);
        Assert.True(marksman.Kills >= Unit.RankKills[1], $"{marksman.Kills} kills");
        Assert.Equal(2, marksman.Rank);
        Assert.Equal(marksman.Def.Hp * (1 + 2 * Unit.HpPerRank), marksman.MaxHp, 3);
        Assert.Contains(events, e => e is UnitPromoted { Rank: 1 } p && p.UnitId == marksman.Id);
        Assert.Contains(events, e => e is UnitPromoted { Rank: 2 } p && p.UnitId == marksman.Id);
        var loaded = World.Load(world.Save(), world.Rules);
        Assert.Equal(marksman.Kills, loaded.Units.Single(x => x.Id == marksman.Id).Kills);
    }
}

public class PatrolTests
{
    [Fact]
    public void APatrolWalksBackAndForth()
    {
        var world = TestWorlds.Rich(TestWorlds.Dummies());
        var keep = world.Buildings.Single(b => b.Kind == BuildingKind.Keep);
        world.TrySpawnUnit(UnitKind.Militia, keep);
        var u = world.Units.Last();
        int homeX = (int)u.X, homeY = (int)u.Y;
        var (tx, ty) = TestWorlds.GrassAtDistance(world, 12, 14);
        TestWorlds.Run(world, new OrderUnits([u.Id], OrderKind.Patrol, tx, ty));
        bool reachedFar = false, cameBack = false;
        for (int t = 0; t < 60 * Balance.TickHz; t++)
        {
            world.Step();
            float dFar = (u.X - tx - 0.5f) * (u.X - tx - 0.5f) + (u.Y - ty - 0.5f) * (u.Y - ty - 0.5f);
            float dHome = (u.X - homeX - 0.5f) * (u.X - homeX - 0.5f) + (u.Y - homeY - 0.5f) * (u.Y - homeY - 0.5f);
            if (dFar < 1) reachedFar = true;
            if (reachedFar && dHome < 1) cameBack = true;
        }
        Assert.True(reachedFar, "never reached the far end");
        Assert.True(cameBack, "never came back");
        Assert.Equal(OrderKind.Patrol, u.Order);
        var loaded = World.Load(world.Save(), world.Rules);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }
}

public class LeashTests
{
    [Fact]
    public void AnIdleSoldierDoesNotWanderOffAfterDemons()
    {
        var world = TestWorlds.Rich(TestWorlds.Dummies());
        var keep = world.Buildings.Single(b => b.Kind == BuildingKind.Keep);
        world.TrySpawnUnit(UnitKind.Militia, keep);
        var u = world.Units.Last();
        TestWorlds.RunSeconds(world, 0.5);
        float ax = u.X, ay = u.Y;
        // A demon just beyond its weapon, well beyond its leash: it mustn't go after it.
        TestWorlds.Run(world, new SpawnDemons(DemonKind.Imp, (int)ax + 7, (int)ay, 1));
        TestWorlds.RunSeconds(world, 10);
        float dx = u.X - ax, dy = u.Y - ay;
        Assert.True(dx * dx + dy * dy < 1.5f, $"wandered {MathF.Sqrt(dx * dx + dy * dy):0.0} tiles");
    }
}

public class FormationTests
{
    [Fact]
    public void AGroupSentToOnePointSettlesQuicklyWithoutShuffling()
    {
        var world = TestWorlds.Rich(TestWorlds.Dummies());
        var keep = world.Buildings.Single(b => b.Kind == BuildingKind.Keep);
        for (int i = 0; i < 12; i++) world.TrySpawnUnit(UnitKind.Militia, keep);
        var (tx, ty) = TestWorlds.GrassAtDistance(world, 10, 12);
        TestWorlds.Run(world, new OrderUnits(world.Units.Select(u => u.Id).ToArray(), OrderKind.Move, tx, ty));
        TestWorlds.RunSeconds(world, 12);
        Assert.All(world.Units, u => Assert.Equal(OrderKind.Idle, u.Order));
        // Settled: a further few seconds and nobody has moved.
        var before = world.Units.Select(u => (u.X, u.Y)).ToList();
        TestWorlds.RunSeconds(world, 3);
        var after = world.Units.Select(u => (u.X, u.Y)).ToList();
        for (int i = 0; i < before.Count; i++)
        {
            float dx = after[i].X - before[i].X, dy = after[i].Y - before[i].Y;
            Assert.True(dx * dx + dy * dy < 0.01f, $"soldier {i} still shuffling");
        }
    }
}

public class HuntTests
{
    [Fact]
    public void ADemonShotFromAfarComesForTheShooterNotTheKeep()
    {
        // The Marksman stands north of the demon; the Keep is west. Shot, it must go north.
        var rules = Rules.Default.WithStartingResources(TestWorlds.Plenty).WithDemon(DemonKind.Brute, d => d with { Hp = 100000 });
        var world = World.Create(new WorldOptions(7, Balance.DefaultMapSize, 0, rules));
        world.DrainEvents();
        var keep = world.Buildings.Single(b => b.Kind == BuildingKind.Keep);
        world.TrySpawnUnit(UnitKind.Marksman, keep);
        var m = world.Units.Last();
        var (sx, sy) = TestWorlds.GrassAtDistance(world, 26, 28);
        TestWorlds.Run(world, new OrderUnits([m.Id], OrderKind.Move, sx, sy - 8));
        TestWorlds.RunSeconds(world, 20);
        TestWorlds.Run(world, new OrderUnits([m.Id], OrderKind.Hold, 0, 0));
        TestWorlds.Run(world, new SpawnDemons(DemonKind.Brute, sx, sy, 1));
        float y0 = world.Horde.Y[0];
        TestWorlds.RunSeconds(world, 6);
        float my = m.Y;
        Assert.True(world.Horde.Y[0] < y0 - 1.5f, $"the demon went from y {y0:0.0} to {world.Horde.Y[0]:0.0}; the Marksman is at y {my:0.0} ({world.Horde.Count} demons)");
    }
}
