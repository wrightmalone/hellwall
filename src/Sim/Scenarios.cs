namespace Hellwall.Sim;

public enum Side
{
    North,
    East,
    South,
    West,
}

/// <summary>
/// Command lists for set-piece situations, shared by the headless bench, the
/// tests and the Godot debug keys so they all measure the same thing. Pure
/// functions of the world: they read it and return commands, nothing else.
/// </summary>
public static class Scenarios
{
    /// <summary>
    /// The bench's wall ring. Its corners sit radius*sqrt(2) from centre, so
    /// this must satisfy 2 * r^2 &lt;= KeepClearRadius^2 to stay on guaranteed
    /// grass: 8 does (128 &lt;= 144), 9 doesn't.
    /// </summary>
    public const int BenchRingRadius = 8;

    /// <summary>
    /// A square ring of walls `radius` tiles out from the Keep's centre, with a
    /// 3-tile gap in the middle of each listed side.
    /// </summary>
    public static List<Command> WallRing(World world, int radius, params Side[] gaps)
    {
        int c = world.Terrain.Width / 2;
        var commands = new List<Command>();
        for (int y = c - radius; y <= c + radius; y++)
        {
            for (int x = c - radius; x <= c + radius; x++)
            {
                bool edge = x == c - radius || x == c + radius || y == c - radius || y == c + radius;
                if (!edge || InGap(x, y, c, radius, gaps)) continue;
                commands.Add(new PlaceBuilding(BuildingKind.Wall, x, y));
            }
        }
        return commands;
    }

    static bool InGap(int x, int y, int c, int radius, Side[] gaps)
    {
        foreach (var side in gaps)
        {
            bool hit = side switch
            {
                Side.North => y == c - radius && Math.Abs(x - c) <= 1,
                Side.South => y == c + radius && Math.Abs(x - c) <= 1,
                Side.West => x == c - radius && Math.Abs(y - c) <= 1,
                Side.East => x == c + radius && Math.Abs(y - c) <= 1,
                _ => false,
            };
            if (hit) return true;
        }
        return false;
    }

    /// <summary>
    /// `total` demons split over `points` spawn points evenly spaced around the
    /// map, `inset` tiles in from the edge, each snapped to the nearest tile
    /// that can reach the colony. One in five is a Hound. Apply any walls
    /// first: reachability is judged against the world as it stands.
    /// </summary>
    public static List<Command> EdgeAssault(World world, int total, int points, int inset = 12)
    {
        int size = world.Terrain.Width;
        float c = size / 2f;
        float ring = c - inset;
        var commands = new List<Command>();
        for (int p = 0; p < points; p++)
        {
            int share = total / points + (p < total % points ? 1 : 0);
            double angle = p * 2 * Math.PI / points;
            int x = (int)(c + Math.Cos(angle) * ring);
            int y = (int)(c + Math.Sin(angle) * ring);
            var tile = world.FindReachableTileNear(x, y, maxRadius: 40);
            if (tile == null) continue;
            int hounds = share / 5;
            commands.Add(new SpawnDemons(DemonKind.Imp, tile.Value.X, tile.Value.Y, share - hounds));
            if (hounds > 0) commands.Add(new SpawnDemons(DemonKind.Hound, tile.Value.X, tile.Value.Y, hounds));
        }
        return commands;
    }
}
