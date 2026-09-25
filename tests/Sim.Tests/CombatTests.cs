using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class CombatTests
{
    [Fact]
    public void AWatchtowerKillsWhatIsInRangeAndNothingElse()
    {
        var world = Rich(Dummies());
        var tower = Built(world, BuildingKind.Watchtower, 58, 58);
        float range = tower.Def.Weapon!.Range;
        // One dummy 4 tiles out, one well beyond range.
        Run(world, new SpawnDemons(DemonKind.Imp, (int)tower.CentreX + 4, (int)tower.CentreY, 1),
                   new SpawnDemons(DemonKind.Imp, (int)(tower.CentreX + range + 5), (int)tower.CentreY, 1));
        Assert.Equal(2, world.Horde.Count);

        var events = RunSeconds(world, 5);
        Assert.Equal(1, world.Horde.Count);
        Assert.True(world.Horde.X[0] > tower.CentreX + range, "the survivor should be the one out of range");
        Assert.Contains(events, e => e is ShotFired { FromUnit: false });
        Assert.Contains(events, e => e is DemonsKilled);
    }

    [Fact]
    public void ATowerWithNoSanctityDoesNotFire()
    {
        var rules = Dummies(Rules.Default.WithBuilding(BuildingKind.Keep, k => k with { SanctitySupply = 0 }));
        var world = Rich(rules);
        var tower = Built(world, BuildingKind.Watchtower, 58, 58);
        Run(world, new SpawnDemons(DemonKind.Imp, (int)tower.CentreX + 3, (int)tower.CentreY, 1));
        var events = RunSeconds(world, 5);
        Assert.Equal(0f, world.Colony.Power);
        Assert.DoesNotContain(events, e => e is ShotFired);
        Assert.Equal(1, world.Horde.Count);
    }

    [Fact]
    public void ABombardHitsEverythingInItsSplash()
    {
        var world = Rich(Dummies());
        var bombard = Built(world, BuildingKind.Bombard, 58, 58);
        // Its first volley lands on the very tick they spawn, so count from the spawn, not from after it.
        var spawnTick = Run(world, new SpawnDemons(DemonKind.Imp, (int)bombard.CentreX + 5, (int)bombard.CentreY, 20));
        int spawned = spawnTick.OfType<DemonsSpawned>().Single().Count;
        // One second: a single volley (the cooldown is 2.5 s).
        RunSeconds(world, 1);
        int killed = spawned - world.Horde.Count;
        Assert.True(killed >= 4, $"one Bombard shot killed only {killed} of a packed 20");
    }

    [Fact]
    public void ShotsAreLoud()
    {
        var world = Rich(Dummies());
        var tower = Built(world, BuildingKind.Watchtower, 58, 58);
        Run(world, new SpawnDemons(DemonKind.Imp, (int)tower.CentreX + 3, (int)tower.CentreY, 5));
        var events = RunSeconds(world, 1);
        Assert.Contains(events, e => e is ShotFired);
        Assert.True(world.Noise.LevelAtTile((int)tower.CentreX, (int)tower.CentreY) >= Balance.WakeThreshold);
    }

    [Fact]
    public void LosingTheKeepLosesTheGameAndStopsTheClock()
    {
        var world = Rich();
        Run(world, new SpawnDemons(DemonKind.Imp, C, C + 6, 150));
        var events = RunSeconds(world, 60);

        Assert.Equal(Outcome.Lost, world.Outcome);
        Assert.Contains(events, e => e is BuildingDestroyed { Kind: BuildingKind.Keep });
        Assert.Contains(events, e => e is OutcomeChanged { Outcome: Outcome.Lost });

        int tick = world.Tick;
        world.Step();
        Assert.Equal(tick, world.Tick);
    }

    [Fact]
    public void ADestroyedWallFreesItsTileAndReopensTheRoute()
    {
        var world = Rich();
        // Seal the Keep in a tight ring, 3 out, so the only way in is through a wall.
        var ring = new List<Command>();
        for (int y = C - 3; y <= C + 3; y++)
            for (int x = C - 3; x <= C + 3; x++)
                if (Math.Max(Math.Abs(x - C), Math.Abs(y - C)) == 3) ring.Add(new PlaceBuilding(BuildingKind.Wall, x, y));
        foreach (var c in ring) world.Enqueue(c);
        RunSeconds(world, 3);
        int before = world.Flow.DistAt(C - 8, C);

        Run(world, new SpawnDemons(DemonKind.Imp, C - 8, C, 20));
        BuildingDestroyed? breach = null;
        for (int t = 0; t < 60 * Balance.TickHz && breach == null; t++)
        {
            world.Step();
            breach = world.DrainEvents().OfType<BuildingDestroyed>().FirstOrDefault(d => d.Kind == BuildingKind.Wall);
        }

        Assert.NotNull(breach);
        Assert.Equal(0, world.BuildingIdAt(breach.X, breach.Y));
        Assert.True(world.IsWalkable(breach.X, breach.Y));
        world.Step(); // the flow field rebuilds at the top of the next tick
        Assert.True(world.Flow.DistAt(C - 8, C) < before, "the route in should get cheaper once a wall is gone");
    }
}

public class RepairTests
{
    [Fact]
    public void ALeftAloneWallMendsAndPaysForIt()
    {
        var world = TestWorlds.Rich();
        var wall = TestWorlds.Built(world, BuildingKind.Wall, TestWorlds.C + 6, TestWorlds.C);
        wall.Hp = wall.Def.Hp / 2;
        TestWorlds.RunSeconds(world, 1);
        double wood = world.Colony[Resource.Wood];
        Assert.True(wall.Hp < wall.Def.Hp * 0.51f, "mended before the delay");
        TestWorlds.RunSeconds(world, world.Rules.Repair.DelaySeconds + 5);
        Assert.True(wall.Hp > wall.Def.Hp * 0.6f, $"hp {wall.Hp}");
        Assert.True(world.Colony[Resource.Wood] < wood, "repair was free");
        TestWorlds.RunSeconds(world, 30);
        Assert.Equal(wall.Def.Hp, wall.Hp, 1);
    }

    [Fact]
    public void RepairWaitsForTheMoney()
    {
        var world = World.Create(new WorldOptions(7, Balance.DefaultMapSize, 0, Rules.Default.WithStartingResources(new Cost { Gold = 1000, Wood = 1000, Stone = 60 })));
        world.DrainEvents();
        var wall = TestWorlds.Built(world, BuildingKind.Wall, TestWorlds.C + 6, TestWorlds.C);
        for (int r = 0; r < Colony.Resources; r++) world.Colony.Stock[r] = 0;
        wall.Hp = wall.Def.Hp / 2;
        TestWorlds.RunSeconds(world, 20);
        Assert.Equal(wall.Def.Hp / 2, wall.Hp, 1);
    }
}
