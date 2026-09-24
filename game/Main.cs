using System.Diagnostics;
using Godot;
using Hellwall.Sim;
using Side = Hellwall.Sim.Side;

namespace Hellwall.Game;

/// <summary>
/// The client: steps the sim at a fixed 20 Hz from a frame-time accumulator,
/// routes input into Commands and ClientState, and wires the views together.
/// It never mutates sim state directly.
///
/// Command-line (after `--`):
///   --seed=N             world seed
///   --screenshot=path    save a frame after warm-up (or at the end of --bench/--demo), then quit
///   --bench[=seconds]    spawn the 20k edge assault, run for N seconds (default 20),
///                        print frame and sim timings, then quit
///   --demo[=seconds]     build a walled town, send a wave at it, screenshot mid-fight
///   --skip=seconds       fast-forward the sim before the first frame
/// </summary>
public partial class Main : Node2D
{
    const int MapSize = 256;
    const int DormantPacks = 40;
    const int T = Palette.TilePx;
    const double TickSeconds = 1.0 / Balance.TickHz;

    World _world = null!;
    Sprite2D _terrain = null!;
    readonly ClientState _state = new();
    static readonly string SavePath = ProjectSettings.GlobalizePath("user://quicksave.hwsave");
    static readonly double[] Speeds = [1, 2, 4];
    int _speed;
    Camera2D _camera = null!;
    HordeRenderer _horde = null!;
    WorldView _view = null!;
    Hud _hud = null!;
    double _accumulator;
    bool _paused;
    string? _screenshotPath;
    int _frames;

    // Timing, measured by the host: the sim has no clock.
    readonly Stopwatch _simClock = new();
    double _simMsWindow;
    int _ticksWindow;
    double _simMsShown;

    // --bench / --demo
    double _benchSeconds;
    double _demoSeconds;
    double _elapsed;
    readonly List<double> _frameMs = new();
    readonly List<double> _tickMs = new();

    public override void _Ready()
    {
        var options = ParseUserArgs();
        uint seed = options.TryGetValue("seed", out var s) ? uint.Parse(s) : 7u;
        _screenshotPath = options.GetValueOrDefault("screenshot");
        if (options.TryGetValue("bench", out var b)) _benchSeconds = b == "true" ? 20 : double.Parse(b);
        if (options.TryGetValue("demo", out var d)) _demoSeconds = d == "true" ? 130 : double.Parse(d);

        var rules = Rules.Default;
        if (_benchSeconds > 0) rules = rules.WithBuilding(BuildingKind.Keep, k => k with { Hp = 1e9f });
        if (_demoSeconds > 0) rules = rules.WithStartingResources(new Cost { Gold = 5000, Wood = 3000, Stone = 2000, Food = 1000 });
        bool scripted = _benchSeconds > 0 || _demoSeconds > 0;
        _world = World.Create(new WorldOptions(seed, MapSize, scripted ? 0 : DormantPacks, rules, Survival: !scripted));

        _terrain = new Sprite2D { Texture = BuildTerrainTexture(_world.Terrain), Centered = false, Scale = new Vector2(T, T), ZIndex = -2 };
        AddChild(_terrain);
        _view = new WorldView { World = _world, State = _state };
        AddChild(_view);
        _horde = new HordeRenderer(T, MapSize) { ZIndex = 1 };
        AddChild(_horde);

        _camera = new Camera2D { Position = new Vector2(MapSize, MapSize) * T / 2f, Zoom = new Vector2(1.6f, 1.6f) };
        AddChild(_camera);

        _hud = new Hud { World = _world, State = _state, Send = Send };
        AddChild(_hud);

        if (_benchSeconds > 0) StartBenchAssault();
        if (_demoSeconds > 0) StartDemo();

        // Fast-forward before the first frame: for screenshots and for jumping into the middle of a run.
        if (options.TryGetValue("skip", out var skip))
        {
            for (int t = 0; t < double.Parse(skip) * Balance.TickHz && _world.Outcome == Outcome.Running; t++) _world.Step();
            HandleEvents();
        }

        // scripts/verify.sh looks for this line: the engine banner alone doesn't prove the C# scene ran.
        GD.Print($"hellwall: world ready seed={_world.Seed} hash={StateHash.Hex(_world)}");
    }

    void Send(Command command) => _world.Enqueue(command);

