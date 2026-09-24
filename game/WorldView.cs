using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Everything in world space except terrain and the horde. Buildings and
/// soldiers are sprites in the y-sorted layer (so trees and walls overlap
/// them properly); state that has to read at a glance (build progress,
/// damage, possession, dark buildings, labels, packs, gates, shots,
/// selection, ranges and the placement ghost) is drawn on an overlay above.
/// </summary>
public partial class WorldView : Node2D
{
    public World World = null!;
    public ClientState State = null!;
    /// <summary>The y-sorted layer the terrain's trees are in.</summary>
    public Node2D Sorted = null!;

    readonly Dictionary<int, Node2D> _buildings = new();
    readonly Dictionary<int, Sprite2D> _units = new();
    readonly Overlay _overlay = new();
    readonly Ground _ground = new();

    public override void _Ready()
    {
        _ground.View = this;
        _ground.ZIndex = -1; // under the sorted layer, above the terrain (z -2)
        AddChild(_ground); // footprint shadows and gates, on the ground under everything that stands
        _overlay.View = this;
        _overlay.ZIndex = 3; // above the sorted layer and the horde
        AddChild(_overlay);
    }

    public void Refresh()
    {
        SyncBuildings();
        SyncUnits();
        _ground.QueueRedraw();
        _overlay.QueueRedraw();
    }

    void SyncBuildings()
    {
        var seen = new HashSet<int>();
        bool changed = false;
        foreach (var b in World.Buildings)
        {
            seen.Add(b.Id);
            if (!_buildings.TryGetValue(b.Id, out var sprite))
            {
                sprite = b.IsWallLike ? new WallSprite(World, b) : new BuildingSprite(b);
                _buildings[b.Id] = sprite;
                Sorted.AddChild(sprite);
                changed = true;
            }
            sprite.Modulate = !b.Complete ? new Color(0.55f, 0.55f, 0.6f, 0.75f)
                : b.Possessed ? new Color(0.75f, 0.4f, 0.95f)
                : !b.Active ? new Color(0.6f, 0.6f, 0.6f)
                : Colors.White;
        }
        foreach (var id in _buildings.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _buildings[id].QueueFree();
            _buildings.Remove(id);
            changed = true;
        }
        // Walls join their neighbours, so any change to the layout redraws them.
        if (changed)
            foreach (var sprite in _buildings.Values)
                if (sprite is WallSprite wall) wall.QueueRedraw();
    }

    readonly Dictionary<int, int> _facing = new();

    void SyncUnits()
    {
        var seen = new HashSet<int>();
        int step = (int)(Time.GetTicksMsec() / 100); // walk frames at 10 a second
        foreach (var u in World.Units)
        {
            seen.Add(u.Id);
            if (!_units.TryGetValue(u.Id, out var sprite))
            {
                sprite = new Sprite2D
                {
                    Texture = Art.Tex(Art.Unit(u.Kind)),
                    Centered = false,
                    RegionEnabled = true,
                    Scale = new Vector2(Art.UnitScale, Art.UnitScale),
                    Offset = -Art.Feet, // feet on the ground point
                    TextureFilter = TextureFilterEnum.LinearWithMipmaps,
                };
                _units[u.Id] = sprite;
                Sorted.AddChild(sprite);
            }
            float dx = u.X - u.PrevX, dy = u.Y - u.PrevY;
            bool moving = dx * dx + dy * dy > 1e-6f;
            if (moving) _facing[u.Id] = Art.Facing(dx, dy);
            int facing = _facing.GetValueOrDefault(u.Id, 1);
            int frame = moving ? (step + u.Id) % Art.Frames : 0;
            sprite.RegionRect = new Rect2(frame * Art.Cell, facing * Art.Cell, Art.Cell, Art.Cell);
            sprite.Position = Iso.P(Mathf.Lerp(u.PrevX, u.X, State.Alpha), Mathf.Lerp(u.PrevY, u.Y, State.Alpha));
        }
        foreach (var id in _units.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _units[id].QueueFree();
            _units.Remove(id);
            _facing.Remove(id);
        }
    }

