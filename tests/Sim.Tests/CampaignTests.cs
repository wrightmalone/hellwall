using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class CampaignTests
{
    static World Start(ScenarioDef s, Rules? rules = null)
    {
        var world = World.Create(s.Options(rules ?? Rules.Default));
        world.DrainEvents();
        return world;
    }

    /// <summary>A mission on the quick rules: one-second days, harmless demons, lots to spend.</summary>
    static Rules Quick() => Rules.Default.Harmless().WithStartingResources(Plenty).WithWilds(w => w with { Packs = 0 })
        .WithSurvival(s => s with { DaySeconds = 1, FirstWaveDay = 1, WaveEveryDays = 1, TelegraphSeconds = 0.5f, FirstWaveSize = 5 });

    [Fact]
    public void TheCampaignParsesAndEveryMissionStartsAndCanBeReached()
    {
        var c = Campaign.Default;
        Assert.NotEmpty(c.Scenarios);
        foreach (var s in c.Scenarios)
        {
            var world = Start(s);
            Assert.Equal(s.Goals.Length, world.Goals.Length);
            Assert.Equal(s.Map, world.Map);
        }
        // Winning missions in order opens them all.
        var won = new HashSet<string>();
        for (bool more = true; more;)
        {
            more = false;
            foreach (var s in c.Scenarios.Where(s => !won.Contains(s.Id) && c.IsOpen(s, won)))
            {
                won.Add(s.Id);
                more = true;
            }
        }
        Assert.Equal(c.Scenarios.Length, won.Count);
        Assert.Single(c.Scenarios, s => s.Requires.Length == 0);
    }

    [Fact]
    public void AMissionsLocksRefuseBuildingsUnitsAndResearch()
    {
        var s = new ScenarioDef { Id = "t", Seed = 7, MapSize = Balance.DefaultMapSize, LockedBuildings = [BuildingKind.Scriptorium], LockedUnits = [UnitKind.Templar], LockedTechs = ["tithes"] };
        var world = Start(s, Rules.Default.WithStartingResources(Plenty));
        Assert.Equal("not in this mission", world.CheckPlacement(BuildingKind.Scriptorium, 58, 58));
        Assert.Equal("not in this mission", world.CheckResearch("tithes"));
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        var rejected = Run(world, new TrainUnit(barracks.Id, UnitKind.Templar)).OfType<CommandRejected>().Single();
        Assert.Equal("not in this mission", rejected.Reason);
    }

    [Fact]
    public void ClosingTheGatesWinsAGatesMissionButOnlyWhenEveryGoalIsDone()
    {
        var s = new ScenarioDef { Id = "t", Days = 400, Hellgates = 2, Objectives = [new() { Kind = ObjectiveKind.CloseGates, Count = 2 }, new() { Kind = ObjectiveKind.Population, Count = 1 }] };
        var world = Start(s, Quick());
        Assert.Equal(2, world.Gates.Count);
        world.DamageGate(world.GateList[0], 1e9f);
        RunSeconds(world, 1);
        Assert.Equal(Outcome.Running, world.Outcome);
        world.DamageGate(world.GateList[1], 1e9f);
        var events = RunSeconds(world, 1);
        Assert.Contains(events, e => e is ObjectiveCompleted { Kind: ObjectiveKind.CloseGates });
        Assert.Equal(Outcome.Won, world.Outcome);
    }

    [Fact]
    public void APopulationReachedOnceStaysDone()
    {
        var s = new ScenarioDef { Id = "t", Days = 400, Hellgates = 0, Objectives = [new() { Kind = ObjectiveKind.Population, Count = 5 }, new() { Kind = ObjectiveKind.Slay, Count = 1_000_000 }] };
        var world = Start(s, Quick());
        RunSeconds(world, 1);
        Assert.True(world.GoalsDone[0], "the Keep's own colonists count");
        Assert.False(world.GoalsDone[1]);
        Assert.Equal(Outcome.Running, world.Outcome);
    }

    [Fact]
    public void AGoalStillUnmetWhenTheConvergenceIsSpentLosesTheMission()
    {
        var s = new ScenarioDef { Id = "t", Days = 3, Hellgates = 0, Convergence = 0.001, Objectives = [new() { Kind = ObjectiveKind.Slay, Count = 1_000_000 }] };
        var world = Start(s, Quick().WithSurvival(v => v with { ConvergenceHoldSeconds = 1 }));
        RunSeconds(world, 30);
        Assert.Equal(Outcome.Lost, world.Outcome);
    }

    [Fact]
    public void TriggersFireOnTheirDayOrGoalOnceEach()
    {
        var s = new ScenarioDef
        {
            Id = "t", Days = 400, Hellgates = 1,
            Objectives = [new() { Kind = ObjectiveKind.CloseGates, Count = 1 }, new() { Kind = ObjectiveKind.Slay, Count = 1_000_000 }],
            Triggers =
            [
                new() { Day = 3, Say = "They come from the west", SpawnKind = DemonKind.Hound, SpawnCount = 12, SpawnSide = Side.West },
                new() { AfterGoal = 0, Say = "Relief arrives", Give = new Cost { Gold = 500 } },
            ],
        };
        var world = Start(s, Quick());
        var early = RunSeconds(world, 1.5);
        Assert.DoesNotContain(early, e => e is ScenarioMessage);
        var day3 = RunSeconds(world, 2);
        var west = Assert.Single(day3.OfType<ScenarioMessage>());
        Assert.Equal("They come from the west", west.Text);
        Assert.Equal(12, west.Spawned);

        double gold = world.Colony[Resource.Gold];
        world.DamageGate(world.GateList[0], 1e9f);
        var relief = RunSeconds(world, 1).OfType<ScenarioMessage>().ToList();
        Assert.Equal("Relief arrives", Assert.Single(relief).Text);
        Assert.True(world.Colony[Resource.Gold] >= gold + 499);
        Assert.Empty(RunSeconds(world, 5).OfType<ScenarioMessage>());
    }

    [Fact]
    public void AMissionSurvivesASave()
    {
        var s = Campaign.Default.Scenarios[1];
        var world = Start(s);
        for (int t = 0; t < 300; t++) world.Step();
        world.FlushCommands();
        var loaded = World.Load(world.Save(), Rules.Default);
        Assert.Equal(s.Id, loaded.Scenario?.Id);
        Assert.Equal(world.GoalsDone, loaded.GoalsDone);
        for (int t = 0; t < 200; t++) { world.Step(); loaded.Step(); }
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }
}
