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
        MoveCamera(local * scale * Palette.TilePx);
        AcceptEvent();
    }

    partial class Overlay : Control
    {
        public Minimap Map = null!;

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

            // The camera's view.
            var cam = Map.Camera;
            var view = Map.GetViewportRect().Size / cam.Zoom;
            var topLeft = (cam.GlobalPosition - view / 2) / Palette.TilePx * s;
            DrawRect(new Rect2(topLeft, view / Palette.TilePx * s), Colors.White, filled: false, width: 1);
            DrawRect(new Rect2(Vector2.Zero, new Vector2(Px, Px)), Colors.Black, filled: false, width: 2);
        }
    }
}
