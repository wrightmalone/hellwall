using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// What the player is doing that the sim doesn't need to know about:
/// selection, the armed build tool, a drag in progress, control groups, and
/// short-lived effects. Shared between input, world view and HUD.
/// </summary>
public sealed class ClientState
{
    public const double ShotLife = 0.12;

    public BuildingKind? Armed;
    /// <summary>A was pressed with soldiers selected: the next left-click on the ground attack-moves them there.</summary>
    public bool AttackMoveArmed;
    /// <summary>With AttackMoveArmed: the click gives a patrol, not an attack-move.</summary>
    public bool PatrolArmed;
    /// <summary>F4: the noise grid over the ground, so it's plain what will wake the packs.</summary>
    public bool ShowNoise;
    public int? SelectedBuilding;
    public readonly HashSet<int> SelectedUnits = new();
    public readonly Dictionary<int, int[]> Groups = new();

    /// <summary>World-pixel position where a left drag began, while the button is held.</summary>
    public Vector2? DragStart;
    public (int X, int Y) DragStartTile;
    public Vector2 MouseWorld;
    public (int X, int Y) HoveredTile;

    /// <summary>Render interpolation between the last two ticks.</summary>
    public float Alpha;

    public readonly List<(ShotFired Shot, double Age)> Shots = new();
    public readonly List<(DemonBurst Burst, double Age)> Bursts = new();
    public readonly List<(DemonSpat Spit, double Age)> Spits = new();
    /// <summary>Where demons fell, fading: a dark splash on the ground, at most MaxMarks of them.</summary>
    public readonly List<(float X, float Y, float Size, double Age)> Marks = new();
    public const int MaxMarks = 2500;
    public const double MarkLife = 4;
    /// <summary>Where an order was just given, shrinking away: green for a move, red for an attack.</summary>
    public readonly List<(float X, float Y, bool Attack, double Age)> OrderPings = new();
    public readonly List<(DemonHowled Howl, double Age)> Howls = new();
    public readonly List<(string Text, double Age)> Log = new();

    public void Say(string text)
    {
        Log.Add((text, 0));
        if (Log.Count > 6) Log.RemoveAt(0);
    }

    /// <summary>
    /// Where the armed building would go: under the cursor, or for walls and
    /// gates being dragged, a straight line from where the drag began along
    /// whichever axis the drag has moved furthest.
    /// </summary>
    public IEnumerable<(int X, int Y)> GhostTiles()
    {
        bool line = Armed is BuildingKind.Wall or BuildingKind.Gate or BuildingKind.StoneWall or BuildingKind.StoneGate && DragStart != null;
        if (!line)
        {
            yield return HoveredTile;
            yield break;
        }
        var (sx, sy) = DragStartTile;
        var (ex, ey) = HoveredTile;
        if (Math.Abs(ex - sx) >= Math.Abs(ey - sy))
            for (int x = Math.Min(sx, ex); x <= Math.Max(sx, ex); x++) yield return (x, sy);
        else
            for (int y = Math.Min(sy, ey); y <= Math.Max(sy, ey); y++) yield return (sx, y);
    }
}