    public override void _Process(double delta)
    {
        if (_paused)
        {
            _world.FlushCommands();
        }
        else
        {
            _accumulator += delta;
            int budget = 5; // cap catch-up so a stall doesn't spiral into a burst of ticks
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

        HandleEvents();
        Age(_state.Shots, delta, ClientState.ShotLife);
        Age(_state.Log, delta, 8);

        _state.Alpha = _paused ? 1f : (float)Math.Clamp(_accumulator / TickSeconds, 0, 1);
        _state.MouseWorld = GetGlobalMousePosition();
        _state.HoveredTile = ((int)Mathf.Floor(_state.MouseWorld.X / T), (int)Mathf.Floor(_state.MouseWorld.Y / T));

        _horde.Sync(_world.Horde, _state.Alpha);
        _view.Refresh();
        PanCamera(delta);
        UpdateDebugLine();

        if (_benchSeconds > 0) StepBench(delta);
        else if (_demoSeconds > 0) StepDemo(delta);
        else if (_screenshotPath != null && ++_frames == 30) SaveScreenshotAndQuit();
    }

    void HandleEvents()
    {
        foreach (var e in _world.DrainEvents())
        {
            switch (e)
            {
                case CommandRejected r: _state.Say($"Can't: {r.Reason}"); break;
                case ShotFired shot: _state.Shots.Add((shot, 0)); break;
                case ConsecrationChanged: _view.RepaintConsecration(); break;
                case BuildingDestroyed b: _state.Say($"{b.Kind} destroyed"); break;
                case UnitTrained u: _state.Say($"{u.Kind} ready"); break;
                case UnitDied u: _state.Say($"{u.Kind} killed"); break;
                case PackWoke p: _state.Say($"A pack of {p.Count} stirs"); break;
                case BuildingPossessed p: _state.Say($"{p.Kind} POSSESSED: {p.Occupants} turning. [X] to purge it"); break;
                case WaveAnnounced w: _state.Say(w.Final ? $"THE CONVERGENCE: {w.Size} from every side" : $"Wave {w.Number}: {w.Size} from the {string.Join(" and ", w.Sides)}"); break;
                case WaveLanded w: _state.Say(w.Final ? "The Convergence is here." : $"Wave {w.Number} has arrived"); break;
                case OutcomeChanged o: _state.Say(o.Outcome == Outcome.Lost ? "The Keep has fallen." : "Victory."); break;
            }
        }
    }

    static void Age<TItem>(List<(TItem, double)> items, double delta, double life)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var (item, age) = items[i];
            if (age + delta > life) items.RemoveAt(i);
            else items[i] = (item, age + delta);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed)
                {
                    _state.DragStart = GetGlobalMousePosition();
                    _state.DragStartTile = _state.HoveredTile;
                    if (_state.Armed is { } k && k is not (BuildingKind.Wall or BuildingKind.Gate))
                    {
                        Send(new PlaceBuilding(k, _state.HoveredTile.X, _state.HoveredTile.Y));
                        _state.DragStart = null;
                    }
                }
                else
                {
                    if (_state.Armed is { } k) PlaceLine(k);
                    else FinishSelection(mb.ShiftPressed);
                    _state.DragStart = null;
                }
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mb:
                if (_state.Armed != null) _state.Armed = null;
                else if (_state.SelectedUnits.Count > 0)
                    Send(new OrderUnits(_state.SelectedUnits.ToArray(), mb.ShiftPressed ? OrderKind.Move : OrderKind.AttackMove, _state.HoveredTile.X, _state.HoveredTile.Y));
                else _state.SelectedBuilding = null;
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                ZoomBy(1.15f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                ZoomBy(1 / 1.15f);
                break;

            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key);
                break;
        }
    }

    void HandleKey(InputEventKey key)
    {
        int group = key.Keycode is >= Key.Key1 and <= Key.Key9 ? (int)(key.Keycode - Key.Key1) + 1 : 0;
        if (group > 0 && key.CtrlPressed)
        {
            _state.Groups[group] = _state.SelectedUnits.ToArray();
            _state.Say($"Group {group}: {_state.SelectedUnits.Count} units");
            return;
        }
        if (group > 0 && key.AltPressed)
        {
            _state.SelectedUnits.Clear();
            foreach (var id in _state.Groups.GetValueOrDefault(group, [])) _state.SelectedUnits.Add(id);
            _state.SelectedBuilding = null;
            return;
        }

        int slot = key.Keycode switch
        {
            >= Key.Key1 and <= Key.Key9 => (int)(key.Keycode - Key.Key1),
            Key.Key0 => 9,
            Key.Minus => 10,
            Key.Equal => 11,
            _ => -1,
        };
        if (slot >= 0 && slot < Palette.BuildBar.Length)
        {
            var kind = Palette.BuildBar[slot];
            _state.Armed = _state.Armed == kind ? null : kind;
            return;
        }

        var selected = _state.SelectedBuilding is { } sid ? _world.BuildingById(sid) : null;
        switch (key.Keycode)
        {
            case Key.Escape:
                _state.Armed = null;
                _state.SelectedUnits.Clear();
                _state.SelectedBuilding = null;
                break;
            case Key.Space: _paused = !_paused; break;
            case Key.Tab:
                _speed = (_speed + 1) % Speeds.Length;
                Engine.TimeScale = Speeds[_speed];
                _state.Say($"Speed {Speeds[_speed]}x");
                break;
            case Key.F5: QuickSave(); break;
            case Key.F9: QuickLoad(); break;
            case Key.X or Key.Delete when selected != null:
                Send(new Demolish(selected.Id));
                break;
            case Key.Q or Key.E or Key.R when selected is { Def.Trains.Length: > 0 }:
                int i = key.Keycode == Key.Q ? 0 : key.Keycode == Key.E ? 1 : 2;
                if (i < selected.Def.Trains.Length) Send(new TrainUnit(selected.Id, selected.Def.Trains[i]));
                break;
            case Key.H when _state.SelectedUnits.Count > 0:
                Send(new OrderUnits(_state.SelectedUnits.ToArray(), OrderKind.Hold, 0, 0));
                break;
            case Key.S when _state.SelectedUnits.Count > 0 && key.ShiftPressed:
                Send(new OrderUnits(_state.SelectedUnits.ToArray(), OrderKind.Idle, 0, 0));
                break;
            case Key.N:
                Send(new MakeNoise(_state.HoveredTile.X, _state.HoveredTile.Y, 40, 3));
                break;
            case Key.K:
                foreach (var c in Scenarios.Wave(_world, Side.West, 100)) Send(c);
                foreach (var c in Scenarios.Wave(_world, Side.East, 100)) Send(c);
                _state.Say("Debug: a wave of 200 from west and east");
                break;
            case Key.J:
                foreach (var c in Scenarios.EdgeAssault(_world, 20000, points: 8)) Send(c);
                break;
        }
    }

    void QuickSave()
    {
        _world.FlushCommands();
        System.IO.File.WriteAllBytes(SavePath, _world.Save());
        _state.Say($"Saved (day {_world.Day})");
    }

    void QuickLoad()
    {
        if (!System.IO.File.Exists(SavePath))
        {
            _state.Say("No quicksave yet: F5 to save");
            return;
        }
        try
        {
            var loaded = World.Load(System.IO.File.ReadAllBytes(SavePath), _world.Rules);
            _world = loaded;
            _view.World = loaded;
            _hud.World = loaded;
            _terrain.Texture = BuildTerrainTexture(loaded.Terrain);
            _view.RepaintConsecration();
            _state.SelectedUnits.Clear();
            _state.SelectedBuilding = null;
            _state.Armed = null;
            _state.Shots.Clear();
            _accumulator = 0;
            _state.Say($"Loaded (day {loaded.Day})");
        }
        catch (FormatException e)
        {
            _state.Say($"Can't load: {e.Message}");
        }
    }

    void PlaceLine(BuildingKind kind)
    {
        foreach (var (x, y) in _state.GhostTiles()) Send(new PlaceBuilding(kind, x, y));
    }

    /// <summary>A click selects what's under the cursor; a drag box-selects soldiers.</summary>
    void FinishSelection(bool additive)
    {
        if (_state.DragStart is not { } start) return;
        var end = GetGlobalMousePosition();
        if (!additive) _state.SelectedUnits.Clear();

        if (start.DistanceTo(end) < 6)
        {
            var p = end / T;
            var unit = _world.Units.Where(u => new Vector2(u.X, u.Y).DistanceTo(p) < 0.6f).OrderBy(u => new Vector2(u.X, u.Y).DistanceTo(p)).FirstOrDefault();
            if (unit != null)
            {
                _state.SelectedUnits.Add(unit.Id);
                _state.SelectedBuilding = null;
                return;
            }
            int id = _world.BuildingIdAt(_state.HoveredTile.X, _state.HoveredTile.Y);
            _state.SelectedBuilding = id == 0 ? null : id;
            return;
        }

        var box = new Rect2(start / T, (end - start) / T).Abs();
        foreach (var u in _world.Units)
            if (box.HasPoint(new Vector2(u.X, u.Y))) _state.SelectedUnits.Add(u.Id);
        if (_state.SelectedUnits.Count > 0) _state.SelectedBuilding = null;
    }

    void UpdateDebugLine()
    {
        if (_ticksWindow >= Balance.TickHz / 2)
        {
            _simMsShown = _simMsWindow / _ticksWindow;
            _simMsWindow = 0;
            _ticksWindow = 0;
        }
        int asleep = _world.Packs.Where(p => !p.Awake).Sum(p => p.Count);
        _hud.DebugText =
            $"{(_paused ? "PAUSED   " : "")}{Speeds[_speed]}x   demons {_world.Horde.Count} (+{asleep} asleep)   killed {_world.Stats.DemonsKilled}   " +
            $"fps {Engine.GetFramesPerSecond():0}  sim {_simMsShown:0.00} ms   ·   Space pause · Tab speed · F5 save · F9 load · debug: [N] noise [K] wave [J] 20k";
    }

    // --- scripted runs ---

    void StartBenchAssault()
    {
        foreach (var c in Scenarios.WallRing(_world, Scenarios.BenchRingRadius, Side.East, Side.West)) _world.Enqueue(c);
        _world.FlushCommands();
        foreach (var c in Scenarios.EdgeAssault(_world, 20000, points: 8)) _world.Enqueue(c);
        _world.FlushCommands();
        _camera.Zoom = new Vector2(0.45f, 0.45f);
    }

    void StepBench(double delta)
    {
        _elapsed += delta;
        if (_elapsed > 1) _frameMs.Add(delta * 1000); // skip the first second of warm-up
        if (_elapsed < _benchSeconds) return;

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

    /// <summary>
    /// The town from the headless town probe, played in real time for a
    /// screenshot: walls, towers, Bombards, a Barracks training soldiers, and a
    /// wave of 200 at 60 seconds. Runs at 4x so the fight arrives quickly.
    /// </summary>
    void StartDemo()
    {
        const int c = MapSize / 2;
        for (int y = c - 8; y <= c + 8; y++)
            for (int x = c - 8; x <= c + 8; x++)
            {
                if (Math.Max(Math.Abs(x - c), Math.Abs(y - c)) != 8) continue;
                bool gate = y == c - 8 && Math.Abs(x - c) <= 1;
                Send(new PlaceBuilding(gate ? BuildingKind.Gate : BuildingKind.Wall, x, y));
            }
        foreach (var (x, y) in new[] { (c - 6, c - 6), (c + 5, c - 6), (c - 6, c + 5), (c + 5, c + 5) }) Send(new PlaceBuilding(BuildingKind.Watchtower, x, y));
        Send(new PlaceBuilding(BuildingKind.Bombard, c - 1, c - 5));
        Send(new PlaceBuilding(BuildingKind.Bombard, c - 1, c + 4));
        Send(new PlaceBuilding(BuildingKind.House, c - 5, c - 1));
        Send(new PlaceBuilding(BuildingKind.House, c + 4, c - 1));
        Send(new PlaceBuilding(BuildingKind.Barracks, c - 5, c + 2));
        _world.FlushCommands();
        Engine.TimeScale = 4;
    }

    bool _demoQueued, _demoWave;

    void StepDemo(double delta)
    {
        _elapsed += delta; // already scaled by TimeScale
        if (!_demoQueued && _world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Barracks && b.Complete) is { } barracks)
        {
            for (int i = 0; i < 6; i++) Send(new TrainUnit(barracks.Id, UnitKind.Militia));
            for (int i = 0; i < 2; i++) Send(new TrainUnit(barracks.Id, UnitKind.Templar));
            _demoQueued = true;
        }
        if (!_demoWave && _world.Tick >= 60 * Balance.TickHz)
        {
            foreach (var cmd in Scenarios.Wave(_world, Side.West, 100)) Send(cmd);
            foreach (var cmd in Scenarios.Wave(_world, Side.East, 100)) Send(cmd);
            _state.SelectedUnits.Clear();
            foreach (var u in _world.Units) _state.SelectedUnits.Add(u.Id);
            _demoWave = true;
        }
        if (_elapsed >= _demoSeconds)
        {
            Engine.TimeScale = 1;
            GD.Print($"hellwall-demo: t={_world.Tick / Balance.TickHz}s outcome={_world.Outcome} demons={_world.Horde.Count} killed={_world.Stats.DemonsKilled} units={_world.Units.Count} buildings={_world.Buildings.Count}");
            if (_screenshotPath != null) SaveScreenshotAndQuit();
            else GetTree().Quit();
        }
    }

    void SaveScreenshotAndQuit()
    {
        GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
        GetTree().Quit();
    }

    void PanCamera(double delta)
    {
        if (Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Alt)) return;
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if ((Input.IsKeyPressed(Key.S) && !Input.IsKeyPressed(Key.Shift)) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        // Real time, not game time, so panning feels the same when paused or sped up.
        float real = (float)(delta / Math.Max(0.01, Engine.TimeScale));
        _camera.Position += dir * 900 * real / _camera.Zoom.X;
    }

    void ZoomBy(float factor)
    {
        float z = Mathf.Clamp(_camera.Zoom.X * factor, 0.2f, 6f);
        _camera.Zoom = new Vector2(z, z);
    }

    static ImageTexture BuildTerrainTexture(Terrain terrain)
    {
        var image = Image.CreateEmpty(terrain.Width, terrain.Height, false, Image.Format.Rgb8);
        for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
                image.SetPixel(x, y, Palette.Tiles[(int)terrain.Get(x, y)]);
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
