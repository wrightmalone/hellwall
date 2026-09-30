using Godot;
using Hellwall.Sim;
using Resource = Hellwall.Sim.Resource;

namespace Hellwall.Game;

/// <summary>
/// The player's economy (Settings, Economy): each resource's income times a multiplier, 25% to 400%,
/// for every run they start from now on: campaign missions, skirmishes, Workshop and hand-made maps
/// alike (Rules.WithEconomy). A run keeps the economy it began with, in its saves. Anything off 100%
/// is a tuned game: achievements, leaderboards and best scores leave it out. The menu's backdrop, the
/// bot and headless runs always play the game's own economy.
/// </summary>
public static class EconomySettings
{
    public const double Min = 0.25, Max = 4;
    public static readonly Resource[] All = Enum.GetValues<Resource>();

    static string Key(Resource r) => "economy_" + r.ToString().ToLowerInvariant();

    public static double Get(Resource r) => Math.Clamp(Settings.Get(Key(r), 1f), Min, Max);
    public static void Set(Resource r, double m) => Settings.Set(Key(r), (float)Math.Clamp(Math.Round(m * 20) / 20, Min, Max));

    /// <summary>The multipliers for a new run (indexed by Resource), or null when every one is 100%.</summary>
    public static double[]? Current()
    {
        var m = All.Select(Get).ToArray();
        return m.All(x => x == 1) ? null : m;
    }

    public static void Reset() { foreach (var r in All) Set(r, 1); }

    /// <summary>"iron 200%, food 50%": what's changed, for the threat card and the end screen (empty if nothing).</summary>
    public static string Describe(double[]? m) => m == null ? "" :
        string.Join(", ", All.Where(r => (int)r < m.Length && m[(int)r] != 1).Select(r => $"{r.ToString().ToLowerInvariant()} {m[(int)r] * 100:0}%"));
}
