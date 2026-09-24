using System.Diagnostics;
using Godot;
using Hellwall.Sim;
using Side = Hellwall.Sim.Side;

namespace Hellwall.Game;

/// <summary>
/// The client: draws the sim's terrain, buildings, packs and horde, steps the
/// sim at a fixed 20 Hz from a frame-time accumulator, and turns input into
/// Commands. It never mutates sim state directly.
///
/// Command-line (after `--`):
///   --seed=N             world seed
///   --screenshot=path    save a frame after warm-up (or at the end of --bench), then quit
///   --bench[=seconds]    spawn the 20k edge assault, run for N seconds (default 20),
///                        print frame and sim timings, then quit
/// </summary>
public partial class Main : Node2D
{
    const int MapSize = 256;
    const int DormantPacks = 40;
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
    HordeRenderer _horde = null!;
    Label _hud = null!;
    double _accumulator;
    bool _paused;
    BuildingKind _armed = BuildingKind.Wall;
    string _lastRejection = "";
    string? _screenshotPath;
    int _frames;

    // Rings drawn where noise was made, so waking is legible: (x, y, radius, age in seconds).
    readonly List<(float X, float Y, float Radius, double Age)> _noiseRings = new();

    // Timing, measured by the host: the sim has no clock.
    readonly Stopwatch _simClock = new();
    double _simMsWindow;
    int _ticksWindow;
    double _simMsShown;
    string _hashShown = "";

    // --bench
    double _benchSeconds;
    double _benchElapsed;
    readonly List<double> _frameMs = new();
    readonly List<double> _tickMs = new();

