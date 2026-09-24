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

    TileMapLayer _ground = null!, _holy = null!, _tuft = null!, _tall = null!;
    readonly Dictionary<string, int> _sources = new();
    TileSet _set = null!;
    int _holySource;

    public override void _Ready()
    {
        _set = new TileSet { TileShape = TileSet.TileShapeEnum.Isometric, TileLayout = TileSet.TileLayoutEnum.DiamondDown, TileSize = new Vector2I(132, 66) };
        foreach (var tile in Enum.GetValues<Tile>())
        {
            foreach (var path in Art.TerrainImages(tile))
                _sources[path] = AddSource(Art.Tex(path));
            foreach (var name in Art.Scenery(tile))
                _sources[name] = AddScenery(name);
        }
        foreach (var name in Art.Tufts) _sources[name] = AddScenery(name);
        _holySource = AddSource(Art.Diamond(new Color(1f, 0.85f, 0.35f, 0.22f)));

        _ground = Layer(false);
        _holy = Layer(false);
        _tuft = Layer(false);
        AddChild(_ground);
        AddChild(_holy);
        AddChild(_tuft); // low enough never to hide anything, so not sorted; buildings draw over them
        _tall = Layer(true);
        Sorted.AddChild(_tall);

        var t = World.Terrain;
        for (int y = 0; y < t.Height; y++)
            for (int x = 0; x < t.Width; x++)
                PaintCell(x, y);
        Align(_ground);
        Align(_holy);
        Align(_tuft);
        Align(_tall);
        RepaintHoly();
    }

    /// <summary>Lay one tile's ground and whatever stands on it. Call again when the tile changes (a tree felled).</summary>
    public void PaintCell(int x, int y)
    {
        var kind = World.Terrain.Get(x, y);
        var cell = new Vector2I(x, y);
        var blocks = Art.TerrainImages(kind);
        var scenery = Art.Scenery(kind);
        _tall.EraseCell(cell);
        _tuft.EraseCell(cell);
        if (kind == Tile.Ore)
        {
            // Crystal blocks are whole tiles: grass underneath, the crystals sorted with everything standing.
            _ground.SetCell(cell, _sources[Art.TerrainImages(Tile.Grass)[0]], Vector2I.Zero);
            _tall.SetCell(cell, _sources[blocks[Pick(x, y, blocks.Length)]], Vector2I.Zero);
            return;
        }
        _ground.SetCell(cell, _sources[blocks[Pick(x, y, blocks.Length)]], Vector2I.Zero);
        if (scenery.Length > 0) _tall.SetCell(cell, _sources[scenery[Pick(x, y, scenery.Length)]], Vector2I.Zero);
        else if (kind == Tile.Grass && Pick(x * 7 + 3, y * 5 + 1, 14) == 0) _tuft.SetCell(cell, _sources[Art.Tufts[Pick(y, x, Art.Tufts.Length)]], Vector2I.Zero);
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

    /// <summary>A baked scenery sprite as a tile: its base point on the cell's centre.</summary>
    int AddScenery(string name)
    {
        var texture = Art.Tex(Art.SceneryPath(name));
        var size = texture.GetSize();
        var source = new TileSetAtlasSource { Texture = texture, TextureRegionSize = new Vector2I((int)size.X, (int)size.Y) };
        int id = _set.AddSource(source);
        source.CreateTile(Vector2I.Zero);
        var data = source.GetTileData(Vector2I.Zero, 0);
        var basePoint = Art.SceneryBase(name);
        data.TextureOrigin = new Vector2I((int)(basePoint.X - size.X / 2), (int)(basePoint.Y - size.Y / 2));
        data.YSortOrigin = 16;
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
