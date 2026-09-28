using Godot;
using Hellwall.Sim;
using Resource = Hellwall.Sim.Resource;
using Side = Hellwall.Sim.Side;

namespace Hellwall.Game;

/// <summary>
/// The map editor's mission settings, on the right: made a mission, a hand-made map plays by its
/// own rules (briefing, difficulty, days, waves, stock, what's locked), its own goals, and its own
/// events (on a day or after a goal: a message, a raid, a gift), the same as a campaign mission's.
/// Off, it's a plain map, and Skirmish's settings apply. Where the Keep stands and the buildings
/// already standing are the editor's tools (the map), not these.
/// </summary>
public partial class MissionPanel : PanelContainer
{
    /// <summary>The panel's width: everything in it is kept narrower, so nothing runs off the screen's edge.</summary>
    public const float Width = 440;

    CheckButton _on = null!;
    VBoxContainer _body = null!;
    TextEdit _brief = null!;
    OptionButton _difficulty = null!;
    SpinBox _days = null!, _waves = null!, _convergence = null!, _gates = null!, _ruins = null!, _strays = null!;
    CheckButton _fog = null!, _woods = null!, _setStock = null!;
    readonly CheckButton[] _sides = new CheckButton[4];
    readonly SpinBox[] _stock = new SpinBox[6];
    VBoxContainer _goals = null!, _events = null!;
    readonly List<(OptionButton Kind, SpinBox Count, SpinBox Day, Control Row)> _goalRows = new();
    readonly List<EventRow> _eventRows = new();
    readonly Dictionary<BuildingKind, CheckButton> _lockedBuildings = new();
    readonly Dictionary<UnitKind, CheckButton> _lockedUnits = new();

    static readonly ObjectiveKind[] GoalKinds = [ObjectiveKind.Survive, ObjectiveKind.CloseGates, ObjectiveKind.Population, ObjectiveKind.Slay, ObjectiveKind.LootRuins];
    static readonly string[] GoalNames = ["Survive", "Close gates", "Colonists", "Slay demons", "Loot ruins"];
    static readonly Resource[] Stock = Enum.GetValues<Resource>();

