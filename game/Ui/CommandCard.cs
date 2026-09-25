using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Bottom centre, and it changes with the selection:
/// nothing (or an ordinary building) selected: the build menu, in four tabs;
/// a Barracks: its army, its queue and its rally point, so every Barracks
/// trains on its own; a Scriptorium: research; soldiers: who's selected and
/// their orders. Buttons only send Commands or change ClientState.
/// </summary>
public partial class CommandCard : PanelContainer
{
    public World World = null!;
    public ClientState State = null!;
    public Action<Command> Send = null!;
    /// <summary>Order the selected soldiers (Main owns the arming and cursor).</summary>
    public Action<string> Order = null!;

    static readonly (string Name, BuildingKind[] Kinds)[] Tabs =
    [
        ("Town", [BuildingKind.House, BuildingKind.Farm, BuildingKind.Hunter, BuildingKind.Fishery, BuildingKind.Woodcutter, BuildingKind.Quarry]),
        ("Works", [BuildingKind.Mine, BuildingKind.SilverMine, BuildingKind.Barracks, BuildingKind.Scriptorium]),
        ("Holy", [BuildingKind.Shrine, BuildingKind.Wardstone]),
        ("Walls", [BuildingKind.Wall, BuildingKind.StoneWall, BuildingKind.Gate]),
        ("Towers", [BuildingKind.Watchtower, BuildingKind.Bombard, BuildingKind.LanceTower, BuildingKind.Censer, BuildingKind.Belfry, BuildingKind.Skyspire]),
    ];

