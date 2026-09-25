namespace Hellwall.Sim;

public enum Tile : byte
{
    Grass,
    Forest,
    Rock,
    Water,
    /// <summary>An iron deposit: walkable, not buildable, worked by a Mine.</summary>
    Ore,
    /// <summary>A silver vein, only near the map's edge: walkable, not buildable, worked by a Silver Mine.</summary>
    Silver,
}

/// <summary>Row-major tile grid. Plain data so it hashes and serializes trivially.</summary>
public sealed class Terrain
{
    public readonly int Width;
    public readonly int Height;
    public readonly Tile[] Tiles;

    public Terrain(int width, int height)
    {
        Width = width;
        Height = height;
        Tiles = new Tile[width * height];
    }

    public int Index(int x, int y) => y * Width + x;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public Tile Get(int x, int y) => Tiles[Index(x, y)];

    /// <summary>A square map from its tiles (a hand-made map); the Keep's clearing is forced to grass.</summary>
    public static Terrain From(int size, Tile[] tiles)
    {
        var t = new Terrain(size, size);
        Array.Copy(tiles, t.Tiles, t.Tiles.Length);
        int c = size / 2, r = Balance.KeepClearRadius;
        for (int y = c - r; y <= c + r; y++)
            for (int x = c - r; x <= c + r; x++)
                if ((x - c) * (x - c) + (y - c) * (y - c) <= r * r) t.Set(x, y, Tile.Grass);
        return t;
    }

    public void Set(int x, int y, Tile tile) => Tiles[Index(x, y)] = tile;

    public static bool IsBuildable(Tile tile) => tile == Tile.Grass;

    /// <summary>Demons cross grass and forest (forest costs double in the flow field); rock and water block.</summary>
    public static bool IsWalkable(Tile tile) => tile is Tile.Grass or Tile.Forest or Tile.Ore or Tile.Silver;
}
