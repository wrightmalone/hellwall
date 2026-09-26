using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// The command card's keys, by position (StarCraft 2's grid layout): the
/// card is three rows of five, and each key presses the cell where it sits
/// on the left hand, whatever is in it. Building is two keys: a category on
/// the top row, then the building below it (House: Q then A). The number
/// keys are control groups; the camera is on the arrows and middle-drag.
/// </summary>
public static class HotkeyGrid
{
    public const int Rows = 3, Cols = 5, Cells = Rows * Cols;

    static readonly Key[] Keys =
    [
        Key.Q, Key.W, Key.E, Key.R, Key.T,
        Key.A, Key.S, Key.D, Key.F, Key.G,
        Key.Z, Key.X, Key.C, Key.V, Key.B,
    ];

    /// <summary>The cell a key presses (row by row, 0 to 14), or -1 for a key off the grid.</summary>
    public static int CellOf(Key key) => Array.IndexOf(Keys, key);

    public static string Label(int cell) => Keys[cell].ToString();

    /// <summary>The build categories, on the top row; each one's buildings fill the two rows below, in order.</summary>
    public static readonly (string Name, BuildingKind[] Kinds)[] Tabs =
    [
        ("Town", [BuildingKind.House, BuildingKind.Farm, BuildingKind.Hunter, BuildingKind.Fishery, BuildingKind.Woodcutter, BuildingKind.Quarry]),
        ("Works", [BuildingKind.Mine, BuildingKind.SilverMine, BuildingKind.Barracks, BuildingKind.Scriptorium]),
        ("Holy", [BuildingKind.Shrine, BuildingKind.Wardstone]),
        ("Walls", [BuildingKind.Wall, BuildingKind.StoneWall, BuildingKind.Gate, BuildingKind.StoneGate]),
        ("Towers", [BuildingKind.Watchtower, BuildingKind.Bombard, BuildingKind.LanceTower, BuildingKind.Censer, BuildingKind.Belfry, BuildingKind.Skyspire]),
    ];

    /// <summary>The first cell of the rows under the categories.</summary>
    public const int FirstBuildCell = Cols;

    /// <summary>The two keys that build it, for hints: "Q A" for a House. Empty if it isn't on the card.</summary>
    public static string KeysFor(BuildingKind kind)
    {
        for (int t = 0; t < Tabs.Length; t++)
        {
            int i = Array.IndexOf(Tabs[t].Kinds, kind);
            if (i >= 0) return $"{Label(t)} {Label(FirstBuildCell + i)}";
        }
        return "";
    }
}
