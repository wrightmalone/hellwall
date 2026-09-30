using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Placeholder art from Kenney's CC0 packs (game/art/kenney, each with its
/// License.txt): Tower Defense and Isometric Tiles Landscape for terrain and
/// buildings; soldiers and demons are sheets baked from Kenney's 3D Mini
/// Dungeon and Graveyard Kit characters (tools/bake.gd). Everything that names a
/// file lives here, so swapping the art is one file.
/// </summary>
public static class Art
{
    const string TD = "res://art/kenney/tower-defense/PNG/";
    const string Land = "res://art/kenney/isometric-landscape/PNG/";

    static readonly Dictionary<string, Texture2D> Cache = new();

    public static Texture2D Tex(string path) => Cache.TryGetValue(path, out var t) ? t : Cache[path] = path.StartsWith(SilverTint) ? Silvered(path[SilverTint.Length..]) : GD.Load<Texture2D>(path);

    /// <summary>A path prefix: the image, drained of colour and washed pale blue-white (silver veins are the ore crystals, silvered).</summary>
    public const string SilverTint = "silver:";

    static Texture2D Silvered(string path)
    {
        var image = GD.Load<Texture2D>(path).GetImage();
        image.Decompress();
        if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
            {
                var c = image.GetPixel(x, y);
                float v = Mathf.Clamp(c.R * 0.3f + c.G * 0.59f + c.B * 0.11f, 0, 1);
                float s = v * v * 1.6f; // contrast, so the facets still read
                image.SetPixel(x, y, new Color(0.25f + s * 0.75f, 0.29f + s * 0.72f, 0.37f + s * 0.66f, c.A));
            }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Terrain blocks per tile kind: every image is a 132-wide block whose top diamond is 66 high.
    /// Forest and rock tiles are grass blocks with scenery standing on them (see Scenery); ore keeps Tower Defense's crystal blocks.</summary>
    public static string[] TerrainImages(Tile tile) => tile switch
    {
        Tile.Ore => Enumerable.Range(1, 4).Select(i => $"{TD}Details/crystals_{i}.png").ToArray(),
        Tile.Silver => Enumerable.Range(1, 4).Select(i => $"{SilverTint}{TD}Details/crystals_{i}.png").ToArray(),
        Tile.Water => [$"{Land}landscapeTiles_066.png"],
        _ => [$"{TD}Landscape/landscape_13.png"],
    };

    const string Baked = "res://art/baked/scenery/";

    /// <summary>
    /// Trees and rocks baked from Quaternius's Stylized Nature MegaKit
    /// (tools/bake_scenery.gd), at the terrain's scale: what stands on a
    /// forest or rock tile, and the tufts dotted on open grass.
    /// </summary>
    public static string[] Scenery(Tile tile) => tile switch
    {
        Tile.Forest => ["tree-1", "tree-2", "tree-3", "tree-4", "tree-5", "pine-1", "pine-2", "pine-3", "pine-4", "pine-5"],
        Tile.Rock => ["rock-1", "rock-2", "rock-3", "rock-4"],
        _ => [],
    };

    public static readonly string[] Tufts = ["tuft-1", "tuft-2", "tuft-3", "tuft-4"];

    public static string SceneryPath(string name) => $"{Baked}{name}.png";

    const string BakedBuildings = "res://art/baked/buildings/";
    static Dictionary<string, Vector2>? _buildingBase;

    /// <summary>
    /// A baked building sprite (KayKit Medieval Hexagon, tools/bake_buildings.gd) and where its
    /// footprint's centre lands in the image, or null where there isn't one (walls and gates
    /// are drawn by WallSprite). Also "scaffold-1..3" and "ruin".
    /// </summary>
    public static (Texture2D Texture, Vector2 Base)? BakedBuilding(string name)
    {
        if (_buildingBase == null)
        {
            _buildingBase = new();
            string path = $"{BakedBuildings}buildings.json";
            if (Godot.FileAccess.FileExists(path))
                foreach (var (key, value) in Json.ParseString(Godot.FileAccess.GetFileAsString(path)).AsGodotDictionary())
                {
                    var xy = value.AsGodotArray();
                    _buildingBase[key.AsString()] = new Vector2((float)xy[0].AsDouble(), (float)xy[1].AsDouble());
                }
        }
        return _buildingBase.TryGetValue(name, out var at) ? (Tex($"{BakedBuildings}{name}.png"), at) : null;
    }

    static Dictionary<string, Vector2>? _sceneryBase;

    /// <summary>Where a scenery sprite's base (the point it stands on) is, in its image's pixels.</summary>
    public static Vector2 SceneryBase(string name)
    {
        if (_sceneryBase == null)
        {
            _sceneryBase = new();
            var json = Json.ParseString(Godot.FileAccess.GetFileAsString($"{Baked}scenery.json")).AsGodotDictionary();
            foreach (var (key, value) in json)
            {
                var xy = value.AsGodotArray();
                _sceneryBase[key.AsString()] = new Vector2((float)xy[0].AsDouble(), (float)xy[1].AsDouble());
            }
        }
        return _sceneryBase[name];
    }

    /// <summary>A diamond, for tinting ground (holy ground, placement ghosts) as a tile layer.</summary>
    public static Texture2D Diamond(Color colour)
    {
        var img = Image.CreateEmpty(132, 66, false, Image.Format.Rgba8);
        for (int y = 0; y < 66; y++)
            for (int x = 0; x < 132; x++)
                if (Mathf.Abs(x - 65.5f) / 66f + Mathf.Abs(y - 32.5f) / 33f <= 1) img.SetPixel(x, y, colour);
        return ImageTexture.CreateFromImage(img);
    }

    static Texture2D? _shadow;

    /// <summary>A soft black ellipse, drawn under figures.</summary>
    public static Texture2D Shadow()
    {
        if (_shadow != null) return _shadow;
        var img = Image.CreateEmpty(32, 16, false, Image.Format.Rgba8);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 32; x++)
            {
                float dx = (x - 15.5f) / 16f, dy = (y - 7.5f) / 8f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                img.SetPixel(x, y, new Color(0, 0, 0, Mathf.Clamp(1 - d, 0, 1) * 0.55f));
            }
        return _shadow = ImageTexture.CreateFromImage(img);
    }

