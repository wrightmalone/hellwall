using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Everything drawn in world space except terrain and the horde: buildings
/// and their state, holy ground, dormant packs, soldiers, shots, selection,
/// and the placement ghost. Two layers so soldiers and effects draw above the
/// horde while buildings draw below it.
/// </summary>
public partial class WorldView : Node2D
{
    const int T = Palette.TilePx;

    public World World = null!;
    public ClientState State = null!;

    readonly Sprite2D _consecrated = new() { Centered = false, Scale = new Vector2(T, T), ZIndex = -1 };
    Image? _consecratedImage;
    readonly Overlay _overlay = new();

    public override void _Ready()
    {
        AddChild(_consecrated);
        _overlay.View = this;
        _overlay.ZIndex = 2; // above the horde (z 1)
        AddChild(_overlay);
        RepaintConsecration();
    }

    public void RepaintConsecration()
    {
        var t = World.Terrain;
        _consecratedImage ??= Image.CreateEmpty(t.Width, t.Height, false, Image.Format.Rgba8);
        for (int y = 0; y < t.Height; y++)
            for (int x = 0; x < t.Width; x++)
                _consecratedImage.SetPixel(x, y, World.Colony.Consecrated[t.Index(x, y)] ? Palette.Consecrated : Colors.Transparent);
        _consecrated.Texture = ImageTexture.CreateFromImage(_consecratedImage);
    }

    public void Refresh()
    {
        QueueRedraw();
        _overlay.QueueRedraw();
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        foreach (var b in World.Buildings)
        {
            var rect = new Rect2(b.X * T, b.Y * T, b.W * T, b.H * T);
            var colour = Palette.Building(b.Kind);
            if (!b.Complete) colour = colour.Darkened(0.55f);
            DrawRect(rect, colour);
            DrawRect(rect, State.SelectedBuilding == b.Id ? Palette.Selected : Colors.Black, filled: false, width: State.SelectedBuilding == b.Id ? 2 : 1);

            string label = Palette.Label(b.Kind);
            if (label.Length > 0 && b.W >= 2)
                DrawString(font, rect.Position + new Vector2(2, T + 2), label, HorizontalAlignment.Left, rect.Size.X - 2, 9, Colors.Black);

            if (!b.Complete)
            {
                float f = b.Def.BuildSeconds <= 0 ? 1 : b.Built / b.Def.BuildSeconds;
                Bar(rect, f, new Color(0.9f, 0.9f, 0.3f));
            }
            else if (b.Possessed)
            {
                DrawRect(rect, new Color(0.45f, 0.1f, 0.55f, 0.7f));
                DrawString(font, rect.Position + new Vector2(1, rect.Size.Y - 2), $"x{b.Occupants}", HorizontalAlignment.Left, rect.Size.X, 9, new Color(1, 0.8f, 1));
            }
            else if (!b.Active)
            {
                // Dark: a grey veil and why.
                DrawRect(rect, new Color(0, 0, 0, 0.45f));
                DrawString(font, rect.Position + new Vector2(1, rect.Size.Y - 2), b.OnGround ? "crew" : "dark", HorizontalAlignment.Left, rect.Size.X, 8, new Color(1, 0.6f, 0.6f));
            }
            if (b.Hp < b.Def.Hp) Bar(rect, b.Hp / b.Def.Hp, new Color(0.9f, 0.2f, 0.2f), top: true);
        }

        foreach (var g in World.Gates)
        {
            if (!g.Alive) continue;
            var rect = new Rect2(g.X * T, g.Y * T, Hellgate.Size * T, Hellgate.Size * T);
            DrawRect(rect.Grow(2), new Color(0.2f, 0, 0.05f));
            DrawRect(rect, new Color(0.75f, 0.1f, 0.25f));
            DrawRect(rect.Grow(-4), new Color(0.15f, 0, 0.1f));
            Bar(rect, g.Hp / World.Rules.Hellgates.Hp, new Color(0.9f, 0.2f, 0.4f), top: true);
        }

        foreach (var p in World.Packs)
        {
            if (p.Awake) continue;
            var centre = new Vector2(p.X + 0.5f, p.Y + 0.5f) * T;
            float r = Mathf.Sqrt(p.Count / (Mathf.Pi * Balance.SpawnDensity)) * T;
            var tint = p.Kind == DemonKind.Hound ? new Color(1f, 0.55f, 0.15f) : new Color(0.85f, 0.12f, 0.10f);
            DrawCircle(centre, r, new Color(tint, 0.18f));
            DrawArc(centre, r, 0, Mathf.Tau, 32, new Color(tint, 0.8f), 2);
            DrawString(font, centre + new Vector2(-14, 6), p.Count.ToString(), fontSize: 16, modulate: Colors.White);
        }
    }

