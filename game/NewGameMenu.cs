using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>What a new run is: every choice the menu offers, and what the command line can set instead.</summary>
public sealed record GameSetup(uint Seed, MapKind Map, Difficulty Difficulty, bool Endless, ScenarioDef? Mission = null, bool Woods = false);

/// <summary>
/// The screen before a run: survival or endless, difficulty, kind of map and
/// seed. Placeholder styling, like the rest of the UI until the vertical slice.
/// </summary>
public partial class NewGameMenu : CanvasLayer
{
    public Action<GameSetup> Start = null!;
    /// <summary>Open the campaign map instead.</summary>
    public Action OpenCampaign = null!;
    public GameSetup Initial = new(11, MapKind.Plains, Difficulty.Normal, false);

    OptionButton _mode = null!, _difficulty = null!, _map = null!;
    CheckButton _woods = null!;
    LineEdit _seed = null!;
    Label _about = null!;

    static readonly Dictionary<MapKind, string> MapAbout = new()
    {
        [MapKind.Plains] = "Open ground, scattered lakes, rock and woods.",
        [MapKind.Lakes] = "The land runs between lakes: chokepoints to hold, and little room to build.",
        [MapKind.Highlands] = "Rock ridges and the passes between them. Stone is plentiful; space isn't.",
        [MapKind.Wildwood] = "Deep forest. Wood everywhere, little open ground.",
    };

    static readonly Dictionary<Difficulty, string> DifficultyAbout = new()
    {
        [Difficulty.Easy] = "Smaller waves and packs, more to start with.",
        [Difficulty.Normal] = "The game as designed.",
        [Difficulty.Hard] = "A fifth more demons in every wave and pack; a Convergence a third larger.",
        [Difficulty.Nightmare] = "Half again as many demons, nearly twice the Convergence, less to start with.",
    };

    /// <summary>Open the map editor instead (null: no button).</summary>
    public Action? OpenEditor;
    /// <summary>Load the quicksave (shown when Saved is set).</summary>
    public Action? Continue;
    /// <summary>What the newest save holds, or null for none.</summary>
    public string? Saved;

    // Skirmish settings: each list's middle-ish entry is the game as designed.
    static readonly (string Label, int Size)[] Sizes = [("Small (192)", 192), ("Normal (256)", 256), ("Large (320)", 320)];
    static readonly int[] DayChoices = [30, 45, 60, 90];
    static readonly (string Label, double Scale)[] WaveChoices = [("Gentle", 0.6), ("Normal", 1), ("Heavy", 1.5), ("Brutal", 2.2)];
    static readonly int[] GateChoices = [0, 2, 4, 6];
    static readonly (string Label, double Scale)[] WildsChoices = [("Sparse", 0.5), ("Normal", 1), ("Crowded", 1.5)];
    static readonly (string Label, int Count)[] StrayChoices = [("None", 0), ("Some", 60), ("Many", 150)];
    static readonly (string Label, int Count)[] RuinChoices = [("None", 0), ("Some", 5), ("Many", 10)];
    static readonly (string Label, double Scale)[] StartChoices = [("Lean", 0.6), ("Normal", 1), ("Rich", 2)];

    OptionButton _size = null!, _days = null!, _waves = null!, _gates = null!, _wilds = null!, _strays = null!, _start = null!, _ruinsOption = null!;
    CheckButton _fog = null!;
    VBoxContainer _more = null!;
    readonly List<ScenarioDef> _handMade = new();

