namespace Hellwall.Sim;

/// <summary>
/// Seeded terrain: two fields of value noise (elevation and moisture) mapped to
/// tiles, with a guaranteed clearing around the centre for the Keep.
///
/// The noise lattice is an integer hash of (seed, x, y) rather than draws from
/// the World's Rng, so terrain never depends on how many numbers anything else
/// has drawn. This is a placeholder until phase 5's procedural maps.
/// </summary>
public static class MapGen
{
    public static Terrain Generate(uint seed, int size)
    {
        var terrain = new Terrain(size, size);
        uint elevationSeed = seed;
        uint moistureSeed = seed ^ 0x9E3779B9u;
        int centre = size / 2;
        int clear = Balance.KeepClearRadius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int dx = x - centre;
                int dy = y - centre;
                if (dx * dx + dy * dy <= clear * clear)
                {
                    terrain.Set(x, y, Tile.Grass);
                    continue;
                }

                double e = Fbm(elevationSeed, x, y);
                double m = Fbm(moistureSeed, x, y);
                Tile tile =
                    e < 0.30 ? Tile.Water :
                    e > 0.70 ? Tile.Rock :
                    m > 0.58 ? Tile.Forest :
                    Tile.Grass;
                terrain.Set(x, y, tile);
            }
        }

        EnsureNearby(terrain, seed, Tile.Rock, minimum: 60, blobRadius: 4, angleSalt: 0x51ED27u);
        EnsureNearby(terrain, seed, Tile.Forest, minimum: 120, blobRadius: 6, angleSalt: 0xA3B195u);
        PlaceOre(terrain, seed);
        return terrain;
    }

    /// <summary>
    /// Iron lies out on the map, not at home: one modest deposit 22-28 tiles
    /// from the Keep (on the landward side, like the fairness patches) and
    /// richer ones further out, one per 2,000 tiles or so of map. An army is
    /// made of iron, so a bigger army means reaching further.
    /// </summary>
    static void PlaceOre(Terrain terrain, uint seed)
    {
        int c = terrain.Width / 2;
        // The near deposit may take forest as well as grass, so it lands wherever the heading points.
        StampPatch(terrain, c, seed ^ 0x0E0E0Eu, 22 + Lattice(seed ^ 0x0E0E0Eu, 5, 6) * 6, Tile.Ore, radius: 3, overForest: true);
        int deposits = terrain.Width * terrain.Height / 2000;
        for (int i = 0; i < deposits; i++)
        {
            int x = (int)(Lattice(seed ^ 0x0BE5u, i, 1) * terrain.Width);
            int y = (int)(Lattice(seed ^ 0x0BE5u, i, 2) * terrain.Height);
            int dx = x - c, dy = y - c;
            if (dx * dx + dy * dy < 34 * 34) continue; // the rich ones are out in the wilds
            int radius = 2 + (int)(Lattice(seed ^ 0x0BE5u, i, 3) * 2);
            Stamp(terrain, x, y, radius, Tile.Ore);
        }
    }

    /// <summary>A patch at `distance` from the centre, on whichever of sixteen headings crosses the most land.</summary>
    static void StampPatch(Terrain terrain, int c, uint salt, double distance, Tile kind, int radius, bool overForest = false)
    {
        double start = Lattice(salt, 1, 2) * 2 * Math.PI;
        double bestAngle = start;
        int bestLand = -1;
        for (int k = 0; k < 16; k++)
        {
            double a = start + k * Math.PI / 8;
            int land = 0;
            for (double d = Balance.KeepClearRadius; d <= distance + radius; d += 0.5)
            {
                int x = c + (int)Math.Round(Math.Cos(a) * d), y = c + (int)Math.Round(Math.Sin(a) * d);
                if (terrain.InBounds(x, y) && terrain.Get(x, y) != Tile.Water) land++;
            }
            if (land > bestLand) { bestLand = land; bestAngle = a; }
        }
        Stamp(terrain, c + (int)Math.Round(Math.Cos(bestAngle) * distance), c + (int)Math.Round(Math.Sin(bestAngle) * distance), radius, kind, overForest);
    }

    /// <summary>Turn the grass in a disc into `kind`, leaving the Keep's clearing and every other tile alone.</summary>
    static void Stamp(Terrain terrain, int bx, int by, int radius, Tile kind, bool overForest = false)
    {
        int c = terrain.Width / 2, clear = Balance.KeepClearRadius;
        for (int y = by - radius; y <= by + radius; y++)
            for (int x = bx - radius; x <= bx + radius; x++)
            {
                if (!terrain.InBounds(x, y)) continue;
                if ((x - bx) * (x - bx) + (y - by) * (y - by) > radius * radius) continue;
                if ((x - c) * (x - c) + (y - c) * (y - c) <= clear * clear) continue;
                var was = terrain.Get(x, y);
                if (was != Tile.Grass && !(overForest && was == Tile.Forest)) continue;
                terrain.Set(x, y, kind);
            }
    }

    /// <summary>
    /// Start fairness: every colony needs stone and wood within reach. If the
    /// ground within 22 tiles of the centre holds fewer than `minimum` tiles of
    /// the kind, stamp a round patch of it 16 to 20 tiles out, at an angle
    /// drawn from the seed. Maps with no rock anywhere near the Keep were
    /// unwinnable by every doctrine the bot knows (`hellwall-sim maps`).
    /// </summary>
    static void EnsureNearby(Terrain terrain, uint seed, Tile kind, int minimum, int blobRadius, uint angleSalt)
    {
        int c = terrain.Width / 2;
        const int reach = 22;
        int have = 0;
        for (int y = c - reach; y <= c + reach; y++)
            for (int x = c - reach; x <= c + reach; x++)
                if ((x - c) * (x - c) + (y - c) * (y - c) <= reach * reach && terrain.Get(x, y) == kind) have++;
        if (have >= minimum) return;

        // Of sixteen directions (starting from one drawn from the seed), take
        // the one whose way out from the Keep crosses the most land: a patch
        // across water is no use, since holy ground can't reach it.
        double start = Lattice(seed ^ angleSalt, 1, 2) * 2 * Math.PI;
        double distance = 16 + Lattice(seed ^ angleSalt, 3, 4) * 4;
        double bestAngle = start;
        int bestLand = -1;
        for (int k = 0; k < 16; k++)
        {
            double a = start + k * Math.PI / 8;
            int land = 0;
            for (double d = Balance.KeepClearRadius; d <= distance + blobRadius; d += 0.5)
            {
                int x = c + (int)Math.Round(Math.Cos(a) * d), y = c + (int)Math.Round(Math.Sin(a) * d);
                if (terrain.InBounds(x, y) && terrain.Get(x, y) != Tile.Water) land++;
            }
            if (land > bestLand) { bestLand = land; bestAngle = a; }
        }
        int bx = c + (int)Math.Round(Math.Cos(bestAngle) * distance);
        int by = c + (int)Math.Round(Math.Sin(bestAngle) * distance);
        int clear = Balance.KeepClearRadius;
        for (int y = by - blobRadius; y <= by + blobRadius; y++)
            for (int x = bx - blobRadius; x <= bx + blobRadius; x++)
            {
                if (!terrain.InBounds(x, y)) continue;
                if ((x - bx) * (x - bx) + (y - by) * (y - by) > blobRadius * blobRadius) continue;
                if ((x - c) * (x - c) + (y - c) * (y - c) <= clear * clear) continue; // the Keep's clearing stays grass
                if (terrain.Get(x, y) != Tile.Grass) continue; // only grass turns: never another patch, never a lake
                terrain.Set(x, y, kind);
            }
    }

    static double Fbm(uint seed, int x, int y) =>
        0.60 * ValueNoise(seed, x / 24.0, y / 24.0) +
        0.30 * ValueNoise(seed + 1, x / 12.0, y / 12.0) +
        0.10 * ValueNoise(seed + 2, x / 6.0, y / 6.0);

    static double ValueNoise(uint seed, double x, double y)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        double tx = Smooth(x - x0);
        double ty = Smooth(y - y0);
        double a = Lattice(seed, x0, y0);
        double b = Lattice(seed, x0 + 1, y0);
        double c = Lattice(seed, x0, y0 + 1);
        double d = Lattice(seed, x0 + 1, y0 + 1);
        double top = a + (b - a) * tx;
        double bottom = c + (d - c) * tx;
        return top + (bottom - top) * ty;
    }

    static double Smooth(double t) => t * t * (3 - 2 * t);

    static double Lattice(uint seed, int x, int y)
    {
        unchecked
        {
            uint h = seed ^ ((uint)x * 0x27D4EB2Du) ^ ((uint)y * 0x165667B1u);
            h ^= h >> 15;
            h *= 0x85EBCA77u;
            h ^= h >> 13;
            h *= 0xC2B2AE3Du;
            h ^= h >> 16;
            return h / 4294967296.0;
        }
    }
}
