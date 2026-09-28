using Godot;
using Hellwall.Achievements;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// The game's achievements (docs/plans/steam.md): the list in res://data/achievements.json, the rules
/// in AchievementTracker (src/Achievements, tested), what's been earned kept in
/// user://achievements.cfg (so a build without Steam shows them too), and each one handed to Steam
/// when it's up. Main feeds it the run; Unlocked tells the HUD, for a moment's notice.
/// </summary>
public static class GameAchievements
{
    const string Path = "user://achievements.cfg", ListPath = "res://data/achievements.json";
    static AchievementTracker? _tracker;
    static long _statSent = -1;

    /// <summary>Told of each one earned, to show it: the current game's HUD (each scene sets its own; the menu's none).</summary>
    public static Action<AchievementDef>? Unlocked;

    public static AchievementTracker Tracker
    {
        get
        {
            if (_tracker != null) return _tracker;
            using var list = Godot.FileAccess.Open(ListPath, Godot.FileAccess.ModeFlags.Read);
            _tracker = new AchievementTracker(list != null ? AchievementTracker.Parse(list.GetAsText()) : []);
            var file = new ConfigFile();
            if (file.Load(Path) == Error.Ok)
            {
                foreach (var id in ((string)file.GetValue("earned", "ids", "")).Split(',', StringSplitOptions.RemoveEmptyEntries)) _tracker.Earned.Add(id);
                _tracker.DemonsSlain = (long)file.GetValue("stats", "demons_slain", 0L);
            }
            return _tracker;
        }
    }

    /// <summary>Newly earned ones: kept, sent to Steam, and announced.</summary>
    public static void Report(List<AchievementDef> fresh)
    {
        if (DisplayServer.GetName() == "headless") return; // tests and tools never touch a player's achievements
        bool statMoved = Tracker.DemonsSlain != _statSent;
        if (fresh.Count == 0 && !statMoved) return;
        Save();
        foreach (var a in fresh) Steam.Unlock(a.Id);
        if (statMoved) { Steam.SetStat("demons_slain", (int)Math.Min(int.MaxValue, Tracker.DemonsSlain)); _statSent = Tracker.DemonsSlain; }
        Steam.StoreStats();
        foreach (var a in fresh)
        {
            Diagnostics.Note($"achievement: {a.Id}");
            Unlocked?.Invoke(a);
        }
    }

    static void Save()
    {
        var file = new ConfigFile();
        file.SetValue("earned", "ids", string.Join(",", Tracker.Earned));
        file.SetValue("stats", "demons_slain", Tracker.DemonsSlain);
        file.Save(Path);
    }

    /// <summary>At launch: the campaign's relics as they stand (they may predate the achievements), and everything earned sent to Steam (earned offline, or before Steam).</summary>
    public static void Launch()
    {
        var c = Campaign.Default;
        Report(Tracker.Campaign(Facts(c, [])));
        foreach (var id in Tracker.Earned) Steam.Unlock(id);
        Steam.SetStat("demons_slain", (int)Math.Min(int.MaxValue, Tracker.DemonsSlain));
        Steam.StoreStats();
    }

    /// <summary>The campaign's relics now, and how many of those taken into this mission were hallowed.</summary>
    public static CampaignFacts Facts(Campaign c, string[] taken)
    {
        var owned = CampaignProgress.Owned(c);
        return new CampaignFacts(owned.Count, owned.Count(t => t.EndsWith('+')), c.Relics.Length, taken.Count(t => t.EndsWith('+')));
    }
}
