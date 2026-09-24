using Godot;
using Hellwall.Sim;
using Resource = Hellwall.Sim.Resource;
using Side = Hellwall.Sim.Side;

namespace Hellwall.Game;

/// <summary>
/// Screen-space UI: resource bar, build bar, inspector, message log, and the
/// outcome banner. Buttons only enqueue Commands or change ClientState; the
/// HUD reads sim state and never changes it.
/// </summary>
public partial class Hud : CanvasLayer
{
    public World World = null!;
    public ClientState State = null!;

    /// <summary>Set by Main: enqueue a command for the sim.</summary>
    public Action<Command> Send = null!;

    Label _top = null!;
    Label _debug = null!;
    Label _log = null!;
    Label _banner = null!;
    Label _clock = null!;
    readonly Dictionary<Side, Label> _edgeWarnings = new();
    PanelContainer _inspector = null!;
    Label _inspectorText = null!;
    HFlowContainer _trainButtons = null!;
    HBoxContainer _buildBar = null!;
    readonly List<(Button Button, BuildingKind Kind)> _buildButtons = new();

    public string DebugText = "";

    public Minimap Minimap = null!;
    /// <summary>Back to the new-game menu.</summary>
    public Action NewRun = null!;
    PanelContainer _end = null!;
    Label _endText = null!;
    Label _help = null!;

    const string HelpText =
        "BUILD   1-0 - =  M C L P  G B U  or the bar; click to place; drag walls for a line; right-click or Esc to disarm\n" +
        "SELECT  click a building or soldier; drag to box-select soldiers (shift adds)\n" +
        "ORDER   right-click attack-move · shift+right-click move · H hold · Shift+S stop\n" +
        "GROUPS  Ctrl+1-9 set · Alt+1-9 recall\n" +
        "BARRACKS  Q E R T Y F train · SCRIPTORIUM  research buttons in the inspector\n" +
        "X / Delete  demolish (purges a possessed building)\n" +
        "Space pause · Tab speed 1x/2x/4x · F5 save · F9 load · WASD pan · wheel zoom · minimap click to jump\n" +
        "Debug: N noise at cursor · K wave · J 20k assault\n" +
        "F1 to close";

    public void ToggleHelp() => _help.Visible = !_help.Visible;

    public override void _Ready()
    {
        _top = Outlined(new Label { Position = new Vector2(10, 6) }, 17);
        AddChild(_top);
        _debug = Outlined(new Label { Position = new Vector2(10, 30) }, 13);
        AddChild(_debug);
        _log = Outlined(new Label { Position = new Vector2(10, 56) }, 14);
        AddChild(_log);

        _banner = Outlined(new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center }, 44);
        AddChild(_banner);

