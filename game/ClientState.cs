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
    /// <summary>The selected building (the first of a group). Setting it starts a fresh selection: the group is cleared.</summary>
    public int? SelectedBuilding
    {
        get => _selectedBuilding;
        set { _selectedBuilding = value; SelectedGroup.Clear(); }
    }
    int? _selectedBuilding;

    /// <summary>A double-click's worth of buildings (all of one kind on screen), the first among them; empty for a single one.</summary>
    public readonly HashSet<int> SelectedGroup = new();

    /// <summary>Every selected building: the group, or the one.</summary>
    public IEnumerable<int> SelectedBuildings => SelectedGroup.Count > 0 ? SelectedGroup : _selectedBuilding is { } id ? [id] : [];
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
    /// <summary>Living woods: trees whose felling would open a way in (ForestWatch), ringed in amber.</summary>
    public IReadOnlyCollection<int> EndangeredTrees = Array.Empty<int>();
    /// <summary>Demons per HordeColumns.Cell square, for the shadow a mass of them casts (empty until measured).</summary>
    public int[] Density = [];

    /// <summary>The Hunters' crews (for the eye only).</summary>
    public readonly Hunters Hunters = new();
    public readonly Commuters Commuters = new();

    /// <summary>The Fisheries' boats (for the eye only).</summary>
    public readonly Fishers Fishers = new();

    /// <summary>The people working the Farms' fields (for the eye only).</summary>
    public readonly Farmers Farmers = new();
    /// <summary>Each marching wave column's centre (tiles) and head-count, for its marker.</summary>
    public readonly List<(int Column, Godot.Vector2 Centre, int Count)> Columns = new();
    public readonly List<(DemonHowled Howl, double Age)> Howls = new();
    /// <summary>Patches an Observatory just charted: a beam out to it and its outline, fading.</summary>
    public readonly List<(GroundCharted Chart, double Age)> Charts = new();
    public const double ChartLife = 3;
    public readonly List<(string Text, double Age)> Log = new();

    public void Say(string text)
    {
        Log.Add((text, 0));
        if (Log.Count > 6) Log.RemoveAt(0);
    }

    /// <summary>
    /// Where the armed building would go: under the cursor, or for walls and
    /// gates being dragged, a straight line from where the drag began along
    /// whichever axis the drag has moved furthest. A gate is three tiles wide
    /// with its way through in the middle, under the cursor: it lies along the
    /// wall there (or along the drag), and a dragged line of them goes every
    /// three tiles. Each is its top-left tile and whether it's turned.
    /// </summary>
    public IEnumerable<(int X, int Y, bool Turned)> Ghosts(World world)
    {
        bool gate = Armed is BuildingKind.Gate or BuildingKind.StoneGate;
        bool line = Armed is BuildingKind.Wall or BuildingKind.Gate or BuildingKind.StoneWall or BuildingKind.StoneGate && DragStart != null;
        (int, int, bool) Gate(int x, int y, bool turned) => turned ? (x, y - 1, true) : (x - 1, y, false);
        if (!line)
        {
            yield return gate ? Gate(HoveredTile.X, HoveredTile.Y, RunsNorthSouth(world, HoveredTile.X, HoveredTile.Y)) : (HoveredTile.X, HoveredTile.Y, false);
            yield break;
        }
        var (sx, sy) = DragStartTile;
        var (ex, ey) = HoveredTile;
        bool along = Math.Abs(ex - sx) >= Math.Abs(ey - sy);
        // A single click (no drag yet) on a gate: lie along the wall, as when hovering.
        if (gate && ex == sx && ey == sy) { yield return Gate(sx, sy, RunsNorthSouth(world, sx, sy)); yield break; }
        int step = gate ? 3 : 1;
        if (along)
        {
            int dir = Math.Sign(ex - sx), n = Math.Abs(ex - sx) / step;
            for (int k = 0; k <= n; k++) yield return gate ? Gate(sx + dir * k * step, sy, false) : (sx + dir * k, sy, false);
        }
        else
        {
            int dir = Math.Sign(ey - sy), n = Math.Abs(ey - sy) / step;
            for (int k = 0; k <= n; k++) yield return gate ? Gate(sx, sy + dir * k * step, true) : (sx, sy + dir * k, false);
        }
    }

    /// <summary>Does the wall at this tile run north to south: more wall (or unwalkable ground) above and below it than either side.</summary>
    public static bool RunsNorthSouth(World world, int x, int y)
    {
        int Solid(int tx, int ty) => !world.Terrain.InBounds(tx, ty) ? 0 : world.IsWallAt(tx, ty) ? 2 : !Terrain.IsWalkable(world.Terrain.Get(tx, ty)) ? 1 : 0;
        int ns = Solid(x, y - 1) + Solid(x, y + 1) + Solid(x, y - 2) + Solid(x, y + 2);
        int ew = Solid(x - 1, y) + Solid(x + 1, y) + Solid(x - 2, y) + Solid(x + 2, y);
        return ns > ew;
    }
}