    int _tab;
    string _mode = "";
    VBoxContainer _body = null!;
    readonly List<(Button Button, BuildingKind Kind)> _build = new();
    readonly List<(Button Button, UnitKind Kind)> _train = new();
    readonly List<Button> _queue = new();
    readonly List<Button> _tabs = new();
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
        if (b is { Def.Trains.Length: > 0, Complete: true }) return $"barracks-{b.Id}";
        if (b is { Def.Researches: true, Complete: true }) return $"lab-{b.Id}-{b.Researching}-{string.Join(",", World.Tech.Researched)}";
        if (State.SelectedUnits.Count > 0) return $"army-{State.SelectedUnits.Count}";
        return $"build-{_tab}";
    }

    public override void _Process(double delta)
    {
        string mode = ModeNow();
        if (mode != _mode) Rebuild(mode);
        UpdateStates();
    }

    void Clear()
    {
        foreach (var c in _body.GetChildren()) c.QueueFree();
        _build.Clear();
        _train.Clear();
        _queue.Clear();
        _tabs.Clear();
        _progress = null;
    }

    void Rebuild(string mode)
    {
        _mode = mode;
        Clear();
        _title = UiKit.Label("", 14, UiKit.Gold);
        _body.AddChild(_title);
        var b = State.SelectedBuilding is { } id ? World.BuildingById(id) : null;
        if (mode.StartsWith("barracks")) BuildBarracks(b!);
        else if (mode.StartsWith("lab")) BuildLab(b!);
        else if (mode.StartsWith("army")) BuildArmy();
        else BuildMenu();
        _hint = UiKit.Label("", 12, UiKit.Muted);
        _body.AddChild(_hint);
    }

    // --- build menu ---

    void BuildMenu()
    {
        _title.Text = "Build";
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        for (int i = 0; i < Tabs.Length; i++)
        {
            int t = i;
            var tab = UiKit.TextButton(Tabs[i].Name, 13);
            tab.ToggleMode = true;
            tab.ButtonPressed = i == _tab;
            tab.Pressed += () => { _tab = t; _mode = ""; };
            tabs.AddChild(tab);
            _tabs.Add(tab);
        }
        _body.AddChild(tabs);
        var grid = new HBoxContainer();
        grid.AddThemeConstantOverride("separation", 4);
        foreach (var kind in Tabs[_tab].Kinds)
        {
            string key = Palette.BuildBar.FirstOrDefault(x => x.Kind == kind).Label ?? "";
            var button = UiKit.IconButton(UiKit.Building(kind), key);
            var k = kind;
            button.Pressed += () => State.Armed = State.Armed == k ? null : k;
            grid.AddChild(button);
            _build.Add((button, kind));
        }
        _body.AddChild(grid);
    }

    // --- a Barracks: the army ---

    void BuildBarracks(Building b)
    {
        _title.Text = $"{b.Kind}: train";
        var grid = new HBoxContainer();
        grid.AddThemeConstantOverride("separation", 4);
        for (int i = 0; i < b.Def.Trains.Length; i++)
        {
            var kind = b.Def.Trains[i];
            string key = i < Main.TrainKeys.Length ? Main.TrainKeys[i].ToString() : "";
            var button = UiKit.IconButton(UiKit.Unit(kind), key);
            int bid = b.Id;
            // Shift-click queues five.
            button.Pressed += () =>
            {
                int n = Input.IsKeyPressed(Key.Shift) ? 5 : 1;
                for (int j = 0; j < n; j++) Send(new TrainUnit(bid, kind));
            };
            grid.AddChild(button);
            _train.Add((button, kind));
        }
        _body.AddChild(grid);

        var queue = new HBoxContainer();
        queue.AddThemeConstantOverride("separation", 3);
        queue.AddChild(UiKit.Label("Queue", 12, UiKit.Muted));
        for (int i = 0; i < Balance.QueueLimit; i++)
        {
            int slot = i;
            var s = UiKit.IconButton(null, "", 34);
            int bid = b.Id;
            s.Pressed += () => Send(new CancelTraining(bid, slot));
            queue.AddChild(s);
            _queue.Add(s);
        }
        _body.AddChild(queue);
        _progress = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6) };
        _progress.AddThemeStyleboxOverride("background", UiKit.Box(new Color(0.24f, 0.23f, 0.21f), null, 3, 0));
        _progress.AddThemeStyleboxOverride("fill", UiKit.Box(UiKit.Gold, UiKit.Gold, 3, 0));
        _body.AddChild(_progress);

        var rally = new HBoxContainer();
        rally.AddThemeConstantOverride("separation", 6);
        var clear = UiKit.TextButton("Clear rally point", 12);
        int id = b.Id;
        clear.Pressed += () => Send(new SetRally(id, -1, 0));
        rally.AddChild(clear);
        _body.AddChild(rally);
    }

    // --- a Scriptorium ---

    void BuildLab(Building b)
    {
        _title.Text = b.Researching is { } r ? $"Researching {World.Rules.Tech(r).Name}" : "Research";
        if (b.Researching != null)
        {
            _progress = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) };
            _progress.AddThemeStyleboxOverride("background", UiKit.Box(new Color(0.24f, 0.23f, 0.21f), null, 3, 0));
            _progress.AddThemeStyleboxOverride("fill", UiKit.Box(UiKit.Gold, UiKit.Gold, 3, 0));
            _body.AddChild(_progress);
            return;
        }
        var flow = new HFlowContainer { CustomMinimumSize = new Vector2(540, 0) };
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);
        foreach (var tech in World.Rules.Techs.Where(t => World.CheckResearch(t.Id) == null))
        {
            var button = UiKit.TextButton($"{tech.Name}  ·  tier {tech.Tier}", 12);
            button.TooltipText = $"{tech.Name}: {tech.Cost}, {tech.Seconds:0} s\n{tech.Description}";
            int bid = b.Id;
            string techId = tech.Id;
            button.Pressed += () => Send(new Research(bid, techId));
            flow.AddChild(button);
        }
        _body.AddChild(flow);
    }

    // --- soldiers ---

    void BuildArmy()
    {
        var units = World.Units.Where(u => State.SelectedUnits.Contains(u.Id)).ToList();
        _title.Text = $"{units.Count} soldier{(units.Count == 1 ? "" : "s")}";
        var groups = new HBoxContainer();
        groups.AddThemeConstantOverride("separation", 4);
        foreach (var g in units.GroupBy(u => u.Kind).OrderBy(g => g.Key))
        {
            var kind = g.Key;
            var b = UiKit.IconButton(UiKit.Unit(kind), $"{g.Count()}", 48);
            b.TooltipText = $"{kind}: click to select only these";
            b.Pressed += () =>
            {
                State.SelectedUnits.Clear();
                foreach (var u in World.Units.Where(u => u.Kind == kind && units.Contains(u))) State.SelectedUnits.Add(u.Id);
            };
            groups.AddChild(b);
        }
        _body.AddChild(groups);
        var orders = new HBoxContainer();
        orders.AddThemeConstantOverride("separation", 4);
        foreach (var (label, order) in new[] { ("A  Attack-move", "attack"), ("Z  Patrol", "patrol"), ("H  Hold", "hold"), ("Shift+S  Stop", "stop") })
        {
            var o = order;
            var b = UiKit.TextButton(label, 12);
            b.Pressed += () => Order(o);
            orders.AddChild(b);
        }
        _body.AddChild(orders);
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
            button.TooltipText = $"{kind}: {Blurbs.Of(kind)}\n{def.Cost} · {def.Hp:0} hp · {def.BuildSeconds:0} s to build{Hud.Describe(def)}" + (mission ? "\nnot in this mission" : locked ? $"\nneeds {World.Rules.Tech(def.RequiresTech!).Name}" : affordable ? "" : "\nyou can't afford it yet");
        }
        foreach (var t in _tabs) t.ButtonPressed = _tabs.IndexOf(t) == _tab;

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
                button.TooltipText = $"{kind}: {Blurbs.Of(kind)}\n{def.Cost}\n{def.Hp:0} hp, range {def.Weapon.Range}, {def.Weapon.Damage:0} dmg every {def.Weapon.Cooldown}s" +
                    (mission ? "\nnot in this mission" : locked ? $"\nneeds {World.Rules.Tech(def.RequiresTech!).Name}" : "") + "\nshift-click: five";
            }
            for (int i = 0; i < _queue.Count; i++)
            {
                bool filled = i < b.Queue.Count;
                _queue[i].Icon = filled ? UiKit.Unit(b.Queue[i]) : null;
                _queue[i].TooltipText = filled ? $"{b.Queue[i]}: click to cancel (full refund)" : "";
                _queue[i].Modulate = filled ? Colors.White : new Color(1, 1, 1, 0.35f);
            }
            if (_progress != null) _progress.Value = b.Queue.Count == 0 ? 0 : b.TrainProgress / Math.Max(0.01f, World.Def(b.Queue[0]).TrainSeconds);
            _hint.Text = b.RallyX >= 0 ? "Right-click the ground to move the rally point" : "Right-click the ground to set a rally point";
        }
        else if (b is { Researching: { } r } && _progress != null)
        {
            _progress.Value = b.ResearchProgress / World.Rules.Tech(r).Seconds;
            _hint.Text = World.Rules.Tech(r).Description;
        }
        else if (_mode.StartsWith("build"))
        {
            _hint.Text = State.Armed is { } k ? $"Placing {k}: click to build, drag walls for a line, right-click to stop" : "Number keys pick a building too";
        }
        else if (_mode.StartsWith("army"))
        {
            _hint.Text = "Right-click to attack-move · shift+right-click to move";
        }
    }
}
