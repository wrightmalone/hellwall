using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Hints for a player's first run, one at a time, each shown until the
/// colony shows it's been done (or dismissed). Keys come from the build bar
/// itself, so a hint can't advertise a key that doesn't exist. Finishing (or
/// switching hints off in the menu) is remembered in settings.
/// </summary>
public partial class Coach : PanelContainer
{
    public World World = null!;

    sealed record Tip(string Id, Func<string> Text, Func<bool> Show, Func<bool> Done);

    readonly List<Tip> _tips = new();
    readonly HashSet<string> _finished = new();
    Tip? _current;
    Label _text = null!;
    bool _waveAnnounced, _packInTheWay, _corruption, _unexplored, _promoted, _spat;
    double _check;

    public static bool Enabled => Settings.Get("hints", true);

    int Count(BuildingKind k) => World.Buildings.Count(b => b.Kind == k);
    bool Built(BuildingKind k, int n = 1) => World.Buildings.Count(b => b.Kind == k && b.Complete) >= n;

    /// <summary>A building named in the hint's own words, with its key from the build bar: K(House, "Houses") is "Houses [1]".</summary>
    static string K(BuildingKind kind, string? name = null)
    {
        string label = Palette.BuildBar.FirstOrDefault(b => b.Kind == kind).Label ?? "";
        return label.Length > 0 ? $"{name ?? kind.ToString()} [{label}]" : name ?? kind.ToString();
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(430, 0);
        var box = new VBoxContainer();
        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(410, 0) };
        _text.AddThemeFontSizeOverride("font_size", 15);
        box.AddChild(_text);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var skip = new Button { Text = "Got it", FocusMode = FocusModeEnum.None };
        skip.Pressed += () => { if (_current != null) Finish(_current); };
        var off = new Button { Text = "No more hints", FocusMode = FocusModeEnum.None };
        off.Pressed += () => { Settings.Set("hints", false); QueueFree(); };
        row.AddChild(off);
        row.AddChild(skip);
        box.AddChild(row);
        AddChild(box);

        bool Always() => true;
        _tips.Add(new("houses", () => $"Build two {K(BuildingKind.House, "Houses")}. Everything goes on holy ground (the golden circle round the Keep). Colonists live in Houses, pay gold and work your buildings.",
            Always, () => Built(BuildingKind.House, 2)));
        _tips.Add(new("wood-food", () => $"A {K(BuildingKind.Woodcutter)} beside forest for wood, and a {K(BuildingKind.Farm)} on open grass for food. Gatherers collect from the tiles around them.",
            Always, () => Count(BuildingKind.Woodcutter) > 0 && Count(BuildingKind.Farm) + Count(BuildingKind.Hunter) > 0));
        _tips.Add(new("stone", () => $"A {K(BuildingKind.Quarry)} beside rock for stone. Towers, Shrines and research need it.",
            Always, () => Count(BuildingKind.Quarry) > 0));
        _tips.Add(new("holy", () => $"Everything that works draws sanctity. A {K(BuildingKind.Shrine)} supplies it; a {K(BuildingKind.Wardstone)} pushes holy ground further out, so you can build there.",
            Always, () => Count(BuildingKind.Shrine) > 0 && Count(BuildingKind.Wardstone) > 0));
        _tips.Add(new("wave", () => $"A wave is coming: its side is marked at the screen edge. Drag a line of {K(BuildingKind.Wall, "Walls")} across that side and put {K(BuildingKind.Watchtower, "Watchtowers")} just behind. Demons break the wall nearest their path; one breach and they take every building they reach.",
            () => _waveAnnounced, () => Built(BuildingKind.Watchtower, 2)));
        _tips.Add(new("packs", () => $"Sleeping demons hold that ground, and nothing can be built near them. Train soldiers at a {K(BuildingKind.Barracks)}, select them (drag a box) and right-click beside the pack to clear it. They're loud: build towers first.",
            () => _packInTheWay, () => World.Units.Count >= 6));
        _tips.Add(new("iron", () => $"Soldiers are made of iron. A {K(BuildingKind.Mine)} works the purple ore crystals; the richest lies out in the wilds, behind the packs.",
            () => Count(BuildingKind.Barracks) > 0, () => Count(BuildingKind.Mine) > 0));
        _tips.Add(new("fog", () => "The dark is unexplored ground: nothing can be built there until something of yours has seen it. Soldiers see furthest; send a few out, carefully. Awake demons only show where you can see.",
            () => _unexplored, () => World.Units.Count >= 6));
        _tips.Add(new("veteran", () => "A soldier ranked up. Kills make Veterans, Elites and Champions: each rank hits harder and takes more. Keep them alive.",
            () => _promoted, () => false));
        _tips.Add(new("spitter", () => "Spitters (green) stop at the wall and spit over it at towers and soldiers a few tiles back. Kill them on the way in, or keep towers out of their reach.",
            () => _spat, () => false));
        _tips.Add(new("silver", () => $"Pale silver veins lie only near the map's edge. A {K(BuildingKind.SilverMine)} there pays for Exorcists, your longest-reaching soldiers. It's a long way out: holy ground has to reach it.",
            () => World.Day >= 20, () => Count(BuildingKind.SilverMine) > 0));
        _tips.Add(new("ruins", () => "Ruins (the dark stones, gold on the minimap): old settlements whose people now guard them as Thralls. Clear the guards and walk a soldier in to take what they left.",
            () => World.Ruins.Any(r => !r.Looted && World.Vision.IsExplored(r.X, r.Y)), () => World.Ruins.Any(r => r.Looted)));
        _tips.Add(new("corruption", () => "The horde is being corrupted. Each corruption lasts for the rest of the run, and they add up. The top right lists what the horde has become.",
            () => _corruption, () => false));
    }

    /// <summary>Main passes every sim event through.</summary>
    public void See(SimEvent e)
    {
        switch (e)
        {
            case WaveAnnounced: _waveAnnounced = true; break;
            case CommandRejected r when r.Reason.Contains("demons sleep nearby"): _packInTheWay = true; break;
            case CorruptionAnnounced: _corruption = true; break;
            case CommandRejected r when r.Reason.Contains("unexplored"): _unexplored = true; break;
            case UnitPromoted: _promoted = true; break;
            case DemonSpat: _spat = true; break;
        }
    }

    void Finish(Tip tip)
    {
        _finished.Add(tip.Id);
        _current = null;
        if (_finished.Count == _tips.Count) Settings.Set("hints", false); // the whole course, once
    }

    public override void _Process(double delta)
    {
        _check -= delta;
        if (_check <= 0)
        {
            _check = 0.5;
            if (_current != null && _current.Done()) Finish(_current);
            foreach (var t in _tips)
                if (_current == null && !_finished.Contains(t.Id) && t.Show())
                {
                    if (t.Done()) _finished.Add(t.Id); // already done unprompted
                    else _current = t;
                }
        }
        Visible = _current != null && World.Outcome == Outcome.Running;
        if (_current != null) _text.Text = _current.Text();
        var screen = GetViewport().GetVisibleRect().Size;
        Position = new Vector2(screen.X - Size.X - 8, 170); // below the threat card
    }
}