        _clock = Outlined(new Label { HorizontalAlignment = HorizontalAlignment.Right }, 17);
        AddChild(_clock);
        foreach (var side in Enum.GetValues<Side>())
        {
            var warning = Outlined(new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center }, 20);
            warning.AddThemeColorOverride("font_color", new Color(1, 0.45f, 0.35f));
            AddChild(warning);
            _edgeWarnings[side] = warning;
        }

        _buildBar = new HBoxContainer();
        _buildBar.AddThemeConstantOverride("separation", 2);
        AddChild(_buildBar);
        foreach (var (kind, _, keyLabel) in Palette.BuildBar)
        {
            var def = World.Rules[kind];
            var button = new Button
            {
                Text = $"{keyLabel} {kind}",
                TooltipText = $"{kind}\n{def.Cost}{Describe(def)}",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, 34), // sized to its text: nineteen buttons must fit 1600 px
            };
            button.AddThemeFontSizeOverride("font_size", 12);
            button.Pressed += () => State.Armed = State.Armed == kind ? null : kind;
            _buildBar.AddChild(button);
            _buildButtons.Add((button, kind));
        }

        AddChild(Minimap);

        _help = Outlined(new Label { Visible = false, Text = HelpText }, 16);
        AddChild(_help);

        // The end of a run: what happened, and the way back to the menu.
        _end = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(420, 0) };
        var endBox = new VBoxContainer();
        endBox.AddThemeConstantOverride("separation", 8);
        _endText = new Label();
        _endText.AddThemeFontSizeOverride("font_size", 16);
        endBox.AddChild(_endText);
        var endRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        var again = new Button { Text = "New run", FocusMode = Control.FocusModeEnum.None };
        again.Pressed += () => NewRun();
        var quit = new Button { Text = "Quit", FocusMode = Control.FocusModeEnum.None };
        quit.Pressed += () => GetTree().Quit();
        endRow.AddChild(again);
        endRow.AddChild(quit);
        endBox.AddChild(endRow);
        _end.AddChild(endBox);
        AddChild(_end);

        _inspector = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(300, 0) };
        var box = new VBoxContainer();
        _inspectorText = new Label();
        _inspectorText.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_inspectorText);
        _trainButtons = new HFlowContainer { CustomMinimumSize = new Vector2(420, 0) };
        box.AddChild(_trainButtons);
        _inspector.AddChild(box);
        AddChild(_inspector);
    }

    static string Describe(BuildingDef def)
    {
        var parts = new List<string>();
        if (def.Housing > 0) parts.Add($"houses {def.Housing}");
        if (def.Workers > 0) parts.Add($"needs {def.Workers} workers");
        if (def.SanctitySupply > 0) parts.Add($"+{def.SanctitySupply} sanctity");
        if (def.SanctityUse > 0) parts.Add($"uses {def.SanctityUse} sanctity");
        if (def.ConsecrateRadius > 0) parts.Add($"consecrates radius {def.ConsecrateRadius}");
        if (def.Produces is { } r) parts.Add($"gathers {r.ToString().ToLowerInvariant()} from {string.Join("/", def.Gathers)}");
        if (def.Weapon is { } w) parts.Add($"range {w.Range}, {w.Damage} dmg / {w.Cooldown}s{(w.Splash > 0 ? $", splash {w.Splash}" : "")}");
        return parts.Count == 0 ? "" : "\n" + string.Join("\n", parts);
    }

    static Label Outlined(Label label, int size)
    {
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 5);
        return label;
    }

    public override void _Process(double delta)
    {
        var screen = GetViewport().GetVisibleRect().Size;
        var colony = World.Colony;

        string Res(Resource r) => $"{r} {colony[r]:0} ({Signed(colony.NetPerSecond[(int)r])}/s)";
        _top.Text =
            $"{Res(Resource.Gold)}   {Res(Resource.Wood)}   {Res(Resource.Stone)}   {Res(Resource.Food)}   {Res(Resource.Iron)}   " +
            $"Colonists {colony.WorkersUsed}/{colony.Colonists} working   Sanctity {colony.SanctityDemand:0}/{colony.SanctitySupply:0}" +
            (colony.Power < 1 ? $"  ({colony.Power:P0} power)" : "") +
            (colony.Starving ? "   STARVING" : "");
        _debug.Text = DebugText;

        _log.Text = string.Join("\n", State.Log.Select(l => l.Text));

        _buildBar.Position = new Vector2(10, screen.Y - 44);
        foreach (var (button, kind) in _buildButtons)
        {
            var def = World.Def(kind);
            bool locked = def.RequiresTech is { } needs && !World.Tech.Has(needs);
            bool affordable = colony.CanAfford(def.Cost);
            button.Modulate = State.Armed == kind ? new Color(0.6f, 1, 0.6f) : locked ? new Color(1, 1, 1, 0.25f) : affordable ? Colors.White : new Color(1, 1, 1, 0.5f);
            button.TooltipText = $"{kind}\n{def.Cost}{Describe(def)}" + (locked ? $"\nneeds {World.Rules.Tech(def.RequiresTech!).Name}" : "");
        }

        UpdateInspector(screen);
        UpdateClock(screen);
        Minimap.Position = new Vector2(10, screen.Y - 44 - 206);
        _help.Position = new Vector2(screen.X / 2 - 420, screen.Y / 2 - 120);

        if (World.Outcome != Outcome.Running)
        {
            _banner.Visible = true;
            _banner.Text = World.Outcome == Outcome.Lost ? (World.Survival is { Endless: true } ? $"The Keep has fallen on day {World.Day}" : "The Keep has fallen") : "The colony endures";
            _banner.Size = new Vector2(screen.X, 60);
            _banner.Position = new Vector2(0, screen.Y * 0.4f);
            if (!_end.Visible)
            {
                var st = World.Stats;
                var s = World.Survival;
                string corruptions = s is { Endless: true, Corruptions.Count: > 0 }
                    ? $"\nThe horde became: {string.Join(", ", s.Corruptions.Select(id => World.Rules.Corruption(id).Name))}" : "";
                string mode = s == null ? "" : s.Endless ? "Endless" : "Survival";
                _endText.Text = $"{mode} · {World.Map} · {World.Rules.Difficulty} · seed {World.Seed}\n" +
                    $"Days survived: {World.Day}\nDemons slain: {st.DemonsKilled}\nBuildings lost: {st.BuildingsLost}\nSoldiers lost: {st.UnitsLost}" +
                    $"\nResearched: {(World.Tech.Researched.Count == 0 ? "nothing" : string.Join(", ", World.Tech.Researched.Select(id => World.Rules.Tech(id).Name)))}{corruptions}";
                _end.Visible = true;
            }
            _end.Position = new Vector2(screen.X / 2 - _end.Size.X / 2, screen.Y * 0.4f + 70);
        }
    }

    static string Signed(double v) => v >= 0 ? $"+{v:0.0}" : $"{v:0.0}";

    double HellgateScale()
    {
        double floor = World.Rules.Hellgates.WaveFloor;
        return floor + (1 - floor) * World.Gates.Count(g => g.Alive) / Math.Max(1, World.Gates.Count);
    }

    static string Clock(double seconds) => seconds <= 0 ? "now" : $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    /// <summary>Day counter top right; each announced wave pinned to the screen edge it's coming from, with its countdown.</summary>
    void UpdateClock(Vector2 screen)
    {
        foreach (var w in _edgeWarnings.Values) w.Visible = false;
        var s = World.Survival;
        if (s == null)
        {
            _clock.Text = $"Day {World.Day}";
        }
        else
        {
            var next = s.Next;
            string upcoming = next == null ? "All waves have come."
                : next.Announced ? $"{(next.Final ? "CONVERGENCE" : next.Surge ? $"SURGE (wave {next.Number})" : $"Wave {next.Number}")}: {next.Size} in {Clock((next.LandsAtTick - World.Tick) / (double)Balance.TickHz)}"
                : $"Next wave: day {s.DayAt(next.LandsAtTick) - 1} ({Clock((next.AnnounceTick(s.Rules) - World.Tick) / (double)Balance.TickHz)} until it's sighted)";
            int open = World.Gates.Count(g => g.Alive);
            string gates = World.Gates.Count == 0 ? "" : $"\nHellgates open: {open} of {World.Gates.Count} (waves at {HellgateScale():P0})";
            string level = World.Rules.Difficulty == Difficulty.Normal ? "" : $" · {World.Rules.Difficulty}";
            string day = s.Endless ? $"Day {World.Day} · endless{level}" : $"Day {Math.Min(World.Day, s.Rules.Days)} of {s.Rules.Days}{level}";
            string corruption = "";
            if (s.Endless)
            {
                if (s.PendingCorruption is { } pending)
                    corruption += $"\nCORRUPTION in {Clock((s.NextCorruptionTick - World.Tick) / (double)Balance.TickHz)}: {World.Rules.Corruption(pending).Name}";
                if (s.Corruptions.Count > 0)
                    corruption += $"\nThe horde: {string.Join(", ", s.Corruptions.Select(id => World.Rules.Corruption(id).Name))}";
            }
            _clock.Text = $"{day}\n{upcoming}{gates}{corruption}";

            foreach (var wave in s.Waves)
            {
                if (!wave.Announced || wave.Landed) continue;
                string eta = Clock((wave.LandsAtTick - World.Tick) / (double)Balance.TickHz);
                foreach (var side in wave.Sides)
                {
                    var label = _edgeWarnings[side];
                    label.Visible = true;
                    int share = wave.Size / wave.Sides.Length;
                    label.Text = side switch
                    {
                        // In the isometric view each map side lies along a screen diagonal.
                        Side.North => $"{share} from the NORTH  >>\n{eta}",
                        Side.East => $"{share} from the EAST  >>\n{eta}",
                        Side.South => $"<<  {share} from the SOUTH\n{eta}",
                        _ => $"<<  {share} from the WEST\n{eta}",
                    };
                    label.Size = new Vector2(300, 0);
                    label.Position = side switch
                    {
                        Side.North => new Vector2(screen.X * 0.72f, 150),
                        Side.East => new Vector2(screen.X * 0.72f, screen.Y - 150),
                        Side.South => new Vector2(screen.X * 0.28f - 300, screen.Y - 150),
                        _ => new Vector2(screen.X * 0.28f - 300, 150),
                    };
                }
            }
        }
        _clock.Size = new Vector2(420, 0);
        _clock.Position = new Vector2(screen.X - 430, 6);
    }

    int? _inspected;
    int _unitsShown = -1;
    string _researchKey = "";

    void UpdateInspector(Vector2 screen)
    {
        string text = "";
        var building = State.SelectedBuilding is { } id ? World.BuildingById(id) : null;
        if (building == null && State.SelectedBuilding != null) State.SelectedBuilding = null;

        if (building != null)
        {
            var b = building;
            string status =
                b.Possessed ? $"POSSESSED: {b.Occupants} still inside. [X] to purge" :
                !b.Complete ? $"Under construction {b.Built / Math.Max(0.001f, b.Def.BuildSeconds):P0}" :
                !b.OnGround ? "Dark: not on consecrated ground" :
                b.NeedsCrew && !b.Staffed ? $"Idle: needs {b.Def.Workers} workers" :
                "Working";
            text = $"{b.Kind}   {b.Hp:0}/{b.Def.Hp:0} hp\n{status}";
            if (b.Def.Produces is { } r && b.Complete) text += $"\n{b.Rate * World.Colony.Power:0.00} {r.ToString().ToLowerInvariant()}/s";
            if (b.Researching is { } rid)
                text += $"\nResearching {World.Rules.Tech(rid).Name} {b.ResearchProgress / World.Rules.Tech(rid).Seconds:P0}";
            if (b.Def.Researches)
                text += $"\nKnown: {(World.Tech.Researched.Count == 0 ? "nothing yet" : string.Join(", ", World.Tech.Researched.Select(t => World.Rules.Tech(t).Name)))}";
            if (b.Queue.Count > 0)
                text += $"\nTraining {b.Queue[0]} {b.TrainProgress / World.Def(b.Queue[0]).TrainSeconds:P0}" +
                        (b.Queue.Count > 1 ? $"  (+{b.Queue.Count - 1} queued)" : "");
            if (b.Kind != BuildingKind.Keep) text += "\n[X] demolish";
        }
        else if (State.SelectedUnits.Count > 0)
        {
            var units = World.Units.Where(u => State.SelectedUnits.Contains(u.Id)).ToList();
            State.SelectedUnits.RemoveWhere(id => World.UnitById(id) == null);
            text = string.Join("   ", units.GroupBy(u => u.Kind).Select(g => $"{g.Count()} {g.Key}")) +
                   $"\n{units.Sum(u => u.Hp):0}/{units.Sum(u => u.Def.Hp):0} hp" +
                   "\nRMB attack-move · Shift+RMB move · H hold · Shift+S stop";
        }

        _inspector.Visible = text.Length > 0;
        _inspectorText.Text = text;

        // Rebuild the buttons only when the selection (or, for a Scriptorium, what can be researched) changes.
        string researchKey = building is { Def.Researches: true }
            ? string.Join(",", World.Rules.Techs.Where(t => World.CheckResearch(t.Id) == null).Select(t => t.Id)) + (building.Researching ?? "")
            : "";
        if (_inspected != building?.Id || (building == null && _unitsShown != State.SelectedUnits.Count) || researchKey != _researchKey)
        {
            _inspected = building?.Id;
            _unitsShown = State.SelectedUnits.Count;
            _researchKey = researchKey;
            foreach (var child in _trainButtons.GetChildren()) child.QueueFree();
            if (building is { Def.Trains.Length: > 0 })
            {
                string[] keys = Main.TrainKeys.Select(k => k.ToString()).ToArray();
                for (int i = 0; i < building.Def.Trains.Length; i++)
                {
                    var kind = building.Def.Trains[i];
                    var def = World.Def(kind);
                    var button = new Button
                    {
                        Text = $"{(i < keys.Length ? keys[i] + " " : "")}{kind}",
                        TooltipText = $"{kind}: {def.Cost}\n{def.Hp} hp, range {def.Weapon.Range}, {def.Weapon.Damage} dmg / {def.Weapon.Cooldown}s",
                        FocusMode = Control.FocusModeEnum.None,
                    };
                    int bid = building.Id;
                    button.Pressed += () => Send(new TrainUnit(bid, kind));
                    _trainButtons.AddChild(button);
                }
            }
            if (building is { Def.Researches: true, Researching: null })
            {
                foreach (var tech in World.Rules.Techs.Where(t => World.CheckResearch(t.Id) == null))
                {
                    var button = new Button
                    {
                        Text = $"{tech.Name}",
                        TooltipText = $"{tech.Name} (tier {tech.Tier}): {tech.Cost}, {tech.Seconds:0}s\n{tech.Description}",
                        FocusMode = Control.FocusModeEnum.None,
                    };
                    button.AddThemeFontSizeOverride("font_size", 12);
                    int bid = building.Id;
                    string techId = tech.Id;
                    button.Pressed += () => Send(new Research(bid, techId));
                    _trainButtons.AddChild(button);
                }
            }
        }

        _inspector.Position = new Vector2(screen.X - _inspector.Size.X - 10, screen.Y - _inspector.Size.Y - 54);
    }
}
