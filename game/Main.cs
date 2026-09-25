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
///   --map=Kind           plains, lakes, highlands or wildwood
///   --difficulty=Level   easy, normal, hard or nightmare
///   --endless            endless mode (no Convergence; corruptions)
///   --menu               show the new-game menu even though other flags were given
///   With none of the above (and not headless), the new-game menu comes first.
///   --screenshot=path    save a frame after warm-up (or at the end of --bench/--demo), then quit
///   --bench[=seconds]    spawn the 20k edge assault, run for N seconds (default 20),
///                        print frame and sim timings, then quit
///   --demo[=seconds]     build a walled town, send a wave at it, screenshot mid-fight
///   --skip=seconds       fast-forward the sim before the first frame
///   --autoplay           let the headless harness's bot play (watch it, or take over any time with Esc)
///   --inspect=Kind       select the first building of that kind (for screenshots)
///   --pausemenu          open the pause menu at once (screenshots)
///   --reveal             no fog of war
///   --editor             open the map editor
///   --look=x,y[,zoom]    start the camera over tile (x, y)
///   --selftest=controls  select the soldiers, press A, click: check they got an attack-move there, print, quit
/// </summary>
public partial class Main : Node2D
{
    const int MapSize = 256;
    const double TickSeconds = 1.0 / Balance.TickHz;

    World _world = null!;
    bool _started;
    /// <summary>The unscaled rules the run was made from: a quickload rescales them to the save's difficulty.</summary>
    Rules _baseRules = Rules.Default;
    Dictionary<string, string> _options = new();
    Coach? _coach;
    Sound _sound = null!;
    /// <summary>Set before reloading the scene for a new run: show the menu whatever the command line said.</summary>
    static bool _menuNext;
    /// <summary>Set before reloading for "back to the campaign": the campaign map comes up instead of the menu.</summary>
    static bool _campaignNext;
    TerrainView? _terrainView;
    /// <summary>Trees, buildings and soldiers, drawn back to front.</summary>
    Node2D? _sorted;
    readonly ClientState _state = new();
    static readonly string SavePath = ProjectSettings.GlobalizePath("user://quicksave.hwsave");
    /// <summary>Barracks train keys, in the order of its Trains list.</summary>
    public static readonly Key[] TrainKeys = [Key.Q, Key.E, Key.R, Key.T, Key.Y, Key.F, Key.V];
    static readonly double[] Speeds = [1, 2, 4];
    int _speed;
    Hellwall.Headless.Bot? _bot;
    Camera2D _camera = null!;
    HordeRenderer _horde = null!;
    WorldView _view = null!;
    Hud _hud = null!;
    Minimap _minimap = null!;
    FogView? _fogView;
    /// <summary>The world's light: a faint blood-red cast while a wave is on its way.</summary>
    CanvasModulate _tint = null!;
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

    /// <summary>One soundtrack for the whole session: it keeps playing across the menu, runs and reloads.</summary>
    static Music? _music;
    static Music TheMusic => _music!;

    public override void _Ready()
    {
        _options = ParseUserArgs();
        if (_options.TryGetValue("dump-music", out var dump)) Music.DumpTo = dump;
        if (_music == null || !IsInstanceValid(_music))
        {
            _music = new Music();
            // Kept above the scene, so reloading the scene (a new run) doesn't restart it.
            GetTree().Root.CallDeferred(Node.MethodName.AddChild, _music);
        }
        _music.World = null;
        if (_options.TryGetValue("ui-scale", out var ui)) Display.Override = float.Parse(ui, System.Globalization.CultureInfo.InvariantCulture);
        Display.Apply(GetTree().Root);
        var o = _options;
        var setup = new GameSetup(
            o.TryGetValue("seed", out var s) ? uint.Parse(s) : 11u,
            o.TryGetValue("map", out var m) ? Enum.Parse<MapKind>(m, ignoreCase: true) : MapKind.Plains,
            o.TryGetValue("difficulty", out var d) ? Enum.Parse<Difficulty>(d, ignoreCase: true) : Difficulty.Normal,
            o.ContainsKey("endless"),
            o.TryGetValue("mission", out var mid) ? Campaign.Default.Find(mid) ?? throw new ArgumentException($"no mission '{mid}'") : null);
        // Any flag that sets up a run (and every headless boot, which is verify.sh) skips the menu.
        bool flagged = new[] { "seed", "map", "difficulty", "endless", "autoplay", "bench", "demo", "skip", "screenshot", "mission", "woods" }.Any(o.ContainsKey);
        if (_retry != null)
        {
            var again = _retry;
            _retry = null;
            Begin(new GameSetup(again.Seed, again.Map, again.Difficulty, false, again));
        }
        else if (_campaignNext || o.ContainsKey("campaign"))
        {
            _campaignNext = false;
            OpenCampaign();
        }
        else if (o.ContainsKey("editor") || o.GetValueOrDefault("selftest") == "editor")
        {
            OpenEditor();
            if (o.GetValueOrDefault("selftest") == "editor") GetChildren().OfType<MapEditor>().Last().CallDeferred(nameof(MapEditor.SelfTest));
        }
        else if ((flagged || DisplayServer.GetName() == "headless") && !o.ContainsKey("menu") && !_menuNext) Begin(setup);
        else ShowMenu(setup);
    }

