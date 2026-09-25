using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// A wall tile drawn in code rather than from a sprite: a post in the middle
/// and an arm toward each neighbouring wall, gate or tower, extruded to the
/// wall's height and shaded by face. A line of them reads as one wall with
/// corners, not a row of crates. Timber is a low brown palisade; stone is
/// taller, grey and crenellated; a gate is the wall with an arch and a door.
/// </summary>
public sealed partial class WallSprite : Node2D
{
    readonly World _world;
    readonly Building _b;

    public WallSprite(World world, Building b)
    {
        _world = world;
        _b = b;
        Position = Iso.P(b.X + 1, b.Y + 1); // sorted by its front corner, like every building
    }

    bool Joins(int x, int y) => _world.BuildingById(_world.BuildingIdAt(x, y)) is { } n && (n.IsWallLike || n.Def.Weapon != null || n.Kind == BuildingKind.Keep);

    public override void _Draw()
    {
        bool stone = _b.Kind is BuildingKind.StoneWall or BuildingKind.StoneGate;
        bool gate = _b.IsGate;
        float height = stone ? 26 : gate ? 24 : 18;
        var top = stone ? new Color(0.78f, 0.78f, 0.74f) : new Color(0.62f, 0.45f, 0.28f);
        var right = top.Darkened(0.22f);
        var left = top.Darkened(0.4f);
        const float lo = 0.33f, hi = 0.67f; // the wall's thickness, a third of a tile
        int x = _b.X, y = _b.Y;

        // Back to front: the arms behind the post (north and west) first.
        if (Joins(x, y - 1)) Box(x + lo, y, x + hi, y + lo, height, top, right, left);
        if (Joins(x - 1, y)) Box(x, y + lo, x + lo, y + hi, height, top, right, left);
        Box(x + lo, y + lo, x + hi, y + hi, height + (stone ? 4 : 0), top, right, left);
        if (Joins(x + 1, y)) Box(x + hi, y + lo, x + 1, y + hi, height, top, right, left);
        if (Joins(x, y + 1)) Box(x + lo, y + hi, x + hi, y + 1, height, top, right, left);

        if (stone)
        {
            // Merlons: small teeth along the top of each arm.
            foreach (var (mx, my) in new[] { (0.5f, 0.12f), (0.12f, 0.5f), (0.88f, 0.5f), (0.5f, 0.88f) })
            {
                bool arm = (mx, my) switch { (0.5f, 0.12f) => Joins(x, y - 1), (0.12f, 0.5f) => Joins(x - 1, y), (0.88f, 0.5f) => Joins(x + 1, y), _ => Joins(x, y + 1) };
                if (arm) Box(x + mx - 0.08f, y + my - 0.08f, x + mx + 0.08f, y + my + 0.08f, height + 7, top, right, left, from: height);
            }
        }

        if (gate)
        {
            // An arch through the wall, on both visible faces, with a timber door.
            var door = new Color(0.35f, 0.22f, 0.12f);
            var fa = Iso.P(x + 0.4f, y + hi) - Position;
            var fb = Iso.P(x + 0.6f, y + hi) - Position;
            DrawColoredPolygon([fa, fb, fb - new Vector2(0, 14), fa - new Vector2(0, 14)], door);
            var ga = Iso.P(x + hi, y + 0.4f) - Position;
            var gb = Iso.P(x + hi, y + 0.6f) - Position;
            DrawColoredPolygon([ga, gb, gb - new Vector2(0, 14), ga - new Vector2(0, 14)], door.Darkened(0.2f));
        }
    }

    /// <summary>A box on the ground from (x0, y0) to (x1, y1) in tile units, `height` px tall (starting at `from`): top and the two faces the camera sees.</summary>
    void Box(float x0, float y0, float x1, float y1, float height, Color top, Color right, Color left, float from = 0)
    {
        bool palisade = _b.Kind == BuildingKind.Wall;
        Vector2 G(float tx, float ty, float up) => Iso.P(tx, ty) - Position - new Vector2(0, up);
        // The face on the +y side (lower left on screen) and the +x side (lower right).
        DrawColoredPolygon([G(x0, y1, from), G(x1, y1, from), G(x1, y1, height), G(x0, y1, height)], left);
        DrawColoredPolygon([G(x1, y0, from), G(x1, y1, from), G(x1, y1, height), G(x1, y0, height)], right);
        DrawColoredPolygon([G(x0, y0, height), G(x1, y0, height), G(x1, y1, height), G(x0, y1, height)], top);
        var edge = new Color(0, 0, 0, 0.25f);
        DrawPolyline([G(x0, y1, height), G(x1, y1, height), G(x1, y0, height)], edge, 1);
        if (!palisade) return;
        // Stakes: a seam and a pointed tip every few pixels along both visible faces.
        var seam = new Color(0.28f, 0.18f, 0.09f, 0.55f);
        void Stakes(Vector2 a, Vector2 b, Color face)
        {
            int n = Math.Max(1, (int)(a.DistanceTo(b) / 5));
            for (int i = 0; i < n; i++)
            {
                var p = a.Lerp(b, (i + 0.5f) / n);
                var q = a.Lerp(b, (float)i / n);
                var r = a.Lerp(b, (float)(i + 1) / n);
                DrawLine(q, q + new Vector2(0, height - from), seam, 1);
                DrawColoredPolygon([q, r, p - new Vector2(0, 4)], face);
            }
        }
        Stakes(G(x0, y1, height), G(x1, y1, height), left);
        Stakes(G(x1, y1, height), G(x1, y0, height), right);
    }
}
