namespace Hellwall.Sim.Tests;

public class EndlessTests
{
    /// <summary>One-second days, a wave a day, a corruption every other day: a whole endless run in seconds. Harmless, so the Keep stands.</summary>
    static Rules Quick(Rules? rules = null) => (rules ?? Rules.Default).Harmless()
        .WithHellgates(h => h with { Count = 0 })
        .WithWilds(w => w with { Packs = 0 })
        .WithSurvival(s => s with
        {
            DaySeconds = 1, Days = 5, FirstWaveDay = 1, WaveEveryDays = 1, TelegraphSeconds = 0.5f, FirstWaveSize = 10,
            FirstCorruptionDay = 2, CorruptionEveryDays = 2, CorruptionWarnSeconds = 0.5f, SurgeEveryWaves = 4,
        });

    static World Endless(Rules rules) => World.Create(new WorldOptions(11, 128, 0, rules, Survival: true, Endless: true));

    static List<SimEvent> Days(World world, int days) => TestWorlds.RunSeconds(world, days * world.Rules.Survival.DaySeconds);

    [Fact]
    public void AnEndlessRunGoesPastItsLastDayWithNoConvergence()
    {
        var world = Endless(Quick());
        var events = Days(world, 20);
        Assert.Equal(Outcome.Running, world.Outcome);
        Assert.DoesNotContain(world.Survival!.Waves, w => w.Final);
        Assert.True(events.OfType<WaveLanded>().Count() >= 18, "a wave a day, well past day 5");
    }

    [Fact]
    public void EverySurgeComesFromEverySideAndBigger()
    {
        var world = Endless(Quick());
        var announced = Days(world, 10).OfType<WaveAnnounced>().ToList();
        var surge = announced.Single(a => a.Number == 4);
        Assert.Equal(4, surge.Sides.Length);
        Assert.True(surge.Size > announced.Single(a => a.Number == 3).Size * 2);
    }

    [Fact]
    public void CorruptionsAreAnnouncedThenTakeHoldAndDontRepeatUntilThePoolIsSpent()
    {
        var world = Endless(Quick());
        var events = Days(world, 2 * world.Rules.Corruptions.Length + 1);
        var announced = events.OfType<CorruptionAnnounced>().ToList();
        var took = events.OfType<CorruptionTook>().ToList();
        Assert.Equal(world.Rules.Corruptions.Length, took.Count);
        Assert.Equal(took.Select(t => t.Id), announced.Select(a => a.Id));
        Assert.Equal(took.Count, took.Select(t => t.Id).Distinct().Count());
        Assert.All(announced.Zip(took), p => Assert.True(p.First.Tick < p.Second.Tick));
    }

    [Fact]
    public void ACorruptionChangesTheHordeFromThenOn()
    {
        var hides = new CorruptionDef { Id = "hides", Name = "Hides", Demons = [new DemonModifier { Stat = "hp", Mul = 1.5 }] };
        var world = Endless(Quick().WithCorruptions([hides]));
        float before = world.Def(DemonKind.Imp).Hp;
        Days(world, 3);
        Assert.Contains("hides", world.Survival!.Corruptions);
        Assert.Equal(before * 1.5f, world.Def(DemonKind.Imp).Hp, 3);
        Assert.Equal(world.Rules[DemonKind.Imp].Hp, before); // the rules themselves are untouched
    }

    [Fact]
    public void DifferentSeedsDrawDifferentCorruptions()
    {
        var orders = new HashSet<string>();
        foreach (uint seed in new uint[] { 1, 2, 3, 4, 5 })
        {
            var world = World.Create(new WorldOptions(seed, 128, 0, Quick(), Survival: true, Endless: true));
            Days(world, 7);
            orders.Add(string.Join(",", world.Survival!.Corruptions));
        }
        Assert.True(orders.Count >= 3, $"five seeds drew only {orders.Count} different openings");
    }

    [Fact]
    public void AnEndlessSaveCarriesItsCorruptionsAndPlannedWaves()
    {
        var rules = Quick();
        var world = Endless(rules);
        Days(world, 7);
        world.FlushCommands();
        var loaded = World.Load(world.Save(), rules);
        Assert.True(loaded.Survival!.Endless);
        Assert.Equal(world.Survival!.Corruptions, loaded.Survival.Corruptions);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
        for (int t = 0; t < 100; t++) { world.Step(); loaded.Step(); }
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }
}
