namespace Hellwall.Sim;

public enum Tile : byte
{
    Grass,
    Forest,
    Rock,
    Water,
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

    public void Set(int x, int y, Tile tile) => Tiles[Index(x, y)] = tile;

    public static bool IsBuildable(Tile tile) => tile == Tile.Grass;

    /// <summary>Demons cross grass and forest (forest costs double in the flow field); rock and water block.</summary>
    public static bool IsWalkable(Tile tile) => tile is Tile.Grass or Tile.Forest;
}
