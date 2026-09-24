using Godot;
using Hellwall.Sim;
using Resource = Hellwall.Sim.Resource;

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
    PanelContainer _inspector = null!;
    Label _inspectorText = null!;
    HBoxContainer _trainButtons = null!;
    HBoxContainer _buildBar = null!;
    readonly List<(Button Button, BuildingKind Kind)> _buildButtons = new();

    public string DebugText = "";

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

        _buildBar = new HBoxContainer();
        _buildBar.AddThemeConstantOverride("separation", 4);
        AddChild(_buildBar);
        for (int i = 0; i < Palette.BuildBar.Length; i++)
        {
            var kind = Palette.BuildBar[i];
            var def = World.Rules[kind];
            var button = new Button
            {
                Text = $"{Palette.BuildKeys[i]} {kind}",
                TooltipText = $"{kind}\n{def.Cost}{Describe(def)}",
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(92, 34),
            };
            button.AddThemeFontSizeOverride("font_size", 12);
            button.Pressed += () => State.Armed = State.Armed == kind ? null : kind;
            _buildBar.AddChild(button);
            _buildButtons.Add((button, kind));
        }

        _inspector = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(300, 0) };
        var box = new VBoxContainer();
        _inspectorText = new Label();
        _inspectorText.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(_inspectorText);
        _trainButtons = new HBoxContainer();
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
            $"{Res(Resource.Gold)}   {Res(Resource.Wood)}   {Res(Resource.Stone)}   {Res(Resource.Food)}   " +
            $"Colonists {colony.WorkersUsed}/{colony.Colonists} working   Sanctity {colony.SanctityDemand:0}/{colony.SanctitySupply:0}" +
            (colony.Power < 1 ? $"  ({colony.Power:P0} power)" : "") +
            (colony.Starving ? "   STARVING" : "");
        _debug.Text = DebugText;

        _log.Text = string.Join("\n", State.Log.Select(l => l.Text));

        _buildBar.Position = new Vector2(10, screen.Y - 44);
        foreach (var (button, kind) in _buildButtons)
        {
            bool affordable = colony.CanAfford(World.Rules[kind].Cost);
            button.Modulate = State.Armed == kind ? new Color(0.6f, 1, 0.6f) : affordable ? Colors.White : new Color(1, 1, 1, 0.45f);
        }

        UpdateInspector(screen);

        if (World.Outcome != Outcome.Running)
        {
            _banner.Visible = true;
            _banner.Text = World.Outcome == Outcome.Lost ? "The Keep has fallen" : "The colony endures";
            _banner.Size = new Vector2(screen.X, 60);
            _banner.Position = new Vector2(0, screen.Y * 0.4f);
        }
    }

    static string Signed(double v) => v >= 0 ? $"+{v:0.0}" : $"{v:0.0}";

    int? _inspected;
    int _unitsShown = -1;

    void UpdateInspector(Vector2 screen)
    {
        string text = "";
        var building = State.SelectedBuilding is { } id ? World.BuildingById(id) : null;
        if (building == null && State.SelectedBuilding != null) State.SelectedBuilding = null;

        if (building != null)
        {
            var b = building;
            string status =
                !b.Complete ? $"Under construction {b.Built / Math.Max(0.001f, b.Def.BuildSeconds):P0}" :
                !b.OnGround ? "Dark: not on consecrated ground" :
                b.NeedsCrew && !b.Staffed ? $"Idle: needs {b.Def.Workers} workers" :
                "Working";
            text = $"{b.Kind}   {b.Hp:0}/{b.Def.Hp:0} hp\n{status}";
            if (b.Def.Produces is { } r && b.Complete) text += $"\n{b.Rate * World.Colony.Power:0.00} {r.ToString().ToLowerInvariant()}/s";
            if (b.Queue.Count > 0)
                text += $"\nTraining {b.Queue[0]} {b.TrainProgress / World.Rules[b.Queue[0]].TrainSeconds:P0}" +
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

        // Rebuild the train buttons only when the selection changes.
        if (_inspected != building?.Id || (building == null && _unitsShown != State.SelectedUnits.Count))
        {
            _inspected = building?.Id;
            _unitsShown = State.SelectedUnits.Count;
            foreach (var child in _trainButtons.GetChildren()) child.QueueFree();
            if (building is { Def.Trains.Length: > 0 })
            {
                string[] keys = ["Q", "E", "R"];
                for (int i = 0; i < building.Def.Trains.Length; i++)
                {
                    var kind = building.Def.Trains[i];
                    var def = World.Rules[kind];
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
        }

        _inspector.Position = new Vector2(screen.X - _inspector.Size.X - 10, screen.Y - _inspector.Size.Y - 54);
    }
}
