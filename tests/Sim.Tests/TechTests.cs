using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class TechTests
{
    /// <summary>A built Scriptorium in a rich colony (the House covers its crew).</summary>
    static (World World, Building Scriptorium) Lab(Rules? rules = null)
    {
        var world = Rich(rules);
        Built(world, BuildingKind.House, 68, 62);
        var lab = Built(world, BuildingKind.Scriptorium, 58, 58);
        RunSeconds(world, 0.1);
        Assert.True(lab.Active);
        return (world, lab);
    }

    static void Learn(World world, Building lab, string id)
    {
        var events = Run(world, new Research(lab.Id, id));
        Assert.Empty(events.OfType<CommandRejected>());
        var done = RunSeconds(world, world.Rules.Tech(id).Seconds + 1);
        Assert.Contains(done, e => e is TechResearched r && r.TechId == id);
    }

    static string Rejection(World world, Command c) => Assert.Single(Run(world, c).OfType<CommandRejected>()).Reason;

    [Fact]
    public void EveryTechIsWellFormed()
    {
        var rules = Rules.Default;
        var ids = rules.Techs.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (var t in rules.Techs)
        {
            Assert.All(t.Requires, r => Assert.Contains(r, ids));
            if (t.ExclusiveWith is { } x) Assert.Equal(t.Id, rules.Tech(x).ExclusiveWith); // pairs are mutual
            Assert.True(t.Modifiers.Length + t.UnlocksBuildings.Length + t.UnlocksUnits.Length > 0, $"{t.Id} does nothing");
        }
        // Everything that asks for a tech names one that exists.
        foreach (var d in rules.Buildings) if (d.RequiresTech != null) Assert.Contains(d.RequiresTech, ids);
        foreach (var d in rules.Units) if (d.RequiresTech != null) Assert.Contains(d.RequiresTech, ids);
    }

    [Fact]
    public void ResearchCostsTimeAndMoneyAndOnlyAScriptoriumDoesIt()
    {
        var (world, lab) = Lab();
        var house = world.Buildings.First(b => b.Kind == BuildingKind.House);
        Assert.Equal("House can't research", Rejection(world, new Research(house.Id, "fletching")));

        double gold = world.Colony[Resource.Gold];
        Run(world, new Research(lab.Id, "fletching"));
        Assert.True(world.Colony[Resource.Gold] < gold - 199);
        RunSeconds(world, world.Rules.Tech("fletching").Seconds - 2);
        Assert.False(world.Tech.Has("fletching"));
        RunSeconds(world, 3);
        Assert.True(world.Tech.Has("fletching"));
    }

    [Fact]
    public void AResearchedModifierReachesTowersBuiltBeforeAndAfter()
    {
        var (world, lab) = Lab();
        var before = Built(world, BuildingKind.Watchtower, 69, 58);
        float baseDamage = world.Rules[BuildingKind.Watchtower].Weapon!.Damage;
        Learn(world, lab, "fletching");
        var after = Built(world, BuildingKind.Watchtower, 69, 69);
        Assert.Equal(baseDamage * 1.25f, before.Def.Weapon!.Damage, 3);
        Assert.Equal(baseDamage * 1.25f, after.Def.Weapon!.Damage, 3);
        Assert.Equal(baseDamage, world.Rules[BuildingKind.Watchtower].Weapon!.Damage); // base rules untouched
    }

    [Fact]
    public void HitPointModifiersKeepDamagedBuildingsInProportion()
    {
        var (world, lab) = Lab();
        Learn(world, lab, "masonry");
        Learn(world, lab, "fletching");
        Learn(world, lab, "pitch");
        var wall = Built(world, BuildingKind.StoneWall, 70, 70);
        wall.Hp = wall.Def.Hp / 2;
        Learn(world, lab, "bastions");
        Assert.Equal(wall.Def.Hp / 2, wall.Hp, 1);
        double mul = world.Rules.Tech("bastions").Modifiers.Single(m => m.Group == "walls" && m.Stat == "hp").Mul;
        Assert.Equal(world.Rules[BuildingKind.StoneWall].Hp * mul, wall.Def.Hp, 1);
    }

    [Fact]
    public void UnlocksGateBuildingsAndUnits()
    {
        var (world, lab) = Lab();
        Assert.Equal("needs Masonry", world.CheckPlacement(BuildingKind.StoneWall, 70, 70));
        Learn(world, lab, "masonry");
        Assert.Null(world.CheckPlacement(BuildingKind.StoneWall, 70, 70));

        var barracks = Built(world, BuildingKind.Barracks, 59, 66);
        Assert.Equal("needs Drill", Rejection(world, new TrainUnit(barracks.Id, UnitKind.Crossbowman)));
    }

    [Fact]
    public void PrerequisitesAndExclusivePairsHold()
    {
        var (world, lab) = Lab();
        Assert.Equal("needs Fletching", Rejection(world, new Research(lab.Id, "pitch")));
        Learn(world, lab, "tithes");
        Learn(world, lab, "hallowing");
        Learn(world, lab, "holyfire");
        Learn(world, lab, "masonry");
        Learn(world, lab, "fletching");
        Learn(world, lab, "pitch");
        // Holy Fire is taken: Bastions is closed for the rest of the run, prerequisites or not.
        Assert.Equal("locked out by Holy Fire", Rejection(world, new Research(lab.Id, "bastions")));
        Assert.Equal("already researched", Rejection(world, new Research(lab.Id, "holyfire")));
    }

    [Fact]
    public void HolyFireBurnsDemonsOnConsecratedGroundOnly()
    {
        var (world, lab) = Lab(Dummies());
        Learn(world, lab, "tithes");
        Learn(world, lab, "hallowing");
        Learn(world, lab, "holyfire");
        // One dummy on holy ground, one well off it.
        var far = world.FindReachableTileNear(C + 30, C, 15)!.Value;
        Assert.False(world.Colony.Consecrated[world.Terrain.Index(far.X, far.Y)]);
        Run(world, new SpawnDemons(DemonKind.Imp, C + 6, C + 6, 1), new SpawnDemons(DemonKind.Imp, far.X, far.Y, 1));
        Assert.Equal(2, world.Horde.Count);
        RunSeconds(world, world.Rules[DemonKind.Imp].Hp / world.Tech.HolyGroundDps + 2);
        Assert.Equal(1, world.Horde.Count);
        Assert.True(world.Horde.X[0] > C + 15, "the survivor should be the one off holy ground");
    }

    [Fact]
    public void ResearchSurvivesASaveAndLoad()
    {
        var (world, lab) = Lab();
        Learn(world, lab, "fletching");
        Run(world, new Research(lab.Id, "tithes"));
        RunSeconds(world, 5);
        var loaded = World.Load(world.Save(), world.Rules);
        Assert.Equal(StateHash.Hex(world), StateHash.Hex(loaded));
        Assert.True(loaded.Tech.Has("fletching"));
        Assert.Equal(world.Rules[BuildingKind.Watchtower].Weapon!.Damage * 1.25f, loaded.Def(BuildingKind.Watchtower).Weapon!.Damage, 3);
        RunSeconds(world, 40);
        RunSeconds(loaded, 40);
        Assert.True(loaded.Tech.Has("tithes"));
        Assert.Equal(StateHash.Hex(world), StateHash.Hex(loaded));
    }

    [Fact]
    public void LosingAScriptoriumRefundsItsResearch()
    {
        var (world, lab) = Lab();
        double gold = world.Colony[Resource.Gold];
        Run(world, new Research(lab.Id, "fletching"));
        Run(world, new Demolish(lab.Id));
        Assert.True(world.Colony[Resource.Gold] > gold, "the research should be refunded (plus half the building)");
        Assert.Null(world.CheckResearch("fletching"));
    }
}
