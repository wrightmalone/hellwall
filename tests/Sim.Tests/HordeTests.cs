namespace Hellwall.Sim.Tests;

/// <summary>
/// Horde movement at test scale (128 map, hundreds of demons), with harmless
/// demons so the colony survives to be walked to. Combat has its own tests;
/// the 20k performance gate is `hellwall-sim bench`, run by scripts/verify.sh.
/// </summary>
public class HordeTests
{
    const int C = Balance.DefaultMapSize / 2;

    static World NewWorld(int packs = 0, bool harmless = true)
    {
        var rules = harmless ? Rules.Default.Harmless() : Rules.Default;
        var world = World.Create(new WorldOptions(7, Balance.DefaultMapSize, packs, rules));
        world.DrainEvents();
        return world;
    }

    static void Apply(World world, IEnumerable<Command> commands)
    {
        foreach (var c in commands) world.Enqueue(c);
        world.Step();
        world.DrainEvents();
    }

    /// <summary>A tile about `tiles` path-steps from the colony, found by walking the flow field's distances.</summary>
    static (int X, int Y) TileAtPathDistance(World world, int tiles)
    {
        int target = tiles * Balance.CostStraight;
        for (int y = 0; y < world.Terrain.Height; y++)
            for (int x = 0; x < world.Terrain.Width; x++)
            {
                int d = world.Flow.DistAt(x, y);
                if (d >= target && d < target + 20 && world.IsWalkable(x, y)) return (x, y);
            }
        throw new InvalidOperationException($"no tile {tiles} path-steps out");
    }

    static void AssertNobodyInsideBlockedTiles(World world)
    {
        var h = world.Horde;
        for (int i = 0; i < h.Count; i++)
            Assert.True(world.IsWalkable((int)h.X[i], (int)h.Y[i]), $"demon {i} at ({h.X[i]:F2}, {h.Y[i]:F2}) is inside a blocked tile");
    }

    [Fact]
    public void FlowIsZeroAtTargetsAndEveryDirectionLeadsDownhill()
    {
        var world = NewWorld();
        var flow = world.Flow;
        Assert.Equal(0, flow.DistAt(C, C));

        int checkedTiles = 0;
        for (int y = 0; y < world.Terrain.Height; y++)
        {
            for (int x = 0; x < world.Terrain.Width; x++)
            {
                int d = flow.DistAt(x, y);
                if (d == 0 || d == FlowField.Unreachable) continue;
                int i = y * flow.Width + x;
                int nx = x + Math.Sign(flow.DirX[i]);
                int ny = y + Math.Sign(flow.DirY[i]);
                Assert.True(flow.DistAt(nx, ny) < d, $"({x},{y}) points uphill");
                checkedTiles++;
            }
        }
        Assert.True(checkedTiles > 5000);
    }

    [Fact]
    public void AWalledPocketIsReachedOnlyThroughAWall()
    {
        var world = NewWorld();
        // A closed 5x5 box of walls in the Keep's clearing; its 3x3 inside is sealed off.
        var box = new List<Command>();
        for (int y = 58; y <= 62; y++)
            for (int x = 68; x <= 72; x++)
                if (x == 68 || x == 72 || y == 58 || y == 62)
                    box.Add(new PlaceBuilding(BuildingKind.Wall, x, y));
        Apply(world, box);

        // Cheapest tile just outside the box's walls, against the centre of its inside.
        int outside = int.MaxValue;
        for (int y = 57; y <= 63; y++)
            for (int x = 67; x <= 73; x++)
                if (x == 67 || x == 73 || y == 57 || y == 63) outside = Math.Min(outside, world.Flow.DistAt(x, y));
        int inside = world.Flow.DistAt(70, 60);
        Assert.NotEqual(FlowField.Unreachable, inside);
        // Getting in costs at least one wall's worth of detour.
        Assert.True(inside - outside >= Balance.CostStraight * Balance.WallCostMultiplier,
            $"inside {inside}, outside {outside}: the pocket is reachable without breaking a wall");
    }

