using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>Placeholder art, all in one place: colours and short labels until real sprites arrive.</summary>
public static class Palette
{
    public const int TilePx = 8;

    public static readonly Color[] Tiles =
    [
        new(0.36f, 0.52f, 0.28f), // Grass
        new(0.16f, 0.32f, 0.18f), // Forest
        new(0.46f, 0.44f, 0.42f), // Rock
        new(0.18f, 0.30f, 0.50f), // Water
    ];

    public static Color Building(BuildingKind kind) => kind switch
    {
        BuildingKind.Keep => new(0.90f, 0.76f, 0.35f),
        BuildingKind.House => new(0.72f, 0.54f, 0.38f),
        BuildingKind.Woodcutter => new(0.55f, 0.40f, 0.22f),
        BuildingKind.Quarry => new(0.62f, 0.60f, 0.58f),
        BuildingKind.Hunter => new(0.58f, 0.66f, 0.36f),
        BuildingKind.Shrine => new(0.95f, 0.92f, 0.70f),
        BuildingKind.Wardstone => new(0.85f, 0.80f, 0.55f),
        BuildingKind.Wall => new(0.66f, 0.66f, 0.70f),
        BuildingKind.Gate => new(0.50f, 0.40f, 0.30f),
        BuildingKind.Watchtower => new(0.45f, 0.55f, 0.75f),
        BuildingKind.Bombard => new(0.35f, 0.38f, 0.55f),
        BuildingKind.Barracks => new(0.70f, 0.30f, 0.30f),
        BuildingKind.Farm => new(0.85f, 0.80f, 0.40f),
        BuildingKind.StoneWall => new(0.50f, 0.50f, 0.56f),
        BuildingKind.LanceTower => new(0.28f, 0.42f, 0.70f),
        BuildingKind.Scriptorium => new(0.62f, 0.46f, 0.78f),
        _ => Colors.Magenta,
    };

    public static string Label(BuildingKind kind) => kind switch
    {
        BuildingKind.Keep => "KEEP",
        BuildingKind.House => "Ho",
        BuildingKind.Woodcutter => "Wc",
        BuildingKind.Quarry => "Qu",
        BuildingKind.Hunter => "Hu",
        BuildingKind.Shrine => "Sh",
        BuildingKind.Wardstone => "",
        BuildingKind.Watchtower => "Tw",
        BuildingKind.Bombard => "Bo",
        BuildingKind.Barracks => "Bar",
        BuildingKind.Farm => "Farm",
        BuildingKind.LanceTower => "La",
        BuildingKind.Scriptorium => "Scr",
        _ => "",
    };

    public static Color Unit(UnitKind kind) => kind switch
    {
        UnitKind.Militia => new(0.55f, 0.75f, 1.0f),
        UnitKind.Marksman => new(0.70f, 1.0f, 0.70f),
        UnitKind.Templar => new(1.0f, 0.95f, 0.75f),
        UnitKind.Crossbowman => new(0.45f, 0.95f, 0.95f),
        _ => Colors.White,
    };

    public static readonly Color Consecrated = new(1.0f, 0.88f, 0.45f, 0.13f);
    public static readonly Color Selected = new(0.35f, 1.0f, 0.35f);
    public static readonly Color GhostOk = new(0.3f, 1f, 0.3f, 0.45f);
    public static readonly Color GhostBad = new(1f, 0.25f, 0.25f, 0.45f);
    public static readonly Color Tracer = new(1f, 0.95f, 0.6f);

    /// <summary>Build bar order, with each slot's hotkey.</summary>
    public static readonly (BuildingKind Kind, Key Key, string Label)[] BuildBar =
    [
        (BuildingKind.House, Key.Key1, "1"), (BuildingKind.Farm, Key.Key2, "2"), (BuildingKind.Hunter, Key.Key3, "3"),
        (BuildingKind.Woodcutter, Key.Key4, "4"), (BuildingKind.Quarry, Key.Key5, "5"), (BuildingKind.Shrine, Key.Key6, "6"),
        (BuildingKind.Wardstone, Key.Key7, "7"), (BuildingKind.Wall, Key.Key8, "8"), (BuildingKind.StoneWall, Key.Key9, "9"),
        (BuildingKind.Watchtower, Key.Key0, "0"), (BuildingKind.Bombard, Key.Minus, "-"), (BuildingKind.LanceTower, Key.Equal, "="),
        (BuildingKind.Gate, Key.G, "G"), (BuildingKind.Barracks, Key.B, "B"), (BuildingKind.Scriptorium, Key.U, "U"),
    ];
}
