using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// A wall tile from baked pieces (tools/bake_walls.gd): a post in the middle and
/// an arm toward each neighbouring wall, gate or tower, so a line of them reads
/// as one wall with corners. Timber is a palisade of sharpened logs; stone is
/// KayKit's crenellated castle wall; a gate spans its tile the way the wall runs.
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

    /// <summary>Where the tile's centre falls in every baked wall piece (walls.json): they all share one canvas.</summary>
    static readonly Vector2 Base = new(78, 131);
    const float Shown = 0.5f; // baked at 132 px a tile, shown at the terrain's 66

    static Texture2D Piece(string set, string piece) => Art.Tex($"res://art/baked/walls/{set}-{piece}.png");

    public override void _Draw()
    {
        // KayKit's walls, baked in the pieces our walls join from (tools/bake_walls.gd): a post on the
        // tile, an arm toward each neighbour it joins, back to front; a gate spans its tile the way the wall runs.
        string set = _b.Kind is BuildingKind.StoneWall or BuildingKind.StoneGate ? "stone" : "wood";
        int x = _b.X, y = _b.Y;
        var centre = Iso.P(x + 0.5f, y + 0.5f) - Position;
        Modulate = _b.Complete ? Colors.White : new Color(1, 1, 1, 0.55f);
        void Draw(string piece)
        {
            var tex = Piece(set, piece);
            var size = tex.GetSize() * Shown;
            DrawTextureRect(tex, new Rect2(centre - Base * Shown, size), false);
        }
        if (_b.IsGate)
        {
            bool eastWest = Joins(x - 1, y) || Joins(x + 1, y) || !(Joins(x, y - 1) || Joins(x, y + 1));
            Draw(eastWest ? "gate-x" : "gate-z");
            return;
        }
        if (Joins(x, y - 1)) Draw("arm-n");
        if (Joins(x - 1, y)) Draw("arm-w");
        Draw("post");
        if (Joins(x + 1, y)) Draw("arm-e");
        if (Joins(x, y + 1)) Draw("arm-s");
    }
}
