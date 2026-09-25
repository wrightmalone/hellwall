using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class SurvivalTests
{
    /// <summary>A three-minute run: 10 s days, a wave every day from day 2, Convergence at the end of day 18.</summary>
    static Rules Quick(int convergence = 40, int waveSize = 4, double growth = 1.1) => Rules.Default.WithSurvival(s => s with
    {
        DaySeconds = 10, Days = 18, FirstWaveDay = 2, WaveEveryDays = 1, TelegraphSeconds = 5, ConvergenceWarnSeconds = 5,
        FirstWaveSize = waveSize, WaveGrowth = growth, ConvergenceSize = convergence,
    });

    /// <summary>A quick survival run without Hellgates or the wilds (they have their own tests).</summary>
    static World Run(Rules rules, bool survival = true) =>
        World.Create(new WorldOptions(7, Balance.DefaultMapSize, 0,
            rules.WithStartingResources(Plenty).WithHellgates(g => g with { Count = 0 }).WithWilds(w => w with { Packs = 0, Strays = 0 }), survival));

    [Fact]
    public void TheScheduleGrowsAndEndsInAConvergenceFromEverySide()
    {
        var s = new Survival(Rules.Default.Survival);
        Assert.True(s.Waves.Count > 10);
        Assert.All(s.Waves.Zip(s.Waves.Skip(1)), p => Assert.True(p.Second.LandsAtTick > p.First.LandsAtTick));
        var regular = s.Waves.Where(w => !w.Final).ToList();
        Assert.True(regular[^1].Size > regular[0].Size * 5, "waves should grow substantially over the run");
        var final = Assert.Single(s.Waves, w => w.Final);
        Assert.Equal(s.Waves[^1], final);
        Assert.Equal(Rules.Default.Survival.Days * s.TicksPerDay, final.LandsAtTick);
    }

    [Fact]
    public void AWaveIsAnnouncedWithItsSidesBeforeItLands()
    {
        var world = Run(Quick());
        var events = RunSeconds(world, 25);
        var announced = events.OfType<WaveAnnounced>().First();
        var landed = events.OfType<WaveLanded>().First(l => l.Number == announced.Number);
        Assert.NotEmpty(announced.Sides);
        Assert.Equal(announced.LandsAtTick, landed.Tick);
        Assert.Equal((int)(5 * Balance.TickHz), announced.LandsAtTick - announced.Tick);
        Assert.Equal(announced.Size, landed.Spawned);
    }

    [Fact]
    public void WithoutASurvivalScheduleNoWavesCome()
    {
        var world = Run(Quick(), survival: false);
        var events = RunSeconds(world, 60);
        Assert.DoesNotContain(events, e => e is WaveAnnounced or WaveLanded);
        Assert.Equal(0, world.Horde.Count);
    }

    [Fact]
    public void SurvivingTheConvergenceWinsTheRun()
    {
        // A garrisoned ring of towers against tiny waves: this should be a comfortable win.
        var world = Run(Quick(convergence: 12, waveSize: 2, growth: 1.0));
        foreach (var (x, y) in new[] { (58, 58), (69, 58), (58, 69), (69, 69) }) world.Enqueue(new PlaceBuilding(BuildingKind.Watchtower, x, y));
        world.Enqueue(new PlaceBuilding(BuildingKind.House, 59, 63));
        var events = RunSeconds(world, 18 * 10 + 90);

        string where = string.Join(", ", Enumerable.Range(0, world.Horde.Count).Take(8).Select(i =>
            $"{world.Horde.Kind[i]}@({world.Horde.X[i]:F0},{world.Horde.Y[i]:F0}) d={world.Flow.DistAt((int)world.Horde.X[i], (int)world.Horde.Y[i])}"));
        Assert.True(world.Outcome == Outcome.Won, $"{world.Outcome} on day {world.Day}, {world.Horde.Count} demons left: {where}");
        Assert.Contains(events, e => e is WaveLanded { Final: true });
        Assert.Contains(events, e => e is OutcomeChanged { Outcome: Outcome.Won });
    }

    [Fact]
    public void LosingTheKeepBeforeTheEndLosesTheRun()
    {
        var world = Run(Quick(convergence: 400));
        var events = RunSeconds(world, 18 * 10 + 120);
        Assert.Equal(Outcome.Lost, world.Outcome);
        Assert.DoesNotContain(events, e => e is OutcomeChanged { Outcome: Outcome.Won });
    }

    [Fact]
    public void TheDayCounterFollowsTheClock()
    {
        var world = Run(Quick());
        Assert.Equal(1, world.Day);
        RunSeconds(world, 10);
        Assert.Equal(2, world.Day);
    }
}
