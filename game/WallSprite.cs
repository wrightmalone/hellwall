using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// A wall tile from baked pieces (tools/bake_walls.gd): a post in the middle and
/// an arm toward each neighbouring wall, gate or tower, so a line of them reads
/// as one wall with corners. Timber is a palisade of sharpened logs; stone is
/// KayKit's crenellated castle wall. A gate is three tiles along its length,
/// wall either side of a doorway, and its doors swing open while anyone of
/// ours is near it (never for demons).
/// </summary>
public sealed partial class WallSprite : Node2D
{
    readonly World _world;
    readonly Building _b;
    float _open; // 0 shut to 1 open
    int _frame;

    public WallSprite(World world, Building b)
    {
        _world = world;
        _b = b;
        // Sorted by its front corner, like every building; a gate by its doorway's, so whoever walks through is drawn with it.
        Position = b.IsGate ? Iso.P(Door.X + 1, Door.Y + 1) : Iso.P(b.X + 1, b.Y + 1);
    }

    (int X, int Y) Door => (_b.X + _b.W / 2, _b.Y + _b.H / 2);

    /// <summary>
    /// A wall reaches toward the tile beside it if that's a wall, a tower or the Keep. A gate only
    /// along its length, at its ends: a wall against its side (in front or behind) doesn't join it.
    /// </summary>
    bool Joins(int x, int y)
    {
        if (_world.BuildingById(_world.BuildingIdAt(x, y)) is not { } n) return false;
        if (n.IsGate) return n.Turned ? x == _b.X && x >= n.X && x < n.X + n.W : y == _b.Y && y >= n.Y && y < n.Y + n.H;
        return n.IsWallLike || n.Def.Weapon != null || n.Kind == BuildingKind.Keep;
    }

    /// <summary>Where the tile's centre falls in every baked wall piece (walls.json): they all share one canvas; gates have their own.</summary>
    static readonly Vector2 Base = new(78, 131);
    static readonly Vector2 GateBase = new(140, 170);
    const int GateFrames = 5;
    const float Shown = 0.5f; // baked at 132 px a tile, shown at the terrain's 66

    /// <summary>How near (tiles from the doorway) one of ours opens a gate, and how fast its doors swing (of the way a second).</summary>
    const float OpenReach = 2.2f, Swing = 2.5f;

    static Texture2D Piece(string set, string piece) => Art.Tex($"res://art/baked/walls/{set}-{piece}.png");

    public override void _Process(double delta)
    {
        if (!_b.IsGate) return;
        var (dx, dy) = (Door.X + 0.5f, Door.Y + 0.5f);
        bool Near(float x, float y) => (x - dx) * (x - dx) + (y - dy) * (y - dy) < OpenReach * OpenReach;
        bool someone = false;
        if (_b.Complete && !_b.Possessed)
        {
            foreach (var u in _world.Units) if (Near(u.X, u.Y)) { someone = true; break; }
            if (!someone) foreach (var w in _world.Woodsmen) if (Near(w.X, w.Y)) { someone = true; break; }
        }
        _open = Mathf.MoveToward(_open, someone ? 1 : 0, Swing * (float)delta);
        int frame = Mathf.RoundToInt(_open * (GateFrames - 1));
        if (frame != _frame) { _frame = frame; QueueRedraw(); }
    }

    public override void _Draw()
    {
        // KayKit's walls, baked in the pieces our walls join from (tools/bake_walls.gd): a post on the
        // tile, an arm toward each neighbour it joins, back to front; a gate whole, the way it lies.
        string set = _b.Kind is BuildingKind.StoneWall or BuildingKind.StoneGate ? "stone" : "wood";
        if (_b.IsGate)
        {
            var tex = Piece(set, $"gate-{(_b.Turned ? "z" : "x")}-{_frame}");
            var door = Iso.P(Door.X + 0.5f, Door.Y + 0.5f) - Position;
            DrawTextureRect(tex, new Rect2(door - GateBase * Shown, tex.GetSize() * Shown), false);
            return;
        }
        int x = _b.X, y = _b.Y;
        var centre = Iso.P(x + 0.5f, y + 0.5f) - Position;
        void Draw(string piece)
        {
            var tex = Piece(set, piece);
            DrawTextureRect(tex, new Rect2(centre - Base * Shown, tex.GetSize() * Shown), false);
        }
        if (Joins(x, y - 1)) Draw("arm-n");
        if (Joins(x - 1, y)) Draw("arm-w");
        Draw("post");
        if (Joins(x + 1, y)) Draw("arm-e");
        if (Joins(x, y + 1)) Draw("arm-s");
    }
}
