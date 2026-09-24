using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class DemonKindTests
{
    static List<Command> SealedRing(World world, int radius) => Scenarios.WallRing(world, radius);

    [Fact]
    public void AGargoyleFliesOverASealedRing()
    {
        var world = Rich();
        foreach (var c in SealedRing(world, 8)) world.Enqueue(c);
        RunSeconds(world, 3);
        Run(world, new SpawnDemons(DemonKind.Gargoyle, C - 20, C, 1));
        Assert.Equal(DemonKind.Gargoyle, world.Horde.Kind[0]);

        bool inside = false;
        for (int t = 0; t < 20 * Balance.TickHz && world.Horde.Count > 0; t++)
        {
            world.Step();
            if (world.Horde.Count > 0 && MathF.Abs(world.Horde.X[0] - C) < 7 && MathF.Abs(world.Horde.Y[0] - C) < 7) inside = true;
        }
        Assert.True(inside, "the gargoyle never got inside the ring");
        Assert.True(world.Buildings.First(b => b.Kind == BuildingKind.Keep).Hp < Rules.Default[BuildingKind.Keep].Hp, "and it should have struck the Keep");
    }

    [Fact]
    public void ABloaterBurstsAgainstAWallAndBattersEverythingNear()
    {
        var world = Rich();
        foreach (var c in SealedRing(world, 8)) world.Enqueue(c);
        RunSeconds(world, 3);
        Run(world, new SpawnDemons(DemonKind.Bloater, C - 11, C, 1));
        var events = RunSeconds(world, 15);

        Assert.Contains(events, e => e is DemonBurst);
        int battered = world.Buildings.Count(b => b.Kind == BuildingKind.Wall && b.Hp < b.Def.Hp);
        Assert.True(battered >= 3, $"one burst should batter several wall tiles, got {battered}");
        Assert.Equal(0, world.Horde.Count);
    }

    [Fact]
    public void ABloaterKilledBesideAWallStillBatters()
    {
        // A Bloater that can't move, shot down 1.5 tiles from a wall.
        var rules = Rules.Default.WithDemon(DemonKind.Bloater, d => d with { Speed = 0 });
        var world = Rich(rules);
        var wall = Built(world, BuildingKind.Wall, C - 6, C);
        Built(world, BuildingKind.Watchtower, C - 4, C - 4);
        Run(world, new SpawnDemons(DemonKind.Bloater, C - 8, C, 1));
        var events = RunSeconds(world, 20);
        Assert.Contains(events, e => e is DemonBurst);
        Assert.True(wall.Hp < wall.Def.Hp, "the burst should reach the wall");
    }

    [Fact]
    public void ABruteBreaksAWallFarFasterThanAnImp()
    {
        double SecondsToBreak(DemonKind kind)
        {
            var world = Rich();
            foreach (var c in SealedRing(world, 3)) world.Enqueue(c);
            RunSeconds(world, 3);
            Run(world, new SpawnDemons(kind, C - 6, C, 1));
            for (int t = 0; t < 180 * Balance.TickHz; t++)
            {
                world.Step();
                if (world.DrainEvents().OfType<BuildingDestroyed>().Any(d => d.Kind == BuildingKind.Wall)) return t / (double)Balance.TickHz;
            }
            return double.PositiveInfinity;
        }
        double brute = SecondsToBreak(DemonKind.Brute), imp = SecondsToBreak(DemonKind.Imp);
        Assert.True(brute * 3 < imp, $"a brute took {brute:F0}s to break a wall, an imp {imp:F0}s");
    }

    [Fact]
    public void WavesBringInNewKindsAsTheRunGoesOn()
    {
        var rules = Rules.Default.WithStartingResources(Plenty).WithSurvival(s => s with
        {
            DaySeconds = 5, Days = 20, FirstWaveDay = 1, WaveEveryDays = 1, TelegraphSeconds = 2, FirstWaveSize = 60, WaveGrowth = 1.0,
            Mix = [new() { Kind = DemonKind.Hound, FromWave = 1, Share = 0.2 }, new() { Kind = DemonKind.Brute, FromWave = 3, Share = 0.1 }],
        });
        var world = World.Create(new WorldOptions(7, Balance.DefaultMapSize, 0, rules, Survival: true));
        var spawned = new List<DemonsSpawned>();
        for (int t = 0; t < 12 * Balance.TickHz; t++)
        {
            world.Step();
            spawned.AddRange(world.DrainEvents().OfType<DemonsSpawned>());
        }
        // Waves 1 and 2 land at 5 s and 10 s: no Brutes yet. Wave 3 at 15 s hasn't landed.
        Assert.Contains(spawned, s => s.Kind == DemonKind.Hound);
        Assert.DoesNotContain(spawned, s => s.Kind == DemonKind.Brute);
        for (int t = 0; t < 6 * Balance.TickHz; t++)
        {
            world.Step();
            spawned.AddRange(world.DrainEvents().OfType<DemonsSpawned>());
        }
        Assert.Contains(spawned, s => s.Kind == DemonKind.Brute && s.Count == 6);
    }
}
