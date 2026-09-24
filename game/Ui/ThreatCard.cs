using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Top right: the survival clock made visible. The day, the next wave with
/// its size, sides and countdown (red once it's sighted), a timeline of the
/// run with every wave on it and the Convergence at the end, the Hellgates,
/// and in endless the corruptions.
/// </summary>
public partial class ThreatCard : PanelContainer
{
    public World World = null!;

    Label _day = null!, _next = null!, _detail = null!, _corrupt = null!, _goals = null!;
    Timeline _line = null!;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UiKit.Box(UiKit.Panel, UiKit.Rim, 6, 8));
        CustomMinimumSize = new Vector2(380, 0);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        AddChild(box);
        var head = new HBoxContainer();
        _day = UiKit.Label("", 15);
        _next = UiKit.Label("", 15, UiKit.Threat);
        head.AddChild(_day);
        head.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        head.AddChild(_next);
        box.AddChild(head);
        _line = new Timeline { World = World, CustomMinimumSize = new Vector2(0, 16) };
        box.AddChild(_line);
        _detail = UiKit.Label("", 13, UiKit.Muted);
        box.AddChild(_detail);
        _goals = UiKit.Label("", 13, UiKit.Gold);
        box.AddChild(_goals);
        _corrupt = UiKit.Label("", 13, new Color(0.85f, 0.55f, 0.95f));
        _corrupt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_corrupt);
    }

    public override void _Process(double delta)
    {
        var s = World.Survival;
        Visible = s != null;
        if (s == null) return;
        string level = World.Rules.Difficulty == Difficulty.Normal ? "" : $"  ·  {World.Rules.Difficulty}";
        _day.Text = (s.Endless ? $"Day {World.Day}  ·  endless" : $"Day {Math.Min(World.Day, s.Rules.Days)} of {s.Rules.Days}") + level;

        var next = s.Next;
        double tps = Balance.TickHz;
        if (next == null)
        {
            _next.Text = "";
            _detail.Text = "Every wave has come.";
        }
        else if (next.Announced)
        {
            string what = next.Final ? "THE CONVERGENCE" : next.Surge ? $"Surge {next.Number}" : $"Wave {next.Number}";
            _next.Text = $"{what} in {UiKit.Clock((next.LandsAtTick - World.Tick) / tps)}";
            _next.AddThemeColorOverride("font_color", UiKit.Threat);
            _detail.Text = $"{next.Size} from the {string.Join(" and ", next.Sides.Select(UiKit.SideOnScreen))}";
        }
        else
        {
            _next.Text = $"{(next.Final ? "Convergence" : $"Wave {next.Number}")} on day {s.DayAt(next.LandsAtTick) - 1}";
            _next.AddThemeColorOverride("font_color", UiKit.Muted);
            _detail.Text = $"About {next.Size}, sighted in {UiKit.Clock((next.AnnounceTick(s.Rules) - World.Tick) / tps)}";
        }
        if (World.Gates.Count > 0) _detail.Text += $"  ·  Hellgates {World.Gates.Count(g => g.Alive)}/{World.Gates.Count}";

        string corrupt = "";
        if (s.Endless)
        {
            if (s.PendingCorruption is { } p) corrupt = $"Corruption in {UiKit.Clock((s.NextCorruptionTick - World.Tick) / tps)}: {World.Rules.Corruption(p).Name}\n";
            if (s.Corruptions.Count > 0) corrupt += "The horde: " + string.Join(", ", s.Corruptions.Select(id => World.Rules.Corruption(id).Name));
        }
        // A mission's goals, ticked off as they're met.
        _goals.Visible = World.Scenario != null;
        if (World.Scenario is { } m)
            _goals.Text = m.Name + ":  " + string.Join("   ", World.Goals.Select((g, i) => $"{(World.GoalsDone[i] ? "[x]" : "[ ]")} {g.Describe()}"));
        _corrupt.Text = corrupt.TrimEnd();
        _corrupt.Visible = corrupt.Length > 0;
        _line.QueueRedraw();
    }

    /// <summary>The run as a strip: time so far filled, every wave a tick, surges taller, the Convergence a block at the end.</summary>
    sealed partial class Timeline : Control
    {
        public World World = null!;

        public override void _Draw()
        {
            var s = World.Survival;
            if (s == null) return;
            float w = Size.X, h = Size.Y;
            int tpd = s.TicksPerDay;
            // Survival: the whole run. Endless: a window from two days back to twelve ahead.
            double from = s.Endless ? Math.Max(0, World.Tick - 2 * tpd) : 0;
            double to = s.Endless ? from + 14 * tpd : (s.Rules.Days + 1) * tpd;
            float X(double tick) => (float)((tick - from) / (to - from)) * w;
            DrawRect(new Rect2(0, 3, w, h - 6), new Color(0.24f, 0.23f, 0.21f));
            DrawRect(new Rect2(0, 3, Math.Clamp(X(World.Tick), 0, w), h - 6), new Color(0.45f, 0.43f, 0.4f));
            foreach (var wave in s.Waves)
            {
                if (wave.LandsAtTick < from || wave.LandsAtTick > to) continue;
                float x = X(wave.LandsAtTick);
                var colour = wave.Landed ? new Color(0.55f, 0.25f, 0.24f) : wave.Announced ? UiKit.Threat : new Color(0.8f, 0.42f, 0.4f);
                if (wave.Final) DrawRect(new Rect2(x - 3, 0, 8, h), new Color(0.65f, 0.12f, 0.14f));
                else DrawRect(new Rect2(x - 1, wave.Surge ? 0 : 2, 3, wave.Surge ? h : h - 4), colour);
            }
            float now = Math.Clamp(X(World.Tick), 0, w);
            DrawRect(new Rect2(now - 1, 0, 2, h), UiKit.Text);
        }
    }
}
