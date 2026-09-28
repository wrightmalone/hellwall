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
///   --arm=Kind --hover=dx,dy  arm a building with the cursor pinned to a tile (from the centre), for placement screenshots
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
    /// <summary>Barracks train keys, in the order of its Trains list.</summary>
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
        SaveFolder.Migrate(); // before anything reads a setting or a save (a first launch after 0.29 moves the old folder across)
        Diagnostics.Start(GetTree());
        if (SaveFolder.Migrated is { } moved) Diagnostics.Note(moved);
        _options = ParseUserArgs();
        if (_options.GetValueOrDefault("selftest") == "migrate") { SaveFolder.SelfTest(); GetTree().Quit(); return; }
        if (_options.GetValueOrDefault("selftest") == "steam") { Steam.SelfTest(); GetTree().Quit(); return; }
        // Steam, if the game was launched through it (or --steam-test, as Valve's test app): nothing happens otherwise.
        Steam.Start(GetTree(), _options.ContainsKey("steam-test"));
        Diagnostics.Note($"steam: {Steam.Status}");
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
        if (_pendingStart != null)
        {
            // Chosen from the menu while the backdrop game was playing: a fresh scene, then this.
            var chosen = _pendingStart;
            _pendingStart = null;
            Begin(chosen);
            if (_pendingContinue is { } slot) { _pendingContinue = null; _continueSlot = slot; CallDeferred(nameof(ContinueLoad)); }
        }
        else if (_editorNext)
        {
            _editorNext = false;
            OpenEditor();
        }
        else if (_again != null)
        {
            var run = _again;
            _again = null;
            Begin(run);
        }
        else if (_retry != null)
        {
            var again = _retry;
            _retry = null;
            Begin(new GameSetup(again.Seed, again.Map, again.Difficulty, false, again, _retryRelics)); // the same relics as the first try
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
            // --editor --screenshot=path [--mission-demo]: a picture of the editor (with its mission panel filled in).
            else if (o.TryGetValue("screenshot", out var editorShot)) GetChildren().OfType<MapEditor>().Last().Screenshot(editorShot, o.ContainsKey("mission-demo"));
        }
        else if ((flagged || DisplayServer.GetName() == "headless") && !o.ContainsKey("menu") && !_menuNext) Begin(setup);
        else ShowMenu(setup);
    }

    void ShowMenu(GameSetup setup)
    {
        Diagnostics.Screen = "main menu";
        Diagnostics.Describe = null;
        // A town at war behind the menu (as in Factorio): the bot playing, quietly, the camera drifting round it.
        if (!_started && (DisplayServer.GetName() != "headless" || _options.ContainsKey("backdrop")) && !_options.ContainsKey("no-backdrop")) StartBackdrop();
        AddChild(new NewGameMenu
        {
            Initial = setup, Start = Play, OpenCampaign = OpenCampaign, OpenEditor = () => { if (_backdrop) { _editorNext = true; GetTree().ReloadCurrentScene(); } else OpenEditor(); },
            ShowNews = _options.ContainsKey("whatsnew"),
            Saved = NewestSlot() is { } newest ? SlotSummary(newest) : null,
            Continue = () =>
            {
                _pendingContinue = NewestSlot() ?? 0;
                Play(new GameSetup(11, MapKind.Plains, Difficulty.Normal, false));
            },
            LoadSlot = slot =>
            {
                _pendingContinue = slot;
                Play(new GameSetup(11, MapKind.Plains, Difficulty.Normal, false));
            },
        });
    }

    /// <summary>Start a run chosen from a menu: with the backdrop playing, in a fresh scene; otherwise here and now.</summary>
    void Play(GameSetup setup)
    {
        if (!_backdrop)
        {
            if (_pendingContinue is { } slot) { _pendingContinue = null; _continueSlot = slot; Begin(setup); CallDeferred(nameof(ContinueLoad)); }
            else Begin(setup);
            return;
        }
        _pendingStart = setup;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetTree().ReloadCurrentScene();
    }

    static GameSetup? _pendingStart, _again;
    static int? _pendingContinue;
    static bool _editorNext;
    /// <summary>The game behind the main menu: the bot's, not the player's; no HUD, no input, hushed.</summary>
    bool _backdrop;
    GameSetup? _setup;

    void StartBackdrop()
    {
        _backdrop = true;
        var day = DateTime.Now;
        uint seed = (uint)(day.DayOfYear * 7919 + day.Hour * 131);
        var maps = new[] { MapKind.Plains, MapKind.Lakes, MapKind.Wildwood, MapKind.Causeway, MapKind.Highlands };
        _scenes ??= LoadSceneList();
        Begin(new GameSetup(seed, maps[seed % maps.Length], Difficulty.Normal, false));
        // The made scenes if there are any (they need no fast-forward); the live run Begin made if not.
        if (_scenes.Count > 0) NextScene();
    }

    void OpenEditor()
    {
        Diagnostics.Screen = "map editor";
        AddChild(new MapEditor
        {
            Back = () => ShowMenu(new GameSetup(11, MapKind.Plains, Difficulty.Normal, false)),
            Play = map => Play(new GameSetup(map.Seed, map.Map, map.IsMission ? map.Difficulty : Difficulty.Normal, false, map with { Id = "map-" + map.Id })),
        });
    }

    void OpenCampaign()
    {
        Diagnostics.Screen = "campaign map";
        CampaignMap? map = null;
        map = new CampaignMap
        {
            Begin = (mission, relics) => { map!.QueueFree(); Play(new GameSetup(mission.Seed, mission.Map, mission.Difficulty, false, mission, relics)); },
            Back = () => { map!.QueueFree(); ShowMenu(new GameSetup(11, MapKind.Plains, Difficulty.Normal, false)); },
        };
        AddChild(map);
    }

    void Begin(GameSetup setup)
    {
        _setup = setup;
        Diagnostics.Screen = $"game: {setup.Mission?.Name ?? "skirmish"} on {setup.Map}, {setup.Difficulty}, seed {setup.Seed}";
        Diagnostics.Describe = () => _world == null ? "" : $"day {_world.Day}, tick {_world.Tick}, {_world.Outcome}{(_paused ? " paused" : "")}, demons {_world.Horde.Count}, soldiers {_world.Units.Count}, buildings {_world.Buildings.Count}, speed {Speeds[_speed]}x";
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
        // Living woods and miners are how the game is: forest is a wall and rock runs out. --no-woods and --no-mining are for A/B tests only.
        bool woods = !options.ContainsKey("no-woods");
        if (woods != rules.Woods.Blocks) rules = rules.WithWoods(w => w with { Blocks = woods });
        if (options.ContainsKey("no-mining")) rules = rules.WithMining(m => m with { Enabled = false });
        if (options.ContainsKey("reveal") || _backdrop) rules = rules.WithFog(f => f with { Enabled = false });
        if (options.ContainsKey("patrons-now")) rules = rules.WithSurvival(s => s with { PatronMilestones = [1, .. s.PatronMilestones] }); // screenshots of the picker
        // A survival run takes its packs from rules.json (wilds).
        _world = setup.Mission is { } mission && !scripted
            ? World.Create(mission.Options(rules, setup.Relics))
            : World.Create(new WorldOptions(seed, MapSize, 0, rules, Survival: !scripted, Difficulty: setup.Difficulty, Endless: setup.Endless && !scripted, Map: setup.Map));

        BuildViews();
        _horde = new HordeRenderer(_world.Terrain.Width) { ZIndex = 1 };
        AddChild(_horde);

        _sound = new Sound();
        AddChild(_sound);
        _tint = new CanvasModulate { Color = Colors.White };
        AddChild(_tint);
        TheMusic.World = _world;

        _camera = new Camera2D { Position = Iso.P(_world.Home.X, _world.Home.Y), Zoom = Vector2.One * 1.1f / Display.UiScale };
        if (options.GetValueOrDefault("look") == "ruin" && _world.Ruins.Count > 0) _camera.Position = Iso.P(_world.Ruins[0].X, _world.Ruins[0].Y);
        else if (options.TryGetValue("look", out var look) && look.Split(',') is [var lx, var ly, ..] parts)
        {
            _camera.Position = Iso.P(float.Parse(lx), float.Parse(ly));
            if (parts.Length > 2) _camera.Zoom = Vector2.One * float.Parse(parts[2]);
        }
        AddChild(_camera);

        _withCoach = !scripted && !_backdrop && !options.ContainsKey("autoplay") && !options.ContainsKey("selftest") && Coach.Enabled;
        BuildHud();
        // A mission opens with its narrator restating the briefing.
        if (_world.Scenario is { } opening && !scripted) _hud.Voice.Say(Campaign.Default.Speaker(""), opening.Briefing);

        if (_benchSeconds > 0) StartBenchAssault();
        if (_demoSeconds > 0) StartDemo();

        if (options.ContainsKey("autoplay") || _backdrop) _bot = new Hellwall.Headless.Bot(_world, Hellwall.Headless.Bot.Style.Full);
        if (_backdrop)
        {
            // Into the thick of it: a town already up, a few waves in.
            for (int t = 0; t < (_scenes is { Count: > 0 } ? 0 : 520 * Balance.TickHz) && _world.Outcome == Outcome.Running; t++)
            {
                if (_world.Tick % Balance.TickHz == 0) _bot!.Act();
                _world.Step();
                _state.Farmers.Step(_world, (float)TickSeconds);
                _bot!.See(_world.DrainEvents());
            }
            _terrainView!.Repaint();
            _hud.Visible = false;
            _sound.Loudness = 0.25f;
            _camera.Zoom = Vector2.One * 0.8f / Display.UiScale;
        }

        // --build=Kind: put one on the first place it can go near the Keep, before any fast-forward (screenshots of what it does).
        if (options.TryGetValue("build", out var buildKind) && Enum.TryParse<BuildingKind>(buildKind, true, out var toBuild))
        {
            int c = _world.Terrain.Width / 2;
            bool placed = false;
            for (int r = 4; r < 40 && !placed; r++)
                for (int dy = -r; dy <= r && !placed; dy++)
                    for (int dx = -r; dx <= r && !placed; dx++)
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && _world.CheckPlacement(toBuild, c + dx, c + dy) == null && (_world.Def(toBuild).Produces == null || _world.EstimateGathering(toBuild, c + dx, c + dy) > 0.02))
                        {
                            _world.Enqueue(new PlaceBuilding(toBuild, c + dx, c + dy));
                            _world.FlushCommands();
                            placed = true;
                        }
        }

        // Fast-forward before the first frame: for screenshots and for jumping into the middle of a run.
        if (options.TryGetValue("skip", out var skip))
        {
            for (int t = 0; t < double.Parse(skip) * Balance.TickHz && _world.Outcome == Outcome.Running; t++)
            {
                if (_bot != null && _world.Tick % Balance.TickHz == 0) _bot.Act();
                _world.Step();
                _state.Farmers.Step(_world, (float)TickSeconds);
                _state.Fishers.Step(_world, (float)TickSeconds);
                _state.Hunters.Step(_world, (float)TickSeconds);
                _state.Commuters.Step(_world, (float)TickSeconds);
                var events = _world.DrainEvents();
                _bot?.See(events);
                foreach (var e in events)
                    if (e is TreeFelled f) _terrainView!.PaintCell(f.X, f.Y);
                    else if (e is DepositWorn worn) _terrainView!.PaintCell(worn.X, worn.Y);
            }
            HandleEvents();
        }

        // Select a building of a kind before the first frame, for screenshots of its inspector.
        if (options.TryGetValue("inspect", out var inspect) && Enum.TryParse<BuildingKind>(inspect, true, out var kindToInspect)
            && _world.Buildings.FirstOrDefault(b => b.Kind == kindToInspect) is { } inspected)
        {
            _state.SelectedBuilding = inspected.Id;
            _camera.Position = Iso.P(inspected.CentreX, inspected.CentreY); // and look at it
        }

        // scripts/verify.sh looks for this line: the engine banner alone doesn't prove the C# scene ran.
        if (options.GetValueOrDefault("selftest") == "controls") CallDeferred(nameof(SelfTestControls));
        if (options.ContainsKey("pausemenu")) CallDeferred(nameof(OpenPauseMenu)); // for screenshots of it
        if (options.ContainsKey("noise")) _state.ShowNoise = true;
        ForestWatch.Log = DisplayServer.GetName() == "headless";
        if (options.TryGetValue("select-group", out var groupKind) && Enum.TryParse<BuildingKind>(groupKind, true, out var gk)) _pendingGroup = gk;
        if (options.TryGetValue("arm", out var armKind) && Enum.TryParse<BuildingKind>(armKind, true, out var toArm)) _state.Armed = toArm; // screenshots of the build grid
        // --hover=dx,dy: the cursor's tile, from the map's centre, for screenshots of a placement (the real mouse is left alone).
        if (options.TryGetValue("hover", out var hover) && hover.Split(',') is [var hx, var hy])
            _hoverOverride = (_world.Terrain.Width / 2 + int.Parse(hx), _world.Terrain.Height / 2 + int.Parse(hy));
        if (options.ContainsKey("select-keep")) _state.SelectedBuilding = _world.Buildings.First(b => b.Kind == BuildingKind.Keep).Id; // screenshots of the inspector
        GD.Print($"hellwall: world ready seed={_world.Seed} map={_world.Map} difficulty={_world.Rules.Difficulty} endless={_world.Survival?.Endless ?? false} hash={StateHash.Hex(_world)}");
        _started = true;
    }

    void Send(Command command) => _world.Enqueue(command);

    bool _withCoach;
    (int X, int Y)? _hoverOverride;

    /// <summary>The screen-space UI for the current world: at the start, and after a quickload.</summary>
    void BuildHud()
    {
        _hud?.QueueFree();
        _minimap = new Minimap { World = _world, Camera = _camera, MoveCamera = p => _camera.Position = p, IgnoreInput = () => Dragging, Columns = _state.Columns };
        _hud = new Hud
        {
            World = _world, State = _state, Send = Send, Minimap = _minimap, NewRun = NewRun,
            BackToCampaign = BackToCampaign, Retry = RetryMission, Again = PlayAgain,
            LoadLatest = NewestSlot() is { } newest ? () => LoadFrom(newest) : null,
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
        // Control groups: Ctrl+1 sets, Esc drops the selection, 1 brings it back.
        async Task Tap(Key k, bool ctrl = false)
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = k, Pressed = true, CtrlPressed = ctrl });
            Input.ParseInputEvent(new InputEventKey { Keycode = k, Pressed = false, CtrlPressed = ctrl });
            Input.FlushBufferedEvents();
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        int soldiers = _state.SelectedUnits.Count;
        await Tap(Key.Key1, ctrl: true);
        _state.SelectedUnits.Clear();
        await Tap(Key.Key1);
        bool grouped = soldiers > 0 && _state.SelectedUnits.Count == soldiers;
        // A building in a group: the Keep on 2.
        var keep = _world.Buildings.First(b => b.Kind == BuildingKind.Keep);
        _state.SelectedUnits.Clear();
        _state.SelectedBuilding = keep.Id;
        await Tap(Key.Key2, ctrl: true);
        _state.SelectedBuilding = null;
        await Tap(Key.Key2);
        grouped &= _state.SelectedBuilding == keep.Id && _state.SelectedUnits.Count == 0;
        await Tap(Key.Key1);
        grouped &= _state.SelectedUnits.Count == soldiers && _state.SelectedBuilding == null;
        await Escape();
        bool cleared = _state.SelectedUnits.Count == 0 && _pauseMenu == null;
        // The build grid: Q (Town) then A arms a House; W (Works) then D, its third, a Barracks; Esc disarms.
        await Tap(Key.Q);
        await Tap(Key.A);
        bool house = _state.Armed == BuildingKind.House;
        await Tap(Key.W);
        await Tap(Key.D);
        bool barracks = _state.Armed == BuildingKind.Barracks;
        await Escape();
        bool built = house && barracks && _state.Armed == null;
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
        bool pass = armed && ordered && kept && grouped && cleared && built && menu && resumed && reloaded;
        GD.Print(pass ? "hellwall-selftest: PASS controls" : $"hellwall-selftest: FAIL controls (units {_world.Units.Count}, armed {armed}, ordered {ordered}, selection kept and disarmed {kept}, group 1 {grouped}, esc cleared {cleared}, build grid {built}, menu paused {menu}, resumed {resumed}, save and load {reloaded})");
        GetTree().Quit();
    }

    public static bool EdgeScroll => Settings.Get("edge_scroll", true);
    public static bool ConfineMouse => Settings.Get("confine_mouse", true);

    /// <summary>This tick's dead, as marks on the ground where you can see them.</summary>
    void CollectMarks()
    {
        var dead = _world.RecentDeaths;
        for (int i = 0; i < dead.Count && _state.Marks.Count < ClientState.MaxMarks; i++)
        {
            var d = dead[i];
            if (!_world.Vision.IsVisible(d.X, d.Y)) continue;
            float size = d.Kind is DemonKind.Brute or DemonKind.Broodmother or DemonKind.Bloater ? 0.55f : 0.3f;
            _state.Marks.Add((d.X, d.Y, size, 0));
        }
    }

    void DisarmAttackMove()
    {
        _state.AttackMoveArmed = false;
        _state.PatrolArmed = false;
        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
    }

    /// <summary>Keep the cursor inside the window while playing (so the edges scroll), and free it on menus and the end screen.</summary>
    void UpdateMouseMode()
    {
        var want = _started && !_backdrop && ConfineMouse && _pauseMenu == null && _world.Outcome == Outcome.Running ? Input.MouseModeEnum.Confined : Input.MouseModeEnum.Visible;
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
        _retryRelics = _world.Relics;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        DisarmAttackMove();
        GetTree().ReloadCurrentScene();
    }

    static ScenarioDef? _retry;
    static string[]? _retryRelics;

    /// <summary>The same run from the start: the setup it began with, in a fresh scene.</summary>
    void PlayAgain()
    {
        _again = _setup;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        DisarmAttackMove();
        GetTree().ReloadCurrentScene();
    }

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
                CollectMarks();
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
        for (int i = _state.OrderPings.Count - 1; i >= 0; i--)
        {
            var p = _state.OrderPings[i];
            if (p.Age + delta > 0.5) _state.OrderPings.RemoveAt(i);
            else _state.OrderPings[i] = (p.X, p.Y, p.Attack, p.Age + delta);
        }
        for (int i = _state.Marks.Count - 1; i >= 0; i--)
        {
            var m = _state.Marks[i];
            if (m.Age + delta > ClientState.MarkLife) _state.Marks.RemoveAt(i);
            else _state.Marks[i] = (m.X, m.Y, m.Size, m.Age + delta);
        }
        Age(_state.Howls, delta, 1.2);
        Age(_state.Log, delta, 8);

        _state.Alpha = _paused ? 1f : (float)Math.Clamp(_accumulator / TickSeconds, 0, 1);
        _state.MouseWorld = GetGlobalMousePosition();
        _state.HoveredTile = _hoverOverride ?? Iso.TileAt(_state.MouseWorld);

        _horde.Sync(_world, _state.Alpha, delta);
        if (!_paused) { _state.Farmers.Step(_world, (float)delta); _state.Fishers.Step(_world, (float)delta); _state.Hunters.Step(_world, (float)delta); _state.Commuters.Step(_world, (float)delta); }
        if (_pendingGroup is { } pending) { _pendingGroup = null; SelectGroupForScreenshot(pending); }
        _view.Refresh();
        PanCamera(delta);
        StepShake(delta);
        StepAutosave();
        if (_backdrop) StepBackdrop(delta);
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

    /// <summary>Living woods: warns before the woodsmen cut a way in for the horde.</summary>
    readonly ForestWatch _forest = new();
    readonly BreachWatch _breach = new();
    double _forestClockLast;
    double _forestClock, _columnClock;

    void HandleEvents()
    {
        _forestClock += GetProcessDeltaTime();
        if (_forestClock > 0.5)
        {
            _forestClockLast = _forestClock;
            _forestClock = 0;
            _forest.Step(_world, _hud.Alerts);
            _state.EndangeredTrees = _forest.Endangered;
            _breach.Step(_world, _hud.Alerts, _forestClockLast);
        }
        _columnClock += GetProcessDeltaTime();
        if (_columnClock > 0.2)
        {
            _columnClock = 0;
            HordeColumns.Measure(_world, _state.Columns);
            int cells = (_world.Terrain.Width / HordeColumns.Cell) * (_world.Terrain.Height / HordeColumns.Cell);
            if (_state.Density.Length != cells) _state.Density = new int[cells];
            HordeColumns.Density(_world, _state.Density);
        }
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
                case WaveLanded w:
                    _state.Say(w.Final ? "The Convergence is here." : $"Wave {w.Number} has arrived");
                    if (w.Final && _world.Rules.Wilds.RiseWithConvergence && _world.Sleeping > 0)
                    {
                        _state.Say($"The wilds rise with it: {_world.Sleeping:N0} demons wake across the map.");
                        _hud.Alerts.Push("rising", $"THE WILDS RISE: {_world.Sleeping:N0} demons wake across the map", AlertFeed.Red, null, 12);
                    }
                    if (w.Final)
                    {
                        // The Convergence: the roar (Sound), the screen, the ground shaking.
                        _hud.Vignette.Flash(new Color(1, 0.12f, 0.05f), 6);
                        _hud.Banner("THE CONVERGENCE", 5);
                        Shake(0.8f);
                    }
                    break;
                case DemonBurst d: _state.Bursts.Add((d, 0)); break;
                case DemonSpat sp: _state.Spits.Add((sp, 0)); break;
                case DemonHowled h: _state.Howls.Add((h, 0)); break;
                case OutcomeChanged { Outcome: Outcome.Running }:
                    _state.Say("The field is yours: no more waves. Clear it at your leisure.");
                    break;
                case OutcomeChanged o:
                    _state.Say(o.Outcome == Outcome.Lost ? $"The Keep has fallen on day {_world.Day}." : "Victory.");
                    // Played on after a win: the win stands, whatever happens after.
                    if (!_world.Aftermath && _world.Scenario is { } done && Campaign.Default.Contains(done))
                    {
                        CampaignProgress.Record(Campaign.Default.Id, done.Id, o.Outcome == Outcome.Won, _world.Day);
                        // Won with its bonus goal met: the mission's relic is hallowed from now on.
                        if (o.Outcome == Outcome.Won && _world.BonusDone && Campaign.Default.RelicFrom(done.Id) is { } relic) CampaignProgress.Hallow(Campaign.Default.Id, relic.Id);
                    }
                    break;
                case CorruptionTook c: _state.Say($"The horde is corrupted: {c.Name}"); break;
                case ScenarioMessage m when m.Text.Length > 0:
                    _hud.Voice.Say(Campaign.Default.Speaker(m.Speaker), m.Text);
                    break;
                case BuildingDestroyed { Kind: BuildingKind.Wall or BuildingKind.StoneWall or BuildingKind.Gate or BuildingKind.StoneGate } fell when OnScreen(fell.X, fell.Y):
                    Shake(0.18f);
                    break;
                case BuildingPossessed p when _world.BuildingById(p.BuildingId) is { } turned:
                    // The breach no one should miss: the screen, the minimap, and (if asked) a pause.
                    _hud.Vignette.Flash(new Color(1, 0.08f, 0.05f), 3.5);
                    _minimap.Alarm(new Vector2(turned.CentreX, turned.CentreY));
                    if (BreachWatch.PauseOnPossession && _bot == null && !_paused)
                    {
                        _paused = true;
                        _state.Say($"Paused: a {turned.Kind} is possessed. Space to go on");
                    }
                    break;
                case TreeFelled f:
                    _forest.Felled(f, _world.Terrain.Width, _hud.Alerts);
                    _terrainView!.PaintCell(f.X, f.Y);
                    _minimapStale = true;
                    break;
                case DepositWorn d:
                    _forest.Worn(d, _world.Terrain.Width, _hud.Alerts);
                    _terrainView!.PaintCell(d.X, d.Y);
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
            Resume = ClosePauseMenu, QuitToMenu = NewRun,
            ContinueCampaign = _world.Aftermath && _world.Scenario is { } s && Campaign.Default.Contains(s) ? BackToCampaign : null,
            Controls = () => _hud.ToggleHelp(), BestiaryText = Blurbs.Bestiary(_world),
            SaveSlot = slot => SaveTo(slot),
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

    /// <summary>
    /// A box select or a wall line ends wherever the button comes up, even over the minimap or a
    /// panel: taken here, before the interface sees it, or a drag that ends on the HUD is lost.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (_backdrop) return; // the menu's backdrop takes no orders
        // Middle-drag pans the camera; like a box select, it's followed over the interface until it's let go.
        if (_middlePan)
        {
            if (@event is InputEventMouseMotion mm) { _camera.Position -= mm.Relative / _camera.Zoom; GetViewport().SetInputAsHandled(); return; }
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: false }) { _middlePan = false; GetViewport().SetInputAsHandled(); return; }
        }
        if (!_started || _pauseMenu != null || _state.DragStart == null) return;
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb) return;
        if (_swallowRelease) { _swallowRelease = false; return; }
        if (_state.Armed is { } k) PlaceLine(k);
        else FinishSelection(mb.ShiftPressed);
        _state.DragStart = null;
        GetViewport().SetInputAsHandled();
    }

    /// <summary>True while a box select or a wall line is being dragged: the minimap stays out of it.</summary>
    public bool Dragging => _state.DragStart != null || _middlePan;

    bool _middlePan;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_started || _backdrop) return;
        if (_pauseMenu != null)
        {
            if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape or Key.F10 }) ClosePauseMenu();
            GetViewport().SetInputAsHandled();
            return;
        }
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: true }:
                _middlePan = true;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed && _state.AttackMoveArmed)
                {
                    if (_state.SelectedUnits.Count > 0)
                    {
                        // Shift: queued after what they're doing, and the order stays armed for the next point.
                        Send(new OrderUnits(_state.SelectedUnits.ToArray(), _state.PatrolArmed ? OrderKind.Patrol : OrderKind.AttackMove, _state.HoveredTile.X, _state.HoveredTile.Y, Queue: mb.ShiftPressed));
                        _state.OrderPings.Add((_state.HoveredTile.X + 0.5f, _state.HoveredTile.Y + 0.5f, true, 0));
                    }
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
                {
                    // Right-click moves, and nothing stops a move: that's how you pull a squad out. Shift queues it after the one they're on.
                    Send(new OrderUnits(_state.SelectedUnits.ToArray(), OrderKind.Move, _state.HoveredTile.X, _state.HoveredTile.Y, Queue: mb.ShiftPressed));
                    _state.OrderPings.Add((_state.HoveredTile.X + 0.5f, _state.HoveredTile.Y + 0.5f, false, 0));
                }
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
        // Control groups on the number keys: Ctrl sets, Shift adds, alone selects, twice quickly goes there.
        int group = key.Keycode is >= Key.Key1 and <= Key.Key9 ? (int)(key.Keycode - Key.Key1) + 1 : key.Keycode == Key.Key0 ? 10 : 0;
        if (group > 0)
        {
            ControlGroup(group, key.CtrlPressed || key.MetaPressed, key.ShiftPressed);
            return;
        }

        // The command card's grid: the key presses whatever sits in its place (HotkeyGrid).
        if (!key.CtrlPressed && !key.AltPressed && !key.MetaPressed && _hud.PressCard(key.Keycode)) return;

        var selected = _state.SelectedBuilding is { } sid ? _world.BuildingById(sid) : null;
        switch (key.Keycode)
        {
            case Key.Escape when _state.AttackMoveArmed:
                DisarmAttackMove();
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
            // Keyboard zoom, as well as the wheel.
            case Key.Equal or Key.KpAdd or Key.Pageup: ZoomBy(1.15f); break;
            case Key.Minus or Key.KpSubtract or Key.Pagedown: ZoomBy(1 / 1.15f); break;
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
            case Key.Delete when selected != null:
                Send(new Demolish(selected.Id));
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

    int _lastGroup;
    ulong _lastGroupAt;

    /// <summary>
    /// A control group holds soldiers, buildings or both (ids are unique across the two). Recalled,
    /// it selects its soldiers if it has any left, and its buildings otherwise (several at once, as
    /// a double-click does), since a selection is one or the other.
    /// </summary>
    void ControlGroup(int group, bool set, bool add)
    {
        if (set || add)
        {
            var now = _state.SelectedUnits.Concat(_state.SelectedBuildings).ToArray();
            var ids = add ? _state.Groups.GetValueOrDefault(group, []).Concat(now).Distinct().ToArray() : now;
            _state.Groups[group] = ids;
            int men = ids.Count(id => _world.UnitById(id) != null), works = ids.Length - men;
            string Of(int n, string one) => $"{n} {one}{(n == 1 ? "" : "s")}";
            _state.Say($"Group {group % 10}: " + string.Join(" and ", new[] { men > 0 ? Of(men, "soldier") : "", works > 0 ? Of(works, "building") : "" }.Where(p => p.Length > 0).DefaultIfEmpty("empty")));
            return;
        }
        var alive = _state.Groups.GetValueOrDefault(group, []).Where(id => _world.UnitById(id) != null || _world.BuildingById(id) != null).ToArray();
        _state.Groups[group] = alive;
        if (alive.Length == 0) return;
        bool again = _lastGroup == group && Time.GetTicksMsec() - _lastGroupAt < 400;
        _lastGroup = group;
        _lastGroupAt = Time.GetTicksMsec();
        _state.Armed = null;
        var units = alive.Select(_world.UnitById).OfType<Unit>().ToList();
        var buildings = alive.Select(_world.BuildingById).OfType<Building>().ToList();
        _state.SelectedUnits.Clear();
        _state.SelectedBuilding = null;
        Vector2 centre;
        if (units.Count > 0)
        {
            foreach (var u in units) _state.SelectedUnits.Add(u.Id);
            centre = new Vector2(units.Average(u => u.X), units.Average(u => u.Y));
        }
        else
        {
            _state.SelectedBuilding = buildings[0].Id;
            if (buildings.Count > 1) foreach (var b in buildings) _state.SelectedGroup.Add(b.Id);
            centre = new Vector2(buildings.Average(b => b.CentreX), buildings.Average(b => b.CentreY));
        }
        if (again) _camera.Position = Iso.P(centre); // twice: the camera to the middle of them
    }

    /// <summary>Screen shake, as "trauma" that decays: the shake is its square, so small knocks stay small. Off in the pause menu.</summary>
    float _trauma;
    readonly Random _shakeRandom = new();
    public static bool ScreenShake => Settings.Get("screen_shake", true);

    void Shake(float amount) { if (ScreenShake && !_backdrop) _trauma = Math.Min(1, _trauma + amount); }

    void StepShake(double delta)
    {
        float real = (float)(delta / Math.Max(0.01, Engine.TimeScale));
        _trauma = Math.Max(0, _trauma - real * 0.9f);
        float s = _trauma * _trauma * 16;
        _camera.Offset = s <= 0.01f || !ScreenShake ? Vector2.Zero : new Vector2((float)(_shakeRandom.NextDouble() * 2 - 1), (float)(_shakeRandom.NextDouble() * 2 - 1)) * s / _camera.Zoom.X;
    }

    bool OnScreen(float x, float y)
    {
        var half = GetViewportRect().Size / _camera.Zoom / 2;
        var p = Iso.P(x, y);
        return MathF.Abs(p.X - _camera.Position.X) < half.X && MathF.Abs(p.Y - _camera.Position.Y) < half.Y;
    }

    /// <summary>
    /// Slot 0 is the quicksave (F5/F9); 1-3 are the pause menu's named slots (9 the self-test's); 10 is
    /// a copy of the newest autosave: all in saves/, which Steam Cloud syncs. 11-15 are the autosaves'
    /// rotation, in autosaves/, which it doesn't: five full saves a machine would eat the quota, and
    /// the newest reaches the others as slot 10 (docs/plans/steam.md).
    /// </summary>
    public static string SlotPath(int slot) => ProjectSettings.GlobalizePath(slot switch
    {
        0 => "user://saves/quicksave.hwsave",
        LatestAutosave => "user://saves/autosave-latest.hwsave",
        >= 11 => $"user://autosaves/autosave{slot - 10}.hwsave",
        _ => $"user://saves/slot{slot}.hwsave",
    });

    /// <summary>The newest autosave's copy in saves/ (synced), whichever machine made it.</summary>
    public const int LatestAutosave = 10;

    /// <summary>What a slot holds, in a line: written beside the save so the menu needn't load it to say.</summary>
    public static string? SlotSummary(int slot)
    {
        string path = SlotPath(slot);
        if (!System.IO.File.Exists(path)) return null;
        string about = System.IO.File.Exists(path + ".txt") ? System.IO.File.ReadAllText(path + ".txt") : "a saved run";
        return $"{about}  ({System.IO.File.GetLastWriteTime(path):ddd HH:mm})";
    }

    /// <summary>The most recently written of all the saves, for the main menu's Continue.</summary>
    /// <summary>Autosaves take these slots, the oldest overwritten each time: the last five, five minutes apart.</summary>
    public static readonly int[] AutosaveSlots = [11, 12, 13, 14, 15];
    public static bool Autosave => Settings.Get("autosave", true);
    const double AutosaveSeconds = 300;

    /// <summary>The newest autosave's slot, if any: one of this machine's, or the synced copy if that's newer (another machine's).</summary>
    public static int? NewestAutosave() => AutosaveSlots.Append(LatestAutosave).Where(s => System.IO.File.Exists(SlotPath(s)))
        .OrderByDescending(s => System.IO.File.GetLastWriteTime(SlotPath(s))).Select(s => (int?)s).FirstOrDefault();

    int _lastAutosaveTick;

    /// <summary>Every five minutes of play (game time: paused doesn't count), over the oldest of the five autosaves.</summary>
    void StepAutosave()
    {
        if (!Autosave || _backdrop || _bot != null || _benchSeconds > 0 || _demoSeconds > 0 || _world.Outcome != Outcome.Running) return;
        double every = _options.TryGetValue("autosave-seconds", out var a) ? double.Parse(a) : AutosaveSeconds;
        if (_world.Tick - _lastAutosaveTick < every * Balance.TickHz) return;
        _lastAutosaveTick = _world.Tick;
        int slot = AutosaveSlots.OrderBy(s => System.IO.File.Exists(SlotPath(s)) ? System.IO.File.GetLastWriteTime(SlotPath(s)) : DateTime.MinValue).First();
        SaveTo(slot, quiet: true);
        // And the copy Steam Cloud carries to the player's other machines.
        foreach (var ext in new[] { "", ".txt" }) System.IO.File.Copy(SlotPath(slot) + ext, SlotPath(LatestAutosave) + ext, overwrite: true);
        System.IO.File.SetLastWriteTime(SlotPath(LatestAutosave), System.IO.File.GetLastWriteTime(SlotPath(slot)));
        _state.Say($"Autosaved (day {_world.Day})");
    }

    public static int? NewestSlot() => Enumerable.Range(0, 4).Concat(AutosaveSlots).Append(LatestAutosave).Where(s => System.IO.File.Exists(SlotPath(s)))
        .OrderByDescending(s => System.IO.File.GetLastWriteTime(SlotPath(s))).Select(s => (int?)s).FirstOrDefault();

    void QuickSave() => SaveTo(0);
    void QuickLoad() => LoadFrom(0);

    int _continueSlot;
    void ContinueLoad() => LoadFrom(_continueSlot);

    void SaveTo(int slot, bool quiet = false)
    {
        _world.FlushCommands();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SlotPath(slot))!);
        System.IO.File.WriteAllBytes(SlotPath(slot), _world.Save());
        string mode = _world.Scenario?.Name ?? (_world.Survival is { Endless: true } ? "Endless" : "Survival");
        System.IO.File.WriteAllText(SlotPath(slot) + ".txt", $"{mode} · {_world.Map} · {_world.Rules.Difficulty} · day {_world.Day}");
        if (!quiet) _state.Say($"Saved (day {_world.Day})");
        Diagnostics.Note($"saved to slot {slot}, day {_world.Day}");
    }

    void LoadFrom(int slot)
    {
        string path = SlotPath(slot);
        Diagnostics.Note($"loading slot {slot}");
        if (!System.IO.File.Exists(path))
        {
            _state.Say(slot == 0 ? "No quicksave yet: F5 to save" : "That slot is empty");
            return;
        }
        try
        {
            var loaded = World.Load(System.IO.File.ReadAllBytes(path), _baseRules);
            ReplaceWorld(loaded);
            _state.Say($"Loaded (day {loaded.Day})");
        }
        catch (FormatException e)
        {
            _state.Say($"Can't load: {e.Message}");
        }
    }

    /// <summary>Carry on with another world (a load, the next backdrop scene): every view rebuilt round it.</summary>
    void ReplaceWorld(World loaded)
    {
        _world = loaded;
        _lastAutosaveTick = loaded.Tick; // five minutes on from here
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
    }

    // --- the scenes behind the main menu ---

    /// <summary>The backdrop's scenes (hellwall-sim menuscenes, made for each build): a save each, where to look, and how close.</summary>
    static List<(string File, Vector2 At, float Zoom, bool Fall)>? _scenes;
    /// <summary>Seconds a scene goes on after its run has ended (a fall: to see the Keep go).</summary>
    double _afterEnd;
    int _scene = -1;
    double _sceneLeft, _fadeIn = 1;
    Vector2 _sceneAt;
    static readonly Random _sceneRandom = new();

    /// <summary>The rules the scenes were made under: the defaults with fog off (as MenuScenes.BackdropRules).</summary>
    static Rules BackdropRules => Rules.Default.WithFog(f => f with { Enabled = false });

    static List<(string File, Vector2 At, float Zoom, bool Fall)> LoadSceneList()
    {
        var list = new List<(string File, Vector2 At, float Zoom, bool Fall)>();
        if (!Godot.FileAccess.FileExists("res://menu/scenes.json")) return list;
        var json = Json.ParseString(Godot.FileAccess.GetFileAsString("res://menu/scenes.json")).AsGodotDictionary();
        foreach (var entry in json["scenes"].AsGodotArray())
        {
            var e = entry.AsGodotDictionary();
            list.Add((e["file"].AsString(), new Vector2((float)e["x"].AsDouble(), (float)e["y"].AsDouble()), (float)e["zoom"].AsDouble(), e.ContainsKey("fall") && e["fall"].AsBool()));
        }
        return list;
    }

    /// <summary>Cut to another scene (never the same one twice running); one that no longer loads is dropped.</summary>
    bool NextScene()
    {
        while (_scenes is { Count: > 0 })
        {
            int pick = _scenes.Count == 1 ? 0 : (_scene + 1 + _sceneRandom.Next(_scenes.Count - 1)) % _scenes.Count;
            // --scene=name: that one first (screenshots, and checking a scene plays as meant).
            if (_scene < 0 && _options.TryGetValue("scene", out var wanted) && _scenes.FindIndex(x => x.File.StartsWith(wanted)) is >= 0 and var named) pick = named;
            var (file, at, zoom, fall) = _scenes[pick];
            try
            {
                var loaded = World.Load(Godot.FileAccess.GetFileAsBytes("res://menu/" + file), BackdropRules);
                ReplaceWorld(loaded);
                _hud.Visible = false;
                _bot = new Hellwall.Headless.Bot(_world, Hellwall.Headless.Bot.Style.Full);
                _scene = pick;
                _sceneAt = at;
                _camera.Zoom = Vector2.One * zoom / Display.UiScale;
                _camera.Position = Iso.P(at);
                // A town falling plays until it falls (and a moment after); the rest, 15 to 20 seconds.
                _sceneLeft = fall ? 45 : 15 + _sceneRandom.NextDouble() * 5;
                _afterEnd = 3;
                _fadeIn = 0;
                if (DisplayServer.GetName() == "headless") GD.Print($"hellwall: backdrop scene {file} (day {loaded.Day})");
                return true;
            }
            catch (FormatException e)
            {
                GD.PushWarning($"menu scene {file} doesn't load ({e.Message}): run hellwall-sim menuscenes");
                _scenes.RemoveAt(pick);
            }
        }
        return false;
    }

    /// <summary>Behind the menu: the scene plays on; near its end it fades, and the next one fades in.</summary>
    void StepBackdrop(double delta)
    {
        float real = (float)(delta / Math.Max(0.01, Engine.TimeScale));
        _fadeIn = Math.Min(1, _fadeIn + real * 2);
        if (_scenes is { Count: > 0 })
        {
            _sceneLeft -= real;
            if (_world.Outcome != Outcome.Running) { _afterEnd -= real; _sceneLeft = Math.Min(_sceneLeft, _afterEnd); }
            if (_sceneLeft <= 0)
            {
                if (DisplayServer.GetName() == "headless") GD.Print($"hellwall: backdrop scene ends: {_world.Outcome} on day {_world.Day}");
                if (!NextScene()) _scenes = null;
            }
            // A slow drift round what the scene is about.
            float angle = (float)(Time.GetTicksMsec() / 1000.0 * Mathf.Tau / 60);
            _camera.Position = Iso.P(_sceneAt + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 3);
        }
        float fadeOut = _scenes is { Count: > 0 } ? (float)Math.Clamp(_sceneLeft / 0.5, 0, 1) : 1;
        float light = (float)Math.Min(_fadeIn, fadeOut);
        _tint.Color = new Color(light, light, light);
    }

    void PlaceLine(BuildingKind kind)
    {
        foreach (var (x, y, turned) in _state.Ghosts(_world)) Send(new PlaceBuilding(kind, x, y, turned));
    }

    /// <summary>A click selects what's under the cursor; a drag box-selects soldiers.</summary>
    int _lastClickUnit, _lastClickBuilding;

    /// <summary>--select-group=Kind: every building of a kind selected, as a double-click would (screenshots); applied on the first frame.</summary>
    BuildingKind? _pendingGroup;

    void SelectGroupForScreenshot(BuildingKind kind)
    {
        var all = _world.Buildings.Where(b => b.Kind == kind).ToList();
        if (all.Count == 0) return;
        _state.SelectedBuilding = all[0].Id;
        foreach (var b in all) _state.SelectedGroup.Add(b.Id);
    }
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
            // A second click on the same building soon after: every building of that kind on screen (to upgrade or hold them together).
            bool again = id != 0 && _lastClickBuilding == id && Time.GetTicksMsec() - _lastClickAt < 400;
            _lastClickBuilding = id;
            _lastClickAt = Time.GetTicksMsec();
            _state.SelectedBuilding = id == 0 ? null : id;
            if (again && _world.BuildingById(id) is { } picked)
            {
                var view = new Rect2(_camera.GlobalPosition - GetViewportRect().Size / _camera.Zoom / 2, GetViewportRect().Size / _camera.Zoom);
                foreach (var b in _world.Buildings)
                    if (b.Kind == picked.Kind && view.HasPoint(Iso.P(b.CentreX, b.CentreY))) _state.SelectedGroup.Add(b.Id);
                if (_state.SelectedGroup.Count < 2) _state.SelectedGroup.Clear(); // just the one: a plain selection
            }
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
                bool gate = y == c - 8 && Math.Abs(x - c) <= 1; // one gate, three tiles wide
                if (!gate) Send(new PlaceBuilding(BuildingKind.Wall, x, y));
                else if (x == c - 1) Send(new PlaceBuilding(BuildingKind.Gate, x, y));
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
        if (_backdrop && _scenes is { Count: > 0 }) return; // StepBackdrop moves the camera
        if (_backdrop)
        {
            // A slow turn round the town, like a banner-bearer walking the walls.
            var keep = _world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Keep);
            float angle = (float)(Time.GetTicksMsec() / 1000.0 * Mathf.Tau / 150);
            var centre = keep != null ? new Vector2(keep.CentreX, keep.CentreY) : new Vector2(_world.Terrain.Width / 2f, _world.Terrain.Height / 2f);
            _camera.Position = Iso.P(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 14);
            if (_world.Outcome != Outcome.Running) { _menuNext = true; GetTree().ReloadCurrentScene(); } // its run is over: another
            return;
        }
        if (_pauseMenu != null) return;
        if (Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Alt)) return;
        // The arrows (the letters are the command card's grid), the screen's edges, and middle-drag (in _UnhandledInput).
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.Right)) dir.X += 1;
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
