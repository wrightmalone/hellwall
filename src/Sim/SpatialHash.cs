namespace Hellwall.Sim;

/// <summary>
/// One cell per tile, rebuilt every tick by counting sort: count per cell,
/// prefix-sum into CellStart, scatter slot indices into Items. O(n), no
/// allocation after construction, and the order within a cell is ascending
/// slot index, so anything that iterates it is deterministic.
///
/// Units in cell c are Items[CellStart[c] .. CellStart[c + 1]).
/// </summary>
internal sealed class SpatialHash
{
    public readonly int Width;
    public readonly int Height;
    public readonly int[] CellStart;
    public int[] Items;

    /// <summary>
    /// Demons per tile. Equal to the CellStart spans after Build, then kept
    /// live by movement so the density cap is exact within a tick.
    /// </summary>
    public readonly int[] Count;

    readonly int[] _cursor;

    public SpatialHash(int width, int height)
    {
        Width = width;
        Height = height;
        CellStart = new int[width * height + 1];
        _cursor = new int[width * height];
        Count = new int[width * height];
        Items = new int[1024];
    }

    public void Build(Horde horde)
    {
        int n = horde.Count;
        if (Items.Length < n) Items = new int[Math.Max(n, Items.Length * 2)];

        Array.Clear(CellStart);
        for (int i = 0; i < n; i++) CellStart[CellOf(horde.X[i], horde.Y[i]) + 1]++;
        for (int c = 0; c < Width * Height; c++)
        {
            Count[c] = CellStart[c + 1];
            CellStart[c + 1] += CellStart[c];
        }
        Array.Copy(CellStart, _cursor, _cursor.Length);
        for (int i = 0; i < n; i++) Items[_cursor[CellOf(horde.X[i], horde.Y[i])]++] = i;
    }

    /// <summary>Inclusive tile range covering a square of the given radius around a point, clamped to the map.</summary>
    public (int X0, int X1, int Y0, int Y1) CellRange(float x, float y, float radius) =>
        (Math.Max(0, (int)(x - radius)), Math.Min(Width - 1, (int)(x + radius)),
         Math.Max(0, (int)(y - radius)), Math.Min(Height - 1, (int)(y + radius)));

    int CellOf(float x, float y)
    {
        int tx = Math.Clamp((int)x, 0, Width - 1);
        int ty = Math.Clamp((int)y, 0, Height - 1);
        return ty * Width + tx;
    }
}
