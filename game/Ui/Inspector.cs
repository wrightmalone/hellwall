using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>Bottom right: the selected building's health, state, output and the demolish button; or the selected soldiers' health.</summary>
public partial class Inspector : PanelContainer
{
    public World World = null!;
    public ClientState State = null!;
    public Action<Command> Send = null!;

    Label _name = null!, _status = null!, _detail = null!;
    ProgressBar _hp = null!;
    Button _demolish = null!;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UiKit.Box(UiKit.Panel, UiKit.Rim, 6, 8));
        CustomMinimumSize = new Vector2(280, 0);
        MouseFilter = MouseFilterEnum.Stop;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        AddChild(box);
        _name = UiKit.Label("", 16);
        box.AddChild(_name);
        _hp = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) };
        _hp.AddThemeStyleboxOverride("background", UiKit.Box(new Color(0.24f, 0.23f, 0.21f), null, 3, 0));
        _hp.AddThemeStyleboxOverride("fill", UiKit.Box(UiKit.Good, UiKit.Good, 3, 0));
        box.AddChild(_hp);
        _status = UiKit.Label("", 13);
        box.AddChild(_status);
        _detail = UiKit.Label("", 12, UiKit.Muted);
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_detail);
        _demolish = UiKit.TextButton("X  Demolish", 12);
        _demolish.Pressed += () => { if (State.SelectedBuilding is { } id) Send(new Demolish(id)); };
        box.AddChild(_demolish);
    }

    public override void _Process(double delta)
    {
        var b = State.SelectedBuilding is { } id ? World.BuildingById(id) : null;
        if (b == null && State.SelectedBuilding != null) State.SelectedBuilding = null;
        State.SelectedUnits.RemoveWhere(u => World.UnitById(u) == null);
        if (b != null)
        {
            Visible = true;
            _name.Text = b.Kind.ToString();
            _hp.Value = b.Hp / b.Def.Hp;
            _status.Text =
                b.Possessed ? $"POSSESSED: {b.Occupants} still inside. Demolish to purge it" :
                !b.Complete ? $"Under construction, {b.Built / Math.Max(0.001f, b.Def.BuildSeconds):P0}" :
                !b.OnGround ? "Dark: not on holy ground" :
                b.NeedsCrew && !b.Staffed ? $"Idle: needs {b.Def.Workers} workers" :
                "Working";
            _status.AddThemeColorOverride("font_color", b.Possessed || !b.OnGround || (b.NeedsCrew && !b.Staffed) ? UiKit.Threat : UiKit.Text);
            var d = new List<string>();
            if (b.Def.Produces is { } r && b.Complete) d.Add($"{b.Rate * World.Colony.Power:0.00} {r.ToString().ToLowerInvariant()} a second");
            if (b.Def.Weapon is { } w) d.Add($"range {w.Range:0.#}, {w.Damage:0} damage every {w.Cooldown:0.##} s");
            if (b.Def.Housing > 0) d.Add($"houses {b.Def.Housing}");
            if (b.Def.SanctitySupply > 0) d.Add($"supplies {b.Def.SanctitySupply:0} sanctity");
            if (b.Def.SanctityUse > 0) d.Add($"draws {b.Def.SanctityUse:0} sanctity");
            _detail.Text = string.Join("\n", d);
            _demolish.Visible = b.Kind != BuildingKind.Keep;
            _demolish.Text = b.Possessed ? "X  Purge" : "X  Demolish";
        }
        else if (State.SelectedUnits.Count > 0)
        {
            Visible = true;
            var units = World.Units.Where(u => State.SelectedUnits.Contains(u.Id)).ToList();
            _name.Text = units.Count == 1 ? units[0].Kind.ToString() : $"{units.Count} soldiers";
            float hp = units.Sum(u => u.Hp), max = units.Sum(u => u.Def.Hp);
            _hp.Value = max <= 0 ? 0 : hp / max;
            _status.Text = $"{hp:0} / {max:0} hp";
            _status.AddThemeColorOverride("font_color", UiKit.Text);
            _detail.Text = units.Count == 1 ? $"{units[0].Order}" : string.Join(", ", units.GroupBy(u => u.Order).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"));
            _demolish.Visible = false;
        }
        else Visible = false;
    }
}