    [Fact]
    public void DemonsReachTheColonyAcrossOpenGround()
    {
        var world = NewWorld();
        var (sx, sy) = TileAtPathDistance(world, 40);
        Apply(world, [new SpawnDemons(DemonKind.Imp, sx, sy, 300)]);
        Assert.Equal(300, world.Horde.Count);

        for (int t = 0; t < 60 * Balance.TickHz; t++) world.Step();

        int near = 0;
        for (int i = 0; i < world.Horde.Count; i++)
            if (world.Flow.DistAt((int)world.Horde.X[i], (int)world.Horde.Y[i]) <= 100) near++;
        Assert.True(near >= 285, $"only {near}/300 within 10 path-tiles of the Keep after 60s");
    }

    [Fact]
    public void DemonsFlowInThroughAGapRatherThanBreakingTheWall()
    {
        var world = NewWorld();
        Apply(world, Scenarios.WallRing(world, 8, Side.West));
        var tile = world.FindReachableTileNear(C - 40, C, 20);
        Assert.NotNull(tile);
        Apply(world, [new SpawnDemons(DemonKind.Imp, tile.Value.X, tile.Value.Y, 300)]);

        for (int t = 0; t < 90 * Balance.TickHz; t++)
        {
            world.Step();
            if (t % 10 == 0) AssertNobodyInsideBlockedTiles(world);
        }

        Assert.True(CountInsideRing(world, 8) >= 270, "demons didn't get inside the ring through its west gap");
    }

    [Fact]
    public void DemonsWalkRoundAShortWall()
    {
        var world = NewWorld();
        // A 9-tile wall across the approach from the west: going round is a few tiles.
        var wall = Enumerable.Range(C - 4, 9).Select(y => (Command)new PlaceBuilding(BuildingKind.Wall, C - 6, y)).ToList();
        Apply(world, wall);
        var tile = world.FindReachableTileNear(C - 40, C, 20);
        Apply(world, [new SpawnDemons(DemonKind.Imp, tile!.Value.X, tile.Value.Y, 200)]);

        for (int t = 0; t < 60 * Balance.TickHz; t++) world.Step();

        int near = 0;
        for (int i = 0; i < world.Horde.Count; i++)
            if (world.Flow.DistAt((int)world.Horde.X[i], (int)world.Horde.Y[i]) <= 100) near++;
        Assert.True(near >= 180, $"only {near}/200 reached the Keep round a short wall");
    }

    [Fact]
    public void DemonsBreakThroughASealedRing()
    {
        var world = NewWorld(harmless: false);
        Apply(world, Scenarios.WallRing(world, 8)); // no gaps
        var tile = world.FindReachableTileNear(C - 40, C, 20);
        Apply(world, [new SpawnDemons(DemonKind.Imp, tile!.Value.X, tile.Value.Y, 300)]);

        var destroyed = new List<BuildingDestroyed>();
        for (int t = 0; t < 120 * Balance.TickHz && world.Outcome == Outcome.Running; t++)
        {
            world.Step();
            destroyed.AddRange(world.DrainEvents().OfType<BuildingDestroyed>());
        }

        Assert.Contains(destroyed, d => d.Kind == BuildingKind.Wall);
        // Once through, they reach the Keep and kill it: breaching means something.
        Assert.Equal(Outcome.Lost, world.Outcome);
    }

    static int CountInsideRing(World world, int radius)
    {
        int inside = 0;
        for (int i = 0; i < world.Horde.Count; i++)
        {
            float x = world.Horde.X[i], y = world.Horde.Y[i];
            if (x > C - radius && x < C + radius && y > C - radius && y < C + radius) inside++;
        }
        return inside;
    }

    [Fact]
    public void NoTileEverExceedsTheDensityCap()
    {
        var world = NewWorld();
        var (sx, sy) = TileAtPathDistance(world, 30);
        Apply(world, [new SpawnDemons(DemonKind.Imp, sx, sy, 1500)]);

        var perTile = new int[world.Terrain.Width * world.Terrain.Height];
        for (int t = 0; t < 60 * Balance.TickHz; t++)
        {
            world.Step();
            if (t % 20 != 0) continue;
            Array.Clear(perTile);
            for (int i = 0; i < world.Horde.Count; i++)
            {
                int c = ++perTile[(int)world.Horde.Y[i] * world.Terrain.Width + (int)world.Horde.X[i]];
                Assert.True(c <= Balance.MaxDensity, $"tick {world.Tick}: a tile holds {c} demons");
            }
        }
    }

