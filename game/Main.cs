using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Phase 0 client: draws the sim's terrain and buildings, steps the sim at a
/// fixed 20 Hz from a frame-time accumulator, and turns input into Commands.
/// It never mutates sim state directly.
///
/// Command-line (after `--`): --seed=N, --screenshot=path.png (save a frame
/// after a short warm-up, then quit; used by scripts/verify.sh).
/// </summary>
public partial class Main : Node2D
{
    const int TilePx = 8;
    const double TickSeconds = 1.0 / Balance.TickHz;

    static readonly Color[] TileColors =
    [
        new(0.36f, 0.52f, 0.28f), // Grass
        new(0.16f, 0.32f, 0.18f), // Forest
        new(0.46f, 0.44f, 0.42f), // Rock
        new(0.18f, 0.30f, 0.50f), // Water
    ];

    static readonly Dictionary<BuildingKind, Color> BuildingColors = new()
    {
        [BuildingKind.Keep] = new(0.85f, 0.72f, 0.35f),
        [BuildingKind.House] = new(0.70f, 0.52f, 0.38f),
        [BuildingKind.Wall] = new(0.62f, 0.62f, 0.66f),
    };

    World _world = null!;
    Camera2D _camera = null!;
    Label _hud = null!;
    double _accumulator;
    bool _paused;
    BuildingKind _armed = BuildingKind.House;
    string _lastRejection = "";
    string? _screenshotPath;
    int _frames;

    public override void _Ready()
    {
        var options = ParseUserArgs();
        uint seed = options.TryGetValue("seed", out var s) ? uint.Parse(s) : 7u;
        _screenshotPath = options.GetValueOrDefault("screenshot");

        _world = World.Create(new WorldOptions(seed));

        var terrain = new Sprite2D
        {
            Texture = BuildTerrainTexture(_world.Terrain),
            Centered = false,
            Scale = new Vector2(TilePx, TilePx),
            ZIndex = -1, // under this node's own _Draw (buildings)
        };
        AddChild(terrain);

        _camera = new Camera2D { Position = new Vector2(_world.Terrain.Width, _world.Terrain.Height) * TilePx / 2f, Zoom = new Vector2(1.5f, 1.5f) };
        AddChild(_camera);

        var hudLayer = new CanvasLayer();
        _hud = new Label { Position = new Vector2(12, 8) };
        _hud.AddThemeFontSizeOverride("font_size", 16);
        hudLayer.AddChild(_hud);
        AddChild(hudLayer);

        // scripts/verify.sh looks for this line: the engine banner alone doesn't prove the C# scene ran.
        GD.Print($"hellwall: world ready seed={_world.Seed} hash={StateHash.Hex(_world)}");
    }

    public override void _Process(double delta)
    {
        if (_paused)
        {
            _world.FlushCommands();
        }
        else
        {
            _accumulator += delta;
            // Cap catch-up so a stall doesn't spiral into a burst of ticks.
            int budget = 5;
            while (_accumulator >= TickSeconds && budget-- > 0)
            {
                _world.Step();
                _accumulator -= TickSeconds;
            }
            if (budget < 0) _accumulator = 0;
        }

        foreach (var e in _world.DrainEvents())
            if (e is CommandRejected r) _lastRejection = $"{r.Reason} (tick {r.Tick})";

        PanCamera(delta);
        UpdateHud();
        QueueRedraw();

        if (_screenshotPath != null && ++_frames == 30)
        {
            GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
            GetTree().Quit();
        }
    }

    public override void _Draw()
    {
        foreach (var b in _world.Buildings)
        {
            var rect = new Rect2(b.X * TilePx, b.Y * TilePx, b.W * TilePx, b.H * TilePx);
            DrawRect(rect, BuildingColors[b.Kind]);
            DrawRect(rect, Colors.Black, filled: false, width: 1);
        }

        // Placement ghost: green where it would succeed, red with the reason otherwise.
        var (tx, ty) = HoveredTile();
        var (w, h) = Balance.Footprint(_armed);
        bool ok = _world.CheckPlacement(_armed, tx, ty) == null;
        DrawRect(new Rect2(tx * TilePx, ty * TilePx, w * TilePx, h * TilePx), ok ? new Color(0.3f, 1f, 0.3f, 0.45f) : new Color(1f, 0.25f, 0.25f, 0.45f));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                var (tx, ty) = HoveredTile();
                _world.Enqueue(new PlaceBuilding(_armed, tx, ty));
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                var (rx, ry) = HoveredTile();
                int id = _world.BuildingIdAt(rx, ry);
                if (id != 0) _world.Enqueue(new Demolish(id));
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                ZoomBy(1.15f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                ZoomBy(1 / 1.15f);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.Keycode)
                {
                    case Key.Key1: _armed = BuildingKind.House; break;
                    case Key.Key2: _armed = BuildingKind.Wall; break;
                    case Key.Space: _paused = !_paused; break;
                }
                break;
        }
    }

    (int X, int Y) HoveredTile()
    {
        var p = GetGlobalMousePosition() / TilePx;
        return ((int)Mathf.Floor(p.X), (int)Mathf.Floor(p.Y));
    }

    void PanCamera(double delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        _camera.Position += dir * (float)(600 * delta) / _camera.Zoom.X;
    }

    void ZoomBy(float factor)
    {
        float z = Mathf.Clamp(_camera.Zoom.X * factor, 0.25f, 6f);
        _camera.Zoom = new Vector2(z, z);
    }

    void UpdateHud()
    {
        double seconds = _world.Tick / (double)Balance.TickHz;
        _hud.Text =
            $"Hellwall — phase 0    {(_paused ? "PAUSED" : "running")}    t={seconds:F1}s  tick {_world.Tick}    fps {Engine.GetFramesPerSecond()}\n" +
            $"armed: {_armed}   [1] House  [2] Wall   LMB place · RMB demolish · Space pause · WASD pan · wheel zoom\n" +
            $"buildings {_world.Buildings.Count}   seed {_world.Seed}   hash {StateHash.Hex(_world)}" +
            (_lastRejection.Length > 0 ? $"\nrejected: {_lastRejection}" : "");
    }

    static ImageTexture BuildTerrainTexture(Terrain terrain)
    {
        var image = Image.CreateEmpty(terrain.Width, terrain.Height, false, Image.Format.Rgb8);
        for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
                image.SetPixel(x, y, TileColors[(int)terrain.Get(x, y)]);
        return ImageTexture.CreateFromImage(image);
    }

    static Dictionary<string, string> ParseUserArgs()
    {
        var result = new Dictionary<string, string>();
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            var body = arg.TrimStart('-');
            int eq = body.IndexOf('=');
            if (eq > 0) result[body[..eq]] = body[(eq + 1)..];
            else result[body] = "true";
        }
        return result;
    }
}