    void ShowMenu(GameSetup setup) => AddChild(new NewGameMenu
    {
        Initial = setup, Start = Begin, OpenCampaign = OpenCampaign, OpenEditor = OpenEditor, ShowNews = _options.ContainsKey("whatsnew"),
        Saved = NewestSlot() is { } newest ? SlotSummary(newest) : null,
        Continue = () =>
        {
            _continueSlot = NewestSlot() ?? 0;
            Begin(new GameSetup(11, MapKind.Plains, Difficulty.Normal, false));
            CallDeferred(nameof(ContinueLoad));
        },
    });

    void OpenEditor() => AddChild(new MapEditor
    {
        Back = () => ShowMenu(new GameSetup(11, MapKind.Plains, Difficulty.Normal, false)),
        Play = map => Begin(new GameSetup(map.Seed, map.Map, Difficulty.Normal, false, map with { Id = "map-" + map.Id })),
    });

    void OpenCampaign()
    {
        CampaignMap? map = null;
        map = new CampaignMap
        {
            Begin = mission => { map!.QueueFree(); Begin(new GameSetup(mission.Seed, mission.Map, mission.Difficulty, false, mission)); },
            Back = () => { map!.QueueFree(); ShowMenu(new GameSetup(11, MapKind.Plains, Difficulty.Normal, false)); },
        };
        AddChild(map);
    }

