using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class ColonyTests
{
    [Fact]
    public void RulesLoadFromTheEmbeddedJson()
    {
        var rules = Rules.Default;
        Assert.Equal(Enum.GetValues<BuildingKind>().Length, rules.Buildings.Length);
        Assert.True(rules[BuildingKind.House].Cost.Wood > 0);
        Assert.Equal(new[] { Tile.Grass }, rules[BuildingKind.Farm].Gathers);
        Assert.Equal(Resource.Wood, rules[BuildingKind.Woodcutter].Produces);
        Assert.NotNull(rules[BuildingKind.Watchtower].Weapon);
        // A modified copy is a different ruleset, and a replay's hash must know it.
        Assert.NotEqual(rules.Hash, rules.Harmless().Hash);
    }

    [Fact]
    public void BuildingCostsResourcesAndIsRejectedWhenShort()
    {
        var world = World.Create(new WorldOptions(7));
        world.DrainEvents();
        double gold = world.Colony[Resource.Gold], wood = world.Colony[Resource.Wood];
        Place(world, BuildingKind.House, 68, 62);
        // One tick of Keep income lands in the same step, so allow for it.
        double houseGold = Rules.Default[BuildingKind.House].Cost.Gold;
        Assert.InRange(world.Colony[Resource.Gold], gold - houseGold, gold - houseGold + 0.2);
        Assert.Equal(wood - Rules.Default[BuildingKind.House].Cost.Wood, world.Colony[Resource.Wood], 6);

        var broke = World.Create(new WorldOptions(7, Rules: Rules.Default.WithStartingResources(new Cost { Gold = 100 })));
        broke.DrainEvents();
        var rejected = Assert.Single(Run(broke, new PlaceBuilding(BuildingKind.House, 68, 62)).OfType<CommandRejected>());
        Assert.Equal("not enough wood", rejected.Reason);
    }

    [Fact]
    public void OnlyConsecratedGroundCanBeBuiltOn()
    {
        var world = Rich();
        var (x, y) = GrassAtDistance(world, 14, 18);
        var rejected = Assert.Single(Run(world, new PlaceBuilding(BuildingKind.Wall, x, y)).OfType<CommandRejected>());
        Assert.Equal("not on consecrated ground", rejected.Reason);
    }

    [Fact]
    public void AWardstoneExtendsHolyGroundOnceItIsBuilt()
    {
        var world = Rich();
        // Due east, just inside the Keep's ground; its own radius reaches well beyond.
        var ward = Place(world, BuildingKind.Wardstone, C + 11, C);
        int beyond = C + 14;
        Assert.False(world.Colony.Consecrated[world.Terrain.Index(beyond, C)], "consecrated before the Wardstone was finished");

        RunSeconds(world, ward.Def.BuildSeconds + 0.2);
        Assert.True(world.Colony.Consecrated[world.Terrain.Index(beyond, C)]);
    }

    [Fact]
    public void LosingAWardstoneDarkensEverythingBeyondIt()
    {
        var world = Rich();
        var ward = Built(world, BuildingKind.Wardstone, C + 11, C);
        var (x, y) = FindConsecratedOnlyBy(world, ward);
        var tower = Built(world, BuildingKind.Watchtower, x, y);
        RunSeconds(world, 0.1);
        Assert.True(tower.Active);

        Run(world, new Demolish(ward.Id));
        RunSeconds(world, 0.1);
        Assert.False(tower.OnGround);
        Assert.False(tower.Active);
    }

    [Fact]
    public void ConstructionTakesTimeAndHousesOnlyCountWhenFinished()
    {
        var world = Rich();
        RunSeconds(world, 0.1); // colonists are counted by the colony step
        int before = world.Colony.Colonists;
        Assert.Equal(world.Rules[BuildingKind.Keep].Housing, before);
        var house = Place(world, BuildingKind.House, 68, 62);
        RunSeconds(world, house.Def.BuildSeconds - 1);
        Assert.False(house.Complete);
        Assert.Equal(before, world.Colony.Colonists);

        var done = RunSeconds(world, 1.2);
        Assert.True(house.Complete);
        Assert.Contains(done, e => e is BuildingCompleted c && c.BuildingId == house.Id);
        Assert.Equal(before + house.Def.Housing, world.Colony.Colonists);
    }

    [Fact]
    public void CrewsAreAssignedFirstComeFirstServed()
    {
        var world = Rich();
        // The Keep houses 8. Three Hunters (2 each) and a Woodcutter (3) want 9.
        var hunters = new[] { (58, 58), (69, 58), (58, 69) }.Select(p => Place(world, BuildingKind.Hunter, p.Item1, p.Item2)).ToList();
        var bombard = Place(world, BuildingKind.Woodcutter, 69, 69);
        RunSeconds(world, 13);
        Assert.All(hunters, t => Assert.True(t.Staffed));
        Assert.False(bombard.Staffed, "the last building placed should be the one left without a crew");
        Assert.False(bombard.Active);

        Built(world, BuildingKind.House, 59, 63);
        RunSeconds(world, 0.1);
        Assert.True(bombard.Staffed);
    }

    [Fact]
    public void GatherersSplitTheTilesTheyShare()
    {
        var world = Rich();
        double alone = world.EstimateGathering(BuildingKind.Farm, 66, 57);
        var first = Built(world, BuildingKind.Farm, 66, 57);
        RunSeconds(world, 0.1);
        Assert.True(alone > 0);
        Assert.Equal(alone, first.Rate, 9); // the preview matches what it then collects
        double firstAlone = first.Rate;

        // Four tiles east: their fields overlap, and the first Farm (lower id) keeps what it had claimed.
        double secondAlone = world.EstimateGathering(BuildingKind.Farm, 70, 57);
        var second = Built(world, BuildingKind.Farm, 70, 57);
        RunSeconds(world, 0.1);
        Assert.True(second.Rate < firstAlone, $"second farm {second.Rate} should get less than the first {firstAlone}: they overlap");
        Assert.True(first.Rate + second.Rate < firstAlone * 2);
    }

    [Fact]
    public void ShortSanctitySlowsEverythingThatDrawsIt()
    {
        // A Keep that supplies 5 against a Watchtower (3), a Hunter (1) and a Bombard (6): 5/10.
        var rules = Rules.Default.WithBuilding(BuildingKind.Keep, k => k with { SanctitySupply = 5 });
        var world = Rich(rules);
        Built(world, BuildingKind.Watchtower, 58, 58);
        var hunter = Built(world, BuildingKind.Hunter, 68, 62);
        Built(world, BuildingKind.Bombard, 58, 69);
        RunSeconds(world, 0.1);
        Assert.Equal(0.5f, world.Colony.Power, 3);

        double expectedFood = hunter.Rate * 0.5 - world.Colony.Colonists * rules.ColonistFoodPerSecond;
        Assert.Equal(expectedFood, world.Colony.NetPerSecond[(int)Resource.Food], 9);
    }

    [Fact]
    public void AStarvingColonyPaysNoTithe()
    {
        var rules = Rules.Default.WithStartingResources(new Cost { Gold = 100 });
        var world = World.Create(new WorldOptions(7, Rules: rules));
        RunSeconds(world, 0.2);
        Assert.True(world.Colony.Starving);
        // Only the Keep's own income is left.
        Assert.Equal(rules[BuildingKind.Keep].Gold, world.Colony.NetPerSecond[(int)Resource.Gold], 9);
    }

    [Fact]
    public void DemolishingRefundsHalfOnceBuiltAndAllBefore()
    {
        var world = Rich();
        var wood0 = world.Colony[Resource.Wood];
        var early = Place(world, BuildingKind.House, 68, 62);
        Run(world, new Demolish(early.Id));
        Assert.Equal(wood0, world.Colony[Resource.Wood], 6);

        var built = Built(world, BuildingKind.House, 68, 62);
        var wood1 = world.Colony[Resource.Wood];
        Run(world, new Demolish(built.Id));
        Assert.Equal(wood1 + built.Def.Cost.Wood * Rules.Default.RefundFraction, world.Colony[Resource.Wood], 6);
    }

    /// <summary>A 2x2 grass spot inside the Wardstone's ground but outside the Keep's.</summary>
    static (int X, int Y) FindConsecratedOnlyBy(World world, Building ward)
    {
        float keepR = world.Rules[BuildingKind.Keep].ConsecrateRadius;
        for (int y = 0; y < world.Terrain.Height - 1; y++)
            for (int x = 0; x < world.Terrain.Width - 1; x++)
            {
                float dx = x + 1 - (C + 0.5f), dy = y + 1 - (C + 0.5f);
                if (dx * dx + dy * dy <= (keepR + 1.5f) * (keepR + 1.5f)) continue;
                if (world.CheckPlacement(BuildingKind.Watchtower, x, y) == null) return (x, y);
            }
        throw new InvalidOperationException("the Wardstone added no buildable ground");
    }
}