    // --- buildings: stacks of Tower Defense pieces, bottom first ---

    static string Grey(int i) => $"{TD}Towers (grey)/tower_{i:00}.png";
    static string Red(int i) => $"{TD}Towers (red)/tower_{i:00}.png";
    static string Brown(int i) => $"{TD}Towers (brown)/tower_{i:00}.png";

    /// <summary>A ruin: three broken stone pieces, drawn dark (see WorldView.RuinSprite).</summary>
    public static readonly string[] RuinPieces = [Grey(18), Grey(8), Grey(28)];

    /// <summary>Mostly one piece each: stacked towers buried the town. Only the Keep and the Lance Tower stand taller.</summary>
    public static string[] BuildingPieces(BuildingKind kind) => kind switch
    {
        BuildingKind.Keep => [Grey(1), Grey(21)],
        BuildingKind.House => [Brown(24)],
        BuildingKind.Cottage => [Grey(24)],
        BuildingKind.Manor => [Grey(18), Grey(24)],
        BuildingKind.Woodcutter => [Brown(35)],
        BuildingKind.Hunter => [Brown(44)],
        BuildingKind.Fishery => [Brown(40)],
        BuildingKind.Farm => [Brown(12)],
        BuildingKind.Quarry => [Grey(7)],
        BuildingKind.Mine => [Brown(29)],
        BuildingKind.SilverMine => [Grey(29)],
        BuildingKind.Shrine => [Grey(28)],
        BuildingKind.Wardstone => [Grey(41)],
        BuildingKind.Wall => [Brown(8)],
        BuildingKind.StoneWall => [Grey(8)],
        BuildingKind.Gate => [Brown(7)],
        BuildingKind.StoneGate => [Grey(8)],
        BuildingKind.Watchtower => [Grey(36)],
        BuildingKind.Bombard => [Red(25)],
        BuildingKind.LanceTower => [Grey(1), Grey(46)],
        BuildingKind.Skyspire => [Grey(41)],
        BuildingKind.Censer => [Red(32)],
        BuildingKind.Belfry => [Red(23)],
        BuildingKind.Barracks => [Red(14)],
        BuildingKind.Scriptorium => [Red(34)],
        BuildingKind.Observatory => [Grey(36)],
        _ => [Grey(1)],
    };

    /// <summary>How wide a building's pieces draw, as a share of its footprint's diamond. Walls fill theirs, so a line of them reads as one wall.</summary>
    public static float Fill(BuildingKind kind) => kind switch
    {
        BuildingKind.Wall or BuildingKind.StoneWall or BuildingKind.Gate or BuildingKind.StoneGate => 1.0f,
        BuildingKind.Farm => 0.45f,
        BuildingKind.Wardstone or BuildingKind.Skyspire or BuildingKind.Censer => 0.8f,
        _ => 0.72f,
    };

    /// <summary>Worked ground under a building, for kinds that want it. None now: a Farm's fields show as its crops, tended by its farmers.</summary>
    public static string? Ground(BuildingKind kind) => null;

    // --- soldiers and demons: sheets baked from Kenney's 3D characters (tools/bake.gd) ---

    /// <summary>Every sheet is Directions rows (facing d points along sim angle d x 45 deg) by Frames walk frames of Cell px.</summary>
    public const int Cell = 72, Frames = 6, Directions = 8;

    /// <summary>Where the figure's feet are in a cell (tools/bake.gd writes it to sheets.json).</summary>
    public static readonly Vector2 Feet = new(36f, 56.78f);

    /// <summary>Soldiers draw at this scale of their baked cell.</summary>
    public const float UnitScale = 0.8f;

    /// <summary>A soldier's height on screen, feet to head, in world pixels (for picking).</summary>
    public const float UnitSize = 30;

    public static string Unit(UnitKind kind) => $"res://art/baked/unit-{kind.ToString().ToLowerInvariant()}.png";

    public static string Demon(DemonKind kind) => $"res://art/baked/demon-{kind.ToString().ToLowerInvariant()}.png";

    /// <summary>Demons draw at this scale of their baked cell (the bake already made the big ones big).</summary>
    public const float DemonScale = 0.72f;

    /// <summary>The facing row for a direction of travel in sim units.</summary>
    public static int Facing(float dx, float dy) =>
        dx == 0 && dy == 0 ? 1 : ((int)Mathf.Round(Mathf.Atan2(dy, dx) / (Mathf.Pi / 4)) + 8) % 8;
}
