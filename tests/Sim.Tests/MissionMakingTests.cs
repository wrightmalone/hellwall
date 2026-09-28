namespace Hellwall.Sim.Tests;

/// <summary>What a hand-made mission can set beyond a map: where the Keep stands, what's already built, goals and events.</summary>
public class MissionMakingTests
{
    static ScenarioDef Mission(Func<ScenarioDef, ScenarioDef>? change = null)
    {
        var tiles = new Tile[128 * 128];
        var def = new ScenarioDef { Id = "map-test", Name = "Test", MapSize = 128, Tiles = ScenarioDef.EncodeTiles(tiles), IsMission = true, Packs = 0, Strays = 0, Hellgates = 0, Ruins = 0 };
        return change?.Invoke(def) ?? def;
    }

    [Fact]
    public void TheKeepStandsWhereTheMissionPutsIt()
    {
        var world = World.Create(Mission(m => m with { KeepX = 30, KeepY = 90 }).Options(Rules.Default));
        var keep = world.Buildings.Single(b => b.Kind == BuildingKind.Keep);
        Assert.Equal((30, 90), world.Home);
        Assert.InRange(keep.CentreX, 29, 32);
        Assert.InRange(keep.CentreY, 89, 92);
        Assert.True(world.Colony.Consecrated[world.Terrain.Index(30, 90)]);
    }

    [Fact]
    public void ItsBuildingsStandFinishedFromTheStart()
    {
        var m = Mission(m => m with { PlacedBuildings = [new(BuildingKind.House, 70, 64), new(BuildingKind.Gate, 70, 70, Turned: true), new(BuildingKind.Keep, 10, 10)] });
        var world = World.Create(m.Options(Rules.Default));
        var house = world.Buildings.Single(b => b.Kind == BuildingKind.House);
        Assert.True(house.Complete);
        Assert.Equal((70, 64), (house.X, house.Y));
        var gate = world.Buildings.Single(b => b.Kind == BuildingKind.Gate);
        Assert.Equal((1, 3), (gate.W, gate.H));
        Assert.Single(world.Buildings, b => b.Kind == BuildingKind.Keep); // a second Keep is never placed
    }

    [Fact]
    public void ItsGoalsAndEventsPlay()
    {
        var m = Mission(m => m with
        {
            Objectives = [new ObjectiveDef { Kind = ObjectiveKind.Slay, Count = 1 }],
            Triggers = [new TriggerDef { Day = 1, Say = "hello", Give = new Cost { Gold = 500 } }],
        });
        var world = World.Create(m.Options(Rules.Default.Harmless()));
        double gold = world.Colony[Resource.Gold];
        var events = TestWorlds.RunSeconds(world, 2);
        Assert.Contains(events, e => e is ScenarioMessage { Text: "hello" });
        Assert.True(world.Colony[Resource.Gold] >= gold + 500 - 1);
        Assert.Equal(ObjectiveKind.Slay, Assert.Single(world.Goals).Kind);
    }

    [Fact]
    public void ADoctoredMissionIsTurnedAway()
    {
        Assert.Null(Mission().Problem());
        Assert.NotNull(Mission(m => m with { KeepX = 500, KeepY = 3 }).Problem());
        Assert.NotNull(Mission(m => m with { Triggers = [new TriggerDef { Day = 2, SpawnCount = 1_000_000 }] }).Problem());
        Assert.NotNull(Mission(m => m with { Triggers = [new TriggerDef { AfterGoal = 5 }] }).Problem());
        Assert.NotNull(Mission(m => m with { Objectives = [.. Enumerable.Repeat(new ObjectiveDef { Kind = ObjectiveKind.Slay, Count = 5 }, 20)] }).Problem());
        Assert.NotNull(Mission(m => m with { PlacedBuildings = [new(BuildingKind.Keep, 3, 3)] }).Problem());
    }

    [Fact]
    public void AMissionWithEverythingSurvivesASave()
    {
        var m = Mission(m => m with { KeepX = 40, KeepY = 40, PlacedBuildings = [new(BuildingKind.Farm, 50, 44)], Triggers = [new TriggerDef { Day = 3, Say = "later" }] });
        var world = World.Create(m.Options(Rules.Default.Harmless()));
        TestWorlds.RunSeconds(world, 2);
        var loaded = World.Load(world.Save(), Rules.Default.Harmless());
        Assert.Equal(world.Home, loaded.Home);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }
}
