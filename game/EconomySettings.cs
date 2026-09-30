using Godot;
using Hellwall.Sim;
using Resource = Hellwall.Sim.Resource;

namespace Hellwall.Game;

/// <summary>
/// The player's economy (Settings, Economy): each resource's income times a multiplier, and how fast
/// workers use up trees, rock and iron ore, and demons' hit points, 25% to 400%, for every run they start from now on:
/// campaign missions, skirmishes, Workshop and hand-made maps alike (Rules.WithEconomy). A run keeps
/// the economy it began with, in its saves. Anything off 100% is a tuned game: achievements,
/// leaderboards and best scores leave it out. The menu's backdrop, the bot and headless runs always
/// play the game's own economy.
/// </summary>
public static class EconomySettings
{
    public const double Min = 0.25, Max = 4;

    /// <summary>One slider: its slot in the economy (Rules.WithEconomy), its settings key, and its name.</summary>
    public sealed record Slot(int Index, string Key, string Name, string Short);

    /// <summary>The resources' incomes.</summary>
    public static readonly Slot[] Incomes = Enum.GetValues<Resource>()
        .Select(r => new Slot((int)r, "economy_" + r.ToString().ToLowerInvariant(), r.ToString(), r.ToString().ToLowerInvariant())).ToArray();

    /// <summary>How fast workers wear the land away.</summary>
    public static readonly Slot[] Wear =
    [
        new(Rules.TreesWear, "wear_trees", "Trees", "trees felled"),
        new(Rules.RockWear, "wear_rock", "Rock", "rock quarried"),
        new(Rules.OreWear, "wear_ore", "Iron ore", "ore mined"),
    ];

    /// <summary>How tough the demons are.</summary>
    public static readonly Slot[] Demons = [new(Rules.DemonHealth, "demon_health", "Health", "demon health")];

    public static IEnumerable<Slot> All => Incomes.Concat(Wear).Concat(Demons);

    public static double Get(Slot s) => Math.Clamp(Settings.Get(s.Key, 1f), Min, Max);
    public static void Set(Slot s, double m) => Settings.Set(s.Key, (float)Math.Clamp(Math.Round(m * 20) / 20, Min, Max));

    /// <summary>The multipliers for a new run (Rules.EconomySlots of them), or null when every one is 100%.</summary>
    public static double[]? Current()
    {
        var m = Enumerable.Repeat(1.0, Rules.EconomySlots).ToArray();
        foreach (var s in All) m[s.Index] = Get(s);
        return m.All(x => x == 1) ? null : m;
    }

    public static void Reset() { foreach (var s in All) Set(s, 1); }

    /// <summary>"iron 200%, trees felled 50%": what's changed, for the threat card and the end screen (empty if nothing).</summary>
    public static string Describe(double[]? m) => m == null ? "" :
        string.Join(", ", All.Where(s => s.Index < m.Length && m[s.Index] != 1).Select(s => $"{s.Short} {m[s.Index] * 100:0}%"));
}
