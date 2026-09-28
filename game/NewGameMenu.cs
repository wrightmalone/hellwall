using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>What a new run is: every choice the menu offers, and what the command line can set instead.</summary>
/// <summary>How a run begins. Relics: the campaign relics taken into a mission (ids, "+" when hallowed).</summary>
public sealed record GameSetup(uint Seed, MapKind Map, Difficulty Difficulty, bool Endless, ScenarioDef? Mission = null, string[]? Relics = null);

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
    LineEdit _seed = null!;
    Label _about = null!;

    static readonly Dictionary<MapKind, string> MapAbout = new()
    {
        [MapKind.Plains] = "Open ground, scattered lakes, rock and woods.",
        [MapKind.Lakes] = "The land runs between lakes: chokepoints to hold, and little room to build.",
        [MapKind.Highlands] = "Rock ridges and the passes between them. Stone is plentiful; space isn't.",
        [MapKind.Wildwood] = "Deep forest. Wood everywhere, little open ground.",
        [MapKind.Causeway] = "A river down the middle, your town on the causeway. Waves from north and south, down both banks: four gaps to hold.",
        [MapKind.TwoFronts] = "Waves from east and west only. The rich north is thick with sleeping demons: clear it if you dare.",
        [MapKind.Crossing] = "A river to the east with one bridge. Everything comes over it.",
        [MapKind.Gorge] = "Rock all round but one winding canyon from the north: the horde comes down it strung out.",
        [MapKind.Hellwall] = "An old rampart across the north with three breaches, and the Hellgates beyond it.",
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
    /// <summary>Open What's new at once (screenshots).</summary>
    public bool ShowNews;
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

    /// <summary>Load a save slot (the menu's Load game page).</summary>
    public Action<int>? LoadSlot;

    Control _panel = null!;
    readonly Dictionary<string, Control> _pages = new();

    public override void _Ready()
    {
        // A light wash over the town playing behind the menu (Main's backdrop game), darker down the left where the menu sits.
        var backdrop = new ColorRect { Color = new Color(0.06f, 0.04f, 0.05f, 0.25f), MouseFilter = Control.MouseFilterEnum.Ignore };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);
        var shade = new ColorRect { Color = new Color(0.05f, 0.035f, 0.04f, 0.78f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        shade.CustomMinimumSize = new Vector2(620, 0);
        AddChild(shade);

        // Scrolls when the page is taller than the window (a large interface size on a small screen).
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        scroll.CustomMinimumSize = new Vector2(620, 0);
        AddChild(scroll);
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right" }) margin.AddThemeConstantOverride($"margin_{side}", 48);
        margin.AddThemeConstantOverride("margin_top", 56);
        margin.AddThemeConstantOverride("margin_bottom", 32);
        scroll.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);

        var title = new Label { Text = "HELLWALL" };
        title.AddThemeFontSizeOverride("font_size", 56);
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.25f));
        column.AddChild(title);
        var version = new Label { Text = $"playtest build {ProjectSettings.GetSetting("application/config/version", "dev")}" };
        version.AddThemeColorOverride("font_color", new Color(0.6f, 0.55f, 0.5f));
        column.AddChild(version);
        // The last session died without closing: say so once, and where the report is, so it can be sent.
        if (Diagnostics.LastCrashReport is { } report)
        {
            var crashed = new VBoxContainer();
            crashed.AddThemeConstantOverride("separation", 4);
            var note = UiKit.Label($"Hellwall closed unexpectedly last time. A report was saved, crashes/{System.IO.Path.GetFileName(report)}: send it along with what you were doing.", 13, new Color(1, 0.6f, 0.5f));
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart; // wraps to the column, never widens it
            crashed.AddChild(note);
            var open = UiKit.TextButton("Open the folder", 13);
            open.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            open.Pressed += Diagnostics.OpenFolder;
            crashed.AddChild(open);
            column.AddChild(crashed);
        }
        _panel = new VBoxContainer();
        column.AddChild(_panel);

        _pages["home"] = HomePage();
        _pages["skirmish"] = SkirmishPage();
        _pages["load"] = LoadPage();
        _pages["settings"] = SettingsPage();
        _pages["extras"] = ExtrasPage();
        foreach (var page in _pages.Values) { page.Visible = false; _panel.AddChild(page); }
        Show("home");
        if (ShowNews) CallDeferred(nameof(OpenNews));
    }

    void Show(string page)
    {
        foreach (var (name, control) in _pages) control.Visible = name == page;
    }

    static VBoxContainer Page(int separation = 8)
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", separation);
        return page;
    }

    Button MenuButton(VBoxContainer page, string text, Action act, int size = 22)
    {
        var b = UiKit.TextButton(text, size);
        b.Alignment = HorizontalAlignment.Left;
        b.CustomMinimumSize = new Vector2(320, 0);
        b.Pressed += act;
        page.AddChild(b);
        return b;
    }

    void Back(VBoxContainer page)
    {
        page.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        MenuButton(page, "Back", () => Show("home"), 16);
    }

    // --- the front page: a short list ---

    Control HomePage()
    {
        var page = Page(10);
        page.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });
        if (Continue != null && Saved is { } saved)
        {
            var resume = MenuButton(page, "Continue", () => { Continue(); QueueFree(); });
            resume.TooltipText = saved;
            var what = UiKit.Label(saved, 13, UiKit.Muted);
            page.AddChild(what);
        }
        MenuButton(page, "Campaign", () => { OpenCampaign(); QueueFree(); });
        MenuButton(page, "Skirmish", () => Show("skirmish"));
        MenuButton(page, "Load game", () => Show("load"));
        MenuButton(page, "Settings", () => Show("settings"));
        MenuButton(page, "Extras", () => Show("extras"));
        MenuButton(page, "Quit", () => GetTree().Quit());
        return page;
    }

    // --- skirmish: the weekly challenge, then a run of your own ---

    Control SkirmishPage()
    {
        var box = Page();
        box.AddChild(UiKit.Label("Skirmish", 22, UiKit.Gold));
        var weekly = Weekly();
        var weeklyButton = UiKit.TextButton($"{weekly.Name}: {weekly.Map}, seed {weekly.Seed}", 15);
        weeklyButton.TooltipText = "The same map and seed for everyone this week, at Normal. Your best score for it is kept.";
        weeklyButton.Pressed += () => { Start(new GameSetup(weekly.Seed, weekly.Map, weekly.Difficulty, false, weekly)); QueueFree(); };
        box.AddChild(weeklyButton);

        _mode = Options(box, "Mode", ["Survival: the Convergence at the end", "Endless: until the Keep falls"], Initial.Endless ? 1 : 0);
        _difficulty = Options(box, "Difficulty", Enum.GetNames<Difficulty>(), (int)Initial.Difficulty);
        _handMade.AddRange(MapFiles.All());
        _map = Options(box, "Map", [.. Enum.GetNames<MapKind>(), .. _handMade.Select(m => m.IsMission ? $"Mission: {m.Name}" : $"Hand-made: {m.Name}")], (int)Initial.Map);

        var seedRow = Row(box, "Seed");
        _seed = new LineEdit { Text = Initial.Seed.ToString(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        seedRow.AddChild(_seed);
        var random = new Button { Text = "Random" };
        random.Pressed += () => _seed.Text = ((uint)GD.Randi() % 100000).ToString();
        seedRow.AddChild(random);

        _about = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(500, 60) };
        _about.AddThemeColorOverride("font_color", new Color(0.8f, 0.78f, 0.72f));
        box.AddChild(_about);

        // The rest of a skirmish's settings: left at their defaults, it's the game as designed.
        box.AddChild(UiKit.Label("More settings", 15, UiKit.Gold));
        _more = new VBoxContainer();
        _more.AddThemeConstantOverride("separation", 4);
        box.AddChild(_more);
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
        foreach (var o in new[] { _difficulty, _map, _mode, _days }) o.ItemSelected += _ => Describe();
        Remembered(true);
        Describe();

        var begin = new Button { Text = "Begin" };
        begin.AddThemeFontSizeOverride("font_size", 22);
        begin.Pressed += Begin;
        box.AddChild(begin);
        Back(box);
        return box;
    }

    // --- load game: every slot, the autosaves too ---

    Control LoadPage()
    {
        var page = Page();
        page.AddChild(UiKit.Label("Load game", 22, UiKit.Gold));
        bool any = false;
        foreach (int slot in new[] { 0, 1, 2, 3 }.Concat(Main.AutosaveSlots))
        {
            if (Main.SlotSummary(slot) is not { } what) continue;
            any = true;
            string name = slot == 0 ? "Quicksave" : slot <= 3 ? $"Slot {slot}" : "Autosave";
            string when = System.IO.File.GetLastWriteTime(Main.SlotPath(slot)).ToString("ddd d MMM, HH:mm");
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var label = UiKit.Label($"{name}: {what}\n{when}", 13);
            label.CustomMinimumSize = new Vector2(380, 0);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            row.AddChild(label);
            int n = slot;
            var load = UiKit.TextButton("Load", 14);
            load.Pressed += () => { LoadSlot?.Invoke(n); QueueFree(); };
            row.AddChild(load);
            page.AddChild(row);
        }
        if (!any) page.AddChild(UiKit.Label("No saves yet. F5 saves in a game, and it autosaves every five minutes.", 14, UiKit.Muted));
        Back(page);
        return page;
    }

    // --- settings: everything the pause menu has, and the rest ---

    Control SettingsPage()
    {
        var side = Page(6);
        side.AddChild(UiKit.Label("Settings", 22, UiKit.Gold));
        side.AddChild(UiKit.Label("Master volume", 13));
        var volume = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = Sound.Volume, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(360, 0) };
        volume.ValueChanged += v => Settings.Set("volume", (float)v);
        side.AddChild(volume);
        side.AddChild(Display.MusicRow());
        side.AddChild(Display.UiScaleRow());
        side.AddChild(Display.MinimapRow());
        var fullscreen = new CheckButton { Text = "Fullscreen", ButtonPressed = Display.Fullscreen };
        fullscreen.Toggled += on => Display.SetFullscreen(on);
        side.AddChild(fullscreen);
        side.AddChild(PauseMenu.Toggle("Scroll at the screen edges", "edge_scroll", true));
        side.AddChild(PauseMenu.Toggle("Keep the mouse inside the window", "confine_mouse", true));
        side.AddChild(PauseMenu.Toggle("Screen shake", "screen_shake", true));
        side.AddChild(PauseMenu.Toggle("Pause when a building is possessed", "pause_on_possession", false));
        side.AddChild(PauseMenu.Toggle("Autosave every 5 minutes (the last five kept)", "autosave", true));
        var folder = UiKit.TextButton("Open the game's folder (saves, logs, crash reports)", 13);
        folder.Pressed += Diagnostics.OpenFolder;
        side.AddChild(folder);
        side.AddChild(PauseMenu.Toggle("Hints for a first run", "hints", true));
        Back(side);
        return side;
    }

    // --- extras ---

    Control ExtrasPage()
    {
        var page = Page(10);
        page.AddChild(UiKit.Label("Extras", 22, UiKit.Gold));
        if (OpenEditor != null) MenuButton(page, "Map editor", () => { OpenEditor(); QueueFree(); }, 18);
        MenuButton(page, "What's new in this build", () => Dialog("What's new", WhatsNew), 18);
        MenuButton(page, "Credits", ShowCredits, 18);
        Back(page);
        return page;
    }

    void Describe()
    {
        var difficulty = (Difficulty)_difficulty.Selected;
        string mode = _mode.Selected == 1
            ? "Endless: no Convergence and no victory. Every few days the horde takes a corruption. Your score is the day you fall."
            : $"Survive {DayChoices[_days?.Selected ?? 2]} days of waves, then the Convergence from every side.";
        var picked = _map.Selected < MapKinds ? null : _handMade[_map.Selected - MapKinds];
        string map = picked == null ? MapAbout[(MapKind)_map.Selected]
            : picked.IsMission ? $"A mission, played as its maker set it ({picked.Difficulty}; the settings here don't apply).\n{picked.Briefing}\nGoals: {string.Join("; ", picked.Goals.Select(g => g.Describe()))}"
            : picked.Briefing is { Length: > 0 } b ? b : "A hand-made map.";
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
        }
        else
        {
            Settings.Set("sk_fog", _fog.ButtonPressed);
        }
    }

    void Begin()
    {
        Remembered(false);
        uint seed = uint.TryParse(_seed.Text.Trim(), out var s) ? s : (uint)GD.Randi();
        var difficulty = (Difficulty)_difficulty.Selected;
        bool endless = _mode.Selected == 1;
        var handMade = _map.Selected >= MapKinds ? _handMade[_map.Selected - MapKinds] : null;
        // A hand-made mission plays as its maker set it: its own difficulty, days, goals and events.
        if (handMade is { IsMission: true })
        {
            Start(new GameSetup(handMade.Seed, handMade.Map, handMade.Difficulty, false, handMade with { Id = "map-" + handMade.Id }));
            QueueFree();
            return;
        }
        var kind = handMade?.Map ?? (MapKind)_map.Selected;
        bool plain = handMade == null && _size.Selected == 1 && _days.Selected == 2 && _waves.Selected == 1 && _gates.Selected == 2
            && _wilds.Selected == 1 && _strays.Selected == 0 && _ruinsOption.Selected == 1 && _start.Selected == 1 && _fog.ButtonPressed;
        if (plain)
        {
            Start(new GameSetup(seed, kind, difficulty, endless));
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
        };
        Start(new GameSetup(seed, kind, difficulty, endless, skirmish));
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
        "  Kay Lousberg (kaylousberg.itch.io): KayKit Medieval Hexagon Pack (the buildings),\n" +
        "    KayKit Dungeon Pack (the gold, food and sanctity icons)\n\n" +
        "Sound and music: synthesized in code, placeholders.\n\n" +
        "Built with Godot (godotengine.org) and .NET.";

    /// <summary>For playtesters: what changed since the last build they had. Kept short; DECISIONS.md has the why.</summary>
    const string WhatsNew =
        "New keys: the command card is a grid, and each key presses the cell in its place (Q W E R T / A S D F G / Z X C V B).\n" +
        "  Build: the top row picks a kind (Q Town, W Works...), the rows below which one (a House is Q then A).\n" +
        "  Soldiers: A attack-move, S stop, D hold, F patrol. 1-9: control groups (Ctrl sets, Shift adds, twice to go there).\n" +
        "  The camera: arrow keys, the screen's edges, or drag with the middle button.\n" +
        "Forests are walls: the horde goes round them, and breaks through trees no sooner than a wall.\n" +
        "The Convergence comes mostly from one side, told ten minutes ahead. Waves come from at most two sides.\n" +
        "Miners wear rock and iron away; a worked-out Quarry or Mine says so. A marker follows each wave in.\n" +
        "Farmers sow, water and reap their fields.\n" +
        "Fog of war: the map is dark until you've seen it, and you build only on explored ground.\n" +
        "Sleeping demons stand where they'll wake; hover a crowd to see how many. Ruins guarded by Thralls hold loot.\n" +
        "Spitters (green) spit over the walls at towers and soldiers. Kill them on the way in.\n" +
        "Silver lies only near the map's edge: a Silver Mine (Works) pays for Exorcists (trained at a Barracks).\n" +
        "Soldiers rank up with kills. Buildings mend themselves when left alone, for a fee.\n" +
        "Upgrade Houses to Cottages and Manors; raise the Keep itself (select it). Stone gates after Masonry.\n" +
        "Patron saints offer a blessing at 40, 90 and 160 colonists. Fishery (Town) for food from water.\n" +
        "Ctrl+A: every soldier. Home: back to the Keep. F4: noise view.\n" +
        "Esc: the pause menu, with save slots, volume, music, fullscreen and interface size.\n" +
        "Skirmish settings, a weekly challenge, a map editor, a ninth mission, music, and an end-of-run chart.";

    void ShowCredits() => Dialog("Credits", CreditsText);
    void OpenNews() => Dialog("What's new", WhatsNew);

    void Dialog(string title, string text)
    {
        var dialog = new AcceptDialog { Title = title, DialogText = text, OkButtonText = "Close", MinSize = new Vector2I(720, 0) };
        dialog.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
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
