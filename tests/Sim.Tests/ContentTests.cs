using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

/// <summary>The phase 4 roster's special rules: one test per ability.</summary>
public class ContentTests
{
    static Unit Train(World world, Building barracks, UnitKind kind)
    {
        var events = Run(world, new TrainUnit(barracks.Id, kind));
        Assert.Empty(events.OfType<CommandRejected>());
        var trained = RunSeconds(world, world.Def(kind).TrainSeconds + 0.5).OfType<UnitTrained>().Single();
        return world.UnitById(trained.UnitId)!;
    }

    static float Moved(World world, double seconds)
    {
        float x = world.Horde.X[0], y = world.Horde.Y[0];
        RunSeconds(world, seconds);
        float dx = world.Horde.X[0] - x, dy = world.Horde.Y[0] - y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    [Fact]
    public void ABelfrySlowsDemonsInEarshotAndNowhereElse()
    {
        var rules = Rules.Default.Harmless().WithBuilding(BuildingKind.Belfry, d => d with { RequiresTech = null });
        float Distance(bool belfry)
        {
            var world = Rich(rules);
            if (belfry) Built(world, BuildingKind.Belfry, 58, 58);
            Run(world, new SpawnDemons(DemonKind.Imp, 55, 59, 1));
            return Moved(world, 1);
        }
        float free = Distance(false), slowed = Distance(true);
        Assert.InRange(slowed / free, 0.45f, 0.65f);

        var w = Rich(rules);
        var b = Built(w, BuildingKind.Belfry, 58, 58);
        RunSeconds(w, 0.1);
        Assert.Equal(b.Def.SlowFactor, w.SlowAt(b.CentreX, b.CentreY));
        Assert.Equal(1f, w.SlowAt(b.CentreX + 10, b.CentreY));
    }

    [Fact]
    public void ABelfryNeedsMasonry()
    {
        var world = Rich();
        var events = Run(world, new PlaceBuilding(BuildingKind.Belfry, C - 12, C));
        Assert.Equal("needs Masonry", Assert.Single(events.OfType<CommandRejected>()).Reason);
    }

    [Fact]
    public void ASkyspireShootsFliersAndLetsWalkersBy()
    {
        var world = Rich(Dummies());
        Built(world, BuildingKind.Skyspire, C - 12, C);
        Run(world, new SpawnDemons(DemonKind.Imp, C - 14, C, 1));
        Run(world, new SpawnDemons(DemonKind.Gargoyle, C - 12, C + 8, 1));
        RunSeconds(world, 10);
        Assert.Equal(DemonKind.Imp, Assert.Single(Enumerable.Range(0, world.Horde.Count).Select(i => world.Horde.Kind[i])));
        Assert.Equal(world.Rules[DemonKind.Imp].Hp, world.Horde.Hp[0]);
    }

    [Fact]
    public void ABombardNeverHitsFliers()
    {
        // Alone in range, a Gargoyle is ignored.
        var world = Rich(Dummies());
        Built(world, BuildingKind.Bombard, C + 6, C - 1);
        Run(world, new SpawnDemons(DemonKind.Gargoyle, C + 7, C + 5, 1));
        RunSeconds(world, 10);
        Assert.Equal(world.Rules[DemonKind.Gargoyle].Hp, Assert.Single(world.Horde.Hp.Take(world.Horde.Count)));

        // Beside an Imp it's shelling, the blast still passes it by.
        world = Rich(Dummies());
        Built(world, BuildingKind.Bombard, C + 6, C - 1);
        Run(world, new SpawnDemons(DemonKind.Imp, C + 7, C + 5, 1));
        Run(world, new SpawnDemons(DemonKind.Gargoyle, C + 7, C + 5, 1));
        RunSeconds(world, 10);
        Assert.Equal(DemonKind.Gargoyle, Assert.Single(Enumerable.Range(0, world.Horde.Count).Select(i => world.Horde.Kind[i])));
        Assert.Equal(world.Rules[DemonKind.Gargoyle].Hp, world.Horde.Hp[0]);
    }

    [Fact]
    public void ACenserBurnsTheCrowdAtItsFoot()
    {
        var world = Rich(Dummies());
        var censer = Built(world, BuildingKind.Censer, C - 12, C);
        Run(world, new SpawnDemons(DemonKind.Imp, C - 14, C, 30));
        int before = world.Horde.Count;
        RunSeconds(world, 15);
        // Everything within its short reach burned, several at a time; the rest stood out of range.
        float r = censer.Def.Weapon!.Range;
        for (int i = 0; i < world.Horde.Count; i++)
        {
            float dx = world.Horde.X[i] - censer.CentreX, dy = world.Horde.Y[i] - censer.CentreY;
            Assert.True(dx * dx + dy * dy > r * r, "a demon in reach survived");
        }
        Assert.True(before - world.Horde.Count >= 6, $"a Censer killed only {before - world.Horde.Count}");
    }

    [Fact]
    public void AChaplainHealsTheSoldiersAroundIt()
    {
        var rules = Rules.Default.WithUnit(UnitKind.Chaplain, d => d with { RequiresTech = null });
        var world = Rich(rules);
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        var militia = Train(world, barracks, UnitKind.Militia);
        var chaplain = Train(world, barracks, UnitKind.Chaplain);
        militia.Hp = 10;
        chaplain.Hp = 10;
        RunSeconds(world, 4);
        Assert.InRange(militia.Hp, 25, militia.Def.Hp);
        Assert.Equal(10, chaplain.Hp); // it heals others, not itself
        RunSeconds(world, 30);
        Assert.Equal(militia.Def.Hp, militia.Hp);
    }

    [Fact]
    public void ChaplainsAndOutridersNeedTheirTechs()
    {
        var world = Rich();
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        Assert.Equal("needs Hallowing", Assert.Single(Run(world, new TrainUnit(barracks.Id, UnitKind.Chaplain)).OfType<CommandRejected>()).Reason);
        Assert.Equal("needs Husbandry", Assert.Single(Run(world, new TrainUnit(barracks.Id, UnitKind.Outrider)).OfType<CommandRejected>()).Reason);
    }

    [Fact]
    public void AHowlerInSightOfTheColonyWakesThePacksNearIt()
    {
        bool Woke(DemonKind kind, int sight)
        {
            var rules = Rules.Default.Harmless().WithDemon(DemonKind.Howler, d => d with { HowlSight = sight });
            var world = World.Create(new WorldOptions(7, Balance.DefaultMapSize, 30, rules));
            world.DrainEvents();
            var pack = world.Packs.OrderByDescending(p => MathF.Abs(p.X - C) + MathF.Abs(p.Y - C)).First(); // far from every building
            // Beside the pack, outside anything but a howl's reach.
            var tile = world.FindReachableTileNear(pack.X + 8, pack.Y, 4)!.Value;
            Run(world, new SpawnDemons(kind, tile.X, tile.Y, 1));
            return RunSeconds(world, 6).OfType<PackWoke>().Any(w => w.PackId == pack.Id);
        }
        Assert.True(Woke(DemonKind.Howler, sight: 200), "in sight of the colony, the howl should wake the pack");
        Assert.False(Woke(DemonKind.Howler, sight: 12), "out in the wilds, with no building in sight, it keeps quiet");
        Assert.False(Woke(DemonKind.Imp, sight: 200), "an Imp walking by is silent");
    }

    [Fact]
    public void ABroodmotherBurstsIntoImpsWhenKilled()
    {
        var world = Rich(Dummies());
        Run(world, new SpawnDemons(DemonKind.Broodmother, C - 20, C, 1));
        world.Horde.Hp[0] = 0;
        var events = Run(world);
        Assert.Equal(1, events.OfType<DemonsKilled>().Single().Count);
        Assert.Equal(world.Rules[DemonKind.Broodmother].BroodCount, world.Horde.Count);
        Assert.All(Enumerable.Range(0, world.Horde.Count), i => Assert.Equal(DemonKind.Imp, world.Horde.Kind[i]));
    }
}
