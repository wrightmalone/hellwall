using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// The ground as isometric tile layers: flat blocks (grass, water) in one
/// layer, and the tall ones (trees, rocks, ore) in a y-sorted layer that
/// sorts with buildings and soldiers, so a tree in front of a house hides
/// it. Holy ground is a translucent gold layer between them, repainted when
/// the holy grid changes. Variants are picked by a hash of the tile, so the
/// same map always looks the same.
/// </summary>
public partial class TerrainView : Node2D
{
    public World World = null!;
    /// <summary>The y-sorted parent that buildings and soldiers also live in.</summary>
    public Node2D Sorted = null!;

    TileMapLayer _ground = null!, _holy = null!, _tall = null!;
    readonly Dictionary<string, int> _sources = new();
    TileSet _set = null!;
    int _holySource;

    public override void _Ready()
    {
        _set = new TileSet { TileShape = TileSet.TileShapeEnum.Isometric, TileLayout = TileSet.TileLayoutEnum.DiamondDown, TileSize = new Vector2I(132, 66) };
        foreach (var tile in Enum.GetValues<Tile>())
            foreach (var path in Art.TerrainImages(tile))
                _sources[path] = AddSource(Art.Tex(path));
        _holySource = AddSource(Art.Diamond(new Color(1f, 0.85f, 0.35f, 0.22f)));

        _ground = Layer(false);
        _holy = Layer(false);
        AddChild(_ground);
        AddChild(_holy);
        _tall = Layer(true);
        Sorted.AddChild(_tall);

        var t = World.Terrain;
        for (int y = 0; y < t.Height; y++)
            for (int x = 0; x < t.Width; x++)
            {
                var kind = t.Get(x, y);
                var images = Art.TerrainImages(kind);
                bool tall = kind is Tile.Forest or Tile.Rock or Tile.Ore;
                string ground = tall ? Art.TerrainImages(Tile.Grass)[0] : images[Pick(x, y, images.Length)];
                _ground.SetCell(new Vector2I(x, y), _sources[ground], Vector2I.Zero);
                if (tall) _tall.SetCell(new Vector2I(x, y), _sources[images[Pick(x, y, images.Length)]], Vector2I.Zero);
            }
        Align(_ground);
        Align(_holy);
        Align(_tall);
        RepaintHoly();
    }

    public void RepaintHoly()
    {
        var t = World.Terrain;
        var holy = World.Colony.Consecrated;
        for (int y = 0; y < t.Height; y++)
            for (int x = 0; x < t.Width; x++)
            {
                bool on = holy[t.Index(x, y)];
                var cell = new Vector2I(x, y);
                if (on) _holy.SetCell(cell, _holySource, Vector2I.Zero);
                else if (_holy.GetCellSourceId(cell) != -1) _holy.EraseCell(cell);
            }
    }

    static int Pick(int x, int y, int n)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        return (int)(h % (uint)n);
    }

    int AddSource(Texture2D texture)
    {
        var size = texture.GetSize();
        var source = new TileSetAtlasSource { Texture = texture, TextureRegionSize = new Vector2I((int)size.X, (int)size.Y) };
        int id = _set.AddSource(source);
        source.CreateTile(Vector2I.Zero);
        var data = source.GetTileData(Vector2I.Zero, 0);
        // Godot centres a tile's texture on the cell; Kenney's blocks put the
        // top diamond's centre 33 px down from the top, so move the texture
        // down by the difference. Tall details (trees) grow upward from the same diamond.
        data.TextureOrigin = new Vector2I(0, -(int)(size.Y / 2 - 33));
        data.YSortOrigin = 16; // the diamond's lower half: sorted like something standing on the tile
        return id;
    }

    TileMapLayer Layer(bool ySorted) => new() { TileSet = _set, Scale = new Vector2(0.5f, 0.5f), YSortEnabled = ySorted, TextureFilter = TextureFilterEnum.Linear };

    /// <summary>Shift a layer so its cell centres land where Iso puts tile centres.</summary>
    static void Align(TileMapLayer layer)
    {
        var local = layer.MapToLocal(Vector2I.Zero) * layer.Scale;
        layer.Position = Iso.P(0.5f, 0.5f) - local;
    }
}
