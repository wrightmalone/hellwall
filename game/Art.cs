using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Placeholder art from Kenney's CC0 packs (game/art/kenney, each with its
/// License.txt): Tower Defense and Isometric Tiles Landscape for terrain and
/// buildings, Tiny Dungeon for soldiers and demons. Everything that names a
/// file lives here, so swapping the art is one file.
/// </summary>
public static class Art
{
    const string TD = "res://art/kenney/tower-defense/PNG/";
    const string Land = "res://art/kenney/isometric-landscape/PNG/";
    const string Tiny = "res://art/kenney/tiny-dungeon/Tiles/";

    static readonly Dictionary<string, Texture2D> Cache = new();

    public static Texture2D Tex(string path) => Cache.TryGetValue(path, out var t) ? t : Cache[path] = GD.Load<Texture2D>(path);

    /// <summary>Terrain blocks per tile kind: every image is a 132-wide block whose top diamond is 66 high.</summary>
    public static string[] TerrainImages(Tile tile) => tile switch
    {
        Tile.Forest => Enumerable.Range(1, 12).Select(i => $"{TD}Details/trees_{i}.png").ToArray(),
        Tile.Rock => Enumerable.Range(1, 8).Select(i => $"{TD}Details/rocks_{i}.png").ToArray(),
        Tile.Ore => Enumerable.Range(1, 4).Select(i => $"{TD}Details/crystals_{i}.png").ToArray(),
        Tile.Water => [$"{Land}landscapeTiles_066.png"],
        _ => [$"{TD}Landscape/landscape_13.png"],
    };

    /// <summary>A diamond, for tinting ground (holy ground, placement ghosts) as a tile layer.</summary>
    public static Texture2D Diamond(Color colour)
    {
        var img = Image.CreateEmpty(132, 66, false, Image.Format.Rgba8);
        for (int y = 0; y < 66; y++)
            for (int x = 0; x < 132; x++)
                if (Mathf.Abs(x - 65.5f) / 66f + Mathf.Abs(y - 32.5f) / 33f <= 1) img.SetPixel(x, y, colour);
        return ImageTexture.CreateFromImage(img);
    }

    // --- buildings: stacks of Tower Defense pieces, bottom first ---

    static string Grey(int i) => $"{TD}Towers (grey)/tower_{i:00}.png";
    static string Red(int i) => $"{TD}Towers (red)/tower_{i:00}.png";
    static string Brown(int i) => $"{TD}Towers (brown)/tower_{i:00}.png";

    /// <summary>Mostly one piece each: stacked towers buried the town. Only the Keep and the Lance Tower stand taller.</summary>
    public static string[] BuildingPieces(BuildingKind kind) => kind switch
    {
        BuildingKind.Keep => [Grey(1), Grey(21)],
        BuildingKind.House => [Brown(24)],
        BuildingKind.Woodcutter => [Brown(35)],
        BuildingKind.Hunter => [Brown(44)],
        BuildingKind.Farm => [Brown(12)],
        BuildingKind.Quarry => [Grey(7)],
        BuildingKind.Mine => [Brown(29)],
        BuildingKind.Shrine => [Grey(28)],
        BuildingKind.Wardstone => [Grey(41)],
        BuildingKind.Wall => [Brown(8)],
        BuildingKind.StoneWall => [Grey(8)],
        BuildingKind.Gate => [Brown(7)],
        BuildingKind.Watchtower => [Grey(36)],
        BuildingKind.Bombard => [Red(25)],
        BuildingKind.LanceTower => [Grey(1), Grey(46)],
        BuildingKind.Skyspire => [Grey(41)],
        BuildingKind.Censer => [Red(32)],
        BuildingKind.Belfry => [Red(23)],
        BuildingKind.Barracks => [Red(14)],
        BuildingKind.Scriptorium => [Red(34)],
        _ => [Grey(1)],
    };

    /// <summary>How wide a building's pieces draw, as a share of its footprint's diamond. Walls fill theirs, so a line of them reads as one wall.</summary>
    public static float Fill(BuildingKind kind) => kind switch
    {
        BuildingKind.Wall or BuildingKind.StoneWall or BuildingKind.Gate => 1.0f,
        BuildingKind.Farm => 0.45f,
        BuildingKind.Wardstone or BuildingKind.Skyspire or BuildingKind.Censer => 0.8f,
        _ => 0.72f,
    };

    /// <summary>Farms and the like show worked ground under the building.</summary>
    public static string? Ground(BuildingKind kind) => kind switch
    {
        BuildingKind.Farm => $"{Land}landscapeTiles_073.png",
        _ => null,
    };

    // --- soldiers and demons: 16 px Tiny Dungeon figures ---

    static string Figure(int i) => $"{Tiny}tile_{i:0000}.png";

    public static string Unit(UnitKind kind) => Figure(kind switch
    {
        UnitKind.Militia => 98,
        UnitKind.Marksman => 112,
        UnitKind.Templar => 96,
        UnitKind.Crossbowman => 87,
        UnitKind.Chaplain => 111,
        UnitKind.Outrider => 88,
        _ => 85,
    });

    public static string Demon(DemonKind kind) => Figure(kind switch
    {
        DemonKind.Hound => 123,
        DemonKind.Thrall => 121,
        DemonKind.Gargoyle => 120,
        DemonKind.Bloater => 108,
        DemonKind.Brute => 109,
        DemonKind.Howler => 124,
        DemonKind.Broodmother => 122,
        _ => 110,
    });

    /// <summary>On-screen height of a figure, in world pixels: big demons are drawn bigger.</summary>
    public static float DemonSize(DemonKind kind) => kind switch
    {
        DemonKind.Brute or DemonKind.Broodmother => 34,
        DemonKind.Bloater => 30,
        DemonKind.Hound => 20,
        _ => 22,
    };

    public const float UnitSize = 24;
}
