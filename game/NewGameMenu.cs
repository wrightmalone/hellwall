using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>What a new run is: every choice the menu offers, and what the command line can set instead.</summary>
public sealed record GameSetup(uint Seed, MapKind Map, Difficulty Difficulty, bool Endless);

/// <summary>
/// The screen before a run: survival or endless, difficulty, kind of map and
/// seed. Placeholder styling, like the rest of the UI until the vertical slice.
/// </summary>
public partial class NewGameMenu : CanvasLayer
{
    public Action<GameSetup> Start = null!;
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
    };

    static readonly Dictionary<Difficulty, string> DifficultyAbout = new()
    {
        [Difficulty.Easy] = "Smaller waves and packs, more to start with.",
        [Difficulty.Normal] = "The game as designed.",
        [Difficulty.Hard] = "A fifth more demons in every wave and pack; a Convergence a third larger.",
        [Difficulty.Nightmare] = "Half again as many demons, nearly twice the Convergence, less to start with.",
    };

    public override void _Ready()
    {
        var backdrop = new ColorRect { Color = new Color(0.06f, 0.04f, 0.05f, 0.94f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(centre);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
        box.AddThemeConstantOverride("separation", 10);
        centre.AddChild(box);

        var title = new Label { Text = "HELLWALL", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 48);
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.55f, 0.25f));
        box.AddChild(title);

        var version = new Label { Text = $"playtest build {ProjectSettings.GetSetting("application/config/version", "dev")}", HorizontalAlignment = HorizontalAlignment.Center };
        version.AddThemeColorOverride("font_color", new Color(0.6f, 0.55f, 0.5f));
        box.AddChild(version);

        _mode = Options(box, "Mode", ["Survival: 60 days, then the Convergence", "Endless: until the Keep falls"], Initial.Endless ? 1 : 0);
        _difficulty = Options(box, "Difficulty", Enum.GetNames<Difficulty>(), (int)Initial.Difficulty);
        _map = Options(box, "Map", Enum.GetNames<MapKind>(), (int)Initial.Map);

        var seedRow = Row(box, "Seed");
        _seed = new LineEdit { Text = Initial.Seed.ToString(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        seedRow.AddChild(_seed);
        var random = new Button { Text = "Random" };
        random.Pressed += () => _seed.Text = ((uint)GD.Randi() % 100000).ToString();
        seedRow.AddChild(random);

        _about = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(460, 60) };
        _about.AddThemeColorOverride("font_color", new Color(0.8f, 0.78f, 0.72f));
        box.AddChild(_about);
        _difficulty.ItemSelected += _ => Describe();
        _map.ItemSelected += _ => Describe();
        _mode.ItemSelected += _ => Describe();
        Describe();

        var volumeRow = Row(box, "Volume");
        var volume = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = Sound.Volume, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        volume.ValueChanged += v => Settings.Set("volume", (float)v);
        volumeRow.AddChild(volume);

        var edge = new CheckBox { Text = "Scroll at the screen edges", ButtonPressed = Main.EdgeScroll };
        edge.Toggled += on => Settings.Set("edge_scroll", on);
        box.AddChild(edge);
        var confine = new CheckBox { Text = "Keep the mouse inside the window", ButtonPressed = Main.ConfineMouse };
        confine.Toggled += on => Settings.Set("confine_mouse", on);
        box.AddChild(confine);

        var hints = new CheckBox { Text = "Hints for a first run", ButtonPressed = Coach.Enabled };
        hints.Toggled += on => Settings.Set("hints", on);
        box.AddChild(hints);

        var start = new Button { Text = "Begin" };
        start.AddThemeFontSizeOverride("font_size", 22);
        start.Pressed += Begin;
        box.AddChild(start);
        start.GrabFocus();
    }

    void Describe()
    {
        var map = (MapKind)_map.Selected;
        var difficulty = (Difficulty)_difficulty.Selected;
        string mode = _mode.Selected == 1
            ? "Endless: no Convergence and no victory. Every few days the horde takes a corruption. Your score is the day you fall."
            : "Survive sixty days of waves, then the Convergence from every side.";
        _about.Text = $"{mode}\n{MapAbout[map]}\n{DifficultyAbout[difficulty]}";
    }

    void Begin()
    {
        uint seed = uint.TryParse(_seed.Text.Trim(), out var s) ? s : (uint)GD.Randi();
        Start(new GameSetup(seed, (MapKind)_map.Selected, (Difficulty)_difficulty.Selected, _mode.Selected == 1));
        QueueFree();
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
