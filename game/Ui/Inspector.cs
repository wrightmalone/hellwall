using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Bottom right: the selected building's health, state and output, or the selected soldiers'
/// health. Its upgrade, hold and demolish buttons (which know about groups, the Keep and
/// possession) aren't drawn here: the command card shows them in their grid cells.
/// </summary>
public partial class Inspector : PanelContainer
{
    public World World = null!;
    public ClientState State = null!;
    public Action<Command> Send = null!;

    Label _name = null!, _status = null!, _detail = null!;
    ProgressBar _hp = null!;
    Button _demolish = null!, _upgrade = null!, _hold = null!;
    public Button UpgradeButton => _upgrade;
    public Button HoldButton => _hold;
    public Button DemolishButton => _demolish;

    public override void _ExitTree()
    {
        // Not in the tree (the card mirrors them), so not freed with it.
        foreach (var b in new[] { _upgrade, _hold, _demolish }) if (b.GetParent() == null) b.QueueFree();
    }

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
        _upgrade = UiKit.TextButton("Upgrade", 12);
        _upgrade.Pressed += () =>
        {
            if (Group() is { Count: > 1 } group) { UpgradeGroup(group); return; }
            if (State.SelectedBuilding is not { } id) return;
            // The Keep is raised a level (a tech it works on itself); anything else upgrades in place.
            if (World.BuildingById(id) is { Kind: BuildingKind.Keep } && World.NextKeepLevel() is { } next) Send(new Research(id, next.Id));
            else Send(new UpgradeBuilding(id));
        };
        _hold = UiKit.TextButton("Put on hold", 12);
        _hold.TooltipText = "Stand the crew down: they go to other work, and its woodsmen or miners stay home.\nNothing is produced until it's back at work.";
        _hold.Pressed += () =>
        {
            if (Group() is { Count: > 1 } group)
            {
                // Any still working: all on hold. All on hold already: all back to work.
                bool hold = group.Any(g => g.NeedsCrew && !g.Paused);
                foreach (var g in group) if (g.NeedsCrew) Send(new SetPaused(g.Id, hold));
                return;
            }
            if (State.SelectedBuilding is { } id && World.BuildingById(id) is { } b) Send(new SetPaused(id, !b.Paused));
        };
        _demolish = UiKit.TextButton("Demolish", 12);
        _demolish.Pressed += () => { if (State.SelectedBuilding is { } id) Send(new Demolish(id)); };
    }

    /// <summary>The selected group's buildings still standing (fewer than two: not a group).</summary>
    List<Building>? Group()
    {
        if (State.SelectedGroup.Count == 0) return null;
        State.SelectedGroup.RemoveWhere(g => World.BuildingById(g) == null);
        return State.SelectedGroup.OrderBy(g => g).Select(g => World.BuildingById(g)!).ToList();
    }

    /// <summary>Upgrade as many of the group as the stores will pay for, lowest id first.</summary>
    void UpgradeGroup(List<Building> group)
    {
        var (eligible, to) = Upgradable(group);
        if (to == null) return;
        int n = Math.Min(eligible.Count, Affordable(World.Def(to.Value).Cost));
        foreach (var g in eligible.Take(n)) Send(new UpgradeBuilding(g.Id));
    }

    /// <summary>The group's buildings ready to upgrade to the most common next tier among them, and that tier.</summary>
    (List<Building> Eligible, BuildingKind? To) Upgradable(List<Building> group)
    {
        var ready = group.Where(g => g.Def.UpgradesTo != null && g.Complete && !g.Possessed && !g.Upgrading).ToList();
        if (ready.Count == 0) return ([], null);
        var to = ready.GroupBy(g => g.Def.UpgradesTo!.Value).OrderByDescending(x => x.Count()).First().Key;
        return (ready.Where(g => g.Def.UpgradesTo == to).ToList(), to);
    }

    /// <summary>How many of `cost` the stores could pay for.</summary>
    int Affordable(Cost cost)
    {
        int n = int.MaxValue;
        foreach (var r in Enum.GetValues<Hellwall.Sim.Resource>())
            if (cost[r] > 0) n = Math.Min(n, (int)(World.Colony[r] / cost[r]));
        return n;
    }

    void ShowGroup(List<Building> group)
    {
        Visible = true;
        var kinds = group.GroupBy(g => g.Kind).OrderByDescending(x => x.Count()).ToList();
        _name.Text = string.Join(", ", kinds.Select(k => $"{k.Count()} {k.Key}{(k.Count() == 1 ? "" : "s")}"));
        float hp = group.Sum(g => g.Hp), max = group.Sum(g => g.Def.Hp);
        _hp.Value = max <= 0 ? 0 : hp / max;
        int upgrading = group.Count(g => g.Upgrading), held = group.Count(g => g.Paused), building = group.Count(g => !g.Complete);
        var parts = new List<string>();
        if (building > 0) parts.Add($"{building} going up");
        if (upgrading > 0) parts.Add($"{upgrading} upgrading");
        if (held > 0) parts.Add($"{held} on hold");
        _status.Text = parts.Count == 0 ? "All working" : string.Join(" · ", parts);
        _status.AddThemeColorOverride("font_color", UiKit.Text);
        int housing = group.Where(g => g.Complete).Sum(g => g.Def.Housing);
        _detail.Text = housing > 0 ? $"houses {housing} between them" : "";
        _demolish.Visible = false; // one at a time: a whole street demolished by a slip would hurt
        _hold.Visible = group.Any(g => g.NeedsCrew && g.Complete);
        _hold.Text = group.Any(g => g.NeedsCrew && !g.Paused) ? "Put all on hold" : "All back to work";
        var (eligible, to) = Upgradable(group);
        _upgrade.Visible = to != null;
        if (to is { } target)
        {
            var def = World.Def(target);
            bool locked = def.RequiresTech is { } needs && !World.Tech.Has(needs);
            int n = Math.Min(eligible.Count, Affordable(def.Cost));
            _upgrade.Disabled = locked || n == 0;
            _upgrade.Text = locked ? $"{target} needs {World.Rules.Tech(def.RequiresTech!).Name}"
                : n == eligible.Count ? $"Upgrade {n} to {target} ({def.Cost} each)"
                : $"Upgrade {n} of {eligible.Count} to {target} ({def.Cost} each; that's all you can pay for)";
            _upgrade.TooltipText = $"{target}: {Blurbs.Of(target)}\nThey work as they are while the builders are at it.";
        }
    }

    public override void _Process(double delta)
    {
        if (Group() is { Count: > 1 } group)
        {
            ShowGroup(group);
            return;
        }
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
                b.Paused ? "On hold (its crew are at other work)" :
                b.NeedsCrew && !b.Staffed ? $"Idle: needs {b.Def.Workers} workers" :
                b.Exhausted && World.HasCrew(b.Def) ? $"Worked out: no {(b.Kind == BuildingKind.Woodcutter ? "trees" : b.Kind == BuildingKind.Mine ? "ore" : "rock")} left in reach. Build another further out" :
                "Working";
            _status.AddThemeColorOverride("font_color", b.Paused ? UiKit.Muted : b.Possessed || !b.OnGround || (b.NeedsCrew && !b.Staffed) || (b.Exhausted && World.HasCrew(b.Def)) ? UiKit.Threat : UiKit.Text);
            var d = new List<string>();
            if (b.Def.Produces is { } r && b.Complete) d.Add($"{b.Rate * World.Colony.Power:0.00} {r.ToString().ToLowerInvariant()} a second");
            if (b.Def.Weapon is { } w) d.Add($"range {w.Range:0.#}, {w.Damage:0} damage every {w.Cooldown:0.##} s");
            if (b.Def.Housing > 0) d.Add($"houses {b.Def.Housing}");
            if (b.Def.SanctitySupply > 0) d.Add($"supplies {b.Def.SanctitySupply:0} sanctity");
            if (b.Def.SanctityUse > 0) d.Add($"draws {b.Def.SanctityUse:0} sanctity");
            _detail.Text = string.Join("\n", d);
            _demolish.Visible = b.Kind != BuildingKind.Keep;
            _hold.Visible = b.NeedsCrew && b.Complete && !b.Possessed;
            _hold.Text = b.Paused ? "Back to work" : "Put on hold";
            _upgrade.Visible = b.Def.UpgradesTo != null && b.Complete && !b.Possessed;
            if (b.Kind == BuildingKind.Keep)
            {
                var next = World.NextKeepLevel();
                _upgrade.Visible = next != null || b.Researching != null;
                if (b.Researching is { } raising)
                {
                    var tech = World.Rules.Tech(raising);
                    _upgrade.Disabled = true;
                    _upgrade.Text = $"Raising: {tech.Name}... {b.ResearchProgress / tech.Seconds:P0}";
                }
                else if (next != null)
                {
                    _upgrade.Disabled = !World.Colony.CanAfford(next.Cost);
                    _upgrade.Text = $"Raise the Keep: {next.Name} ({next.Cost})";
                    _upgrade.TooltipText = $"{next.Name}: {next.Description}\n{next.Seconds:0} s";
                }
            }
            if (b.Def.UpgradesTo is { } to)
            {
                var upDef = World.Def(to);
                bool locked = upDef.RequiresTech is { } needs && !World.Tech.Has(needs);
                _upgrade.Disabled = b.Upgrading || locked || !World.Colony.CanAfford(upDef.Cost);
                _upgrade.Text = b.Upgrading ? $"Upgrading to {to}... {b.UpgradeProgress / upDef.BuildSeconds:P0}"
                    : locked ? $"{to} needs {World.Rules.Tech(upDef.RequiresTech!).Name}" : $"Upgrade to {to} ({upDef.Cost})";
                _upgrade.TooltipText = $"{to}: {Blurbs.Of(to)}\n{upDef.Hp:0} hp{Hud.Describe(upDef)}\nIt works as it is while the builders are at it.";
            }
            _demolish.Text = b.Possessed ? "Purge" : "Demolish";
        }
        else if (State.SelectedUnits.Count > 0)
        {
            Visible = true;
            var units = World.Units.Where(u => State.SelectedUnits.Contains(u.Id)).ToList();
            _name.Text = units.Count == 1 ? $"{(units[0].Rank > 0 ? Unit.RankName(units[0].Rank) + " " : "")}{units[0].Kind}" : $"{units.Count} soldiers";
            float hp = units.Sum(u => u.Hp), max = units.Sum(u => u.MaxHp);
            _hp.Value = max <= 0 ? 0 : hp / max;
            _status.Text = $"{hp:0} / {max:0} hp";
            _status.AddThemeColorOverride("font_color", UiKit.Text);
            _detail.Text = units.Count == 1 ? $"{units[0].Order} · {units[0].Kills} kills{NextRank(units[0])}" : string.Join(", ", units.GroupBy(u => u.Order).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"));
            _demolish.Visible = false;
            _upgrade.Visible = false;
            _hold.Visible = false;
        }
        else Visible = false;
    
    static string NextRank(Unit u) => u.Rank >= 3 ? "" : $" ({Unit.RankKills[u.Rank] - u.Kills} to {Unit.RankName(u.Rank + 1)})";
}
}