    [Fact]
    public void PacksSleepUntilNoiseReachesThem()
    {
        var world = NewWorld(packs: 6);
        Assert.Equal(6, world.Packs.Count);
        for (int t = 0; t < 200; t++) world.Step();
        Assert.All(world.Packs, p => Assert.False(p.Awake));
        Assert.Equal(0, world.Horde.Count);

        var loud = world.Packs[0];
        world.Enqueue(new MakeNoise(loud.X, loud.Y, 10, 2));
        world.Step();
        var woke = world.DrainEvents().OfType<PackWoke>().ToList();

        Assert.True(loud.Awake);
        Assert.Contains(woke, e => e.PackId == loud.Id);
        Assert.Equal(woke.Sum(e => e.Count), world.Horde.Count);
        foreach (var p in world.Packs.Where(p => p != loud))
        {
            float d = MathF.Sqrt((p.X - loud.X) * (p.X - loud.X) + (p.Y - loud.Y) * (p.Y - loud.Y));
            // Beyond radius + a noise cell, a pack can't have heard it.
            if (d > 10 + Balance.NoiseCellSize * 1.5f) Assert.False(p.Awake, $"pack {p.Id}, {d:F0} tiles away, woke");
        }
    }

    [Fact]
    public void NoiseFadesBelowTheWakeThreshold()
    {
        var world = NewWorld();
        world.Enqueue(new MakeNoise(20, 20, 16, 2));
        world.Step();
        Assert.True(world.Noise.LevelAtTile(20, 20) >= Balance.WakeThreshold);
        // Two half-lives take 2.0 down to 0.5.
        for (int t = 0; t < 2 * Balance.NoiseHalfLifeSeconds * Balance.TickHz; t++) world.Step();
        Assert.InRange(world.Noise.LevelAtTile(20, 20), 0.45f, 0.55f);
    }

    [Fact]
    public void BuildingIsLoud()
    {
        var world = NewWorld();
        Apply(world, [new PlaceBuilding(BuildingKind.House, 68, 62)]);
        Assert.True(world.Noise.LevelAtTile(69, 63) >= Balance.WakeThreshold);
    }

    [Fact]
    public void CannotBuildOnTopOfDemons()
    {
        var world = NewWorld();
        Apply(world, [new SpawnDemons(DemonKind.Imp, 70, 70, 1)]);
        int x = (int)world.Horde.X[0], y = (int)world.Horde.Y[0];
        Assert.Equal("demons in the way", world.CheckPlacement(BuildingKind.Wall, x, y));
    }

    [Fact]
    public void SteadyStateTickAllocatesNothing()
    {
        var world = NewWorld();
        var (sx, sy) = TileAtPathDistance(world, 30);
        Apply(world, [new SpawnDemons(DemonKind.Imp, sx, sy, 2000)]);
        for (int t = 0; t < 40; t++) world.Step(); // warm up JIT and array sizes

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 0; t < 200; t++) world.Step();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"{allocated} bytes allocated over 200 ticks");
    }

    [Fact]
    public void BenchRingIsBuiltCompleteOnEverySeed()
    {
        for (uint seed = 0; seed < 32; seed++)
        {
            var world = World.Create(new WorldOptions(seed, 256));
            foreach (var c in Scenarios.WallRing(world, Scenarios.BenchRingRadius, Side.East, Side.West)) world.Enqueue(c);
            world.Step();
            Assert.Empty(world.DrainEvents().OfType<CommandRejected>());
        }
    }
}

public class SurvivalAllocationTests
{
    /// <summary>The steady-state tick of a real run (fog, ruins, patrons, history, repair) allocates nothing either.</summary>
    [Fact]
    public void ASurvivalTickAllocatesNothing()
    {
        // No garrison: shots are events, and events are meant to allocate. Everything else a real run has is here.
        var world = World.Create(new WorldOptions(7, 256, 0, Rules.Default, Survival: true));
        world.UnitList.Clear();
        for (int t = 0; t < 400; t++) world.Step(); // warm up
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 0; t < 200; t++) world.Step();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"{allocated} bytes allocated over 200 ticks");
    }
}
