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

        return terrain;
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
