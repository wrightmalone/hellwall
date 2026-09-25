using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Living woods only: watches the trees the woodsmen are felling near the
/// town, and warns before one comes down that would cut a new way through
/// the forest (it joins two stretches of open ground that don't otherwise
/// meet close by), then again when it falls. Read-only: the sim neither
/// knows nor cares. The trees in question get an amber ring while they last.
/// </summary>
public sealed class ForestWatch
{
    const int Window = 6;        // tiles each way in which "otherwise meet" is judged
    const float NearTown = 14;   // only gaps this close to a building matter
    const float WarnAt = 0.6f;   // warn once a gap tree is down to this share of its health

    readonly HashSet<int> _warned = new();
    /// <summary>Print each warning (headless checks).</summary>
    public static bool Log;
    public readonly HashSet<int> Endangered = new();

    /// <summary>Check the trees being chopped; say what's about to open.</summary>
    public void Step(World world, AlertFeed alerts)
    {
        Endangered.Clear();
        if (!world.ForestBlocks) return;
        int w = world.Terrain.Width;
        foreach (var m in world.Woodsmen)
        {
            if (m.State != WoodsmanState.Chopping || m.Tree < 0) continue;
            int tree = m.Tree, x = tree % w, y = tree / w;
            if (!_warned.Contains(tree) && world.TreeHealth(tree) > world.TreeFullHealth * WarnAt) continue;
            if (!NearBuildings(world, x, y) || !OpensAGap(world, x, y)) continue;
            Endangered.Add(tree);
            if (!_warned.Add(tree)) continue; // warned already: the ring stays, the alert doesn't repeat
            if (Log) GD.Print($"hellwall: forest gap warning at {x},{y}");
            alerts.Push("gap-" + tree, "Woodsmen are about to cut a way through the forest: demons will walk in here", new Color(1, 0.75f, 0.3f), new Vector2(x + 0.5f, y + 0.5f), 14);
        }
    }

    /// <summary>A tree came down: if it was one we warned of, the way is open now.</summary>
    public void Felled(TreeFelled f, int width, AlertFeed alerts)
    {
        if (!_warned.Remove(f.Y * width + f.X)) return;
        alerts.Push("open-" + f.X + "," + f.Y, "The forest is open: a new way in for the horde", new Color(1, 0.35f, 0.3f), new Vector2(f.X + 0.5f, f.Y + 0.5f), 14);
    }

    static bool NearBuildings(World world, int x, int y)
    {
        foreach (var b in world.Buildings)
        {
            if (b.IsWallLike) continue;
            float dx = b.CentreX - x, dy = b.CentreY - y;
            if (dx * dx + dy * dy <= NearTown * NearTown) return true;
        }
        return false;
    }

    static bool OpensAGap(World world, int x, int y) => WoodsSystem.OpensAGap(world.Terrain, x, y, Window);
}