    sealed class EventRow
    {
        public Control Row = null!;
        public OptionButton When = null!, Speaker = null!, RaidKind = null!, RaidSide = null!;
        public SpinBox At = null!, RaidCount = null!;
        public LineEdit Say = null!;
        public readonly SpinBox[] Gift = new SpinBox[4]; // gold, wood, stone, food
    }

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UiKit.Box(UiKit.Panel, UiKit.Rim, 6, 8));
        CustomMinimumSize = new Vector2(Width, 0);
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 6);
        AddChild(outer);
        outer.AddChild(UiKit.Label("Mission", 18, UiKit.Gold));
        _on = new CheckButton { Text = "Play as a mission", TooltipText = "Its own briefing, rules, goals and events, instead of Skirmish's settings", FocusMode = FocusModeEnum.None };
        _on.Toggled += on => _body.Visible = on;
        outer.AddChild(_on);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        outer.AddChild(scroll);
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Visible = false };
        _body.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_body);

        Head("Briefing (read out as it begins)");
        _brief = new TextEdit { CustomMinimumSize = new Vector2(0, 90), WrapMode = TextEdit.LineWrappingMode.Boundary, PlaceholderText = "What the player is told: the hook, and what's coming." };
        _body.AddChild(_brief);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 8);
        _body.AddChild(grid);
        _difficulty = new OptionButton();
        foreach (var d in Enum.GetNames<Difficulty>()) _difficulty.AddItem(d);
        _difficulty.Selected = (int)Difficulty.Normal;
        Field(grid, "Difficulty", _difficulty);
        _days = Spin(grid, "Days (the Convergence at the end)", 1, 200, 1, 30);
        _waves = Spin(grid, "Waves (x their usual size)", 0, 5, 0.1, 1);
        _convergence = Spin(grid, "Convergence (x its size)", 0, 5, 0.1, 1);
        _gates = Spin(grid, "Hellgates at random (-1: the usual)", -1, 8, 1, -1);
        _ruins = Spin(grid, "Ruins (-1: the usual)", -1, 20, 1, -1);
        _strays = Spin(grid, "Stragglers (-1: the usual)", -1, 500, 1, -1);
        _fog = new CheckButton { Text = "Fog of war", ButtonPressed = true, FocusMode = FocusModeEnum.None };
        _body.AddChild(_fog);
        _woods = new CheckButton { Text = "Living woods", TooltipText = "Forest is a wall, and woodsmen fell it", FocusMode = FocusModeEnum.None };
        _body.AddChild(_woods);

        Head("Waves come from (none ticked: the map's own sides)");
        var sides = new HBoxContainer();
        for (int i = 0; i < 4; i++)
        {
            _sides[i] = new CheckButton { Text = ((Side)i).ToString(), FocusMode = FocusModeEnum.None };
            _sides[i].AddThemeFontSizeOverride("font_size", 12);
            sides.AddChild(_sides[i]);
        }
        _body.AddChild(sides);

        _setStock = new CheckButton { Text = "Starting stock of its own", FocusMode = FocusModeEnum.None };
        _body.AddChild(_setStock);
        var stock = new GridContainer { Columns = 3 };
        for (int i = 0; i < Stock.Length; i++)
        {
            var cell = new VBoxContainer();
            cell.AddChild(UiKit.Label(Stock[i].ToString(), 11, UiKit.Muted));
            _stock[i] = new SpinBox { MinValue = 0, MaxValue = 100000, Step = 10, Value = Rules.Default.StartingResources[Stock[i]], SelectAllOnFocus = true };
            cell.AddChild(_stock[i]);
            stock.AddChild(cell);
        }
        _body.AddChild(stock);
        _setStock.Toggled += on => stock.Visible = on;
        stock.Visible = false;

        Head("Goals (all to win; none: survive)");
        _goals = new VBoxContainer();
        _body.AddChild(_goals);
        var addGoal = UiKit.TextButton("Add a goal", 13);
        addGoal.Pressed += () => AddGoal(new ObjectiveDef { Kind = ObjectiveKind.Survive });
        _body.AddChild(addGoal);

        Head("Events (on a day, or once a goal is done)");
        _events = new VBoxContainer();
        _events.AddThemeConstantOverride("separation", 8);
        _body.AddChild(_events);
        var addEvent = UiKit.TextButton("Add an event", 13);
        addEvent.Pressed += () => AddEvent(new TriggerDef { Day = 2 });
        _body.AddChild(addEvent);

        Head("Not in this mission");
        var locks = new GridContainer { Columns = 2 };
        foreach (var kind in Enum.GetValues<BuildingKind>())
        {
            var def = Rules.Default[kind];
            if (kind == BuildingKind.Keep || def.UpgradeOnly) continue;
            var b = new CheckButton { Text = kind.ToString(), FocusMode = FocusModeEnum.None };
            b.AddThemeFontSizeOverride("font_size", 12);
            _lockedBuildings[kind] = b;
            locks.AddChild(b);
        }
        foreach (var kind in Enum.GetValues<UnitKind>())
        {
            var b = new CheckButton { Text = kind.ToString(), FocusMode = FocusModeEnum.None };
            b.AddThemeFontSizeOverride("font_size", 12);
            _lockedUnits[kind] = b;
            locks.AddChild(b);
        }
        _body.AddChild(locks);
        Narrow(this);
    }

    /// <summary>Dropdowns as wide as the room they're given, not their longest item: nothing pushes the panel past the screen's edge.</summary>
    static void Narrow(Node root)
    {
        foreach (var o in root.FindChildren("*", nameof(OptionButton), true, false).OfType<OptionButton>())
        {
            o.FitToLongestItem = false;
            o.ClipText = true;
        }
    }

    void Head(string text) => _body.AddChild(UiKit.Label(text, 13, UiKit.Muted));

    static void Field(GridContainer grid, string label, Control control)
    {
        var l = UiKit.Label(label, 12);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(170, 0);
        grid.AddChild(l);
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        grid.AddChild(control);
    }

    static SpinBox Spin(GridContainer grid, string label, double min, double max, double step, double value)
    {
        var s = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, SelectAllOnFocus = true };
        Field(grid, label, s);
        return s;
    }

    static SpinBox Small(double min, double max, double value, string tip)
    {
        var s = new SpinBox { MinValue = min, MaxValue = max, Step = 1, Value = value, SelectAllOnFocus = true, TooltipText = tip, CustomMinimumSize = new Vector2(70, 0) };
        s.GetLineEdit().CustomMinimumSize = Vector2.Zero;
        return s;
    }

    void AddGoal(ObjectiveDef g)
    {
        var row = new HBoxContainer();
        var kind = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(110, 0) };
        foreach (var n in GoalNames) kind.AddItem(n);
        kind.Selected = Math.Max(0, Array.IndexOf(GoalKinds, g.Kind));
        var count = Small(0, 100000, g.Count, "How many (gates, colonists, demons, ruins)");
        var day = Small(0, 400, g.Day, "By the end of this day (0: any time)");
        var remove = UiKit.TextButton("x", 12);
        row.AddChild(kind);
        row.AddChild(count);
        row.AddChild(UiKit.Label("by day", 11, UiKit.Muted));
        row.AddChild(day);
        row.AddChild(remove);
        _goals.AddChild(row);
        Narrow(row);
        var entry = (kind, count, day, (Control)row);
        _goalRows.Add(entry);
        remove.Pressed += () => { _goalRows.Remove(entry); row.QueueFree(); };
    }

    void AddEvent(TriggerDef t)
    {
        var e = new EventRow();
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        var when = new HBoxContainer();
        e.When = new OptionButton { CustomMinimumSize = new Vector2(110, 0) };
        e.When.AddItem("On day");
        e.When.AddItem("After goal");
        e.When.Selected = t.AfterGoal >= 0 ? 1 : 0;
        e.At = Small(0, 400, t.AfterGoal >= 0 ? t.AfterGoal + 1 : t.Day, "The day it happens, or which goal (1 is the first) it follows");
        e.Speaker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Who says it" };
        foreach (var s in Campaign.Default.Speakers) e.Speaker.AddItem(s.Name);
        e.Speaker.Selected = Math.Max(0, Array.FindIndex(Campaign.Default.Speakers, s => s.Id == t.Speaker));
        var remove = UiKit.TextButton("x", 12);
        when.AddChild(e.When);
        when.AddChild(e.At);
        when.AddChild(e.Speaker);
        when.AddChild(remove);
        box.AddChild(when);
        e.Say = new LineEdit { Text = t.Say, PlaceholderText = "What's said (optional)" };
        box.AddChild(e.Say);
        var raid = new HBoxContainer();
        raid.AddChild(UiKit.Label("Raid", 12, UiKit.Muted));
        e.RaidCount = Small(0, 5000, t.SpawnCount, "How many come (0: no raid); told 45 seconds ahead");
        e.RaidKind = new OptionButton { CustomMinimumSize = new Vector2(110, 0) };
        foreach (var k in Enum.GetNames<DemonKind>()) e.RaidKind.AddItem(k);
        e.RaidKind.Selected = (int)t.SpawnKind;
        e.RaidSide = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var s in Enum.GetNames<Side>()) e.RaidSide.AddItem("from the " + s.ToLowerInvariant());
        e.RaidSide.Selected = (int)t.SpawnSide;
        raid.AddChild(e.RaidCount);
        raid.AddChild(e.RaidKind);
        raid.AddChild(e.RaidSide);
        box.AddChild(raid);
        box.AddChild(UiKit.Label("Gift", 12, UiKit.Muted));
        var gift = new GridContainer { Columns = 4 };
        Resource[] giftKinds = [Resource.Gold, Resource.Wood, Resource.Stone, Resource.Food];
        for (int i = 0; i < 4; i++)
        {
            gift.AddChild(UiKit.Label(giftKinds[i].ToString(), 12));
            e.Gift[i] = Small(0, 100000, t.Give?[giftKinds[i]] ?? 0, giftKinds[i].ToString().ToLowerInvariant());
            gift.AddChild(e.Gift[i]);
        }
        box.AddChild(gift);
        box.AddChild(new HSeparator());
        _events.AddChild(box);
        Narrow(box);
        e.Row = box;
        _eventRows.Add(e);
        remove.Pressed += () => { _eventRows.Remove(e); box.QueueFree(); };
    }

    /// <summary>The map with this mission's settings (or none, when it isn't one).</summary>
    public ScenarioDef Apply(ScenarioDef map)
    {
        if (!_on.ButtonPressed) return map with { IsMission = false };
        var goals = _goalRows.Select(g => new ObjectiveDef { Kind = GoalKinds[g.Kind.Selected], Count = (int)g.Count.Value, Day = (int)g.Day.Value }).ToArray();
        Resource[] giftKinds = [Resource.Gold, Resource.Wood, Resource.Stone, Resource.Food];
        var events = _eventRows.Select(e =>
        {
            bool afterGoal = e.When.Selected == 1;
            var gift = new Cost { Gold = e.Gift[0].Value, Wood = e.Gift[1].Value, Stone = e.Gift[2].Value, Food = e.Gift[3].Value };
            return new TriggerDef
            {
                Day = afterGoal ? 0 : Math.Max(1, (int)e.At.Value),
                AfterGoal = afterGoal ? Math.Clamp((int)e.At.Value - 1, 0, Math.Max(0, goals.Length - 1)) : -1,
                Say = e.Say.Text.Trim(),
                Speaker = Campaign.Default.Speakers.Length > 0 ? Campaign.Default.Speakers[Math.Max(0, e.Speaker.Selected)].Id : "",
                SpawnCount = (int)e.RaidCount.Value,
                SpawnKind = (DemonKind)e.RaidKind.Selected,
                SpawnSide = (Side)e.RaidSide.Selected,
                Give = giftKinds.Any(k => gift[k] > 0) ? gift : null,
            };
        }).ToArray();
        return map with
        {
            IsMission = true,
            Briefing = _brief.Text.Trim(),
            Difficulty = (Difficulty)_difficulty.Selected,
            Days = (int)_days.Value,
            Waves = _waves.Value,
            Convergence = _convergence.Value,
            Hellgates = (int)_gates.Value,
            Ruins = (int)_ruins.Value,
            Strays = (int)_strays.Value,
            Fog = _fog.ButtonPressed,
            LivingWoods = _woods.ButtonPressed,
            WaveSides = Enumerable.Range(0, 4).Where(i => _sides[i].ButtonPressed).Select(i => (Side)i).ToArray(),
            Start = _setStock.ButtonPressed ? new Cost { Gold = _stock[0].Value, Wood = _stock[1].Value, Stone = _stock[2].Value, Food = _stock[3].Value, Iron = _stock[4].Value, Silver = _stock[5].Value } : null,
            Objectives = goals,
            Triggers = events,
            LockedBuildings = _lockedBuildings.Where(p => p.Value.ButtonPressed).Select(p => p.Key).ToArray(),
            LockedUnits = _lockedUnits.Where(p => p.Value.ButtonPressed).Select(p => p.Key).ToArray(),
        };
    }

    /// <summary>Show a map's mission settings (a plain map: the defaults, and off).</summary>
    public void Load(ScenarioDef m)
    {
        foreach (var g in _goalRows) g.Row.QueueFree();
        _goalRows.Clear();
        foreach (var e in _eventRows) e.Row.QueueFree();
        _eventRows.Clear();
        _on.ButtonPressed = m.IsMission;
        _body.Visible = m.IsMission;
        _brief.Text = m.Briefing;
        _difficulty.Selected = (int)m.Difficulty;
        _days.Value = m.Days > 0 ? m.Days : 30;
        _waves.Value = m.Waves;
        _convergence.Value = m.Convergence;
        _gates.Value = m.Hellgates;
        _ruins.Value = m.Ruins;
        _strays.Value = m.Strays;
        _fog.ButtonPressed = m.Fog;
        _woods.ButtonPressed = m.LivingWoods;
        for (int i = 0; i < 4; i++) _sides[i].ButtonPressed = m.WaveSides.Contains((Side)i);
        _setStock.ButtonPressed = m.Start != null;
        for (int i = 0; i < Stock.Length; i++) _stock[i].Value = (m.Start ?? Rules.Default.StartingResources)[Stock[i]];
        foreach (var g in m.Objectives) AddGoal(g);
        foreach (var t in m.Triggers) AddEvent(t);
        foreach (var (kind, b) in _lockedBuildings) b.ButtonPressed = m.Locks(kind);
        foreach (var (kind, b) in _lockedUnits) b.ButtonPressed = m.Locks(kind);
    }

    /// <summary>For the editor's self-test: made a mission with a goal and an event.</summary>
    public void SelfTestFill()
    {
        _on.ButtonPressed = true;
        _brief.Text = "Hold the ford.";
        _days.Value = 12;
        AddGoal(new ObjectiveDef { Kind = ObjectiveKind.Slay, Count = 50 });
        AddEvent(new TriggerDef { Day = 3, Say = "They're coming", SpawnCount = 20, SpawnKind = DemonKind.Hound, SpawnSide = Side.East });
        _lockedBuildings[BuildingKind.Bombard].ButtonPressed = true;
    }
}
