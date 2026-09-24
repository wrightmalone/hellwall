namespace Hellwall.Sim;

/// <summary>
/// A sleeping pack of demons. Kept as one record until noise wakes it, which
/// is how the map holds tens of thousands of demons without simulating them:
/// a dormant pack costs one distance check per tick, whatever its size.
/// </summary>
public sealed class Pack
{
    public int Id;
    public int X;
    public int Y;
    public int Count;
    public DemonKind Kind;
    public bool Awake;
    /// <summary>A straggler group: a handful, anywhere on the map, keeping only a small ring clear.</summary>
    public bool Stray;

    /// <summary>The radius of the loose crowd the pack stands in.</summary>
    public float Spread(float density) => MathF.Sqrt(Count / (MathF.PI * density)) + 0.5f;

    /// <summary>
    /// Where sleeper i stands, as a tile position: a fixed point in the pack's
    /// disc from a hash of (pack, i), so the client draws them exactly where
    /// the sim wakes them, without either drawing from the World's Rng.
    /// Also a facing, 0..7.
    /// </summary>
    public (float X, float Y, int Facing) Spot(int i, float density)
    {
        uint h = Hash((uint)Id, (uint)i);
        float angle = (h & 0xFFFF) / 65536f * MathF.Tau;
        float r = Spread(density) * MathF.Sqrt((h >> 16 & 0xFFF) / 4096f);
        return (X + 0.5f + MathF.Cos(angle) * r, Y + 0.5f + MathF.Sin(angle) * r, (int)(h >> 28) & 7);
    }

    static uint Hash(uint a, uint b)
    {
        unchecked
        {
            uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du;
            h ^= h >> 12; h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }
    }
}

/// <summary>
/// Noise on a coarse grid. Construction (and later combat) deposits noise
/// with a linear falloff over a radius; it decays with a fixed half-life; a
/// dormant pack wakes when the level at its cell crosses the threshold. The
/// player's success is loud, and loudness is what pulls the map in.
/// </summary>
public sealed class NoiseGrid
{
    public readonly int CellSize;
    public readonly int Width;
    public readonly int Height;
    public readonly float[] Level;

    static readonly float DecayPerTick =
        (float)Math.Pow(0.5, 1.0 / (Balance.NoiseHalfLifeSeconds * Balance.TickHz));

    public NoiseGrid(int mapWidth, int mapHeight, int cellSize = Balance.NoiseCellSize)
    {
        CellSize = cellSize;
        Width = (mapWidth + cellSize - 1) / cellSize;
        Height = (mapHeight + cellSize - 1) / cellSize;
        Level = new float[Width * Height];
    }

    public void Decay()
    {
        for (int i = 0; i < Level.Length; i++) Level[i] *= DecayPerTick;
    }

    /// <summary>Deposit noise centred on a world position; louder sources overwrite, they don't stack.</summary>
    public void Emit(float x, float y, float radius, float intensity)
    {
        int c0x = Math.Max(0, (int)((x - radius) / CellSize));
        int c1x = Math.Min(Width - 1, (int)((x + radius) / CellSize));
        int c0y = Math.Max(0, (int)((y - radius) / CellSize));
        int c1y = Math.Min(Height - 1, (int)((y + radius) / CellSize));
        for (int cy = c0y; cy <= c1y; cy++)
        {
            for (int cx = c0x; cx <= c1x; cx++)
            {
                float dx = (cx + 0.5f) * CellSize - x;
                float dy = (cy + 0.5f) * CellSize - y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d >= radius) continue;
                float v = intensity * (1 - d / radius);
                int i = cy * Width + cx;
                if (v > Level[i]) Level[i] = v;
            }
        }
    }

    public float LevelAtTile(int x, int y)
    {
        int cx = Math.Clamp(x / CellSize, 0, Width - 1);
        int cy = Math.Clamp(y / CellSize, 0, Height - 1);
        return Level[cy * Width + cx];
    }
}
