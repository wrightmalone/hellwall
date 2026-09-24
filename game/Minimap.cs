using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// The whole map in the corner: terrain and holy ground as a texture
/// (repainted only when the grid changes), with buildings, the horde,
/// Hellgates, sleeping packs and the camera's view drawn over it each frame.
/// Click or drag on it to move the camera.
/// </summary>
public partial class Minimap : Control
{
    const int Px = 200;

    public World World = null!;
    public Camera2D Camera = null!;
    public Action<Vector2> MoveCamera = null!;

    readonly TextureRect _terrain = new() { MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
    readonly Overlay _overlay = new();

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(Px, Px);
        Size = new Vector2(Px, Px);
        MouseFilter = MouseFilterEnum.Stop;
        _terrain.Size = new Vector2(Px, Px);
        AddChild(_terrain);
        _overlay.Map = this;
        _overlay.MouseFilter = MouseFilterEnum.Ignore;
        _overlay.Size = new Vector2(Px, Px);
        AddChild(_overlay);
        Repaint();
    }

    /// <summary>Redraw the terrain layer (holy ground included). Call when the grid changes.</summary>
    public void Repaint()
    {
        var t = World.Terrain;
        var image = Image.CreateEmpty(t.Width, t.Height, false, Image.Format.Rgb8);
        for (int y = 0; y < t.Height; y++)
            for (int x = 0; x < t.Width; x++)
            {
                var c = Palette.Tiles[(int)t.Get(x, y)];
                if (World.Colony.Consecrated[t.Index(x, y)]) c = c.Lerp(new Color(1, 0.9f, 0.5f), 0.25f);
                image.SetPixel(x, y, c);
            }
        _terrain.Texture = ImageTexture.CreateFromImage(image);
    }

    public override void _Process(double delta) => _overlay.QueueRedraw();

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb) Jump(mb.Position);
        else if (@event is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } mm) Jump(mm.Position);
    }

    void Jump(Vector2 local)
    {
        float scale = World.Terrain.Width / (float)Px;
        MoveCamera(Iso.P(local * scale));
        AcceptEvent();
    }

    partial class Overlay : Control
    {
        public Minimap Map = null!;
        readonly Dictionary<int, float> _lastHp = new();
        readonly Dictionary<int, (Vector2 At, float Life)> _pings = new();

        public override void _Draw()
        {
            var world = Map.World;
            float s = Px / (float)world.Terrain.Width;

            foreach (var b in world.Buildings)
            {
                var colour = b.Possessed ? new Color(0.7f, 0.2f, 0.8f) : b.IsWallLike ? new Color(0.85f, 0.85f, 0.9f) : Palette.Building(b.Kind);
                DrawRect(new Rect2(b.X * s, b.Y * s, MathF.Max(1, b.W * s), MathF.Max(1, b.H * s)), colour);
            }

            foreach (var p in world.Packs)
                if (!p.Awake) DrawCircle(new Vector2(p.X, p.Y) * s, 2, new Color(0.9f, 0.4f, 0.2f, 0.8f));

            // The horde, subsampled: a dot per demon up to a few thousand, which is plenty to read.
            var h = world.Horde;
            int step = Math.Max(1, h.Count / 3000);
            for (int i = 0; i < h.Count; i += step)
                DrawRect(new Rect2(h.X[i] * s, h.Y[i] * s, 1, 1), new Color(1, 0.15f, 0.1f));

            foreach (var g in world.Gates)
                if (g.Alive) DrawRect(new Rect2(g.X * s - 1, g.Y * s - 1, Hellgate.Size * s + 2, Hellgate.Size * s + 2), new Color(1, 0.1f, 0.5f));

            foreach (var u in world.Units)
                DrawRect(new Rect2(u.X * s, u.Y * s, 1.5f, 1.5f), new Color(0.5f, 0.9f, 1));

            // Where waves are coming from: a red arrow on each announced side.
            if (world.Survival is { } sv)
                foreach (var wave in sv.Waves)
                {
                    if (!wave.Announced || wave.Landed) continue;
                    foreach (var side in wave.Sides)
                    {
                        Vector2 at = side switch
                        {
                            Hellwall.Sim.Side.North => new(Px / 2f, 7), Hellwall.Sim.Side.South => new(Px / 2f, Px - 7),
                            Hellwall.Sim.Side.West => new(7, Px / 2f), _ => new(Px - 7, Px / 2f),
                        };
                        var inward = (new Vector2(Px / 2f, Px / 2f) - at).Normalized();
                        var across = new Vector2(-inward.Y, inward.X);
                        DrawColoredPolygon([at + inward * 7, at - inward * 4 + across * 6, at - inward * 4 - across * 6], new Color(1, 0.25f, 0.2f));
                    }
                }

            // Pings where buildings are taking damage, so a fight off screen is seen.
            foreach (var b in world.Buildings)
            {
                if (_lastHp.TryGetValue(b.Id, out float was) && b.Hp < was - 0.01f) _pings[b.Id] = (new Vector2(b.CentreX, b.CentreY), 1.5f);
                _lastHp[b.Id] = b.Hp;
            }
            foreach (var id in _pings.Keys.ToList())
            {
                var (at, life) = _pings[id];
                life -= (float)GetProcessDeltaTime();
                if (life <= 0) { _pings.Remove(id); continue; }
                _pings[id] = (at, life);
                float r = 3 + (1.5f - life) * 6;
                DrawArc(at * s, r, 0, Mathf.Tau, 16, new Color(1, 0.3f, 0.2f, life / 1.5f), 1.5f);
            }

            // The camera's view.
            var cam = Map.Camera;
            // The screen's four corners on the ground: a diamond on the map, since the view is isometric.
            var half = Map.GetViewportRect().Size / cam.Zoom / 2;
            var c = cam.GlobalPosition;
            Vector2[] corners = [c - half, c + new Vector2(half.X, -half.Y), c + half, c + new Vector2(-half.X, half.Y), c - half];
            DrawPolyline(corners.Select(p => Iso.Tile(p) * s).ToArray(), Colors.White, 1);
            DrawRect(new Rect2(Vector2.Zero, new Vector2(Px, Px)), Colors.Black, filled: false, width: 2);
        }
    }
}
