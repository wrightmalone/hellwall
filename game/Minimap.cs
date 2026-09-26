using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// The whole map in the corner, drawn as a diamond in the same projection as
/// the main view, so north (the map's y = 0 edge) is up and to the right in
/// both and the wave callouts point the same way everywhere. Terrain and
/// holy ground are a texture (repainted only when the grid changes), sheared
/// into the diamond; buildings, the horde, gates, packs, incoming waves,
/// damage pings and the camera's view are drawn over it each frame. Click
/// or drag to move the camera.
/// </summary>
public partial class Minimap : Control
{
    /// <summary>The diamond is W wide and W/2 tall, like a tile.</summary>
    const float W = 320, H = W / 2;

    public World World = null!;
    public Camera2D Camera = null!;
    public Action<Vector2> MoveCamera = null!;
    /// <summary>While this says so (a box select being dragged over it), the minimap takes no input.</summary>
    public Func<bool>? IgnoreInput;

    ImageTexture? _terrain;
    readonly Dictionary<int, float> _lastHp = new();
    readonly Dictionary<int, (Vector2 At, float Life)> _pings = new();
    /// <summary>Big slow rings where something's gone badly wrong (a possession): seconds left, from Alarm.</summary>
    readonly List<(Vector2 At, float Life)> _alarms = new();
    const float AlarmSeconds = 6;

