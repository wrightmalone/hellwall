namespace Hellwall.Sim;

/// <summary>
/// What a relic does to the mission it's taken into. Every part bends the mission's rules
/// before it starts (never a tech you could research): modifiers as a tech's, a gift of stock,
/// or one of a few numbers. Slots are for the campaign (how many relics can be taken).
/// </summary>
public sealed record RelicEffect
{
    /// <summary>As a tech's (TechModifier): applied to the base definitions, so research builds on them.</summary>
    public TechModifier[] Modifiers { get; init; } = [];
    /// <summary>Added to the starting stock.</summary>
    public Cost? Start { get; init; }
    /// <summary>Hellgate bands this many times their size.</summary>
    public double HellgateBands { get; init; } = 1;
    /// <summary>Woodsmen fell trees this many times as fast.</summary>
    public double Chop { get; init; } = 1;
    /// <summary>The Convergence is told this many seconds ahead (0: the rules' own).</summary>
    public float ConvergenceWarnSeconds { get; init; }
    /// <summary>The first wave comes this many days later.</summary>
    public int WaveDelayDays { get; init; }
    /// <summary>More relics can be taken into each mission.</summary>
    public int Slots { get; init; }
    /// <summary>Only adds slots: it works by being owned, so it's never taken into a mission (it would waste a slot).</summary>
    public bool Passive => Slots > 0 && Modifiers.Length == 0 && Start == null && HellgateBands == 1 && Chop == 1 && ConvergenceWarnSeconds == 0 && WaveDelayDays == 0;

    /// <summary>What it does, in a line, for the picker.</summary>
    public string Text { get; init; } = "";
}

/// <summary>
/// A campaign reward: won with a mission (From), taken into later ones. Its mission's bonus goal
/// (Bonus), met, hallows it: the same relic, the Hallowed effect instead. See docs/plans/campaign.md.
/// </summary>
public sealed record RelicDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Whose it was and why it matters: a line for the writers to replace.</summary>
    public string Lore { get; init; } = "";
    /// <summary>The mission that gives it.</summary>
    public string From { get; init; } = "";
    public RelicEffect Effect { get; init; } = new();
    public RelicEffect Hallowed { get; init; } = new();
    /// <summary>That mission's bonus goal: met by the win, it hallows the relic.</summary>
    public ObjectiveDef? Bonus { get; init; }
}

public static class Relics
{
    /// <summary>How a taken relic is written: its id, with "+" when hallowed.</summary>
    public static (string Id, bool Hallowed) Parse(string taken) => taken.EndsWith('+') ? (taken[..^1], true) : (taken, false);

    public static string Taken(string id, bool hallowed) => hallowed ? id + "+" : id;

    /// <summary>The mission's rules with these relics taken, in the order given (the save keeps it, and the rules hash depends on it).</summary>
    public static Rules Apply(Rules rules, Campaign campaign, IEnumerable<string> taken)
    {
        foreach (var t in taken)
        {
            var (id, hallowed) = Parse(t);
            if (campaign.Relic(id) is not { } relic) throw new FormatException($"no relic '{id}'");
            rules = Apply(rules, hallowed ? relic.Hallowed : relic.Effect);
        }
        return rules;
    }

    static Rules Apply(Rules r, RelicEffect e)
    {
        if (e.Modifiers.Length > 0) r = r.WithModifiers(e.Modifiers);
        if (e.Start is { } gift) r = r.WithStartingResources(r.StartingResources.Plus(gift));
        if (e.HellgateBands != 1) r = r.WithHellgates(h => h with { BandSize = Math.Max(1, (int)Math.Round(h.BandSize * e.HellgateBands)) });
        if (e.Chop != 1) r = r.WithWoods(w => w with { ChopDps = (float)(w.ChopDps * e.Chop) });
        if (e.ConvergenceWarnSeconds > 0) r = r.WithSurvival(s => s with { ConvergenceWarnSeconds = e.ConvergenceWarnSeconds });
        if (e.WaveDelayDays > 0) r = r.WithSurvival(s => s with { FirstWaveDay = s.FirstWaveDay + e.WaveDelayDays });
        return r;
    }
}