    public override void _Ready()
    {
        var backdrop = new ColorRect { Color = new Color(0.06f, 0.04f, 0.05f, 0.94f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);

        // Scrolls when the page is taller than the window (a large interface size on a small screen).
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(scroll);
        var centre = new CenterContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        scroll.AddChild(centre);
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 10);
        centre.AddChild(page);

        var title = new Label { Text = "HELLWALL", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 48);
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.25f));
        page.AddChild(title);
        var version = new Label { Text = $"playtest build {ProjectSettings.GetSetting("application/config/version", "dev")}", HorizontalAlignment = HorizontalAlignment.Center };
        version.AddThemeColorOverride("font_color", new Color(0.6f, 0.55f, 0.5f));
        page.AddChild(version);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 28);
        page.AddChild(columns);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
        box.AddThemeConstantOverride("separation", 8);
        columns.AddChild(box);
        var side = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        side.AddThemeConstantOverride("separation", 8);
        columns.AddChild(side);

        // --- play ---
        if (Continue != null && Saved is { } saved)
        {
            var resume = UiKit.TextButton($"Continue: {saved}", 16);
            resume.Pressed += () => { Continue(); QueueFree(); };
            box.AddChild(resume);
        }
        var weekly = Weekly();
        var campaign = UiKit.TextButton($"Campaign: {Campaign.Default.Name}", 18);
        campaign.Pressed += () => { OpenCampaign(); QueueFree(); };
        box.AddChild(campaign);
        var weeklyButton = UiKit.TextButton($"{weekly.Name}: {weekly.Map}, seed {weekly.Seed}", 15);
        weeklyButton.TooltipText = "The same map and seed for everyone this week, at Normal. Your best score for it is kept.";
        weeklyButton.Pressed += () => { Start(new GameSetup(weekly.Seed, weekly.Map, weekly.Difficulty, false, weekly)); QueueFree(); };
        box.AddChild(weeklyButton);
        if (OpenEditor != null)
        {
            var editor = UiKit.TextButton("Map editor", 15);
            editor.Pressed += () => { OpenEditor(); QueueFree(); };
            box.AddChild(editor);
        }
        box.AddChild(UiKit.Label("Skirmish", 16, UiKit.Gold));

        _mode = Options(box, "Mode", ["Survival: the Convergence at the end", "Endless: until the Keep falls"], Initial.Endless ? 1 : 0);
        _difficulty = Options(box, "Difficulty", Enum.GetNames<Difficulty>(), (int)Initial.Difficulty);
        _handMade.AddRange(MapFiles.All());
        _map = Options(box, "Map", [.. Enum.GetNames<MapKind>(), .. _handMade.Select(m => $"Hand-made: {m.Name}")], (int)Initial.Map);

        var seedRow = Row(box, "Seed");
        _seed = new LineEdit { Text = Initial.Seed.ToString(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        seedRow.AddChild(_seed);
        var random = new Button { Text = "Random" };
        random.Pressed += () => _seed.Text = ((uint)GD.Randi() % 100000).ToString();
        seedRow.AddChild(random);

        _about = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(480, 60) };
        _about.AddThemeColorOverride("font_color", new Color(0.8f, 0.78f, 0.72f));
        box.AddChild(_about);

        // The rest of a skirmish's settings, in the right-hand column: left at their defaults, it's the game as designed.
        side.AddChild(UiKit.Label("Skirmish settings", 16, UiKit.Gold));
        _more = new VBoxContainer();
        _more.AddThemeConstantOverride("separation", 4);
        side.AddChild(_more);
        _size = Options(_more, "Map size", Sizes.Select(s => s.Label).ToArray(), 1);
        _days = Options(_more, "Days", DayChoices.Select(d => $"{d} days").ToArray(), 2);
        _waves = Options(_more, "Waves", WaveChoices.Select(w => w.Label).ToArray(), 1);
        _gates = Options(_more, "Hellgates", GateChoices.Select(g => g == 0 ? "None" : g.ToString()).ToArray(), 2);
        _wilds = Options(_more, "Packs", WildsChoices.Select(w => w.Label).ToArray(), 1);
        _strays = Options(_more, "Stragglers", StrayChoices.Select(w => w.Label).ToArray(), 0);
        _ruinsOption = Options(_more, "Ruins", RuinChoices.Select(w => w.Label).ToArray(), 1);
        _start = Options(_more, "Start with", StartChoices.Select(w => w.Label).ToArray(), 1);
        _fog = new CheckButton { Text = "Fog of war", ButtonPressed = true };
        _more.AddChild(_fog);
        _woods = new CheckButton { Text = "Living woods (experimental): forest is a wall, woodsmen fell it", ButtonPressed = Initial.Woods };
        _woods.TooltipText = "No one walks through the trees. Woodcutters send out woodsmen who fell them one by one, so the forest\nshrinks and opens new ways into your town. The horde can hack through trees, slowly.";
        _more.AddChild(_woods);
        foreach (var o in new[] { _difficulty, _map, _mode, _days }) o.ItemSelected += _ => Describe();
        Remembered(true);
        Describe();

        var begin = new Button { Text = "Begin" };
        begin.AddThemeFontSizeOverride("font_size", 22);
        begin.Pressed += Begin;
        box.AddChild(begin);
        begin.GrabFocus();

        // --- options ---
        side.AddChild(new HSeparator());
        side.AddChild(UiKit.Label("Options", 16, UiKit.Gold));
        side.AddChild(UiKit.Label("Master volume", 13));
        var volume = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = Sound.Volume, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        volume.ValueChanged += v => Settings.Set("volume", (float)v);
        side.AddChild(volume);
        side.AddChild(Display.MusicRow());
        side.AddChild(Display.UiScaleRow());
        var fullscreen = new CheckButton { Text = "Fullscreen", ButtonPressed = Display.Fullscreen };
        fullscreen.Toggled += on => Display.SetFullscreen(on);
        side.AddChild(fullscreen);
        var edge = new CheckButton { Text = "Scroll at the screen edges", ButtonPressed = Main.EdgeScroll };
        edge.Toggled += on => Settings.Set("edge_scroll", on);
        side.AddChild(edge);
        var confine = new CheckButton { Text = "Keep the mouse inside the window", ButtonPressed = Main.ConfineMouse };
        confine.Toggled += on => Settings.Set("confine_mouse", on);
        side.AddChild(confine);
        var hints = new CheckButton { Text = "Hints for a first run", ButtonPressed = Coach.Enabled };
        hints.Toggled += on => Settings.Set("hints", on);
        side.AddChild(hints);
        var credits = UiKit.TextButton("Credits", 13);
        credits.Pressed += ShowCredits;
        side.AddChild(credits);
        var quit = UiKit.TextButton("Exit game", 13);
        quit.Pressed += () => GetTree().Quit();
        side.AddChild(quit);
    }

    void Describe()
    {
        var difficulty = (Difficulty)_difficulty.Selected;
        string mode = _mode.Selected == 1
            ? "Endless: no Convergence and no victory. Every few days the horde takes a corruption. Your score is the day you fall."
            : $"Survive {DayChoices[_days?.Selected ?? 2]} days of waves, then the Convergence from every side.";
        string map = _map.Selected < MapKinds ? MapAbout[(MapKind)_map.Selected] : _handMade[_map.Selected - MapKinds].Briefing is { Length: > 0 } b ? b : "A hand-made map.";
        _about.Text = $"{mode}\n{map}\n{DifficultyAbout[difficulty]}";
    }

    /// <summary>The skirmish settings last used, kept in settings.cfg: restored when the menu opens, saved when a run begins.</summary>
    void Remembered(bool restore)
    {
        var options = new (string Key, OptionButton Box)[] { ("sk_size", _size), ("sk_days", _days), ("sk_waves", _waves), ("sk_gates", _gates), ("sk_packs", _wilds), ("sk_strays", _strays), ("sk_ruins", _ruinsOption), ("sk_start", _start) };
        foreach (var (key, box) in options)
        {
            if (restore)
            {
                int i = Settings.Get(key, box.Selected);
                if (i >= 0 && i < box.ItemCount) box.Selected = i;
            }
            else Settings.Set(key, box.Selected);
        }
        if (restore)
        {
            _fog.ButtonPressed = Settings.Get("sk_fog", true);
            _woods.ButtonPressed = Initial.Woods || Settings.Get("sk_woods", false);
        }
        else
        {
            Settings.Set("sk_fog", _fog.ButtonPressed);
            Settings.Set("sk_woods", _woods.ButtonPressed);
        }
    }

    void Begin()
    {
        Remembered(false);
        uint seed = uint.TryParse(_seed.Text.Trim(), out var s) ? s : (uint)GD.Randi();
        var difficulty = (Difficulty)_difficulty.Selected;
        bool endless = _mode.Selected == 1;
        var handMade = _map.Selected >= MapKinds ? _handMade[_map.Selected - MapKinds] : null;
        var kind = handMade?.Map ?? (MapKind)_map.Selected;
        bool plain = handMade == null && _size.Selected == 1 && _days.Selected == 2 && _waves.Selected == 1 && _gates.Selected == 2
            && _wilds.Selected == 1 && _strays.Selected == 0 && _ruinsOption.Selected == 1 && _start.Selected == 1 && _fog.ButtonPressed;
        if (plain)
        {
            Start(new GameSetup(seed, kind, difficulty, endless, Woods: _woods.ButtonPressed));
            QueueFree();
            return;
        }
        // Anything off the defaults (or a hand-made map) is a skirmish: a scenario of its own, which its saves carry.
        var rules = Rules.Default;
        int size = handMade?.MapSize ?? Sizes[_size.Selected].Size;
        double area = size * size / (256.0 * 256.0);
        var skirmish = (handMade ?? new ScenarioDef()) with
        {
            Id = handMade != null ? "map-" + handMade.Id : "skirmish",
            Name = handMade?.Name ?? "Skirmish",
            Briefing = "",
            Seed = seed, Map = kind, MapSize = size, Difficulty = difficulty, Endless = endless,
            Days = DayChoices[_days.Selected],
            Waves = WaveChoices[_waves.Selected].Scale,
            Convergence = WaveChoices[_waves.Selected].Scale,
            Hellgates = GateChoices[_gates.Selected],
            // A hand-made map that wants only its own packs keeps it that way.
            Packs = handMade?.Packs == 0 ? 0 : (int)Math.Round(rules.Wilds.Packs * WildsChoices[_wilds.Selected].Scale * area),
            Strays = (int)Math.Round(StrayChoices[_strays.Selected].Count * area),
            Ruins = RuinChoices[_ruinsOption.Selected].Count,
            Start = rules.StartingResources.Scale(StartChoices[_start.Selected].Scale),
            Fog = _fog.ButtonPressed,
            LivingWoods = _woods.ButtonPressed,
        };
        Start(new GameSetup(seed, kind, difficulty, endless, skirmish, _woods.ButtonPressed));
        QueueFree();
    }

    static readonly int MapKinds = Enum.GetValues<MapKind>().Length;

    /// <summary>Who made what: the same list as CREDITS.md. Every pack is CC0; credit is a courtesy we keep.</summary>
    const string CreditsText =
        "HELLWALL: a playtest build\n\n" +
        "Art (all CC0, public domain)\n" +
        "  Kenney (kenney.nl): Tower Defense, Isometric Tiles Landscape, Graveyard Kit\n" +
        "  Quaternius (quaternius.com): RPG Characters (the soldiers), Ultimate Monsters (the demons),\n" +
        "    Stylized Nature MegaKit (trees, rocks, grass)\n" +
        "  Kay Lousberg (kaylousberg.itch.io): KayKit Dungeon Pack (the gold, food and sanctity icons)\n\n" +
        "Sound and music: synthesized in code, placeholders.\n\n" +
        "Built with Godot (godotengine.org) and .NET.";

    void ShowCredits()
    {
        var dialog = new AcceptDialog { Title = "Credits", DialogText = CreditsText, OkButtonText = "Close" };
        AddChild(dialog);
        dialog.PopupCentered();
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
    }

    /// <summary>This ISO week's challenge: a seed and a kind of map from the year and week, at Normal, as designed.</summary>
    static ScenarioDef Weekly()
    {
        var today = DateTime.UtcNow;
        int year = System.Globalization.ISOWeek.GetYear(today), week = System.Globalization.ISOWeek.GetWeekOfYear(today);
        uint h = unchecked((uint)(year * 73856093) ^ (uint)(week * 19349663));
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return new ScenarioDef
        {
            Id = $"weekly-{year}-{week:00}", Name = $"Weekly challenge {year}-W{week:00}",
            Seed = h % 100000, Map = (MapKind)(h / 100000 % (uint)MapKinds), Difficulty = Difficulty.Normal,
        };
    }

    static HBoxContainer Row(VBoxContainer box, string label)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(100, 0) });
        box.AddChild(row);
        return row;
    }

    static OptionButton Options(VBoxContainer box, string label, string[] items, int selected)
    {
        var row = Row(box, label);
        var options = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var item in items) options.AddItem(item);
        options.Selected = selected;
        row.AddChild(options);
        return options;
    }
}
