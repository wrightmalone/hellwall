using Godot;
using Hellwall.Sim;
using Resource = Hellwall.Sim.Resource;

namespace Hellwall.Game;

/// <summary>The thin strip across the top: every resource with its rate, the workforce, holy power, and the game speed.</summary>
public partial class ResourceBar : PanelContainer
{
    public World World = null!;
    public Func<string> Speed = null!;

    readonly Dictionary<Resource, (Label Amount, Label Rate)> _res = new();
    Label _people = null!, _holy = null!, _speed = null!, _starving = null!;
    HolyMeter _meter = null!;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", UiKit.Box(UiKit.Panel, UiKit.Rim, 0, 4));
        MouseFilter = MouseFilterEnum.Stop;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        AddChild(row);
        foreach (var r in Enum.GetValues<Resource>())
        {
            var cell = new HBoxContainer { TooltipText = Tip(r) };
            cell.AddThemeConstantOverride("separation", 4);
            cell.AddChild(new TextureRect { Texture = UiKit.Resource(r), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(24, 24), MouseFilter = MouseFilterEnum.Pass });
            var amount = UiKit.Label("", 15);
            var rate = UiKit.Label("", 12, UiKit.Muted);
            cell.AddChild(amount);
            cell.AddChild(rate);
            row.AddChild(cell);
            _res[r] = (amount, rate);
        }
        var people = new HBoxContainer { TooltipText = "Colonists at work / colonists. Houses hold them; every building with a crew needs them." };
        people.AddChild(new TextureRect { Texture = UiKit.Unit(UnitKind.Militia), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(24, 24), MouseFilter = MouseFilterEnum.Pass });
        _people = UiKit.Label("", 15);
        people.AddChild(_people);
        row.AddChild(people);

        var holy = new HBoxContainer { TooltipText = "Sanctity used / supplied. Shrines supply it; every tower, workplace and Barracks draws it. Short, and everything slows." };
        holy.AddThemeConstantOverride("separation", 5);
        holy.AddChild(new TextureRect { Texture = UiKit.Holy, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(24, 24), MouseFilter = MouseFilterEnum.Pass });
        _meter = new HolyMeter { CustomMinimumSize = new Vector2(70, 10), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Pass };
        holy.AddChild(_meter);
        _holy = UiKit.Label("", 13, UiKit.Muted);
        holy.AddChild(_holy);
        row.AddChild(holy);

        _starving = UiKit.Label("STARVING", 13, UiKit.Threat);
        row.AddChild(_starving);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _speed = UiKit.Label("", 13, UiKit.Muted);
        row.AddChild(_speed);
    }

    static string Tip(Resource r) => r switch
    {
        Resource.Gold => "Gold: colonists' tithes. Pays for nearly everything.",
        Resource.Wood => "Wood: Woodcutters beside forest. Walls and building.",
        Resource.Stone => "Stone: Quarries beside rock. Towers, Shrines, research.",
        Resource.Food => "Food: Farms and Hunters. Colonists and soldiers eat it.",
        Resource.Iron => "Iron: Mines on ore (the purple crystals). Every soldier is made of it.",
        _ => "Silver: Silver Mines on the pale veins near the map's edge. The Exorcist is made of it.",
    };

    public override void _Process(double delta)
    {
        var c = World.Colony;
        foreach (var (r, (amount, rate)) in _res)
        {
            amount.Text = UiKit.Money(c[r]);
            double net = c.NetPerSecond[(int)r];
            rate.Text = net >= 0.05 ? $"+{net:0.0}" : net <= -0.05 ? $"{net:0.0}" : "";
            rate.AddThemeColorOverride("font_color", net < 0 ? UiKit.Threat : UiKit.Good);
        }
        _people.Text = $"{c.WorkersUsed}/{c.Colonists}";
        _meter.Demand = c.SanctityDemand;
        _meter.Supply = c.SanctitySupply;
        _meter.QueueRedraw();
        _holy.Text = $"{c.SanctityDemand:0}/{c.SanctitySupply:0}" + (c.Power < 1 ? $"  {c.Power:P0} power" : "");
        _holy.AddThemeColorOverride("font_color", c.Power < 1 ? UiKit.Threat : UiKit.Muted);
        _starving.Visible = c.Starving;
        _speed.Text = Speed();
    }

    sealed partial class HolyMeter : Control
    {
        public double Demand, Supply;

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            DrawRect(r, new Color(0.25f, 0.24f, 0.22f));
            float f = Supply <= 0 ? 1 : (float)Math.Min(1, Demand / Supply);
            DrawRect(new Rect2(0, 0, Size.X * f, Size.Y), Demand > Supply ? UiKit.Threat : UiKit.Gold);
        }
    }
}