    void Bar(Rect2 rect, float fraction, Color colour, bool top = false)
    {
        float y = top ? rect.Position.Y - 3 : rect.End.Y - 3;
        DrawRect(new Rect2(rect.Position.X, y, rect.Size.X, 2), new Color(0, 0, 0, 0.7f));
        DrawRect(new Rect2(rect.Position.X, y, rect.Size.X * Mathf.Clamp(fraction, 0, 1), 2), colour);
    }

    /// <summary>Soldiers, shots, selection box, ranges and the placement ghost.</summary>
    partial class Overlay : Node2D
    {
        public WorldView View = null!;

        public override void _Draw()
        {
            var world = View.World;
            var state = View.State;
            var font = ThemeDB.FallbackFont;

            foreach (var (shot, age) in state.Shots)
            {
                float a = 1 - (float)(age / ClientState.ShotLife);
                var from = new Vector2(shot.FromX, shot.FromY) * T;
                var to = new Vector2(shot.ToX, shot.ToY) * T;
                DrawLine(from, to, new Color(Palette.Tracer, a), shot.FromUnit ? 1 : 2);
                if (shot.Splash > 0) DrawArc(to, shot.Splash * T, 0, Mathf.Tau, 20, new Color(1, 0.6f, 0.2f, a), 2);
            }

            foreach (var (burst, age) in state.Bursts)
            {
                float t = (float)(age / 0.4);
                DrawCircle(new Vector2(burst.X, burst.Y) * T, burst.Radius * T * (0.4f + 0.6f * t), new Color(0.6f, 0.9f, 0.2f, 0.5f * (1 - t)));
            }

            foreach (var u in world.Units)
            {
                var p = new Vector2(Mathf.Lerp(u.PrevX, u.X, state.Alpha), Mathf.Lerp(u.PrevY, u.Y, state.Alpha)) * T;
                bool selected = state.SelectedUnits.Contains(u.Id);
                DrawCircle(p, 0.42f * T, Colors.Black);
                DrawCircle(p, 0.34f * T, Palette.Unit(u.Kind));
                if (selected) DrawArc(p, 0.6f * T, 0, Mathf.Tau, 16, Palette.Selected, 1.5f);
                if (u.Hp < u.Def.Hp)
                {
                    var r = new Rect2(p.X - 0.5f * T, p.Y - 0.75f * T, T, 2);
                    DrawRect(r, new Color(0, 0, 0, 0.7f));
                    DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * u.Hp / u.Def.Hp, 2)), new Color(0.3f, 1, 0.3f));
                }
            }

            // Range of the selected tower.
            if (state.SelectedBuilding is { } sb && world.BuildingById(sb) is { Def.Weapon: { } w } tower)
                DrawArc(new Vector2(tower.CentreX, tower.CentreY) * T, w.Range * T, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.35f), 1);

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
                    DrawRect(new Rect2(tx * T, ty * T, def.W * T, def.H * T), why == null ? Palette.GhostOk : Palette.GhostBad);
                }
                var (hx, hy) = state.HoveredTile;
                string? reason = world.CheckPlacement(kind, hx, hy);
                string note = reason ?? (def.Produces is { } res ? $"+{world.EstimateGathering(kind, hx, hy):0.00} {res.ToString().ToLowerInvariant()}/s" : "");
                if (def.Weapon is { } weapon)
                    DrawArc(new Vector2(hx + def.W / 2f, hy + def.H / 2f) * T, weapon.Range * T, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.3f), 1);
                if (note.Length > 0)
                {
                    var at = new Vector2((hx + def.W) * T + 4, hy * T + 10);
                    DrawString(font, at + Vector2.One, note, fontSize: 12, modulate: Colors.Black);
                    DrawString(font, at, note, fontSize: 12, modulate: reason == null ? Colors.White : new Color(1, 0.6f, 0.6f));
                }
            }
        }
    }
}
