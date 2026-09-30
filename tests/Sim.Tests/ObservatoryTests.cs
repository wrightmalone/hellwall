using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

/// <summary>The Observatory: charts the fog a patch at a time, nearest first, for gold and a crew.</summary>
public class ObservatoryTests
{
    /// <summary>A fogged, harmless survival colony with Cartography learned and an Observatory at work.</summary>
    static (World World, Building Observatory, Building Lab) Charting(Rules? rules = null)
    {
        var world = World.Create(new WorldOptions(7, 128, 0, (rules ?? OpenWoods).Harmless().WithStartingResources(Plenty), Survival: true));
        world.DrainEvents();
        Assert.True(world.Vision.Enabled);
        var (hx, hy) = world.Home;
        Built(world, BuildingKind.House, hx + 4, hy - 2);
        Built(world, BuildingKind.House, hx - 5, hy - 2);
        var lab = Built(world, BuildingKind.Scriptorium, hx - 2, hy + 4);
        Run(world, new Research(lab.Id, "cartography"));
        RunSeconds(world, world.Rules.Tech("cartography").Seconds + 1);
        Assert.True(world.Tech.Has("cartography"));
        var obs = Built(world, BuildingKind.Observatory, hx + 3, hy + 4);
        RunSeconds(world, 0.1);
        Assert.True(obs.Active);
        return (world, obs, lab);
    }

    static int ExploredCount(World world) => world.Vision.Explored.Count(e => e);

    [Fact]
    public void ItChartsUnknownGroundInRangeNearestFirst()
    {
        var (world, obs, _) = Charting();
        var charts = RunSeconds(world, obs.Def.ScanEvery * 3 + 1).OfType<GroundCharted>().ToList();
        Assert.Equal(3, charts.Count);
        float nearestSeen = 0;
        foreach (var c in charts)
        {
            float dx = c.X + c.Size / 2f - obs.CentreX, dy = c.Y + c.Size / 2f - obs.CentreY;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            Assert.True(d <= obs.Def.ScanRange);
            Assert.True(d >= nearestSeen - c.Size, "a later chart jumped well inside an earlier one");
            nearestSeen = Math.Max(nearestSeen, d);
            for (int y = c.Y; y < c.Y + c.Size; y++)
                for (int x = c.X; x < c.X + c.Size; x++) Assert.True(world.Vision.IsExplored(x, y));
        }
        // Nearest first: none of the charts was far from the edge of what was known.
        Assert.All(charts, c => Assert.True(MathF.Sqrt(MathF.Pow(c.X - obs.CentreX, 2) + MathF.Pow(c.Y - obs.CentreY, 2)) < world.Rules.Fog.StartReveal + 3 * c.Size));
    }

    [Fact]
    public void PausedItChartsNothingAndCostsNothing()
    {
        var (world, obs, _) = Charting();
        double running = world.Colony.NetPerSecond[(int)Resource.Gold];
        Run(world, new SetPaused(obs.Id, true));
        RunSeconds(world, 0.2);
        Assert.Equal(-obs.Def.Gold, world.Colony.NetPerSecond[(int)Resource.Gold] - running, 3);
        int before = ExploredCount(world);
        Assert.Empty(RunSeconds(world, obs.Def.ScanEvery * 2).OfType<GroundCharted>());
        Assert.Equal(before, ExploredCount(world));
    }

    [Fact]
    public void WithNothingLeftToChartItWaitsWithoutWasting()
    {
        var (world, obs, _) = Charting();
        var all = new bool[world.Vision.Explored.Length];
        Array.Fill(all, true);
        world.Vision.Load(all);
        Assert.Empty(RunSeconds(world, obs.Def.ScanEvery + 2).OfType<GroundCharted>());
        Assert.True(obs.Exhausted);
        Assert.True(obs.ScanTimer >= obs.Def.ScanEvery);
    }

    [Fact]
    public void StarChartsChartFasterAndFurther()
    {
        var (world, obs, lab) = Charting();
        float every = obs.Def.ScanEvery, range = obs.Def.ScanRange;
        Run(world, new Research(lab.Id, "starcharts"));
        RunSeconds(world, world.Rules.Tech("starcharts").Seconds + 1);
        Assert.True(world.Tech.Has("starcharts"));
        Assert.True(obs.Def.ScanEvery < every);
        Assert.True(obs.Def.ScanRange > range);
    }

    [Fact]
    public void TheGoldSliderLeavesItsRunningCostAlone()
    {
        var rich = Rules.Default.WithEconomy([4, 1, 1, 1, 1, 1, 1, 1, 1]);
        Assert.Equal(Rules.Default.Buildings[(int)BuildingKind.Observatory].Gold, rich.Buildings[(int)BuildingKind.Observatory].Gold);
        Assert.Equal(Rules.Default.Buildings[(int)BuildingKind.Keep].Gold * 4, rich.Buildings[(int)BuildingKind.Keep].Gold, 6);
    }

    [Fact]
    public void ASaveMidChartCarriesOn()
    {
        var (world, obs, _) = Charting();
        RunSeconds(world, obs.Def.ScanEvery * 1.5);
        var loaded = World.Load(world.Save(), world.Rules);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
        var a = RunSeconds(world, obs.Def.ScanEvery).OfType<GroundCharted>().Select(c => (c.X, c.Y)).ToList();
        var b = RunSeconds(loaded, obs.Def.ScanEvery).OfType<GroundCharted>().Select(c => (c.X, c.Y)).ToList();
        Assert.Single(a);
        Assert.Equal(a, b);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }
}
