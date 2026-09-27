using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Bottom centre: a grid of three rows by five, and it changes with the selection.
/// Nothing selected: the build menu, its categories on the top row and their
/// buildings below. A Barracks: its army, queue and rally point. A Scriptorium:
/// research. Any other building (or a group of them): upgrade, hold, demolish.
/// Soldiers: pick out one kind, and their orders. Each cell answers to the key
/// in its place (HotkeyGrid), so the keys can never disagree with what's shown.
/// Buttons only send Commands or change ClientState.
/// </summary>
public partial class CommandCard : PanelContainer
{
    public World World = null!;
    public ClientState State = null!;
    public Action<Command> Send = null!;
    /// <summary>Order the selected soldiers (Main owns the arming and cursor).</summary>
    public Action<string> Order = null!;
    /// <summary>Its upgrade, hold and demolish buttons, which know a group and the Keep: the card shows and presses them.</summary>
    public Inspector Inspector = null!;

    const int CellW = 64, CellH = 52;

    int _tab;
    string _mode = "";
    VBoxContainer _body = null!;
    GridContainer _grid = null!;
    /// <summary>What each cell does when clicked or keyed (null: empty).</summary>
    readonly Action?[] _press = new Action?[HotkeyGrid.Cells];
    readonly Button?[] _cells = new Button?[HotkeyGrid.Cells];
    readonly List<(Button Button, BuildingKind Kind)> _build = new();
    readonly List<(Button Button, UnitKind Kind)> _train = new();
    readonly List<Button> _queue = new();
    readonly List<Button> _tabs = new();
    /// <summary>The Inspector's buttons this card is showing, each with the cell it mirrors.</summary>
    readonly List<(Button Cell, Button Source, string Short)> _mirrors = new();
    Label _title = null!, _hint = null!;
    ProgressBar? _progress;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UiKit.Box(UiKit.Panel, UiKit.Rim, 6, 8));
        MouseFilter = MouseFilterEnum.Stop;
        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", 6);
        AddChild(_body);
    }

    string ModeNow()
    {
        var b = State.SelectedBuilding is { } id ? World.BuildingById(id) : null;
        if (State.SelectedUnits.Count > 0) return $"army-{State.SelectedUnits.Count}";
        if (b is { Def.Trains.Length: > 0, Complete: true, Possessed: false }) return $"barracks-{b.Id}";
        if (b is { Def.Researches: true, Complete: true, Possessed: false }) return $"lab-{b.Id}-{b.Researching}-{string.Join(",", World.Tech.Researched)}";
        if (b != null) return $"building-{b.Id}-{State.SelectedGroup.Count}";
        return $"build-{_tab}";
    }

    public override void _Process(double delta)
    {
        EnsureMode();
        UpdateStates();
    }

    void EnsureMode()
    {
        string mode = ModeNow();
        if (mode != _mode) Rebuild(mode);
    }

    /// <summary>A key on the grid: press whatever is in its cell. False if the key isn't on the grid or the cell is empty or unavailable.</summary>
    public bool Press(Key key)
    {
        int cell = HotkeyGrid.CellOf(key);
        if (cell < 0) return false;
        EnsureMode();
        UpdateStates();
        if (_press[cell] is not { } act || _cells[cell] is not { Visible: true, Disabled: false }) return false;
        act();
        return true;
    }

    void Clear()
    {
        foreach (var c in _body.GetChildren()) c.QueueFree();
        Array.Clear(_press);
        Array.Clear(_cells);
        _build.Clear();
        _train.Clear();
        _queue.Clear();
        _tabs.Clear();
        _mirrors.Clear();
        _progress = null;
    }

    void Rebuild(string mode)
    {
        _mode = mode;
        Clear();
        _title = UiKit.Label("", 14, UiKit.Gold);
        _body.AddChild(_title);
        _grid = new GridContainer { Columns = HotkeyGrid.Cols };
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
        _body.AddChild(_grid);
        var b = State.SelectedBuilding is { } id ? World.BuildingById(id) : null;
        if (mode.StartsWith("barracks")) BuildBarracks(b!);
        else if (mode.StartsWith("lab")) BuildLab(b!);
        else if (mode.StartsWith("building")) BuildBuilding(b!);
        else if (mode.StartsWith("army")) BuildArmy();
        else BuildMenu();
        // Empty cells hold their places, so every key stays where the hand expects it.
        for (int i = 0; i < HotkeyGrid.Cells; i++)
        {
            if (_cells[i] == null)
            {
                var blank = UiKit.IconButton(null, "", CellH);
                blank.CustomMinimumSize = new Vector2(CellW, CellH);
                blank.Disabled = true;
                blank.Modulate = new Color(1, 1, 1, 0.25f);
                _cells[i] = blank;
            }
            _grid.AddChild(_cells[i]);
        }
        _hint = UiKit.Label("", 12, UiKit.Muted);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hint.CustomMinimumSize = new Vector2(HotkeyGrid.Cols * (CellW + 4), 0);
        _body.AddChild(_hint);
    }

    /// <summary>Put a button in a cell, with its key in the corner, doing `act` when clicked or keyed.</summary>
    Button Cell(int cell, Texture2D? icon, string text, Action act)
    {
        var button = UiKit.IconButton(icon, HotkeyGrid.Label(cell), CellH);
        button.CustomMinimumSize = new Vector2(CellW, CellH);
        if (text.Length > 0)
        {
            button.Text = text;
            button.AddThemeFontSizeOverride("font_size", 12);
            button.VerticalIconAlignment = VerticalAlignment.Top;
        }
        button.Pressed += act;
        _press[cell] = act;
        _cells[cell] = button;
        return button;
    }

    /// <summary>A cell that shows and presses one of the Inspector's buttons (which keeps its text, state and logic), under a short name.</summary>
    void Mirror(int cell, Button source, string shortName)
    {
        var button = Cell(cell, null, shortName, () => source.EmitSignal(BaseButton.SignalName.Pressed));
        _mirrors.Add((button, source, shortName));
    }

    // --- build menu ---

    void BuildMenu()
    {
        _title.Text = "Build";
        var tabs = HotkeyGrid.Tabs;
        for (int i = 0; i < tabs.Length; i++)
        {
            int t = i;
            var tab = Cell(i, null, tabs[i].Name, () => { _tab = t; _mode = ""; State.Armed = null; });
            tab.ToggleMode = true;
            _tabs.Add(tab);
        }
        var kinds = tabs[_tab].Kinds;
        for (int i = 0; i < kinds.Length && HotkeyGrid.FirstBuildCell + i < HotkeyGrid.Cells; i++)
        {
            var k = kinds[i];
            var button = Cell(HotkeyGrid.FirstBuildCell + i, UiKit.Building(k), "", () => State.Armed = State.Armed == k ? null : k);
            _build.Add((button, k));
        }
    }

    // --- a Barracks: the army ---

    void BuildBarracks(Building b)
    {
        _title.Text = $"{b.Kind}: train";
        int bid = b.Id;
        for (int i = 0; i < b.Def.Trains.Length && i < HotkeyGrid.Cells - 2; i++)
        {
            var kind = b.Def.Trains[i];
            // Shift queues five.
            var button = Cell(i, UiKit.Unit(kind), "", () =>
            {
                int n = Input.IsKeyPressed(Key.Shift) ? 5 : 1;
                for (int j = 0; j < n; j++) Send(new TrainUnit(bid, kind));
            });
            _train.Add((button, kind));
        }
        Cell(HotkeyGrid.Cells - 2, null, "Rally\nclear", () => Send(new SetRally(bid, -1, 0))).TooltipText = "Clear the rally point: new soldiers stand at the door";
        Mirror(HotkeyGrid.Cells - 1, Inspector.DemolishButton, "Demolish");

        var queue = new HBoxContainer();
        queue.AddThemeConstantOverride("separation", 3);
        queue.AddChild(UiKit.Label("Queue", 12, UiKit.Muted));
        for (int i = 0; i < Balance.QueueLimit; i++)
        {
            int slot = i;
            var s = UiKit.IconButton(null, "", 34);
            s.Pressed += () => Send(new CancelTraining(bid, slot));
            queue.AddChild(s);
            _queue.Add(s);
        }
        _body.AddChild(queue);
        _progress = Progress(6);
    }

    ProgressBar Progress(int height)
    {
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, height) };
        bar.AddThemeStyleboxOverride("background", UiKit.Box(new Color(0.24f, 0.23f, 0.21f), null, 3, 0));
        bar.AddThemeStyleboxOverride("fill", UiKit.Box(UiKit.Gold, UiKit.Gold, 3, 0));
        _body.AddChild(bar);
        return bar;
    }

    // --- a Scriptorium ---

    void BuildLab(Building b)
    {
        _title.Text = b.Researching is { } r ? $"Researching {World.Rules.Tech(r).Name}" : "Research";
        Mirror(HotkeyGrid.Cells - 1, Inspector.DemolishButton, "Demolish");
        if (b.Researching != null)
        {
            _progress = Progress(8);
            return;
        }
        int cell = 0;
        foreach (var tech in World.Rules.Techs.Where(t => World.CheckResearch(t.Id) == null))
        {
            if (cell >= HotkeyGrid.Cells - 1) break;
            int bid = b.Id;
            string techId = tech.Id;
            var button = Cell(cell++, null, Short(tech.Name), () => Send(new Research(bid, techId)));
            button.TooltipText = $"{tech.Name} (tier {tech.Tier}): {UiKit.CostText(tech.Cost, World.Colony)}, {tech.Seconds:0} s\n{tech.Description}";
        }
    }

    /// <summary>A name that fits a cell: its first word, or two short ones on two lines.</summary>
    static string Short(string name)
    {
        var words = name.Split(' ');
        return words.Length > 1 && words[0].Length + words[1].Length < 14 ? $"{words[0]}\n{words[1]}" : words[0];
    }

    // --- any other building, or a group of them ---

    void BuildBuilding(Building b)
    {
        _title.Text = State.SelectedGroup.Count > 1 ? $"{State.SelectedGroup.Count} {b.Kind}s" : b.Kind.ToString();
        Mirror(0, Inspector.UpgradeButton, b.Kind == BuildingKind.Keep ? "Raise" : "Upgrade");
        Mirror(1, Inspector.HoldButton, "Hold");
        Mirror(HotkeyGrid.Cells - 1, Inspector.DemolishButton, "Demolish");
    }

    // --- soldiers ---

    static readonly (int Cell, string Label, string Order, string Tip)[] Orders =
    [
        (HotkeyGrid.Cols + 0, "Attack", "attack", "Attack-move: then click where; they fight what they meet on the way. Shift-click queues more"),
        (HotkeyGrid.Cols + 1, "Stop", "stop", "Stop where they are"),
        (HotkeyGrid.Cols + 2, "Hold", "hold", "Hold: stand and shoot, never chase"),
        (HotkeyGrid.Cols + 3, "Patrol", "patrol", "Patrol: then click; back and forth between here and there, fighting"),
    ];

    void BuildArmy()
    {
        var units = World.Units.Where(u => State.SelectedUnits.Contains(u.Id)).ToList();
        _title.Text = $"{units.Count} soldier{(units.Count == 1 ? "" : "s")}";
        int cell = 0;
        foreach (var g in units.GroupBy(u => u.Kind).OrderBy(g => g.Key))
        {
            if (cell >= HotkeyGrid.Cols) break;
            var kind = g.Key;
            var ids = g.Select(u => u.Id).ToList();
            var b = Cell(cell++, UiKit.Unit(kind), $"{g.Count()}", () =>
            {
                State.SelectedUnits.Clear();
                foreach (var id in ids) State.SelectedUnits.Add(id);
            });
            b.IconAlignment = HorizontalAlignment.Left;
            b.VerticalIconAlignment = VerticalAlignment.Center;
            b.TooltipText = $"{kind}: select only these";
        }
        foreach (var (at, label, order, tip) in Orders)
        {
            var o = order;
            Cell(at, null, label, () => Order(o)).TooltipText = tip;
        }
    }

    void UpdateStates()
    {
        var colony = World.Colony;
        foreach (var (button, kind) in _build)
        {
            var def = World.Def(kind);
            bool mission = World.Scenario?.Locks(kind) == true;
            bool locked = mission || (def.RequiresTech is { } needs && !World.Tech.Has(needs));
            bool affordable = colony.CanAfford(def.Cost);
            button.Modulate = State.Armed == kind ? new Color(0.75f, 1, 0.7f) : locked ? new Color(1, 1, 1, 0.3f) : affordable ? Colors.White : new Color(1, 0.75f, 0.75f, 0.75f);
            button.TooltipText = $"{kind}: {Blurbs.Of(kind)}\n{UiKit.CostText(def.Cost, colony)} · {def.Hp:0} hp · {def.BuildSeconds:0} s to build{Hud.Describe(def)}" + (mission ? "\nnot in this mission" : locked ? $"\nneeds {World.Rules.Tech(def.RequiresTech!).Name}" : affordable ? "" : "\n[color=#ff6a55]you can't afford it yet[/color]");
        }
        for (int i = 0; i < _tabs.Count; i++) _tabs[i].ButtonPressed = i == _tab;
        foreach (var (cell, source, shortName) in _mirrors)
        {
            cell.Visible = true;
            cell.Disabled = !source.Visible || source.Disabled;
            cell.Modulate = source.Visible ? Colors.White : new Color(1, 1, 1, 0.25f);
            cell.Text = source == Inspector.DemolishButton && source.Text.Contains("Purge") ? "Purge" : source == Inspector.HoldButton && source.Text.Contains("Back") ? "Resume" : shortName;
            // The button's own words without the cost it states in plain text: the tooltip gives the cost, what's short in red.
            string title = source.Text.Contains(" (") && source.TooltipText.Contains("Costs") ? source.Text[..source.Text.IndexOf(" (")] : source.Text;
            cell.TooltipText = source.Visible ? title + (source.TooltipText.Length > 0 ? "\n" + source.TooltipText : "") : "";
        }

        var b = State.SelectedBuilding is { } id ? World.BuildingById(id) : null;
        if (b != null && _train.Count > 0)
        {
            foreach (var (button, kind) in _train)
            {
                var def = World.Def(kind);
                bool mission = World.Scenario?.Locks(kind) == true;
                bool locked = mission || (def.RequiresTech is { } needs && !World.Tech.Has(needs));
                bool affordable = colony.CanAfford(def.Cost);
                button.Modulate = locked ? new Color(1, 1, 1, 0.3f) : affordable ? Colors.White : new Color(1, 0.75f, 0.75f, 0.75f);
                button.TooltipText = $"{kind}: {Blurbs.Of(kind)}\n{UiKit.CostText(def.Cost, colony)}\n{def.Hp:0} hp, range {def.Weapon.Range}, {def.Weapon.Damage:0} dmg every {def.Weapon.Cooldown}s" +
                    (mission ? "\nnot in this mission" : locked ? $"\nneeds {World.Rules.Tech(def.RequiresTech!).Name}" : "") + "\nshift: five";
            }
            for (int i = 0; i < _queue.Count; i++)
            {
                bool filled = i < b.Queue.Count;
                _queue[i].Icon = filled ? UiKit.Unit(b.Queue[i]) : null;
                _queue[i].TooltipText = filled ? $"{b.Queue[i]}: click to cancel (full refund)" : "";
                _queue[i].Modulate = filled ? Colors.White : new Color(1, 1, 1, 0.35f);
            }
            if (_progress != null) _progress.Value = b.Queue.Count == 0 ? 0 : b.TrainProgress / Math.Max(0.01f, World.Def(b.Queue[0]).TrainSeconds);
            _hint.Text = (b.RallyX >= 0 ? "Right-click the ground to move the rally point" : "Right-click the ground to set a rally point") + " · shift to train five";
        }
        else if (b is { Researching: { } r } && _progress != null)
        {
            _progress.Value = b.ResearchProgress / World.Rules.Tech(r).Seconds;
            _hint.Text = World.Rules.Tech(r).Description;
        }
        else if (_mode.StartsWith("building"))
        {
            // The long form of what Upgrade would do (cost, how many of a group) where there's room to read it.
            var up = Inspector.UpgradeButton;
            _hint.Text = up.Visible ? up.Text : "Esc to go back to building";
        }
        else if (_mode.StartsWith("build"))
        {
            _hint.Text = State.Armed is { } k ? $"Placing {k}: click to build, drag walls for a line, right-click to stop" : "Top row: what kind · the rows below: which one";
        }
        else if (_mode.StartsWith("army"))
        {
            _hint.Text = "Right-click to move · A then click to attack-move · shift to queue after the current order";
        }
    }
}
