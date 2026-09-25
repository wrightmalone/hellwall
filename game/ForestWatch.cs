using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Watches the trees woodsmen are felling (living woods) and the rock miners
/// are wearing away near the town, and warns before one goes that would cut a
/// new way in (it joins two stretches of open ground that don't otherwise
/// meet close by), then again when it's gone. Also says when a Quarry or Mine
/// has nothing left in reach. Read-only: the sim neither knows nor cares. The
/// tiles in question get an amber ring while they last.
/// </summary>
public sealed class ForestWatch
{
    const int Window = 6;        // tiles each way in which "otherwise meet" is judged
    const float NearTown = 14;   // only gaps this close to a building matter
    const float WarnAt = 0.6f;   // warn once a gap tree is down to this share of its health

    readonly HashSet<int> _warned = new();
    readonly HashSet<int> _workedOut = new();
    /// <summary>Print each warning (headless checks).</summary>
    public static bool Log;
    public readonly HashSet<int> Endangered = new();

    /// <summary>Check the trees being chopped; say what's about to open.</summary>
    public void Step(World world, AlertFeed alerts)
    {
        Endangered.Clear();
        WatchWorkedOut(world, alerts);
        if (!world.ForestBlocks && !world.Mining) return;
        int w = world.Terrain.Width;
        foreach (var m in world.Woodsmen)
        {
            if (m.State != WoodsmanState.Chopping || m.Tree < 0) continue;
            int tile = m.Tree, x = tile % w, y = tile / w;
            var kind = world.Terrain.Tiles[tile];
            // Ore is open ground already; only trees (when they're a wall) and rock keep the horde out.
            if (!(kind == Tile.Rock || (kind == Tile.Forest && world.ForestBlocks))) continue;
            if (!_warned.Contains(tile) && world.TreeHealth(tile) > world.FullHp(kind) * WarnAt) continue;
            if (!NearBuildings(world, x, y) || !OpensAGap(world, x, y)) continue;
            Endangered.Add(tile);
            if (!_warned.Add(tile)) continue; // warned already: the ring stays, the alert doesn't repeat
            if (Log) GD.Print($"hellwall: {(kind == Tile.Rock ? "rock" : "forest")} gap warning at {x},{y}");
            alerts.Push("gap-" + tile, kind == Tile.Rock
                ? "Miners are about to break a way through the rock: demons will walk in here"
                : "Woodsmen are about to cut a way through the forest: demons will walk in here", new Color(1, 0.75f, 0.3f), new Vector2(x + 0.5f, y + 0.5f), 14);
        }
    }

    /// <summary>Say once when a Quarry or Mine runs out of ground to work (and again if it runs out again later).</summary>
    void WatchWorkedOut(World world, AlertFeed alerts)
    {
        foreach (var b in world.Buildings)
        {
            if (!b.Def.Miners) continue;
            if (!b.Exhausted) { _workedOut.Remove(b.Id); continue; }
            if (!_workedOut.Add(b.Id)) continue;
            string what = b.Kind == BuildingKind.Mine ? "ore" : "rock";
            alerts.Push("spent-" + b.Id, $"A {b.Kind} is worked out: nothing left in reach. Demolish it and build another by fresh {what}",
                new Color(0.95f, 0.85f, 0.55f), new Vector2(b.CentreX, b.CentreY), 20);
        }
    }

    /// <summary>Miners wore away a rock: if it was one we warned of, the way is open now.</summary>
    public void Worn(DepositWorn d, int width, AlertFeed alerts)
    {
        if (!_warned.Remove(d.Y * width + d.X)) return;
        alerts.Push("open-" + d.X + "," + d.Y, "The rock is broken through: a new way in for the horde", new Color(1, 0.35f, 0.3f), new Vector2(d.X + 0.5f, d.Y + 0.5f), 14);
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

    static bool OpensAGap(World world, int x, int y) => WoodsSystem.OpensAGap(world.Terrain, x, y, Window, forestOpen: !world.ForestBlocks);
}