    void Begin(GameSetup setup)
    {
        var options = _options;
        uint seed = setup.Seed;
        _screenshotPath = options.GetValueOrDefault("screenshot");
        if (options.TryGetValue("bench", out var b)) _benchSeconds = b == "true" ? 20 : double.Parse(b);
        if (options.TryGetValue("demo", out var d)) _demoSeconds = d == "true" ? 130 : double.Parse(d);

        var rules = Rules.Default;
        if (_benchSeconds > 0) rules = rules.WithBuilding(BuildingKind.Keep, k => k with { Hp = 1e9f });
        if (_demoSeconds > 0) rules = rules.WithStartingResources(new Cost { Gold = 5000, Wood = 3000, Stone = 2000, Food = 1000, Iron = 1000 });
        bool scripted = _benchSeconds > 0 || _demoSeconds > 0;
        _baseRules = rules; // saves record whether the woods were living, and Load puts that back
        if (setup.Woods || options.ContainsKey("woods")) rules = rules.WithWoods(w => w with { Blocks = true });
        if (options.ContainsKey("reveal")) rules = rules.WithFog(f => f with { Enabled = false });
        if (options.ContainsKey("patrons-now")) rules = rules.WithSurvival(s => s with { PatronMilestones = [1, .. s.PatronMilestones] }); // screenshots of the picker
        // A survival run takes its packs from rules.json (wilds).
        _world = setup.Mission is { } mission && !scripted
            ? World.Create(mission.Options(rules))
            : World.Create(new WorldOptions(seed, MapSize, 0, rules, Survival: !scripted, Difficulty: setup.Difficulty, Endless: setup.Endless && !scripted, Map: setup.Map));

        BuildViews();
        _horde = new HordeRenderer(_world.Terrain.Width) { ZIndex = 1 };
        AddChild(_horde);

        _sound = new Sound();
        AddChild(_sound);
        _tint = new CanvasModulate { Color = Colors.White };
        AddChild(_tint);
        TheMusic.World = _world;

        _camera = new Camera2D { Position = Iso.P(_world.Terrain.Width / 2f, _world.Terrain.Height / 2f), Zoom = Vector2.One * 1.1f / Display.UiScale };
        if (options.GetValueOrDefault("look") == "ruin" && _world.Ruins.Count > 0) _camera.Position = Iso.P(_world.Ruins[0].X, _world.Ruins[0].Y);
        else if (options.TryGetValue("look", out var look) && look.Split(',') is [var lx, var ly, ..] parts)
        {
            _camera.Position = Iso.P(float.Parse(lx), float.Parse(ly));
            if (parts.Length > 2) _camera.Zoom = Vector2.One * float.Parse(parts[2]);
        }
        AddChild(_camera);

        _withCoach = !scripted && !options.ContainsKey("autoplay") && !options.ContainsKey("selftest") && Coach.Enabled;
        BuildHud();
        // A mission opens with its narrator restating the briefing.
        if (_world.Scenario is { } opening && !scripted) _hud.Voice.Say(Campaign.Default.Speaker(""), opening.Briefing);

        if (_benchSeconds > 0) StartBenchAssault();
        if (_demoSeconds > 0) StartDemo();

        if (options.ContainsKey("autoplay")) _bot = new Hellwall.Headless.Bot(_world, Hellwall.Headless.Bot.Style.Full);

        // Fast-forward before the first frame: for screenshots and for jumping into the middle of a run.
        if (options.TryGetValue("skip", out var skip))
        {
            for (int t = 0; t < double.Parse(skip) * Balance.TickHz && _world.Outcome == Outcome.Running; t++)
            {
                if (_bot != null && _world.Tick % Balance.TickHz == 0) _bot.Act();
                _world.Step();
                var events = _world.DrainEvents();
                _bot?.See(events);
                foreach (var e in events)
                    if (e is TreeFelled f) _terrainView!.PaintCell(f.X, f.Y);
            }
            HandleEvents();
        }

        // Select a building of a kind before the first frame, for screenshots of its inspector.
        if (options.TryGetValue("inspect", out var inspect) && Enum.TryParse<BuildingKind>(inspect, true, out var kindToInspect))
            _state.SelectedBuilding = _world.Buildings.FirstOrDefault(b => b.Kind == kindToInspect)?.Id;

        // scripts/verify.sh looks for this line: the engine banner alone doesn't prove the C# scene ran.
        if (options.GetValueOrDefault("selftest") == "controls") CallDeferred(nameof(SelfTestControls));
        if (options.ContainsKey("pausemenu")) CallDeferred(nameof(OpenPauseMenu)); // for screenshots of it
        if (options.ContainsKey("noise")) _state.ShowNoise = true;
        if (options.ContainsKey("select-keep")) _state.SelectedBuilding = _world.Buildings.First(b => b.Kind == BuildingKind.Keep).Id; // screenshots of the inspector
        GD.Print($"hellwall: world ready seed={_world.Seed} map={_world.Map} difficulty={_world.Rules.Difficulty} endless={_world.Survival?.Endless ?? false} hash={StateHash.Hex(_world)}");
        _started = true;
    }

    void Send(Command command) => _world.Enqueue(command);

    bool _withCoach;

    /// <summary>The screen-space UI for the current world: at the start, and after a quickload.</summary>
    void BuildHud()
    {
        _hud?.QueueFree();
        _minimap = new Minimap { World = _world, Camera = _camera, MoveCamera = p => _camera.Position = p };
        _hud = new Hud
        {
            World = _world, State = _state, Send = Send, Minimap = _minimap, NewRun = NewRun,
            BackToCampaign = BackToCampaign, Retry = RetryMission,
            JumpTo = tile => _camera.Position = Iso.P(tile),
            Speed = () => (_paused ? "PAUSED  ·  " : "") + $"{Speeds[_speed]}x  ·  F1 help",
            Order = OrderSelected,
        };
        AddChild(_hud);
        if (_withCoach)
        {
            _coach = new Coach { World = _world };
            _hud.AddChild(_coach);
        }
    }

