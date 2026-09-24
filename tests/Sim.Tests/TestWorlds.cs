namespace Hellwall.Sim.Tests;

/// <summary>Shared setup for colony, combat and unit tests. The Keep sits on (63..65, 63..65) of a 128 map.</summary>
internal static class TestWorlds
{
    public const int C = 64;

    public static readonly Cost Plenty = new() { Gold = 10000, Wood = 10000, Stone = 10000, Food = 10000, Iron = 10000 };

    public static World Rich(Rules? rules = null, uint seed = 7)
    {
        var world = World.Create(new WorldOptions(seed, Balance.DefaultMapSize, 0, (rules ?? Rules.Default).WithStartingResources(Plenty)));
        world.DrainEvents();
        return world;
    }

    /// <summary>Demons that neither move nor hurt: targets for towers and soldiers.</summary>
    public static Rules Dummies(Rules? rules = null)
    {
        var r = rules ?? Rules.Default;
        foreach (var kind in Enum.GetValues<DemonKind>()) r = r.WithDemon(kind, d => d with { Speed = 0, Damage = 0, ExplodeDamage = 0 });
        return r;
    }

    public static List<SimEvent> Run(World world, params Command[] commands)
    {
        foreach (var c in commands) world.Enqueue(c);
        world.Step();
        return world.DrainEvents();
    }

    public static List<SimEvent> RunSeconds(World world, double seconds)
    {
        var events = new List<SimEvent>();
        for (int t = 0; t < seconds * Balance.TickHz && world.Outcome == Outcome.Running; t++)
        {
            world.Step();
            events.AddRange(world.DrainEvents());
        }
        return events;
    }

    public static Building Place(World world, BuildingKind kind, int x, int y)
    {
        var events = Run(world, new PlaceBuilding(kind, x, y));
        var rejected = events.OfType<CommandRejected>().FirstOrDefault();
        Assert.True(rejected == null, $"{kind} at ({x},{y}) rejected: {rejected?.Reason}");
        return world.Buildings.Single(b => b.Id == events.OfType<BuildingPlaced>().Single().BuildingId);
    }

    public static Building Built(World world, BuildingKind kind, int x, int y)
    {
        var b = Place(world, kind, x, y);
        RunSeconds(world, b.Def.BuildSeconds + 0.2);
        Assert.True(b.Complete);
        return b;
    }

    /// <summary>A buildable grass tile whose distance from the Keep's centre lies in [min, max).</summary>
    public static (int X, int Y) GrassAtDistance(World world, float min, float max)
    {
        for (int y = 0; y < world.Terrain.Height; y++)
            for (int x = 0; x < world.Terrain.Width; x++)
            {
                float dx = x + 0.5f - (C + 0.5f), dy = y + 0.5f - (C + 0.5f);
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d >= min && d < max && world.Terrain.Get(x, y) == Tile.Grass && world.BuildingIdAt(x, y) == 0) return (x, y);
            }
        throw new InvalidOperationException($"no grass between {min} and {max} tiles out");
    }
}
