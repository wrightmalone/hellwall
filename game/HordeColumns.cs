using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Where each wave's columns are now: the centre and head-count of the demons
/// that marched in with each side of each wave (Horde.Column), for the marker
/// that follows them in. Read-only over the sim, refreshed a few times a second.
/// </summary>
public static class HordeColumns
{
    /// <summary>Below this many left, a column's no longer worth a marker: it's a straggle, not a threat.</summary>
    const int Least = 5;

    public static void Measure(World world, List<(int Column, Vector2 Centre, int Count)> into)
    {
        into.Clear();
        var h = world.Horde;
        // Few columns at once (a wave's sides), so a small linear table rather than a dictionary.
        Span<int> ids = stackalloc int[16];
        Span<float> sx = stackalloc float[16], sy = stackalloc float[16];
        Span<int> n = stackalloc int[16];
        int used = 0;
        for (int i = 0; i < h.Count; i++)
        {
            int c = h.Column[i];
            if (c == 0) continue;
            int k = 0;
            while (k < used && ids[k] != c) k++;
            if (k == used)
            {
                if (used == ids.Length) continue;
                ids[used] = c; sx[used] = sy[used] = 0; n[used] = 0; used++;
            }
            sx[k] += h.X[i]; sy[k] += h.Y[i]; n[k]++;
        }
        for (int k = 0; k < used; k++)
            if (n[k] >= Least) into.Add((ids[k], new Vector2(sx[k] / n[k], sy[k] / n[k]), n[k]));
    }
}
