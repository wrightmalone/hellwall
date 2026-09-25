using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class SaveTests
{
    /// <summary>
    /// A world with a bit of everything in flight: a town mid-construction, a
    /// Barracks training, soldiers under orders, a wave announced and one
    /// landed, dormant and woken packs, noise, and a possessed building.
    /// </summary>
    static World Busy()
    {
        var rules = Rules.Default.WithStartingResources(Plenty).WithSurvival(s => s with
        {
            DaySeconds = 10, Days = 30, FirstWaveDay = 2, WaveEveryDays = 1, TelegraphSeconds = 5, FirstWaveSize = 12,
        });
        var world = World.Create(new WorldOptions(7, Balance.DefaultMapSize, 6, rules, Survival: true));
        foreach (var (x, y) in new[] { (58, 58), (69, 58) }) world.Enqueue(new PlaceBuilding(BuildingKind.Watchtower, x, y));
        world.Enqueue(new PlaceBuilding(BuildingKind.House, 68, 62));
        world.Enqueue(new PlaceBuilding(BuildingKind.House, 59, 63));
        world.Enqueue(new PlaceBuilding(BuildingKind.Barracks, 59, 66));
        world.Enqueue(new PlaceBuilding(BuildingKind.Wardstone, C + 11, C));
        RunSeconds(world, 16);
        var barracks = world.Buildings.Single(b => b.Kind == BuildingKind.Barracks);
        for (int i = 0; i < 8; i++) world.Enqueue(new TrainUnit(barracks.Id, UnitKind.Militia));
        RunSeconds(world, 14);
        world.Enqueue(new OrderUnits(world.Units.Select(u => u.Id).ToArray(), OrderKind.AttackMove, 60, 60));
        world.Enqueue(new MakeNoise(world.Packs[0].X, world.Packs[0].Y, 10, 3));
        // A crowd right against the west Watchtower (battered, not possessed), so demons are mid-attack when we save.
        world.Enqueue(new SpawnDemons(DemonKind.Imp, 56, 58, 20));
        RunSeconds(world, 3);
        return world;
    }

    [Fact]
    public void TheBusyWorldReallyIsBusy()
    {
        var world = Busy();
        Assert.True(world.Horde.Count > 0);
        Assert.NotEmpty(world.Units);
        Assert.Contains(world.Units, u => u.Order == OrderKind.AttackMove);
        Assert.Contains(world.Packs, p => p.Awake);
        Assert.Contains(world.Packs, p => !p.Awake);
        Assert.Contains(world.Survival!.Waves, w => w.Landed);
        // Every field has to hold a non-default value somewhere, or dropping it from the save goes unnoticed.
        var h = world.Horde;
        var checks = new (string What, bool Ok)[]
        {
            ("a demon mid-attack", Enumerable.Range(0, h.Count).Any(i => h.Cooldown[i] > 0)),
            ("a demon moving", Enumerable.Range(0, h.Count).Any(i => h.VX[i] != 0)),
            ("a damaged building", world.Buildings.Any(b => b.Hp < b.Def.Hp)),
            ("a Barracks training", world.Buildings.Any(b => b.Queue.Count > 0 || b.TrainProgress > 0)),
            ("a tower reloading", world.Buildings.Any(b => b.Cooldown > 0)),
            ("a soldier reloading", world.Units.Any(u => u.Cooldown > 0)),
            ("noise in the air", world.Noise.Level.Any(l => l > 0)),
        };
        var missing = checks.Where(c => !c.Ok).Select(c => c.What).ToList();
        Assert.True(missing.Count == 0, "the busy world lacks: " + string.Join(", ", missing));
        Assert.Equal(Outcome.Running, world.Outcome);
    }

    [Fact]
    public void ALoadedWorldHashesTheSameAsTheOneSaved()
    {
        var world = Busy();
        var loaded = World.Load(world.Save(), world.Rules);
        Assert.Equal(StateHash.Hex(world), StateHash.Hex(loaded));
    }

    [Fact]
    public void ALoadedWorldPlaysOnExactlyAsTheOriginalDoes()
    {
        var world = Busy();
        var loaded = World.Load(world.Save(), world.Rules);
        for (int t = 0; t < 30 * Balance.TickHz; t++)
        {
            if (t == 100)
            {
                // The same order to both, mid-run.
                var ids = world.Units.Select(u => u.Id).ToArray();
                world.Enqueue(new OrderUnits(ids, OrderKind.Move, C - 5, C + 5));
                loaded.Enqueue(new OrderUnits(ids, OrderKind.Move, C - 5, C + 5));
            }
            world.Step();
            loaded.Step();
            Assert.True(StateHash.Hex(world) == StateHash.Hex(loaded), $"diverged {t} ticks after loading");
        }
        Assert.True(world.Stats.DemonsKilled > 0, "the continuation should have included some fighting");
    }

    [Fact]
    public void ASaveOnlyLoadsUnderItsOwnRules()
    {
        var world = Busy();
        var bytes = world.Save();
        Assert.Throws<FormatException>(() => World.Load(bytes, Rules.Default));
        Assert.Throws<FormatException>(() => World.Load([1, 2, 3, 4, 5, 6, 7, 8], world.Rules));
    }

    [Fact]
    public void SavingWithCommandsPendingIsRefused()
    {
        var world = World.Create(new WorldOptions(7));
        world.Enqueue(new PlaceBuilding(BuildingKind.House, 68, 62));
        Assert.Throws<InvalidOperationException>(() => world.Save());
    }
}

public class DamagedSaveTests
{
    [Fact]
    public void ATruncatedSaveIsRefusedCleanly()
    {
        var world = TestWorlds.Rich();
        var bytes = world.Save();
        foreach (int keep in new[] { 12, bytes.Length / 3, bytes.Length / 2, bytes.Length - 3 })
            Assert.Throws<FormatException>(() => World.Load(bytes[..keep], world.Rules));
    }
}