    public void Alarm(Vector2 tile) => _alarms.Add((tile, AlarmSeconds));

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(W, H + 16);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        Repaint();
    }

    float N => World.Terrain.Width;

    /// <summary>Tile units to minimap pixels: the isometric projection, scaled to the diamond (8 px margin on top for the compass).</summary>
    Vector2 M(float x, float y) => new(W / 2 + (x - y) * (W / 2) / N, 8 + (x + y) * (H / 2) / N);

    Vector2 M(Vector2 t) => M(t.X, t.Y);

    /// <summary>Minimap pixels back to tile units.</summary>
    Vector2 Tile(Vector2 p)
    {
        float u = (p.X - W / 2) / ((W / 2) / N), v = (p.Y - 8) / ((H / 2) / N);
        return new Vector2((u + v) / 2, (v - u) / 2);
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
                if (!World.Vision.IsExplored(x, y)) c = new Color(0.03f, 0.03f, 0.05f);
                image.SetPixel(x, y, c);
            }
        _terrain = ImageTexture.CreateFromImage(image);
    }

    int _revision;

    public override void _Process(double delta)
    {
        // New ground explored: repaint (the terrain layer is cheap at this size, but not every frame).
        if (World.Vision.Revision != _revision && (_revision == 0 || Engine.GetProcessFrames() % 15 == 0))
        {
            _revision = World.Vision.Revision;
            Repaint();
        }
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (IgnoreInput?.Invoke() == true) return;
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb) Jump(mb.Position);
        else if (@event is InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } mm) Jump(mm.Position);
    }

    void Jump(Vector2 local)
    {
        var t = Tile(local);
        MoveCamera(Iso.P(Mathf.Clamp(t.X, 0, N), Mathf.Clamp(t.Y, 0, N)));
        AcceptEvent();
    }

    /// <summary>The marching wave columns (ClientState.Columns), if the game shows them.</summary>
    public List<(int Column, Vector2 Centre, int Count)>? Columns;

    public override void _Draw()
    {
        var world = World;
        float s = (W / 2) / N;
        // Backing and frame, then the terrain sheared into the diamond.
        Vector2[] diamond = [M(0, 0), M(N, 0), M(N, N), M(0, N)];
        DrawColoredPolygon(diamond, new Color(0.08f, 0.08f, 0.07f));
        if (_terrain != null)
        {
            DrawSetTransformMatrix(new Transform2D(new Vector2(s, s / 2), new Vector2(-s, s / 2), M(0, 0)));
            DrawTexture(_terrain, Vector2.Zero);
            DrawSetTransformMatrix(Transform2D.Identity);
        }

        foreach (var b in world.Buildings)
        {
            var colour = b.Possessed ? new Color(0.7f, 0.2f, 0.8f) : b.IsWallLike ? new Color(0.85f, 0.85f, 0.9f) : Palette.Building(b.Kind);
            DrawColoredPolygon([M(b.X, b.Y), M(b.X + b.W, b.Y), M(b.X + b.W, b.Y + b.H), M(b.X, b.Y + b.H)], colour);
        }
        foreach (var p in world.Packs)
            if (!p.Awake && world.Vision.IsExplored(p.X, p.Y)) DrawCircle(M(p.X + 0.5f, p.Y + 0.5f), p.Stray ? 0.8f : 1.6f, new Color(0.9f, 0.4f, 0.2f, 0.8f));

        // The horde, subsampled: a dot per demon up to a few thousand, which is plenty to read.
        var h = world.Horde;
        int step = Math.Max(1, h.Count / 3000);
        for (int i = 0; i < h.Count; i += step)
            if (world.Vision.IsVisible(h.X[i], h.Y[i])) DrawRect(new Rect2(M(h.X[i], h.Y[i]), Vector2.One), new Color(1, 0.15f, 0.1f));
        foreach (var r in world.Ruins)
            if (!r.Looted && world.Vision.IsExplored(r.X, r.Y)) DrawCircle(M(r.X + 0.5f, r.Y + 0.5f), 2.5f, UiKit.Gold);
        foreach (var g in world.Gates)
            if (g.Alive && world.Vision.IsExplored(g.X + 1, g.Y + 1)) DrawColoredPolygon([M(g.X, g.Y), M(g.X + 3, g.Y), M(g.X + 3, g.Y + 3), M(g.X, g.Y + 3)], new Color(1, 0.1f, 0.5f));
        foreach (var u in world.Units)
            DrawRect(new Rect2(M(u.X, u.Y) - Vector2.One, new Vector2(2, 2)), new Color(0.5f, 0.9f, 1));

        // Each wave column on the march: a pulsing ring on its centre, seen through fog like its marker on the map.
        if (Columns != null)
        {
            float pulse = 0.6f + 0.4f * Mathf.Sin((float)Time.GetTicksMsec() / 220f);
            foreach (var (_, centre, _) in Columns)
            {
                DrawCircle(M(centre.X, centre.Y), 3.5f, new Color(0.35f, 0.04f, 0.03f));
                DrawArc(M(centre.X, centre.Y), 3.5f, 0, Mathf.Tau, 16, new Color(1, 0.3f, 0.2f, pulse), 1.5f);
            }
        }

        // Where waves are coming from: a red arrow on the middle of each announced side.
        if (world.Survival is { } sv)
            foreach (var wave in sv.Waves)
            {
                if (!wave.Announced || wave.Landed) continue;
                foreach (var side in wave.Sides)
                {
                    var at = M(SideMiddle(side));
                    var inward = (M(N / 2, N / 2) - at).Normalized();
                    var across = new Vector2(-inward.Y, inward.X);
                    var tip = at + inward * 4;
                    DrawColoredPolygon([tip + inward * 7, tip - inward * 4 + across * 6, tip - inward * 4 - across * 6], new Color(1, 0.25f, 0.2f));
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
            DrawArc(M(at), 3 + (1.5f - life) * 6, 0, Mathf.Tau, 16, new Color(1, 0.3f, 0.2f, life / 1.5f), 1.5f);
        }

        for (int i = _alarms.Count - 1; i >= 0; i--)
        {
            var (at, life) = _alarms[i];
            life -= (float)GetProcessDeltaTime();
            if (life <= 0) { _alarms.RemoveAt(i); continue; }
            _alarms[i] = (at, life);
            float t = (AlarmSeconds - life) % 1f;
            DrawArc(M(at), 4 + t * 18, 0, Mathf.Tau, 24, new Color(1, 0.15f, 0.1f, (1 - t) * Mathf.Min(1, life)), 2.5f);
            DrawCircle(M(at), 3, new Color(1, 0.2f, 0.1f));
        }

        // The camera's view: the screen's corners on the ground.
        var half = Camera.GetViewportRect().Size / Camera.Zoom / 2;
        var c = Camera.GlobalPosition;
        Vector2[] corners = [c - half, c + new Vector2(half.X, -half.Y), c + half, c + new Vector2(-half.X, half.Y), c - half];
        DrawPolyline(corners.Select(p => M(Iso.Tile(p))).ToArray(), Colors.White, 1);

        DrawPolyline([.. diamond, diamond[0]], new Color(0.38f, 0.34f, 0.27f), 2);
        // The compass: N on the north edge, which runs up and to the right, as in the main view.
        var font = ThemeDB.FallbackFont;
        var north = M(N * 0.75f, -N * 0.06f);
        DrawString(font, north + new Vector2(-4, 4), "N", fontSize: 13, modulate: UiKit.Gold);
    }

    Vector2 SideMiddle(Hellwall.Sim.Side side) => side switch
    {
        Hellwall.Sim.Side.North => new Vector2(N / 2, 0),
        Hellwall.Sim.Side.South => new Vector2(N / 2, N),
        Hellwall.Sim.Side.West => new Vector2(0, N / 2),
        _ => new Vector2(N, N / 2),
    };
}
