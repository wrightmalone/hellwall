using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class PossessionTests
{
    /// <summary>One imp, next to the building. It can strike on the tick it spawns, so this returns that tick's events.</summary>
    static List<SimEvent> OneImpBeside(World world, Building b) =>
        Run(world, new SpawnDemons(DemonKind.Imp, b.X + b.W, b.Y, 1));

    [Fact]
    public void ADemonReachingAHouseTakesItAndItsPeopleComeOutAsThralls()
    {
        var world = Rich();
        var house = Built(world, BuildingKind.House, 68, 62);
        RunSeconds(world, 0.1);
        int colonists = world.Colony.Colonists;

        var events = OneImpBeside(world, house);
        events.AddRange(RunSeconds(world, 3));
        var possessed = Assert.Single(events.OfType<BuildingPossessed>());
        Assert.Equal(house.Id, possessed.BuildingId);
        Assert.Equal(house.Def.Housing, possessed.Occupants);
        Assert.Equal(colonists - house.Def.Housing, world.Colony.Colonists);

        events = RunSeconds(world, house.Def.Housing * world.Rules.PossessionSpawnSeconds + 1);
        Assert.Contains(events, e => e is BuildingDestroyed d && d.BuildingId == house.Id);
        int thralls = Enumerable.Range(0, world.Horde.Count).Count(i => world.Horde.Kind[i] == DemonKind.Thrall);
        Assert.True(thralls >= house.Def.Housing - 1, $"expected ~{house.Def.Housing} thralls, found {thralls}");
    }

    [Fact]
    public void PurgingAPossessedBuildingStopsItButRefundsNothing()
    {
        var world = Rich();
        var house = Built(world, BuildingKind.House, 68, 62);
        OneImpBeside(world, house);
        for (int t = 0; t < 60 && !house.Possessed; t++) world.Step();
        Assert.True(house.Possessed);

        double wood = world.Colony[Resource.Wood];
        Run(world, new Demolish(house.Id));
        Assert.Null(world.BuildingById(house.Id));
        Assert.Equal(wood, world.Colony[Resource.Wood], 6);
        int demons = world.Horde.Count;
        RunSeconds(world, 5);
        Assert.True(world.Horde.Count <= demons, "a purged building must not keep releasing thralls");
    }

    [Fact]
    public void WorkplacesAreTakenButTowersAndWallsAreOnlyBatteredDown()
    {
        var world = Rich();
        var hunter = Built(world, BuildingKind.Hunter, 68, 62);
        var tower = Built(world, BuildingKind.Watchtower, 58, 58);
        var wall = Built(world, BuildingKind.Wall, 70, 70);
        RunSeconds(world, 0.2);
        Assert.True(hunter.Staffed && tower.Staffed);
        Assert.Equal(hunter.Def.Workers, hunter.PeopleInside);
        Assert.Equal(0, tower.PeopleInside); // crewed, but a fortification
        Assert.Equal(0, wall.PeopleInside);
        Assert.Equal(0, world.Buildings.First(b => b.Kind == BuildingKind.Keep).PeopleInside);
    }

    [Fact]
    public void TheKeepIsNeverPossessedOnlyDestroyed()
    {
        var world = Rich();
        Run(world, new SpawnDemons(DemonKind.Imp, C, C + 5, 60));
        var events = RunSeconds(world, 90);
        Assert.DoesNotContain(events, e => e is BuildingPossessed { Kind: BuildingKind.Keep });
        Assert.Equal(Outcome.Lost, world.Outcome);
    }

    [Fact]
    public void ASoldierKilledByTheHordeRisesAsAThrall()
    {
        var world = Rich();
        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        world.Enqueue(new TrainUnit(barracks.Id, UnitKind.Militia));
        RunSeconds(world, world.Rules[UnitKind.Militia].TrainSeconds + 0.5);
        var soldier = Assert.Single(world.Units);
        Run(world, new OrderUnits([soldier.Id], OrderKind.Hold, 0, 0));
        Run(world, new SpawnDemons(DemonKind.Imp, (int)soldier.X, (int)soldier.Y + 3, 30));

        var events = RunSeconds(world, 20);
        var died = events.OfType<UnitDied>().Single(d => d.UnitId == soldier.Id);
        Assert.True(died.Rose);
    }

    [Fact]
    public void OneBreachCascades()
    {
        // The same small raid against the same six houses, with and without
        // anyone home: possession should turn it into a bigger horde.
        int Horde(bool inhabited)
        {
            var rules = inhabited ? Rules.Default : Rules.Default.WithBuilding(BuildingKind.House, h => h with { Housing = 0 });
            var world = Rich(rules);
            foreach (var (x, y) in new[] { (58, 58), (61, 58), (68, 58), (71, 58), (58, 69), (71, 69) }) world.Enqueue(new PlaceBuilding(BuildingKind.House, x, y));
            RunSeconds(world, 8);
            Run(world, new SpawnDemons(DemonKind.Imp, C - 10, C - 7, 8));
            int peak = 0;
            for (int t = 0; t < 40 * Balance.TickHz && world.Outcome == Outcome.Running; t++)
            {
                world.Step();
                peak = Math.Max(peak, world.Horde.Count);
            }
            return peak;
        }

        int empty = Horde(inhabited: false), full = Horde(inhabited: true);
        Assert.True(full >= empty + 20, $"eight imps against empty houses peaked at {empty} demons, against inhabited ones at {full}");
    }
}