    /// <summary>A building: Tower Defense pieces stacked bottom first, standing on its footprint's centre.</summary>
    sealed partial class BuildingSprite : Node2D
    {
        public BuildingSprite(Building b)
        {
            // Sorted by the footprint's front corner, so it draws after anything standing behind it.
            Position = Iso.P(b.X + b.W, b.Y + b.H);
            var centre = Iso.P(b.X + b.W / 2f, b.Y + b.H / 2f) - Position;
            if (Art.Ground(b.Kind) is { } ground)
            {
                var tex = Art.Tex(ground);
                for (int y = 0; y < b.H; y++)
                    for (int x = 0; x < b.W; x++)
                    {
                        var at = Iso.P(b.X + x + 0.5f, b.Y + y + 0.5f) - Position;
                        AddChild(new Sprite2D { Texture = tex, Centered = false, Scale = new Vector2(0.5f, 0.5f), Position = at - new Vector2(33, 16.5f), ZIndex = -1 });
                    }
            }
            float lift = 0;
            foreach (var path in Art.BuildingPieces(b.Kind))
            {
                var tex = Art.Tex(path);
                float w = tex.GetWidth(), h = tex.GetHeight();
                // Scaled so the piece spans that share of the footprint's diamond width.
                float scale = Art.Fill(b.Kind) * (b.W + b.H) * Iso.HalfW / w;
                // A piece's base diamond is its bottom w/2 pixels; its centre sits w/4 above the bottom edge.
                var sprite = new Sprite2D { Texture = tex, Centered = false, Scale = new Vector2(scale, scale) };
                sprite.Position = centre + new Vector2(-w / 2 * scale, -(h - w / 4) * scale - lift);
                AddChild(sprite);
                lift += (h - w / 2) * scale; // the next piece stands on this one's top
            }
        }
    }

    /// <summary>On the ground: footprints, Hellgates, sleeping packs.</summary>
    sealed partial class Ground : Node2D
    {
        public WorldView View = null!;

        public override void _Draw()
        {
            var world = View.World;
            var font = ThemeDB.FallbackFont;
            foreach (var g in world.Gates)
            {
                if (!g.Alive) continue;
                DrawColoredPolygon(Iso.Diamond(g.X, g.Y, Hellgate.Size, Hellgate.Size), new Color(0.25f, 0f, 0.06f, 0.9f));
                DrawColoredPolygon(Iso.Diamond(g.X + 0.6f, g.Y + 0.6f, Hellgate.Size - 1.2f, Hellgate.Size - 1.2f), new Color(0.85f, 0.1f, 0.3f, 0.9f));
            }
            // Your soldiers stand on blue rings, so they can be found in a crowd.
            foreach (var u in world.Units)
            {
                var feet = new Vector2(Mathf.Lerp(u.PrevX, u.X, View.State.Alpha), Mathf.Lerp(u.PrevY, u.Y, View.State.Alpha));
                Iso.Ellipse(this, feet, 0.32f, new Color(0, 0, 0, 0.35f), filled: true);
                Iso.Ellipse(this, feet, 0.34f, new Color(0.35f, 0.7f, 1f, 0.95f), 2);
            }

            foreach (var p in world.Packs)
            {
                if (p.Awake) continue;
                float r = Mathf.Sqrt(p.Count / (Mathf.Pi * Balance.SpawnDensity));
                var tint = p.Kind == DemonKind.Hound ? new Color(1f, 0.55f, 0.15f) : new Color(0.85f, 0.12f, 0.10f);
                var c = new Vector2(p.X + 0.5f, p.Y + 0.5f);
                Iso.Ellipse(this, c, r, new Color(tint, 0.2f), filled: true);
                Iso.Ellipse(this, c, r, new Color(tint, 0.85f), 2);
                var at = Iso.P(c);
                DrawString(font, at + new Vector2(-10, 6), p.Count.ToString(), fontSize: 16, modulate: Colors.White);
            }
        }
    }

    /// <summary>Above everything: what state things are in, and what the player is doing.</summary>
    sealed partial class Overlay : Node2D
    {
        public WorldView View = null!;