    /// <summary>The command card's order buttons, the same as their keys.</summary>
    void OrderSelected(string order)
    {
        if (_state.SelectedUnits.Count == 0) return;
        switch (order)
        {
            case "attack":
                _state.AttackMoveArmed = true;
                _state.Armed = null;
                Input.SetDefaultCursorShape(Input.CursorShape.Cross);
                break;
            case "patrol":
                _state.AttackMoveArmed = true;
                _state.PatrolArmed = true;
                _state.Armed = null;
                Input.SetDefaultCursorShape(Input.CursorShape.Cross);
                break;
            case "hold": Send(new OrderUnits(_state.SelectedUnits.ToArray(), OrderKind.Hold, 0, 0)); break;
            case "stop": Send(new OrderUnits(_state.SelectedUnits.ToArray(), OrderKind.Idle, 0, 0)); break;
        }
    }

    bool _swallowRelease;

    /// <summary>
    /// The attack-move controls, end to end through Godot's input: A with the
    /// soldiers selected arms it, a left-click on the ground orders it.
    /// scripts/verify.sh runs this headless and looks for the PASS line.
    /// </summary>
    async void SelfTestControls()
    {
        // The starting garrison turns out on the first ticks.
        for (int i = 0; i < 300 && _world.Units.Count == 0; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        foreach (var u in _world.Units) _state.SelectedUnits.Add(u.Id);
        // Click in the middle of the screen, on open ground. A headless window is 64 px square,
        // all HUD, so the HUD is hidden for the test: this is about the ground click, not the panels.
        _hud.Visible = false;
        var centre = GetViewport().GetVisibleRect().Size / 2;
        Input.WarpMouse(centre);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.A, Pressed = true });
        Input.FlushBufferedEvents();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool armed = _state.AttackMoveArmed;
        var target = _state.HoveredTile;
        // Straight into the viewport: a headless build has no window for Input to route mouse events through.
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = centre, GlobalPosition = centre });
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = centre, GlobalPosition = centre });
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _world.FlushCommands();
        bool ordered = _world.Units.Count > 0 && _world.Units.All(u => u.Order == OrderKind.AttackMove && u.DestX == target.X && u.DestY == target.Y);
        bool kept = _state.SelectedUnits.Count == _world.Units.Count && !_state.AttackMoveArmed;

        // Esc clears the selection; Esc again, with nothing selected, opens the pause menu and stops the sim; Esc closes it.
        async Task Escape()
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            Input.FlushBufferedEvents();
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = false });
            Input.FlushBufferedEvents();
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await Escape();
        bool cleared = _state.SelectedUnits.Count == 0 && _pauseMenu == null;
        await Escape();
        int tick = _world.Tick;
        for (int i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool menu = _pauseMenu != null && _paused && _world.Tick == tick;
        await Escape();
        bool resumed = _pauseMenu == null && !_paused;
        // A save and a load through the same path the menus use, in slot 9 (no menu shows it, so no player's save is touched).
        _world.FlushCommands();
        string hash = StateHash.Hex(_world);
        SaveTo(9);
        LoadFrom(9);
        bool reloaded = StateHash.Hex(_world) == hash;
        foreach (var f in new[] { SlotPath(9), SlotPath(9) + ".txt" }) System.IO.File.Delete(f);
        bool pass = armed && ordered && kept && cleared && menu && resumed && reloaded;
        GD.Print(pass ? "hellwall-selftest: PASS controls" : $"hellwall-selftest: FAIL controls (units {_world.Units.Count}, armed {armed}, ordered {ordered}, selection kept and disarmed {kept}, esc cleared {cleared}, menu paused {menu}, resumed {resumed}, save and load {reloaded})");
        GetTree().Quit();
    }

    public static bool EdgeScroll => Settings.Get("edge_scroll", true);
    public static bool ConfineMouse => Settings.Get("confine_mouse", true);

    void DisarmAttackMove()
    {
        _state.AttackMoveArmed = false;
        _state.PatrolArmed = false;
        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
    }

    /// <summary>Keep the cursor inside the window while playing (so the edges scroll), and free it on menus and the end screen.</summary>
    void UpdateMouseMode()
    {
        var want = _started && ConfineMouse && _pauseMenu == null && _world.Outcome == Outcome.Running ? Input.MouseModeEnum.Confined : Input.MouseModeEnum.Visible;
        if (Input.MouseMode != want) Input.MouseMode = want;
    }

    /// <summary>The world-space views, (re)built for the current world: at the start, and after a quickload.</summary>
    void BuildViews()
    {
        _terrainView?.QueueFree();
        _sorted?.QueueFree();
        _view?.QueueFree();
        _fogView?.QueueFree();
        _fogView = new FogView { World = _world, ZIndex = 2 }; // over the world and the horde, under the overlay (3)
        AddChild(_fogView);
        _sorted = new Node2D { YSortEnabled = true };
        _terrainView = new TerrainView { World = _world, Sorted = _sorted, ZIndex = -2 };
        AddChild(_terrainView);
        AddChild(_sorted);
        _view = new WorldView { World = _world, State = _state, Sorted = _sorted };
        AddChild(_view);
    }

    /// <summary>Back to the campaign map (a mission just ended).</summary>
    void BackToCampaign()
    {
        _campaignNext = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        DisarmAttackMove();
        GetTree().ReloadCurrentScene();
    }

    void RetryMission()
    {
        _retry = _world.Scenario;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        DisarmAttackMove();
        GetTree().ReloadCurrentScene();
    }

    static ScenarioDef? _retry;

    void NewRun()
    {
        _menuNext = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        DisarmAttackMove();
        GetTree().ReloadCurrentScene();
    }

    public override void _Process(double delta)
    {
        if (!_started)
        {
            // --menu --screenshot=path: a picture of the menu itself.
            if (_options.TryGetValue("screenshot", out var shot) && ++_frames == 30) { _screenshotPath = shot; SaveScreenshotAndQuit(); }
            return;
        }
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
                if (_bot != null && _world.Tick % Balance.TickHz == 0) _bot.Act();
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
        Age(_state.Bursts, delta, 0.4);
        Age(_state.Spits, delta, 0.45);
        Age(_state.Howls, delta, 1.2);
        Age(_state.Log, delta, 8);

        _state.Alpha = _paused ? 1f : (float)Math.Clamp(_accumulator / TickSeconds, 0, 1);
        _state.MouseWorld = GetGlobalMousePosition();
        _state.HoveredTile = Iso.TileAt(_state.MouseWorld);

        _horde.Sync(_world, _state.Alpha, delta);
        _view.Refresh();
        PanCamera(delta);
        var coming = _world.Survival?.Next;
        var light = coming is { Announced: true } ? (coming.Final ? new Color(1, 0.78f, 0.74f) : new Color(1, 0.9f, 0.87f)) : Colors.White;
        _tint.Color = _tint.Color.Lerp(light, (float)Math.Min(1, delta * 0.8));
        UpdateMouseMode();
        if (_state.AttackMoveArmed && _state.SelectedUnits.Count == 0) DisarmAttackMove();
        UpdateDebugLine();

        if (_benchSeconds > 0) StepBench(delta);
        else if (_demoSeconds > 0) StepDemo(delta);
        else if (_screenshotPath != null && ++_frames == 30) SaveScreenshotAndQuit();
    }

    /// <summary>Trees came down since the minimap was last painted; it repaints at most twice a second.</summary>
    bool _minimapStale;
    double _minimapClock;

    void HandleEvents()
    {
        _minimapClock += GetProcessDeltaTime();
        if (_minimapStale && _minimapClock > 0.5)
        {
            _minimap.Repaint();
            _minimapStale = false;
            _minimapClock = 0;
        }
        var events = _world.DrainEvents();
        _bot?.See(events);
        foreach (var e in events)
        {
            _coach?.See(e);
            _sound.See(e);
            _hud.Alerts.See(e);
            switch (e)
            {
                case CommandRejected r: _state.Say($"Can't: {r.Reason}"); break;
                case ShotFired shot: _state.Shots.Add((shot, 0)); break;
                case ConsecrationChanged:
                    _terrainView!.RepaintHoly();
                    _minimap.Repaint();
                    break;
                case WaveLanded w: _state.Say(w.Final ? "The Convergence is here." : $"Wave {w.Number} has arrived"); break;
                case DemonBurst d: _state.Bursts.Add((d, 0)); break;
                case DemonSpat sp: _state.Spits.Add((sp, 0)); break;
                case DemonHowled h: _state.Howls.Add((h, 0)); break;
                case OutcomeChanged o:
                    _state.Say(o.Outcome == Outcome.Lost ? $"The Keep has fallen on day {_world.Day}." : "Victory.");
                    if (_world.Scenario is { } done && Campaign.Default.Contains(done)) CampaignProgress.Record(Campaign.Default.Id, done.Id, o.Outcome == Outcome.Won, _world.Day);
                    break;
                case CorruptionTook c: _state.Say($"The horde is corrupted: {c.Name}"); break;
                case ScenarioMessage m when m.Text.Length > 0:
                    _hud.Voice.Say(Campaign.Default.Speaker(m.Speaker), m.Text);
                    break;
                case TreeFelled f:
                    _terrainView!.PaintCell(f.X, f.Y);
                    _minimapStale = true;
                    break;
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

    /// <summary>The pause menu, while it's open: the sim is stopped and the game takes no input but Esc.</summary>
    PauseMenu? _pauseMenu;
    bool _pausedBeforeMenu;

    void OpenPauseMenu()
    {
        if (_pauseMenu != null || _world.Outcome != Outcome.Running) return;
        _pausedBeforeMenu = _paused;
        _paused = true;
        DisarmAttackMove();
        _state.Armed = null;
        _pauseMenu = new PauseMenu
        {
            Resume = ClosePauseMenu, QuitToMenu = NewRun, Controls = () => _hud.ToggleHelp(),
            SaveSlot = SaveTo,
            LoadSlot = slot => { ClosePauseMenu(); LoadFrom(slot); },
        };
        AddChild(_pauseMenu);
    }

    void ClosePauseMenu()
    {
        if (_pauseMenu == null) return;
        _pauseMenu.QueueFree();
        _pauseMenu = null;
        _paused = _pausedBeforeMenu;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_started) return;
        if (_pauseMenu != null)
        {
            if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape or Key.F10 }) ClosePauseMenu();
            GetViewport().SetInputAsHandled();
            return;
        }
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed && _state.AttackMoveArmed)
                {
                    if (_state.SelectedUnits.Count > 0)
                        Send(new OrderUnits(_state.SelectedUnits.ToArray(), _state.PatrolArmed ? OrderKind.Patrol : OrderKind.AttackMove, _state.HoveredTile.X, _state.HoveredTile.Y));
                    if (!mb.ShiftPressed) DisarmAttackMove(); // shift-click to give several in a row
                    _swallowRelease = true;
                    break;
                }
                if (!mb.Pressed && _swallowRelease)
                {
                    _swallowRelease = false;
                    break;
                }
                if (mb.Pressed)
                {
                    _state.DragStart = GetGlobalMousePosition();
                    _state.DragStartTile = _state.HoveredTile;
                    if (_state.Armed is { } k && k is not (BuildingKind.Wall or BuildingKind.Gate or BuildingKind.StoneWall or BuildingKind.StoneGate))
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
                if (_state.AttackMoveArmed) DisarmAttackMove();
                else if (_state.Armed != null) _state.Armed = null;
                else if (_state.SelectedUnits.Count == 0 && _state.SelectedBuilding is { } rb && _world.BuildingById(rb) is { Def.Trains.Length: > 0 })
                    Send(new SetRally(rb, _state.HoveredTile.X, _state.HoveredTile.Y));
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

        foreach (var (kind, hotkey, _) in Palette.BuildBar)
        {
            if (key.Keycode != hotkey) continue;
            _state.Armed = _state.Armed == kind ? null : kind;
            return;
        }

        var selected = _state.SelectedBuilding is { } sid ? _world.BuildingById(sid) : null;
        switch (key.Keycode)
        {
            case Key.Escape when _state.AttackMoveArmed:
                DisarmAttackMove();
                break;
            case Key.Z when _state.SelectedUnits.Count > 0:
                _state.AttackMoveArmed = true;
                _state.PatrolArmed = true;
                _state.Armed = null;
                Input.SetDefaultCursorShape(Input.CursorShape.Cross);
                break;
            case Key.A when _state.SelectedUnits.Count > 0 && !key.CtrlPressed && !key.AltPressed:
                _state.PatrolArmed = false;
                _state.AttackMoveArmed = true;
                _state.Armed = null;
                Input.SetDefaultCursorShape(Input.CursorShape.Cross);
                break;
            case Key.Escape when _bot == null && _state.Armed == null && _state.SelectedUnits.Count == 0 && _state.SelectedBuilding == null:
            case Key.F10:
                OpenPauseMenu();
                break;
            case Key.Escape:
                if (_bot != null)
                {
                    _bot = null;
                    _state.Say("You have the colony.");
                }
                _state.Armed = null;
                _state.SelectedUnits.Clear();
                _state.SelectedBuilding = null;
                break;
            case Key.Space: _paused = !_paused; break;
            case Key.A when key.CtrlPressed:
                // Every soldier.
                _state.SelectedUnits.Clear();
                foreach (var u in _world.Units) _state.SelectedUnits.Add(u.Id);
                _state.SelectedBuilding = null;
                break;
            case Key.Tab:
                _speed = (_speed + 1) % Speeds.Length;
                Engine.TimeScale = Speeds[_speed];
                _state.Say($"Speed {Speeds[_speed]}x");
                break;
            case Key.F1: _hud.ToggleHelp(); break;
            case Key.F3: _hud.ShowDebug = !_hud.ShowDebug; break;
            case Key.Backspace or Key.Home:
                // Home: back to the Keep.
                if (_world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Keep) is { } home) _camera.Position = Iso.P(home.CentreX, home.CentreY);
                break;
            case Key.F4:
                _state.ShowNoise = !_state.ShowNoise;
                _state.Say(_state.ShowNoise ? "Noise view: orange where the colony is loud; red wakes sleeping packs" : "Noise view off");
                break;
            case Key.F5: QuickSave(); break;
            case Key.F9: QuickLoad(); break;
            case Key.X or Key.Delete when selected != null:
                Send(new Demolish(selected.Id));
                break;
            case Key.Q or Key.E or Key.R or Key.T or Key.Y or Key.F or Key.V when selected is { Def.Trains.Length: > 0 }:
                int i = Array.IndexOf(TrainKeys, key.Keycode);
                if (i < selected.Def.Trains.Length) Send(new TrainUnit(selected.Id, selected.Def.Trains[i]));
                break;
            case Key.H when _state.SelectedUnits.Count > 0:
                Send(new OrderUnits(_state.SelectedUnits.ToArray(), OrderKind.Hold, 0, 0));
                break;
            case Key.S when _state.SelectedUnits.Count > 0 && key.ShiftPressed:
                Send(new OrderUnits(_state.SelectedUnits.ToArray(), OrderKind.Idle, 0, 0));
                break;
            case Key.F6:
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

    /// <summary>Slot 0 is the quicksave (F5/F9); 1-3 are the pause menu's named slots.</summary>
    public static string SlotPath(int slot) => slot == 0 ? SavePath : ProjectSettings.GlobalizePath($"user://slot{slot}.hwsave");

    /// <summary>What a slot holds, in a line: written beside the save so the menu needn't load it to say.</summary>
    public static string? SlotSummary(int slot)
    {
        string path = SlotPath(slot);
        if (!System.IO.File.Exists(path)) return null;
        string about = System.IO.File.Exists(path + ".txt") ? System.IO.File.ReadAllText(path + ".txt") : "a saved run";
        return $"{about}  ({System.IO.File.GetLastWriteTime(path):ddd HH:mm})";
    }

    /// <summary>The most recently written of all the saves, for the main menu's Continue.</summary>
    public static int? NewestSlot() => Enumerable.Range(0, 4).Where(s => System.IO.File.Exists(SlotPath(s)))
        .OrderByDescending(s => System.IO.File.GetLastWriteTime(SlotPath(s))).Select(s => (int?)s).FirstOrDefault();

    void QuickSave() => SaveTo(0);
    void QuickLoad() => LoadFrom(0);

    int _continueSlot;
    void ContinueLoad() => LoadFrom(_continueSlot);

    void SaveTo(int slot)
    {
        _world.FlushCommands();
        System.IO.File.WriteAllBytes(SlotPath(slot), _world.Save());
        string mode = _world.Scenario?.Name ?? (_world.Survival is { Endless: true } ? "Endless" : "Survival");
        System.IO.File.WriteAllText(SlotPath(slot) + ".txt", $"{mode} · {_world.Map} · {_world.Rules.Difficulty} · day {_world.Day}");
        _state.Say($"Saved (day {_world.Day})");
    }

    void LoadFrom(int slot)
    {
        string path = SlotPath(slot);
        if (!System.IO.File.Exists(path))
        {
            _state.Say(slot == 0 ? "No quicksave yet: F5 to save" : "That slot is empty");
            return;
        }
        try
        {
            var loaded = World.Load(System.IO.File.ReadAllBytes(path), _baseRules);
            _world = loaded;
            TheMusic.World = _world;
            BuildViews();
            // The horde layer is sized to the map, which a save may change.
            _horde.QueueFree();
            _horde = new HordeRenderer(_world.Terrain.Width) { ZIndex = 1 };
            AddChild(_horde);
            MoveChild(_horde, -1);
            BuildHud();
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
    int _lastClickUnit;
    ulong _lastClickAt;

    void FinishSelection(bool additive)
    {
        if (_state.DragStart is not { } start) return;
        var end = GetGlobalMousePosition();
        if (!additive) _state.SelectedUnits.Clear();

        // Soldiers are picked by their figures on screen (feet to head), not by the ground under the cursor.
        static Vector2 Body(Unit u) => Iso.P(u.X, u.Y) - new Vector2(0, Art.UnitSize / 2);
        if (start.DistanceTo(end) < 6)
        {
            var unit = _world.Units.Where(u => Body(u).DistanceTo(end) < 16).OrderBy(u => Body(u).DistanceTo(end)).FirstOrDefault();
            if (unit != null)
            {
                // A second click on the same soldier soon after: every soldier of that kind on screen.
                bool twice = _lastClickUnit == unit.Id && Time.GetTicksMsec() - _lastClickAt < 400;
                _lastClickUnit = unit.Id;
                _lastClickAt = Time.GetTicksMsec();
                if (twice)
                {
                    var view = new Rect2(_camera.GlobalPosition - GetViewportRect().Size / _camera.Zoom / 2, GetViewportRect().Size / _camera.Zoom);
                    foreach (var u in _world.Units)
                        if (u.Kind == unit.Kind && view.HasPoint(Body(u))) _state.SelectedUnits.Add(u.Id);
                }
                _state.SelectedUnits.Add(unit.Id);
                _state.SelectedBuilding = null;
                return;
            }
            int id = _world.BuildingIdAt(_state.HoveredTile.X, _state.HoveredTile.Y);
            _state.SelectedBuilding = id == 0 ? null : id;
            return;
        }

        var box = new Rect2(start, end - start).Abs();
        foreach (var u in _world.Units)
            if (box.HasPoint(Body(u))) _state.SelectedUnits.Add(u.Id);
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
            $"fps {Engine.GetFramesPerSecond():0}  sim {_simMsShown:0.00} ms   ·   F1 controls · Esc menu · Space pause · Tab speed · F5 save · F9 load";
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
        int c = MapSize / 2;
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
        if (_pauseMenu != null) return;
        if (Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Alt)) return;
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if ((Input.IsKeyPressed(Key.S) && !Input.IsKeyPressed(Key.Shift)) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        // A is attack-move while soldiers are selected; it only pans when none are.
        if ((Input.IsKeyPressed(Key.A) && _state.SelectedUnits.Count == 0) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        // At the edge of the window, when edge scrolling is on and the window has the mouse.
        if (EdgeScroll && DisplayServer.WindowIsFocused())
        {
            var mouse = GetViewport().GetMousePosition();
            var size = GetViewport().GetVisibleRect().Size;
            const float edge = 8;
            if (mouse.X >= 0 && mouse.Y >= 0 && mouse.X <= size.X && mouse.Y <= size.Y)
            {
                if (mouse.X < edge) dir.X -= 1;
                if (mouse.X > size.X - edge) dir.X += 1;
                if (mouse.Y < edge) dir.Y -= 1;
                if (mouse.Y > size.Y - edge) dir.Y += 1;
            }
        }
        if (dir.LengthSquared() > 1) dir = dir.Normalized();
        // Real time, not game time, so panning feels the same when paused or sped up.
        float real = (float)(delta / Math.Max(0.01, Engine.TimeScale));
        _camera.Position += dir * 900 * real / _camera.Zoom.X;
    }

    void ZoomBy(float factor)
    {
        float z = Mathf.Clamp(_camera.Zoom.X * factor, 0.2f, 6f);
        _camera.Zoom = new Vector2(z, z);
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
