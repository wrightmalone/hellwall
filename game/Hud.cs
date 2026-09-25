using Godot;
using Hellwall.Sim;
using Resource = Hellwall.Sim.Resource;
using Side = Hellwall.Sim.Side;

namespace Hellwall.Game;

/// <summary>
/// Screen-space UI, laid out: resource bar (top), threat card (top right),
/// alerts (top left), minimap (bottom left), command card (bottom centre),
/// inspector (bottom right), plus help, wave warnings and the end of a run. Buttons only enqueue Commands or change ClientState; the
/// HUD reads sim state and never changes it.
/// </summary>
public partial class Hud : CanvasLayer
{
    public World World = null!;
    public ClientState State = null!;

    /// <summary>Set by Main: enqueue a command for the sim.</summary>
    public Action<Command> Send = null!;
    /// <summary>Set by Main: move the camera to a point in tile units.</summary>
    public Action<Vector2> JumpTo = null!;
    /// <summary>Set by Main: the speed and pause state, for the resource bar.</summary>
    public Func<string> Speed = null!;
    /// <summary>Set by Main: an order for the selected soldiers ("attack", "hold", "stop").</summary>
    public Action<string> Order = null!;

    public string DebugText = "";
    public bool ShowDebug;

    public Minimap Minimap = null!;
    /// <summary>Back to the new-game menu.</summary>
    public Action NewRun = null!;
    /// <summary>A mission ended: back to the campaign map, or play it again.</summary>
    public Action BackToCampaign = null!;
    public Action Retry = null!;

    public AlertFeed Alerts = null!;
    /// <summary>The campaign's speakers.</summary>
    public TalkingHead Voice = null!;
    ResourceBar _bar = null!;
    ThreatCard _threat = null!;
    CommandCard _card = null!;
    Inspector _inspector = null!;
    Label _debug = null!, _notices = null!, _banner = null!, _help = null!, _endText = null!;
    PanelContainer _end = null!;
    readonly Dictionary<Side, Label> _edgeWarnings = new();

    const string HelpText =
        "BUILD   pick from the build card (bottom) or 1-0 - =  M C L P  G B U; click to place; drag walls for a line; right-click or Esc to stop\n" +
        "SELECT  click a building or soldier; drag to box-select soldiers (shift adds)\n" +
        "ORDER   A then click, or right-click: attack-move · Z then click: patrol · shift+right-click move · H hold · Shift+S stop\n" +
        "BARRACKS  select it to train: Q E R T Y F V, shift-click for five; click a queued soldier to cancel; right-click the ground for a rally point\n" +
        "GROUPS  Ctrl+1-9 set · Alt+1-9 recall · Ctrl+A every soldier · double-click a soldier for all of its kind on screen\n" +
        "X / Delete  demolish (purges a possessed building) · alerts on the left: click to go there\n" +
        "Esc or F10 menu · Space pause · Tab speed · F5 save · F9 load · WASD or screen edges pan · wheel zoom · minimap click to jump · F3 debug\n" +
        "Debug: F6 noise at cursor · K wave · J 20k assault\n" +
        "F1 to close";

    public void ToggleHelp() => _help.Visible = !_help.Visible;

