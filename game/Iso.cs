using Godot;

namespace Hellwall.Game;

/// <summary>
/// The isometric projection, in one place. The sim is a square grid in tile
/// units; the client draws it as 2:1 diamonds, 66 x 33 world pixels a tile
/// (Kenney's 132 px tiles at half scale). Tile (x, y) spans (x..x+1, y..y+1),
/// so its centre is (x + 0.5, y + 0.5). Map north (y = 0) runs up and to the
/// right on screen, east down and to the right.
/// </summary>
public static class Iso
{
    public const float HalfW = 33f;
    public const float HalfH = 16.5f;

    /// <summary>World pixels of a point in tile units (on the ground plane, the tops of the terrain blocks).</summary>
    public static Vector2 P(float tx, float ty) => new((tx - ty) * HalfW, (tx + ty) * HalfH);

    public static Vector2 P(Vector2 t) => P(t.X, t.Y);

    /// <summary>The tile-unit point under a world pixel on the ground plane.</summary>
    public static Vector2 Tile(Vector2 p) => new((p.X / HalfW + p.Y / HalfH) / 2, (p.Y / HalfH - p.X / HalfW) / 2);

    public static (int X, int Y) TileAt(Vector2 p)
    {
        var t = Tile(p);
        return ((int)Mathf.Floor(t.X), (int)Mathf.Floor(t.Y));
    }

    /// <summary>The ground diamond of a w x h footprint whose top-left tile is (x, y).</summary>
    public static Vector2[] Diamond(float x, float y, float w = 1, float h = 1) =>
        [P(x, y), P(x + w, y), P(x + w, y + h), P(x, y + h)];

    /// <summary>A circle of `radius` tiles on the ground: an ellipse on screen, twice as wide as tall.</summary>
    public static void Ellipse(CanvasItem c, Vector2 centreTiles, float radius, Color colour, float width = 1, bool filled = false)
    {
        var centre = P(centreTiles);
        float rx = radius * HalfW * 1.4142f, ry = radius * HalfH * 1.4142f;
        const int n = 40;
        var points = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float a = i * Mathf.Tau / n;
            points[i] = centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        if (filled) c.DrawColoredPolygon(points[..n], colour);
        else c.DrawPolyline(points, colour, width);
    }
}
