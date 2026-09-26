using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Makes a breach impossible to miss, the complaint They Are Billions players
/// have most after "no saves": one possessed House cascades while you're
/// looking elsewhere. Two steps. When demons come near a building with people
/// in it (the first blow takes it), an alert that says so, before it's too
/// late. When one is possessed, an alarm, a red pulse
/// round the screen, a ping on the minimap, and (a setting) the game pauses.
/// Read-only over the sim.
/// </summary>
public sealed class BreachWatch
{
    /// <summary>Don't repeat the alert for the same building sooner than this.</summary>
    const double Again = 20;
    /// <summary>How close a demon must be to a building with people in it to be worth the alert.</summary>
    const int Near = 4;

    readonly Dictionary<int, double> _warned = new();
    int[] _near = [];
    readonly Queue<(int X, int Y, int D)> _queue = new();
    static readonly (int, int)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    double _clock;

    public static bool PauseOnPossession => Settings.Get("pause_on_possession", false);

    /// <summary>
    /// Twice a second: a demon within a few tiles of a building with people in it gets an alert
    /// (a building with people isn't damaged, it's taken at the first blow, so this is the only
    /// warning there is). Marks the ground near each such building with its id, then looks at
    /// where the demons stand.
    /// </summary>
    public void Step(World world, AlertFeed alerts, double dt)
    {
        _clock += dt;
        var t = world.Terrain;
        if (_near.Length != t.Width * t.Height) _near = new int[t.Width * t.Height];
        Array.Clear(_near);
        bool any = false;
        foreach (var b in world.Buildings)
        {
            if (b.Possessed || b.PeopleInside <= 0 || b.IsWallLike) continue;
            if (_warned.TryGetValue(b.Id, out double at) && _clock - at < Again) continue;
            any = true;
            // Out from its walls over open ground only: a demon on the far side of a wall isn't at the door.
            _queue.Clear();
            for (int y = b.Y - 1; y <= b.Y + b.H; y++)
                for (int x = b.X - 1; x <= b.X + b.W; x++)
                    if (t.InBounds(x, y) && world.IsWalkable(x, y) && _near[y * t.Width + x] == 0) { _near[y * t.Width + x] = b.Id; _queue.Enqueue((x, y, 1)); }
            while (_queue.Count > 0)
            {
                var (x, y, d) = _queue.Dequeue();
                if (d >= Near) continue;
                foreach (var (ox, oy) in Steps)
                {
                    int nx = x + ox, ny = y + oy;
                    if (!t.InBounds(nx, ny) || _near[ny * t.Width + nx] != 0 || !world.IsWalkable(nx, ny)) continue;
                    _near[ny * t.Width + nx] = b.Id;
                    _queue.Enqueue((nx, ny, d + 1));
                }
            }
        }
        if (!any) return;
        var h = world.Horde;
        for (int i = 0; i < h.Count; i++)
        {
            int x = (int)h.X[i], y = (int)h.Y[i];
            if (!t.InBounds(x, y)) continue;
            int id = _near[y * t.Width + x];
            if (id == 0 || world.BuildingById(id) is not { } b) continue;
            _warned[id] = _clock;
            // Don't mark it again this pass.
            for (int j = 0; j < _near.Length; j++) if (_near[j] == id) _near[j] = 0;
            if (ForestWatch.Log) GD.Print($"hellwall: demons at a {b.Kind} ({b.PeopleInside} inside) t={world.Tick / 20}");
            alerts.Push("near-" + id, $"Demons at the {b.Kind}: {b.PeopleInside} inside will turn at the first blow", new Color(0.55f, 0.25f, 0.05f, 0.95f), new Vector2(b.CentreX, b.CentreY), 10);
        }
    }
}