    public override void _Ready()
    {
        _bar = new ResourceBar { World = World, Speed = Speed };
        AddChild(_bar);
        _threat = new ThreatCard { World = World };
        AddChild(_threat);
        Alerts = new AlertFeed { World = World, JumpTo = JumpTo };
        AddChild(Alerts);
        Voice = new TalkingHead();
        AddChild(Voice);
        AddChild(Minimap);
        _card = new CommandCard { World = World, State = State, Send = Send, Order = Order };
        AddChild(_card);
        _inspector = new Inspector { World = World, State = State, Send = Send };
        AddChild(_inspector);

        _debug = Outlined(new Label { Visible = false }, 12);
        AddChild(_debug);
        _notices = Outlined(new Label { HorizontalAlignment = HorizontalAlignment.Center }, 14);
        AddChild(_notices);

        _banner = Outlined(new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center }, 44);
        AddChild(_banner);
        foreach (var side in Enum.GetValues<Side>())
        {
            var warning = Outlined(new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center }, 20);
            warning.AddThemeColorOverride("font_color", new Color(1, 0.45f, 0.35f));
            AddChild(warning);
            _edgeWarnings[side] = warning;
        }

        _help = Outlined(new Label { Visible = false, Text = HelpText }, 16);
        AddChild(_help);

        // The end of a run: what happened, and the way back to the menu.
        _end = UiKit.PanelBox();
        _end.Visible = false;
        _end.CustomMinimumSize = new Vector2(420, 0);
        var endBox = new VBoxContainer();
        endBox.AddThemeConstantOverride("separation", 8);
        _endText = UiKit.Label("", 16);
        endBox.AddChild(_endText);
        _chart = new RunChart { World = World };
        endBox.AddChild(_chart);
        var endRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        endRow.AddThemeConstantOverride("separation", 8);
        if (World.Scenario != null)
        {
            if (Campaign.Default.Contains(World.Scenario))
            {
                var campaign = UiKit.TextButton("Back to the campaign", 15);
                campaign.Pressed += () => BackToCampaign();
                endRow.AddChild(campaign);
            }
            else
            {
                var menu = UiKit.TextButton("Main menu", 15);
                menu.Pressed += () => NewRun();
                endRow.AddChild(menu);
            }
            var retry = UiKit.TextButton("Play it again", 15);
            retry.Pressed += () => Retry();
            endRow.AddChild(retry);
        }
        else
        {
            var again = UiKit.TextButton("New run", 15);
            again.Pressed += () => NewRun();
            endRow.AddChild(again);
        }
        var quit = UiKit.TextButton("Quit", 15);
        quit.Pressed += () => GetTree().Quit();
        endRow.AddChild(quit);
        endBox.AddChild(endRow);
        _end.AddChild(endBox);
        AddChild(_end);
    }

    public static string Describe(BuildingDef def)
    {
        var parts = new List<string>();
        if (def.Housing > 0) parts.Add($"houses {def.Housing}");
        if (def.Workers > 0) parts.Add($"needs {def.Workers} workers");
        if (def.SanctitySupply > 0) parts.Add($"+{def.SanctitySupply} sanctity");
        if (def.SanctityUse > 0) parts.Add($"uses {def.SanctityUse} sanctity");
        if (def.ConsecrateRadius > 0) parts.Add($"consecrates radius {def.ConsecrateRadius}");
        if (def.Produces is { } r) parts.Add($"gathers {r.ToString().ToLowerInvariant()} from {string.Join("/", def.Gathers)}");
        if (def.Weapon is { } w) parts.Add($"range {w.Range}, {w.Damage} dmg / {w.Cooldown}s{(w.Splash > 0 ? $", splash {w.Splash}" : "")}");
        if (def.SlowRadius > 0) parts.Add($"slows demons within {def.SlowRadius} to {def.SlowFactor:P0}");
        return parts.Count == 0 ? "" : "\n" + string.Join("\n", parts);
    }

    static Label Outlined(Label label, int size)
    {
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 5);
        return label;
    }

    RunChart _chart = null!;

    public override void _Process(double delta)
    {
        var screen = GetViewport().GetVisibleRect().Size;
        _bar.Position = Vector2.Zero;
        _bar.Size = new Vector2(screen.X, 0);
        float top = _bar.Size.Y + 6;
        _threat.Position = new Vector2(screen.X - _threat.Size.X - 8, top);
        Alerts.Position = new Vector2(8, top);
        Minimap.Position = new Vector2(8, screen.Y - Minimap.Size.Y - 8);
        _inspector.Position = new Vector2(screen.X - _inspector.Size.X - 8, screen.Y - _inspector.Size.Y - 8);
        _card.Position = new Vector2(Mathf.Max(Minimap.Size.X + 16, (screen.X - _card.Size.X) / 2), screen.Y - _card.Size.Y - 8);
        _notices.Text = string.Join("\n", State.Log.TakeLast(3).Select(l => l.Text));
        _notices.Size = new Vector2(screen.X, 0);
        _notices.Position = new Vector2(0, _card.Position.Y - _notices.Size.Y - 6);
        _debug.Visible = ShowDebug;
        _debug.Text = DebugText;
        _debug.Position = new Vector2(8, top + Alerts.Size.Y + 8);
        _help.Position = new Vector2(screen.X / 2 - 460, screen.Y / 2 - 140);
        UpdateEdgeWarnings(screen);

        if (World.Outcome != Outcome.Running)
        {
            _banner.Visible = true;
            bool keepStands = World.Buildings.Any(b => b.Kind == BuildingKind.Keep);
            _banner.Text = World.Outcome == Outcome.Won ? (Campaign.Default.Contains(World.Scenario) ? "Mission won" : "The colony endures")
                : !keepStands ? (World.Survival is { Endless: true } ? $"The Keep has fallen on day {World.Day}" : "The Keep has fallen")
                : "Out of time: the Convergence came and went";
            _banner.Size = new Vector2(screen.X, 60);
            _banner.Position = new Vector2(0, screen.Y * 0.3f);
            if (!_end.Visible)
            {
                var st = World.Stats;
                var s = World.Survival;
                string corruptions = s is { Endless: true, Corruptions.Count: > 0 }
                    ? $"\nThe horde became: {string.Join(", ", s.Corruptions.Select(id => World.Rules.Corruption(id).Name))}" : "";
                string mode = World.Scenario is { } m ? m.Name : s == null ? "" : s.Endless ? "Endless" : "Survival";
                string goals = World.Scenario == null ? "" : "\n" + string.Join("\n", World.Goals.Select((g, i) => $"{(World.GoalsDone[i] ? "done" : "not done")}:  {g.Describe()}")) + "\n";
                string opens = "";
                if (World.Scenario is { } won && World.Outcome == Outcome.Won && Campaign.Default.Contains(won))
                {
                    var c = Campaign.Default;
                    var wonSet = CampaignProgress.Won(c.Id);
                    var next = c.Scenarios.Where(x => x.Requires.Contains(won.Id) && c.IsOpen(x, wonSet)).Select(x => x.Name).ToList();
                    if (next.Count > 0) opens = $"\nNow open: {string.Join(", ", next)}";
                }
                var (score, best) = Score.Record(World, mode);
                string scoreLine = $"Score {score:N0}" + (best > score ? $"   (best {best:N0})" : best == score ? "   (a new best)" : "") + "\n";
                _endText.Text = $"{mode} · {World.Map} · {World.Rules.Difficulty} · seed {World.Seed}\n{goals}{scoreLine}" +
                    $"Days survived: {World.Day}\nDemons slain: {st.DemonsKilled}\nBuildings lost: {st.BuildingsLost}\nSoldiers lost: {st.UnitsLost}" +
                    $"\nResearched: {(World.Tech.Researched.Count == 0 ? "nothing" : string.Join(", ", World.Tech.Researched.Select(id => World.Rules.Tech(id).Name)))}{corruptions}{opens}";
                _chart.Refresh();
                _end.Visible = true;
                _card.Visible = false; // the run is over: nothing to build, and it would cover the buttons
                Voice.Silence();
            }
            _end.Position = new Vector2(screen.X / 2 - _end.Size.X / 2, screen.Y * 0.3f + 70);
        }
    }

    /// <summary>Each announced wave pinned to the corner of the screen its map side lies toward, with its countdown.</summary>
    void UpdateEdgeWarnings(Vector2 screen)
    {
        foreach (var w in _edgeWarnings.Values) w.Visible = false;
        if (World.Survival is not { } s) return;
        foreach (var wave in s.Waves)
        {
            if (!wave.Announced || wave.Landed) continue;
            string eta = UiKit.Clock((wave.LandsAtTick - World.Tick) / (double)Balance.TickHz);
            foreach (var side in wave.Sides)
            {
                var label = _edgeWarnings[side];
                label.Visible = true;
                int share = wave.Size / wave.Sides.Length;
                // In the isometric view each map side lies along a screen diagonal.
                label.Text = side switch
                {
                    Side.North => $"{share} from the NORTH  >>\n{eta}",
                    Side.East => $"{share} from the EAST  >>\n{eta}",
                    Side.South => $"<<  {share} from the SOUTH\n{eta}",
                    _ => $"<<  {share} from the WEST\n{eta}",
                };
                label.Size = new Vector2(300, 0);
                label.Position = side switch
                {
                    Side.North => new Vector2(screen.X * 0.7f, screen.Y * 0.28f),
                    Side.East => new Vector2(screen.X * 0.7f, screen.Y * 0.62f),
                    Side.South => new Vector2(screen.X * 0.3f - 300, screen.Y * 0.62f),
                    _ => new Vector2(screen.X * 0.3f - 300, screen.Y * 0.28f),
                };
            }
        }
    }
}
