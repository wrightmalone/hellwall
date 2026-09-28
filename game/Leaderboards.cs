using System.Runtime.CompilerServices;
using Godot;
using Hellwall.Sim;
#if !NO_STEAMWORKS
using Steamworks;
#endif

namespace Hellwall.Game;

/// <summary>
/// A Steam leaderboard for each Workshop map (docs/plans/steam.md, step 7): its name is the item's id
/// and a fingerprint of the map as published (ScenarioDef.Version), so a map updated by its maker gets
/// a fresh board, and only a play of the map as it stands counts. When a fair run on it ends, its score
/// goes up (Steam keeps each player's best, with the days held and the demons slain beside it), and
/// the end screen shows the rank, the players around it and your friends'. Only with Steam up; every
/// Steamworks type is in the nested Live class and the *Core methods (see Steam.cs).
/// </summary>
public static class Leaderboards
{
    /// <summary>One row of a board: rank, name, score, and the days held.</summary>
    public sealed record Row(int Rank, string Name, int Score, int Days, bool You);

    /// <summary>What came back: your rank and best, the board's size, the rows round you, and your friends'.</summary>
    public sealed record Standing(int Rank, int Best, int Entries, bool Improved, List<Row> Around, List<Row> Friends);

    /// <summary>A board's name for a map: only a Workshop map (with its published version) has one. At most 128 characters, as Steam allows.</summary>
    public static string? BoardName(ScenarioDef? map) =>
        map is { WorkshopId: > 0, Version.Length: > 0 } ? $"map_{map.WorkshopId}_{map.Version}" : null;

    /// <summary>A map's fingerprint: FNV-1a of its file as published, 16 hex digits.</summary>
    public static string VersionOf(string publishedJson)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in publishedJson) { h ^= c; h *= 1099511628211UL; }
        return h.ToString("x16");
    }

    /// <summary>The lines the end screen shows for a standing.</summary>
    public static string Describe(Standing s)
    {
        var lines = new List<string>
        {
            $"Leaderboard: #{s.Rank:N0} of {s.Entries:N0}" + (s.Improved ? "  (a new best)" : $"  (your best {s.Best:N0})"),
        };
        if (s.Around.Count > 0) lines.Add("Around you:  " + string.Join("   ", s.Around.Select(r => $"#{r.Rank} {(r.You ? "you" : r.Name)} {r.Score:N0}")));
        if (s.Friends.Count > 0) lines.Add("Friends:  " + string.Join("   ", s.Friends.Take(6).Select(r => $"#{r.Rank} {(r.You ? "you" : r.Name)} {r.Score:N0}")));
        return string.Join("\n", lines);
    }

    /// <summary>Send a run's score to its map's board, then fetch where it stands. done gets null (and a reason) if Steam couldn't.</summary>
    public static void Submit(string board, long score, int days, int kills, Action<Standing?, string?> done)
    {
        if (!Steam.Running) { done(null, "the leaderboard needs Steam"); return; }
        try { SubmitCore(board, (int)Math.Min(int.MaxValue, score), days, kills, done); }
        catch (Exception e) { done(null, $"the leaderboard couldn't be reached ({e.GetType().Name})"); }
    }

#if NO_STEAMWORKS
    static void SubmitCore(string board, int score, int days, int kills, Action<Standing?, string?> done) => throw new PlatformNotSupportedException();
#else
    static class Live
    {
        public static CallResult<LeaderboardFindResult_t>? Found;
        public static CallResult<LeaderboardScoreUploaded_t>? Uploaded;
        public static CallResult<LeaderboardScoresDownloaded_t>? Around, Friends;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void SubmitCore(string board, int score, int days, int kills, Action<Standing?, string?> done)
    {
        Live.Found = CallResult<LeaderboardFindResult_t>.Create((found, failed) =>
        {
            if (failed || found.m_bLeaderboardFound == 0) { done(null, "the leaderboard couldn't be found or made"); return; }
            var handle = found.m_hSteamLeaderboard;
            Live.Uploaded = CallResult<LeaderboardScoreUploaded_t>.Create((up, upFailed) =>
            {
                if (upFailed || up.m_bSuccess == 0) { done(null, "the score didn't reach the leaderboard"); return; }
                int rank = up.m_nGlobalRankNew, entries = SteamUserStats.GetLeaderboardEntryCount(handle);
                bool improved = up.m_bScoreChanged != 0;
                Live.Around = CallResult<LeaderboardScoresDownloaded_t>.Create((a, aFailed) =>
                {
                    var around = aFailed ? [] : Rows(a.m_hSteamLeaderboardEntries, a.m_cEntryCount);
                    int best = around.FirstOrDefault(r => r.You)?.Score ?? score;
                    Live.Friends = CallResult<LeaderboardScoresDownloaded_t>.Create((f, fFailed) =>
                        done(new Standing(rank, best, entries, improved, around, fFailed ? [] : Rows(f.m_hSteamLeaderboardEntries, f.m_cEntryCount)), null));
                    Live.Friends.Set(SteamUserStats.DownloadLeaderboardEntries(handle, ELeaderboardDataRequest.k_ELeaderboardDataRequestFriends, 0, 0));
                });
                Live.Around.Set(SteamUserStats.DownloadLeaderboardEntries(handle, ELeaderboardDataRequest.k_ELeaderboardDataRequestGlobalAroundUser, -2, 2));
            });
            Live.Uploaded.Set(SteamUserStats.UploadLeaderboardScore(handle, ELeaderboardUploadScoreMethod.k_ELeaderboardUploadScoreMethodKeepBest, score, [days, kills], 2));
        });
        Live.Found.Set(SteamUserStats.FindOrCreateLeaderboard(board, ELeaderboardSortMethod.k_ELeaderboardSortMethodDescending, ELeaderboardDisplayType.k_ELeaderboardDisplayTypeNumeric));
    }

    static List<Row> Rows(SteamLeaderboardEntries_t entries, int count)
    {
        var me = SteamUser.GetSteamID();
        var rows = new List<Row>();
        var details = new int[2];
        for (int i = 0; i < count; i++)
        {
            if (!SteamUserStats.GetDownloadedLeaderboardEntry(entries, i, out var e, details, details.Length)) continue;
            string name = SteamFriends.GetFriendPersonaName(e.m_steamIDUser);
            rows.Add(new Row(e.m_nGlobalRank, string.IsNullOrEmpty(name) ? "a player" : name, e.m_nScore, e.m_cDetails > 0 ? details[0] : 0, e.m_steamIDUser == me));
        }
        return rows;
    }
#endif
}
