using Godot;
using Hellwall.Sim;
using Side = Hellwall.Sim.Side;

namespace Hellwall.Game;

/// <summary>
/// Top left: what just happened that matters, as cards. Anything with a
/// place is a button that moves the camera there: when a breach starts a
/// cascade, the player has to get to it fast. Repeats within a few seconds
/// fold into one card with a count.
/// </summary>
public partial class AlertFeed : VBoxContainer
{
    public World World = null!;
    public Action<Vector2> JumpTo = null!;

    sealed class Alert
    {
        public required string Key;
        public required string Text;
        public required Color Colour;
        public Vector2? Tile;
        public int Count = 1;
        public double Age;
        public double Life = 9;
        public Button? Button;
    }

    readonly List<Alert> _alerts = new();
    const int Max = 6;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);
        CustomMinimumSize = new Vector2(300, 0);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Push(string key, string text, Color colour, Vector2? tile = null, double life = 9)
    {
        var same = _alerts.FirstOrDefault(a => a.Key == key && a.Age < 5);
        if (same != null)
        {
            same.Count++;
            same.Age = 0;
            same.Tile = tile ?? same.Tile;
            Refresh(same);
            return;
        }
        var alert = new Alert { Key = key, Text = text, Colour = colour, Tile = tile, Life = life };
        var button = UiKit.TextButton("", 13);
        button.Alignment = HorizontalAlignment.Left;
        button.AddThemeStyleboxOverride("normal", UiKit.Box(colour, colour.Lightened(0.2f), 5, 5));
        button.AddThemeStyleboxOverride("hover", UiKit.Box(colour.Lightened(0.12f), UiKit.Gold, 5, 5));
        button.AddThemeColorOverride("font_color", UiKit.Text);
        button.MouseFilter = tile == null ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        button.Pressed += () => { if (alert.Tile is { } t) JumpTo(t); };
        alert.Button = button;
        AddChild(button);
        MoveChild(button, 0);
        _alerts.Insert(0, alert);
        Refresh(alert);
        while (_alerts.Count > Max) Drop(_alerts[^1]);
    }

    void Refresh(Alert a)
    {
        if (a.Button == null) return;
        a.Button.Text = (a.Count > 1 ? $"{a.Text}  x{a.Count}" : a.Text) + (a.Tile != null ? "   (click to go)" : "");
    }

    void Drop(Alert a)
    {
        a.Button?.QueueFree();
        _alerts.Remove(a);
    }

    public override void _Process(double delta)
    {
        foreach (var a in _alerts.ToList())
        {
            a.Age += delta;
            if (a.Age > a.Life) Drop(a);
            else if (a.Button != null) a.Button.Modulate = new Color(1, 1, 1, a.Age > a.Life - 1.5 ? (float)((a.Life - a.Age) / 1.5) : 1);
        }
    }

    static readonly Color Red = UiKit.ThreatDark;
    static readonly Color Amber = new(0.45f, 0.29f, 0.07f, 0.95f);
    static readonly Color Grey = new(0.25f, 0.24f, 0.22f, 0.95f);
    static readonly Color Gold = new(0.42f, 0.34f, 0.12f, 0.95f);
    static readonly Color Violet = new(0.33f, 0.18f, 0.42f, 0.95f);

    /// <summary>Main passes every sim event through; the ones worth a card become one.</summary>
    public void See(SimEvent e)
    {
        switch (e)
        {
            case BuildingPossessed p when World.BuildingById(p.BuildingId) is { } b:
                Push("possessed", $"{p.Kind} possessed: {p.Occupants} turning", Red, new Vector2(b.CentreX, b.CentreY), 14);
                break;
            case BuildingDestroyed { Kind: BuildingKind.Wall or BuildingKind.StoneWall or BuildingKind.Gate or BuildingKind.StoneGate } w:
                Push("breach", "The wall is breached", Amber, new Vector2(w.X + 0.5f, w.Y + 0.5f), 12);
                break;
            case BuildingDestroyed d:
                Push("lost-" + d.Kind, $"{d.Kind} destroyed", Red, new Vector2(d.X + 1, d.Y + 1));
                break;
            case PackWoke p:
                Push("pack", $"A pack of {p.Count} stirs", Grey, new Vector2(p.X + 0.5f, p.Y + 0.5f));
                break;
            case WaveAnnounced w:
                Push("wave", w.Final ? $"THE CONVERGENCE in {UiKit.Clock((w.LandsAtTick - w.Tick) / (double)Balance.TickHz)}: {w.Size}, most from the {UiKit.SideOnScreen(w.Sides[0])}. Harden that side" : $"Wave {w.Number}: {w.Size} from the {string.Join(" and ", w.Sides.Select(UiKit.SideOnScreen))}", Red, EdgeOf(w.Sides[0]), 12);
                break;
            case TechResearched t:
                Push("tech-" + t.TechId, $"Researched {World.Rules.Tech(t.TechId).Name}", Gold);
                break;
            case HellgateClosed g:
                Push("gate", "A Hellgate is closed", Gold, new Vector2(g.X + 1.5f, g.Y + 1.5f));
                break;
            case CorruptionAnnounced c:
                Push("corrupt", $"Corruption coming: {c.Name}. {c.Description}", Violet, null, 14);
                break;
            case ScenarioMessage m when m.Spawned > 0:
                // The line itself is spoken in the talking head; the feed keeps a card to jump to where they're coming from.
                Push("script-" + m.Index, $"{m.Spawned} demons from the {m.Side.ToString().ToLowerInvariant()}", Gold, EdgeOf(m.Side), 16);
                break;
            case PatronOffered:
                Push("patron", "A patron saint offers a blessing: choose one (top of the screen)", Gold, null, 12);
                break;
            case PatronChosen c:
                Push("patron", $"Blessed: {World.Rules.Tech(c.TechId).Name}", Gold, null, 8);
                break;
            case RuinLooted l:
                Push("ruin-" + l.RuinId, $"Ruins looted: {l.Loot}", Gold, new Vector2(l.X, l.Y), 12);
                break;
            case UnitPromoted p:
                Push("rank-" + p.UnitId, $"A {p.Kind} is now {Unit.RankName(p.Rank)}", Gold, new Vector2(p.X, p.Y), 6);
                break;
            case UnitDied { Rose: true } u:
                Push("rose", "A soldier has risen as a Thrall", Red, new Vector2(u.X, u.Y));
                break;
        }
    }

    Vector2 EdgeOf(Side side)
    {
        int n = World.Terrain.Width;
        return side switch
        {
            Side.North => new Vector2(n / 2f, 12),
            Side.South => new Vector2(n / 2f, n - 12),
            Side.West => new Vector2(12, n / 2f),
            _ => new Vector2(n - 12, n / 2f),
        };
    }
}
