namespace Hellwall.Sim.Tests;

public class PlacementTests
{
    /// <summary>A fresh world with its creation events (the Keep's placement) already drained.</summary>
    static World NewWorld(uint seed = 7)
    {
        var world = World.Create(new WorldOptions(seed));
        world.DrainEvents();
        return world;
    }

    [Fact]
    public void CreationAnnouncesTheKeep()
    {
        var world = World.Create(new WorldOptions(7));
        var placed = Assert.Single(world.DrainEvents().OfType<BuildingPlaced>());
        Assert.Equal(BuildingKind.Keep, placed.Kind);
    }

    static List<SimEvent> Run(World world, params Command[] commands)
    {
        foreach (var c in commands) world.Enqueue(c);
        world.Step();
        return world.DrainEvents();
    }

    [Fact]
    public void KeepStartsAtMapCentre()
    {
        var world = NewWorld();
        var keep = Assert.Single(world.Buildings);
        Assert.Equal(BuildingKind.Keep, keep.Kind);
        Assert.Equal(keep.Id, world.BuildingIdAt(Balance.DefaultMapSize / 2, Balance.DefaultMapSize / 2));
    }

    [Fact]
    public void HouseOnClearGrassIsPlaced()
    {
        var world = NewWorld();
        var events = Run(world, new PlaceBuilding(BuildingKind.House, 68, 62));
        var placed = Assert.Single(events.OfType<BuildingPlaced>());
        Assert.Single(events.OfType<NoiseMade>()); // building is loud
        Assert.Equal(BuildingKind.House, placed.Kind);
        Assert.Equal(placed.BuildingId, world.BuildingIdAt(69, 63));
    }

    [Theory]
    [InlineData(63, 63, "tile occupied")]     // on the Keep
    [InlineData(-1, 10, "out of bounds")]
    [InlineData(127, 127, "out of bounds")]   // 2x2 hangs off the edge
    public void InvalidPlacementIsRejectedWithReason(int x, int y, string reason)
    {
        var world = NewWorld();
        var rejected = Assert.IsType<CommandRejected>(Assert.Single(Run(world, new PlaceBuilding(BuildingKind.House, x, y))));
        Assert.Equal(reason, rejected.Reason);
        Assert.Single(world.Buildings);
    }

    [Fact]
    public void WaterIsNotBuildable()
    {
        var world = NewWorld();
        var (x, y) = FindTile(world, Tile.Water);
        var rejected = Assert.IsType<CommandRejected>(Assert.Single(Run(world, new PlaceBuilding(BuildingKind.Wall, x, y))));
        Assert.Equal("terrain not buildable", rejected.Reason);
    }

    [Fact]
    public void SecondKeepIsRejected()
    {
        var world = NewWorld();
        var rejected = Assert.IsType<CommandRejected>(Assert.Single(Run(world, new PlaceBuilding(BuildingKind.Keep, 40, 64))));
        Assert.Equal("only one Keep", rejected.Reason);
    }

    [Fact]
    public void DemolishFreesTilesButNeverTheKeep()
    {
        var world = NewWorld();
        var placed = Run(world, new PlaceBuilding(BuildingKind.House, 68, 62)).OfType<BuildingPlaced>().Single();

        Assert.Single(Run(world, new Demolish(placed.BuildingId)).OfType<BuildingRemoved>());
        Assert.Equal(0, world.BuildingIdAt(68, 62));

        var keepId = world.Buildings[0].Id;
        Assert.IsType<CommandRejected>(Assert.Single(Run(world, new Demolish(keepId))));
        Assert.Single(world.Buildings);
    }

    [Fact]
    public void FlushWhilePausedPlacesWithoutAdvancingTime()
    {
        var world = NewWorld();
        world.Enqueue(new PlaceBuilding(BuildingKind.House, 68, 62));
        world.FlushCommands();
        Assert.Equal(0, world.Tick);
        Assert.Equal(2, world.Buildings.Count);
    }

    [Fact]
    public void KeepClearingIsGrassOnEverySeed()
    {
        for (uint seed = 0; seed < 64; seed++)
        {
            var world = NewWorld(seed);
            int c = Balance.DefaultMapSize / 2;
            int r = Balance.KeepClearRadius;
            for (int y = c - r; y <= c + r; y++)
                for (int x = c - r; x <= c + r; x++)
                    if ((x - c) * (x - c) + (y - c) * (y - c) <= r * r)
                        Assert.Equal(Tile.Grass, world.Terrain.Get(x, y));
        }
    }

    static (int X, int Y) FindTile(World world, Tile tile)
    {
        var t = world.Terrain;
        for (int y = 0; y < t.Height; y++)
            for (int x = 0; x < t.Width; x++)
                if (t.Get(x, y) == tile) return (x, y);
        throw new InvalidOperationException($"seed {world.Seed} has no {tile}; pick another seed for this test");
    }
}

public class StoneGateTests
{
    [Fact]
    public void AStoneGateLetsPeopleThroughAndNotTheHorde()
    {
        var world = TestWorlds.Rich();
        Assert.Contains("needs Masonry", world.CheckPlacement(BuildingKind.StoneGate, TestWorlds.C + 6, TestWorlds.C));
        world.Grant("masonry");
        var gate = TestWorlds.Built(world, BuildingKind.StoneGate, TestWorlds.C + 6, TestWorlds.C);
        Assert.True(gate.IsWallLike && gate.IsGate);
        Assert.True(world.IsHumanWalkable(gate.X, gate.Y));
        Assert.False(world.IsWalkable(gate.X, gate.Y));
        Assert.True(gate.Def.Hp > world.Def(BuildingKind.Gate).Hp);
    }
}

public class CutOffTests
{
    [Fact]
    public void ClosingTheOnlyWayInWarnsOfWhatItCutsOff()
    {
        // A House boxed in on three sides by walls; a building across the fourth would seal it.
        var world = TestWorlds.Rich();
        var house = TestWorlds.Place(world, BuildingKind.House, TestWorlds.C + 6, TestWorlds.C - 1);
        for (int y = TestWorlds.C - 2; y <= TestWorlds.C + 1; y++) TestWorlds.Place(world, BuildingKind.Wall, TestWorlds.C + 8, y);
        for (int x = TestWorlds.C + 5; x <= TestWorlds.C + 7; x++)
        {
            TestWorlds.Place(world, BuildingKind.Wall, x, TestWorlds.C - 2);
            TestWorlds.Place(world, BuildingKind.Wall, x, TestWorlds.C + 1);
        }
        // Open to the west only (column C+5): a wall there seals the House.
        Assert.Equal(0, world.CutOff(BuildingKind.Wall, TestWorlds.C + 3, TestWorlds.C - 1).Buildings);
        TestWorlds.Place(world, BuildingKind.Wall, TestWorlds.C + 5, TestWorlds.C - 1);
        Assert.Equal(1, world.CutOff(BuildingKind.Wall, TestWorlds.C + 5, TestWorlds.C).Buildings);
    }
}