    public override void _Ready()
    {
        var options = ParseUserArgs();
        uint seed = options.TryGetValue("seed", out var s) ? uint.Parse(s) : 7u;
        _screenshotPath = options.GetValueOrDefault("screenshot");
        if (options.TryGetValue("bench", out var b)) _benchSeconds = b == "true" ? 20 : double.Parse(b);

        _world = World.Create(new WorldOptions(seed, MapSize, _benchSeconds > 0 ? 0 : DormantPacks));

        AddChild(new Sprite2D
        {
            Texture = BuildTerrainTexture(_world.Terrain),
            Centered = false,
            Scale = new Vector2(TilePx, TilePx),
            ZIndex = -1, // under this node's own _Draw (buildings)
        });

        _horde = new HordeRenderer(TilePx);
        AddChild(_horde);

        _camera = new Camera2D { Position = new Vector2(MapSize, MapSize) * TilePx / 2f, Zoom = new Vector2(0.45f, 0.45f) };
        AddChild(_camera);

        var hudLayer = new CanvasLayer();
        _hud = new Label { Position = new Vector2(12, 8) };
        _hud.AddThemeFontSizeOverride("font_size", 16);
        _hud.AddThemeColorOverride("font_outline_color", Colors.Black);
        _hud.AddThemeConstantOverride("outline_size", 4);
        hudLayer.AddChild(_hud);
        AddChild(hudLayer);

        if (_benchSeconds > 0) StartBenchAssault();
        _hashShown = StateHash.Hex(_world);

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
                _simClock.Restart();
                _world.Step();
                double ms = _simClock.Elapsed.TotalMilliseconds;
                _simMsWindow += ms;
                _ticksWindow++;
                if (_benchSeconds > 0) _tickMs.Add(ms);
                _accumulator -= TickSeconds;
            }
            if (budget < 0) _accumulator = 0;
        }

        foreach (var e in _world.DrainEvents())
        {
            switch (e)
            {
                case CommandRejected r: _lastRejection = $"{r.Reason} (tick {r.Tick})"; break;
                case NoiseMade n: _noiseRings.Add((n.X, n.Y, n.Radius, 0)); break;
            }
        }
        for (int i = _noiseRings.Count - 1; i >= 0; i--)
        {
            var ring = _noiseRings[i];
            ring.Age += delta;
            if (ring.Age > 1.2) _noiseRings.RemoveAt(i);
            else _noiseRings[i] = ring;
        }

        float alpha = _paused ? 1f : (float)Math.Clamp(_accumulator / TickSeconds, 0, 1);
        _horde.Sync(_world.Horde, alpha);

        PanCamera(delta);
        UpdateHud();
        QueueRedraw();

        if (_benchSeconds > 0) StepBench(delta);
        else if (_screenshotPath != null && ++_frames == 30) SaveScreenshotAndQuit();
    }

    public override void _Draw()
    {
        foreach (var b in _world.Buildings)
        {
            var rect = new Rect2(b.X * TilePx, b.Y * TilePx, b.W * TilePx, b.H * TilePx);
            DrawRect(rect, BuildingColors[b.Kind]);
            DrawRect(rect, Colors.Black, filled: false, width: 1);
        }

        // Dormant packs: a dark ring sized by head count, labelled with it.
        var font = ThemeDB.FallbackFont;
        foreach (var p in _world.Packs)
        {
            if (p.Awake) continue;
            var centre = new Vector2(p.X + 0.5f, p.Y + 0.5f) * TilePx;
            float r = Mathf.Sqrt(p.Count / (Mathf.Pi * Balance.SpawnDensity)) * TilePx;
            var tint = p.Kind == DemonKind.Hound ? new Color(1f, 0.55f, 0.15f) : new Color(0.85f, 0.12f, 0.10f);
            DrawCircle(centre, r, new Color(tint, 0.18f));
            DrawArc(centre, r, 0, Mathf.Tau, 32, new Color(tint, 0.8f), 2);
            DrawString(font, centre + new Vector2(-14, 6), p.Count.ToString(), fontSize: 16, modulate: Colors.White);
        }

        foreach (var (x, y, radius, age) in _noiseRings)
        {
            float t = (float)(age / 1.2);
            DrawArc(new Vector2(x, y) * TilePx, radius * TilePx * (0.3f + 0.7f * t), 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.7f * (1 - t)), 3);
        }

        // Placement ghost: green where it would succeed, red otherwise.
        var (tx, ty) = HoveredTile();
        var def = _world.Rules[_armed];
        int w = def.W, h = def.H;
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
            case InputEventMouseMotion { ButtonMask: MouseButtonMask.Left } when _armed == BuildingKind.Wall:
                // Drag to lay a line of walls.
                var (dx, dy) = HoveredTile();
                if (_world.CheckPlacement(BuildingKind.Wall, dx, dy) == null) _world.Enqueue(new PlaceBuilding(BuildingKind.Wall, dx, dy));
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
                    case Key.N:
                        var (nx, ny) = HoveredTile();
                        _world.Enqueue(new MakeNoise(nx, ny, 40, 3));
                        break;
                    case Key.H:
                        foreach (var c in Scenarios.EdgeAssault(_world, 2000, points: 4)) _world.Enqueue(c);
                        break;
                    case Key.J:
                        foreach (var c in Scenarios.EdgeAssault(_world, 20000, points: 8)) _world.Enqueue(c);
                        break;
                }
                break;
        }
    }

    void StartBenchAssault()
    {
        foreach (var c in Scenarios.WallRing(_world, Scenarios.BenchRingRadius, Side.East, Side.West)) _world.Enqueue(c);
        _world.FlushCommands();
        foreach (var c in Scenarios.EdgeAssault(_world, 20000, points: 8)) _world.Enqueue(c);
        _world.FlushCommands();
    }

    void StepBench(double delta)
    {
        _benchElapsed += delta;
        if (_benchElapsed > 1) _frameMs.Add(delta * 1000); // skip the first second of warm-up
        if (_benchElapsed < _benchSeconds) return;

        static double Pct(List<double> v, double p)
        {
            var sorted = v.OrderBy(x => x).ToList();
            return sorted.Count == 0 ? 0 : sorted[(int)(p * (sorted.Count - 1))];
        }
        double avgFrame = _frameMs.Average();
        GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"hellwall-bench: demons={_world.Horde.Count} frames={_frameMs.Count} avg_fps={1000 / avgFrame:F1} frame_ms p50={Pct(_frameMs, 0.5):F2} p95={Pct(_frameMs, 0.95):F2} p99={Pct(_frameMs, 0.99):F2} tick_ms mean={_tickMs.Average():F2} p95={Pct(_tickMs, 0.95):F2} ticks={_tickMs.Count} refresh_hz={DisplayServer.ScreenGetRefreshRate():F0}"));
        if (_screenshotPath != null) SaveScreenshotAndQuit();
        else GetTree().Quit();
    }

    void SaveScreenshotAndQuit()
    {
        GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
        GetTree().Quit();
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
        _camera.Position += dir * (float)(900 * delta) / _camera.Zoom.X;
    }

    void ZoomBy(float factor)
    {
        float z = Mathf.Clamp(_camera.Zoom.X * factor, 0.2f, 6f);
        _camera.Zoom = new Vector2(z, z);
    }

    void UpdateHud()
    {
        // Refresh the slow-changing numbers twice a second: hashing 20k demons every frame is wasteful.
        if (_ticksWindow >= Balance.TickHz / 2)
        {
            _simMsShown = _simMsWindow / _ticksWindow;
            _simMsWindow = 0;
            _ticksWindow = 0;
            _hashShown = StateHash.Hex(_world);
        }

        int asleep = 0, sleeping = 0;
        foreach (var p in _world.Packs)
            if (!p.Awake) { asleep++; sleeping += p.Count; }

        double seconds = _world.Tick / (double)Balance.TickHz;
        _hud.Text =
            $"Hellwall — phase 1    {(_paused ? "PAUSED" : "running")}    t={seconds:F1}s    fps {Engine.GetFramesPerSecond():F0}    sim {_simMsShown:F2} ms/tick\n" +
            $"demons {_world.Horde.Count}    dormant packs {asleep} ({sleeping} asleep)    buildings {_world.Buildings.Count}    hash {_hashShown}\n" +
            $"armed: {_armed}   [1] House  [2] Wall (drag for lines)   LMB place · RMB demolish · [N] noise at cursor · [H] 2k assault · [J] 20k assault\n" +
            $"Space pause · WASD pan · wheel zoom" +
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
