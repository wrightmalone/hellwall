using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>Which missions have been won, in user://campaign.cfg: the campaign's only memory between runs.</summary>
public static class CampaignProgress
{
    const string Path = "user://campaign.cfg";

    static ConfigFile Load()
    {
        var f = new ConfigFile();
        f.Load(Path);
        return f;
    }

    public static HashSet<string> Won(string campaign) =>
        new(((string)Load().GetValue(campaign, "won", "")).Split(',', StringSplitOptions.RemoveEmptyEntries));

    public static int BestDay(string campaign, string mission) => (int)Load().GetValue(campaign, "best-" + mission, 0);

    public static void Record(string campaign, string mission, bool won, int day)
    {
        var f = Load();
        var set = new HashSet<string>(((string)f.GetValue(campaign, "won", "")).Split(',', StringSplitOptions.RemoveEmptyEntries));
        if (won) set.Add(mission);
        f.SetValue(campaign, "won", string.Join(",", set));
        if (day > (int)f.GetValue(campaign, "best-" + mission, 0)) f.SetValue(campaign, "best-" + mission, day);
        f.Save(Path);
    }
}

/// <summary>
/// The campaign as a map: each mission a marker where the campaign data puts
/// it, lines to the missions it opens, won ones gold, open ones bright,
/// locked ones grey. Picking one shows its briefing and goals; Begin starts it.
/// </summary>
public partial class CampaignMap : CanvasLayer
{
    public Action<ScenarioDef> Begin = null!;
    public Action Back = null!;

    readonly Campaign _campaign = Campaign.Default;
    HashSet<string> _won = new();
    ScenarioDef? _picked;
    Board _board = null!;
    Label _name = null!, _facts = null!, _brief = null!, _goals = null!;
    Button _begin = null!;

    public override void _Ready()
    {
        _won = CampaignProgress.Won(_campaign.Id);
        var backdrop = new ColorRect { Color = new Color(0.06f, 0.04f, 0.05f, 0.97f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 10);
        root.OffsetLeft = 24; root.OffsetRight = -24; root.OffsetTop = 18; root.OffsetBottom = -18;
        AddChild(root);

        var head = new HBoxContainer();
        var title = UiKit.Label(_campaign.Name, 30, UiKit.Gold);
        head.AddChild(title);
        head.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        head.AddChild(UiKit.Label($"{_won.Count} of {_campaign.Scenarios.Length} won", 15, UiKit.Muted));
        var back = UiKit.TextButton("Back", 14);
        back.Pressed += () => Back();
        head.AddChild(back);
        root.AddChild(head);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16);
        root.AddChild(body);

        _board = new Board { Map = this, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddChild(_board);

        var side = UiKit.PanelBox();
        side.CustomMinimumSize = new Vector2(380, 0);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        side.AddChild(box);
        _name = UiKit.Label("", 22, UiKit.Gold);
        _facts = UiKit.Label("", 13, UiKit.Muted);
        _brief = UiKit.Label("", 14);
        _brief.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _brief.CustomMinimumSize = new Vector2(350, 0);
        _goals = UiKit.Label("", 14);
        _begin = UiKit.TextButton("Begin", 18);
        _begin.Pressed += () => { if (_picked != null) Begin(_picked); };
        box.AddChild(_name);
        box.AddChild(_facts);
        box.AddChild(_brief);
        box.AddChild(UiKit.Label("Goals", 13, UiKit.Muted));
        box.AddChild(_goals);
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        box.AddChild(_begin);
        body.AddChild(side);

        // Start on the furthest mission that's open and not yet won, or the first.
        Pick(_campaign.Scenarios.LastOrDefault(s => IsOpen(s) && !_won.Contains(s.Id)) ?? _campaign.Scenarios[0]);
    }

    bool IsOpen(ScenarioDef s) => _campaign.IsOpen(s, _won);

    void Pick(ScenarioDef s)
    {
        _picked = s;
        bool open = IsOpen(s);
        _name.Text = s.Name;
        int best = CampaignProgress.BestDay(_campaign.Id, s.Id);
        _facts.Text = $"{s.Map} · {s.Difficulty} · {(s.Days > 0 ? s.Days : 60)} days" + (_won.Contains(s.Id) ? " · won" : best > 0 ? $" · best: day {best}" : "");
        _brief.Text = open ? s.Briefing : $"Win {string.Join(" and ", s.Requires.Select(r => _campaign.Find(r)!.Name))} to open this mission.";
        _goals.Text = string.Join("\n", s.Goals.Select(g => "·  " + g.Describe()));
        _begin.Disabled = !open;
        _begin.Text = _won.Contains(s.Id) ? "Play again" : "Begin";
        _board.QueueRedraw();
    }

    /// <summary>The map itself: a drawn board of mission markers, clickable.</summary>
    sealed partial class Board : Control
    {
        public CampaignMap Map = null!;
        const float R = 16;

        Vector2 At(ScenarioDef s) => new(40 + s.MapX * (Size.X - 80), 40 + s.MapY * (Size.Y - 80));

        public override void _GuiInput(InputEvent e)
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
            var hit = Map._campaign.Scenarios.FirstOrDefault(s => At(s).DistanceTo(mb.Position) < R + 6);
            if (hit != null) Map.Pick(hit);
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.2f, 0.17f, 0.12f));
            DrawRect(new Rect2(Vector2.Zero, Size), UiKit.Rim, false, 2);
            var font = ThemeDB.FallbackFont;
            foreach (var s in Map._campaign.Scenarios)
                foreach (var r in s.Requires)
                {
                    var from = Map._campaign.Find(r)!;
                    bool lit = Map._won.Contains(r);
                    DrawLine(At(from), At(s), lit ? UiKit.Gold : new Color(0.4f, 0.36f, 0.3f), lit ? 3 : 2);
                }
            foreach (var s in Map._campaign.Scenarios)
            {
                var at = At(s);
                bool won = Map._won.Contains(s.Id), open = Map.IsOpen(s), picked = Map._picked == s;
                var fill = won ? UiKit.Gold : open ? new Color(0.85f, 0.36f, 0.28f) : new Color(0.35f, 0.33f, 0.3f);
                DrawCircle(at, R + (picked ? 5 : 2), picked ? UiKit.Text : new Color(0.08f, 0.07f, 0.06f));
                DrawCircle(at, R, fill);
                if (won) DrawPolyline([at + new Vector2(-7, 0), at + new Vector2(-2, 6), at + new Vector2(8, -6)], new Color(0.15f, 0.12f, 0.05f), 3);
                var label = s.Name;
                var size = font.GetStringSize(label, fontSize: 15);
                DrawString(font, at + new Vector2(-size.X / 2, R + 22), label, fontSize: 15, modulate: open ? UiKit.Text : UiKit.Muted);
            }
        }
    }
}
