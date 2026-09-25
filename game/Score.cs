using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// A run's score, and the best per kind of run (mode, map, difficulty) in
/// user://scores.cfg. Days held count most; kills, research and a win add;
/// buildings lost take away; the difficulty multiplies the lot, and fog off
/// takes a fifth. Placeholder weights, to be tuned when there's a leaderboard.
/// </summary>
public static class Score
{
    const string Path = "user://scores.cfg";

    public static long Of(World world)
    {
        var st = world.Stats;
        double points = world.Day * 100 + st.DemonsKilled + world.Tech.Researched.Count * 150 - st.BuildingsLost * 5;
        if (world.Outcome == Outcome.Won) points *= 1.5;
        double mul = world.Rules.Difficulty switch { Difficulty.Easy => 0.5, Difficulty.Hard => 1.6, Difficulty.Nightmare => 2.5, _ => 1 };
        if (!world.Vision.Enabled) mul *= 0.8;
        return Math.Max(0, (long)Math.Round(points * mul));
    }

    /// <summary>This run's score, and the best for its kind (including this one). Records it once per run.</summary>
    public static (long Score, long Best) Record(World world, string mode)
    {
        long score = Of(world);
        var file = new ConfigFile();
        file.Load(Path);
        string key = $"{mode}-{world.Map}-{world.Rules.Difficulty}".ToLowerInvariant().Replace(' ', '-');
        long best = (long)file.GetValue("best", key, 0L);
        if (_recorded != world)
        {
            _recorded = world;
            if (score > best)
            {
                file.SetValue("best", key, score);
                file.Save(Path);
            }
        }
        return (score, Math.Max(best, score));
    }

    static World? _recorded;
}
