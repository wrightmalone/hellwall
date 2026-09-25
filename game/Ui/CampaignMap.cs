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
        _facts.Text = $"{s.Map} · {s.Difficulty} · {(s.Days > 0 ? s.Days : 60)} days" + (_won.Contains(s.Id) ? " · won" : best > 0 ? $" · best: day {best}" : "")
            + (Settings.Get("sk_woods", false) ? " · living woods on" : "");
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

        ImageTexture? _land;
        Vector2 _landSize;

        /// <summary>
        /// The march as an old map: land and sea, forest and hills from value noise,
        /// darkening to a red glow in the east where the Hellwall stands. Painted
        /// once per size, into a texture.
        /// </summary>
        void PaintLand()
        {
            int w = Math.Max(16, (int)Size.X), h = Math.Max(16, (int)Size.Y);
            var data = new byte[w * h * 3];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    float e = Fbm(u * 5, v * 3.2f, 1) + 0.18f - 0.35f * MathF.Max(0, 0.12f - MathF.Min(MathF.Min(u, 1 - u), MathF.Min(v, 1 - v))) * 8;
                    float m = Fbm(u * 7 + 11, v * 5 + 3, 2);
                    Color c;
                    if (e < 0.42f) c = new Color(0.17f, 0.23f, 0.28f).Lerp(new Color(0.22f, 0.3f, 0.34f), MathF.Max(0, (e - 0.3f) / 0.12f));
                    else if (e > 0.7f) c = new Color(0.42f, 0.36f, 0.28f).Lerp(new Color(0.55f, 0.5f, 0.42f), (e - 0.7f) / 0.3f);
                    else if (m > 0.56f) c = new Color(0.3f, 0.35f, 0.21f);
                    else c = new Color(0.5f, 0.43f, 0.3f).Lerp(new Color(0.58f, 0.5f, 0.35f), Fbm(u * 30, v * 30, 3));
                    // Contour lines on the hills, and a coastline.
                    if (e > 0.58f && Frac(e * 18) < 0.12f) c = c.Darkened(0.25f);
                    if (e is > 0.42f and < 0.445f) c = c.Darkened(0.35f);
                    // The east burns.
                    float hell = Mathf.Clamp((u - 0.72f) / 0.28f, 0, 1);
                    c = c.Lerp(new Color(0.35f, 0.06f, 0.05f), hell * 0.75f);
                    // Vignette, like an old sheet.
                    float d = MathF.Max(MathF.Abs(u - 0.5f), MathF.Abs(v - 0.5f)) * 2;
                    c = c.Darkened(Mathf.Clamp((d - 0.7f) / 0.3f, 0, 1) * 0.55f);
                    int o = (y * w + x) * 3;
                    data[o] = (byte)(c.R * 255); data[o + 1] = (byte)(c.G * 255); data[o + 2] = (byte)(c.B * 255);
                }
            _land = ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rgb8, data));
            _landSize = Size;
        }

        static float Frac(float x) => x - MathF.Floor(x);

        static float Fbm(float x, float y, int seed) => 0.55f * Noise(x, y, seed) + 0.3f * Noise(x * 2, y * 2, seed + 7) + 0.15f * Noise(x * 4, y * 4, seed + 13);

        static float Noise(float x, float y, int seed)
        {
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3 - 2 * tx);
            ty = ty * ty * (3 - 2 * ty);
            float a = Hash(x0, y0, seed), b = Hash(x0 + 1, y0, seed), c = Hash(x0, y0 + 1, seed), d = Hash(x0 + 1, y0 + 1, seed);
            return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) / 4294967296f;
            }
        }

        public override void _Draw()
        {
            if (_land == null || _landSize != Size) PaintLand();
            DrawTexture(_land!, Vector2.Zero);
            DrawRect(new Rect2(Vector2.Zero, Size), UiKit.Rim, false, 2);
            var font = ThemeDB.FallbackFont;
            // Roads: dashed, gold once the mission before is won.
            foreach (var s in Map._campaign.Scenarios)
                foreach (var r in s.Requires)
                {
                    var from = At(Map._campaign.Find(r)!);
                    var to = At(s);
                    bool lit = Map._won.Contains(r);
                    var colour = lit ? UiKit.Gold : new Color(0.2f, 0.16f, 0.1f, 0.9f);
                    float length = from.DistanceTo(to);
                    for (float t = 0; t < length; t += 14)
                        DrawLine(from.Lerp(to, t / length), from.Lerp(to, Math.Min(length, t + 8) / length), colour, lit ? 3 : 2);
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
                var labelAt = at + new Vector2(-size.X / 2, R + 22);
                DrawStringOutline(font, labelAt, label, fontSize: 15, size: 5, modulate: new Color(0.06f, 0.05f, 0.04f, 0.9f));
                DrawString(font, labelAt, label, fontSize: 15, modulate: open ? UiKit.Text : new Color(0.8f, 0.76f, 0.68f));
            }
        }
    }
}