        public override void _Draw()
        {
            var world = View.World;
            var state = View.State;
            var font = ThemeDB.FallbackFont;

            foreach (var b in world.Buildings)
            {
                var top = Iso.P(b.X, b.Y);
                var front = Iso.P(b.X + b.W, b.Y + b.H);
                float width = (b.W + b.H) * Iso.HalfW;
                var barAt = new Vector2(front.X - width / 4, front.Y + 3);
                if (state.SelectedBuilding == b.Id) DrawPolyline([.. Iso.Diamond(b.X, b.Y, b.W, b.H), top], Palette.Selected, 2);
                if (!b.Complete) Bar(barAt, width / 2, b.Def.BuildSeconds <= 0 ? 1 : b.Built / b.Def.BuildSeconds, new Color(0.95f, 0.9f, 0.3f));
                if (b.Hp < b.Def.Hp) Bar(barAt + new Vector2(0, 4), width / 2, b.Hp / b.Def.Hp, new Color(0.95f, 0.25f, 0.2f));
                string tag = b.Possessed ? $"POSSESSED x{b.Occupants}" : b.Complete && !b.Active ? (b.OnGround ? "no crew" : "dark") : "";
                if (tag.Length > 0) Text(font, front + new Vector2(-24, 18), tag, 11, b.Possessed ? new Color(1, 0.7f, 1) : new Color(1, 0.65f, 0.6f));
            }

            // What's under the cursor, by name: the art is placeholder and not every building is obvious.
            if (state.Armed == null && world.BuildingById(world.BuildingIdAt(state.HoveredTile.X, state.HoveredTile.Y)) is { } hovered)
                Text(font, state.MouseWorld + new Vector2(14, -6), hovered.Kind.ToString(), 14, Colors.White);

            foreach (var (shot, age) in state.Shots)
            {
                float a = 1 - (float)(age / ClientState.ShotLife);
                var from = Iso.P(shot.FromX, shot.FromY) - new Vector2(0, shot.FromUnit ? 12 : 30);
                var to = Iso.P(shot.ToX, shot.ToY) - new Vector2(0, 8);
                DrawLine(from, to, new Color(Palette.Tracer, a), shot.FromUnit ? 1 : 2);
                if (shot.Splash > 0) Iso.Ellipse(this, new Vector2(shot.ToX, shot.ToY), shot.Splash, new Color(1, 0.6f, 0.2f, a), 2);
            }

            foreach (var (howl, age) in state.Howls)
            {
                float t = (float)(age / 1.2);
                Iso.Ellipse(this, new Vector2(howl.X, howl.Y), howl.Radius * 0.5f * t, new Color(0.9f, 0.3f, 0.9f, 0.6f * (1 - t)), 1.5f);
            }

            foreach (var (burst, age) in state.Bursts)
            {
                float t = (float)(age / 0.4);
                Iso.Ellipse(this, new Vector2(burst.X, burst.Y), burst.Radius * (0.4f + 0.6f * t), new Color(0.6f, 0.9f, 0.2f, 0.5f * (1 - t)), filled: true);
            }

            foreach (var u in world.Units)
            {
                var feet = new Vector2(Mathf.Lerp(u.PrevX, u.X, state.Alpha), Mathf.Lerp(u.PrevY, u.Y, state.Alpha));
                var p = Iso.P(feet);
                if (state.SelectedUnits.Contains(u.Id)) Iso.Ellipse(this, feet, 0.45f, Palette.Selected, 1.5f);
                if (u.Hp < u.Def.Hp) Bar(p + new Vector2(-10, -Art.UnitSize - 5), 20, u.Hp / u.Def.Hp, new Color(0.3f, 1, 0.3f));
            }

            if (state.SelectedBuilding is { } sb && world.BuildingById(sb) is { Def.Weapon: { } w } tower)
                Iso.Ellipse(this, new Vector2(tower.CentreX, tower.CentreY), w.Range, new Color(1, 1, 1, 0.4f), 1);
            if (state.SelectedBuilding is { } sb2 && world.BuildingById(sb2) is { Def.SlowRadius: > 0 } bell)
                Iso.Ellipse(this, new Vector2(bell.CentreX, bell.CentreY), bell.Def.SlowRadius, new Color(0.6f, 0.8f, 1, 0.4f), 1);

            if (state.DragStart is { } ds && state.Armed == null)
            {
                var r = new Rect2(ds, state.MouseWorld - ds).Abs();
                DrawRect(r, new Color(0.35f, 1, 0.35f, 0.08f));
                DrawRect(r, Palette.Selected, filled: false, width: 1);
            }

            if (state.Armed is { } kind)
            {
                var def = world.Rules[kind];
                foreach (var (tx, ty) in state.GhostTiles())
                {
                    string? why = world.CheckPlacement(kind, tx, ty);
                    DrawColoredPolygon(Iso.Diamond(tx, ty, def.W, def.H), why == null ? Palette.GhostOk : Palette.GhostBad);
                }
                var (hx, hy) = state.HoveredTile;
                string? reason = world.CheckPlacement(kind, hx, hy);
                string note = reason ?? (def.Produces is { } res ? $"+{world.EstimateGathering(kind, hx, hy):0.00} {res.ToString().ToLowerInvariant()}/s" : "");
                if (def.Weapon is { } weapon) Iso.Ellipse(this, new Vector2(hx + def.W / 2f, hy + def.H / 2f), weapon.Range, new Color(1, 1, 1, 0.35f), 1);
                if (def.SlowRadius > 0) Iso.Ellipse(this, new Vector2(hx + def.W / 2f, hy + def.H / 2f), def.SlowRadius, new Color(0.6f, 0.8f, 1, 0.35f), 1);
                if (note.Length > 0) Text(font, Iso.P(hx + def.W, hy) + new Vector2(8, 0), note, 13, reason == null ? Colors.White : new Color(1, 0.6f, 0.6f));
            }
        }

        void Bar(Vector2 at, float width, float fraction, Color colour)
        {
            DrawRect(new Rect2(at, new Vector2(width, 3)), new Color(0, 0, 0, 0.7f));
            DrawRect(new Rect2(at, new Vector2(width * Mathf.Clamp(fraction, 0, 1), 3)), colour);
        }

        void Text(Font font, Vector2 at, string text, int size, Color colour)
        {
            DrawString(font, at + Vector2.One, text, fontSize: size, modulate: Colors.Black);
            DrawString(font, at, text, fontSize: size, modulate: colour);
        }
    }
}
