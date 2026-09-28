using System.Runtime.CompilerServices;
using Godot;
using Hellwall.Sim;
#if !NO_STEAMWORKS
using Steamworks;
#endif

namespace Hellwall.Game;

/// <summary>One map on the Workshop, as the browser lists it.</summary>
public sealed record WorkshopItem(ulong Id, string Title, string Description, string Author, uint VotesUp, uint VotesDown, string[] Tags, ulong Players, string PreviewUrl)
{
    public bool IsMission => Tags.Contains("Mission");
}

public enum WorkshopSort { Popular, Newest, TopRated, MostPlayed }

/// <summary>
/// Where the browser's maps come from: Steam's Workshop (SteamWorkshopSource), or, for the self-test and
/// screenshots on a machine without Steam, the player's own maps dressed as Workshop items (FakeWorkshopSource).
/// </summary>
public interface IWorkshopSource
{
    /// <summary>Why it can't list anything (null: it can).</summary>
    string? Unavailable { get; }
    /// <summary>A page (from 1) of maps, sorted, searched and filtered by a tag; done(items, total, error).</summary>
    void Query(WorkshopSort sort, string search, string? tag, uint page, Action<List<WorkshopItem>?, uint, string?> done);
    /// <summary>The map itself: subscribed, downloaded, read and checked; done(map, error). Called again each frame by Poll until it answers.</summary>
    void Fetch(WorkshopItem item, Action<ScenarioDef?, string?> done);
    /// <summary>Its preview picture, if it has one.</summary>
    void Preview(WorkshopItem item, Node host, Action<Image?> done);
    void Vote(ulong id, bool up);
    /// <summary>Each frame: downloads under way.</summary>
    void Poll();
}

/// <summary>The maps played and won from the Workshop (user://workshop.cfg, synced): the browser's marks, as Creeper World 3's.</summary>
public static class WorkshopRecord
{
    const string Path = "user://workshop.cfg";

    static HashSet<string> Read(string key)
    {
        var f = new ConfigFile();
        f.Load(Path);
        return new(((string)f.GetValue("maps", key, "")).Split(',', StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool Played(ulong id) => Read("played").Contains(id.ToString());
    public static bool Won(ulong id) => Read("won").Contains(id.ToString());

    /// <summary>A Workshop map's scenario id ("ws-123"), back to its item id (0: not a Workshop map).</summary>
    public static ulong IdOf(ScenarioDef? map) => map?.Id is { } id && id.StartsWith("ws-") && ulong.TryParse(id[3..], out var n) ? n : 0;

    public static void Mark(ulong id, bool won)
    {
        if (id == 0 || DisplayServer.GetName() == "headless" && !SelfTesting) return;
        var f = new ConfigFile();
        f.Load(Path);
        foreach (var key in won ? new[] { "played", "won" } : ["played"])
        {
            var set = new HashSet<string>(((string)f.GetValue("maps", key, "")).Split(',', StringSplitOptions.RemoveEmptyEntries)) { id.ToString() };
            f.SetValue("maps", key, string.Join(",", set));
        }
        f.Save(Path);
    }

    /// <summary>Set by the browser's self-test, which marks and reads back (and then clears) one of its own.</summary>
    public static bool SelfTesting;

    public static void Forget(ulong id)
    {
        var f = new ConfigFile();
        if (f.Load(Path) != Error.Ok) return;
        foreach (var key in new[] { "played", "won" })
            f.SetValue("maps", key, string.Join(",", ((string)f.GetValue("maps", key, "")).Split(',', StringSplitOptions.RemoveEmptyEntries).Where(s => s != id.ToString())));
        f.Save(Path);
    }
}

/// <summary>
/// The Workshop page, off the main menu: sort (popular, newest, top rated, most played), search, missions
/// or maps; each map's preview, author, votes, players and tags, whether you've played or won it, and
/// Play, which fetches it and starts it as its maker set it. Without Steam it says so.
/// </summary>
public partial class WorkshopBrowser : VBoxContainer
{
    public IWorkshopSource Source = null!;
    /// <summary>Start a Workshop map (its scenario id "ws-[item]").</summary>
    public Action<ScenarioDef> Play = null!;
    public Action Back = null!;

    WorkshopSort _sort = WorkshopSort.Popular;
    string? _tag;
    uint _page = 1;
    LineEdit _search = null!;
    VBoxContainer _list = null!;
    Label _status = null!;
    Button _more = null!;
    readonly List<Button> _sorts = new();
    /// <summary>How many rows are listed (for the self-test).</summary>
    public int Rows { get; private set; }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        AddChild(UiKit.Label("Workshop", 22, UiKit.Gold));
        var sorts = new HBoxContainer();
        foreach (var (name, sort) in new[] { ("Popular", WorkshopSort.Popular), ("Newest", WorkshopSort.Newest), ("Top rated", WorkshopSort.TopRated), ("Most played", WorkshopSort.MostPlayed) })
        {
            var b = UiKit.TextButton(name, 13);
            b.Pressed += () => { _sort = sort; Reload(); };
            b.SetMeta("sort", (int)sort);
            sorts.AddChild(b);
            _sorts.Add(b);
        }
        AddChild(sorts);
        var find = new HBoxContainer();
        _search = new LineEdit { PlaceholderText = "Search by name", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _search.TextSubmitted += _ => Reload();
        find.AddChild(_search);
        var kind = new OptionButton();
        foreach (var k in new[] { "Missions and maps", "Missions", "Maps" }) kind.AddItem(k);
        kind.ItemSelected += i => { _tag = i switch { 1 => "Mission", 2 => "Map", _ => null }; Reload(); };
        find.AddChild(kind);
        var go = UiKit.TextButton("Search", 13);
        go.Pressed += Reload;
        find.AddChild(go);
        AddChild(find);
        _status = UiKit.Label("", 13, UiKit.Muted);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_status);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 560) };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_list);
        AddChild(scroll);
        _more = UiKit.TextButton("More", 13);
        _more.Pressed += () => { _page++; Load(append: true); };
        AddChild(_more);
        var back = UiKit.TextButton("Back", 16);
        back.Pressed += () => Back();
        AddChild(back);
        Reload();
    }

    public override void _Process(double delta) => Source.Poll();

    /// <summary>
    /// --selftest=workshop: the browser over made-up maps (no Steam needed) lists them all, a search finds
    /// one, and fetching one gives a map the game will play, as a Workshop map ("ws-" id); played and won
    /// marks are kept, and cleared again after.
    /// </summary>
    public static async void SelfTest(Node host)
    {
        var demo = FakeWorkshopSource.Demo();
        var source = new FakeWorkshopSource(demo);
        ScenarioDef? played = null;
        var browser = new WorkshopBrowser { Source = source, Back = () => { }, Play = m => played = m };
        host.AddChild(browser);
        for (int i = 0; i < 5; i++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        bool listed = browser.Rows == demo.Count;
        browser._search.Text = "Ford";
        browser.Reload();
        for (int i = 0; i < 3; i++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        bool searched = browser.Rows == 1;
        // Play the first row: its button, as a player would.
        var row = browser._list.GetChildren().OfType<HBoxContainer>().FirstOrDefault(r => !r.IsQueuedForDeletion());
        WorkshopRecord.SelfTesting = true;
        row?.GetChildren().OfType<Button>().Last().EmitSignal(BaseButton.SignalName.Pressed);
        bool fetched = played is { } m && m.Id == "ws-1000" && m.WorkshopId == 1000 && m.Problem() == null && World.Create(m.Options(Rules.Default)).Terrain.Width == 128;
        // Its leaderboard: named for the item and this version of it (the same map, the same board; a changed one, another).
        string? board = Leaderboards.BoardName(played);
        bool boards = board is { Length: <= 128 } && board == Leaderboards.BoardName(played! with { Name = "a copy" })
            && board != Leaderboards.BoardName(played! with { Version = Leaderboards.VersionOf(played!.ToJson() + " ") })
            && Leaderboards.BoardName(demo[0]) == null
            && Leaderboards.Describe(new Leaderboards.Standing(3, 5200, 40, true, [new(2, "Ada", 6000, 30, false), new(3, "me", 5200, 28, true)], [])).Contains("#3 you 5,200");
        WorkshopRecord.Mark(1000, won: true);
        bool marked = WorkshopRecord.Played(1000) && WorkshopRecord.Won(1000);
        WorkshopRecord.Forget(1000);
        marked &= !WorkshopRecord.Played(1000);
        WorkshopRecord.SelfTesting = false;
        bool unavailable = new SteamWorkshopSource().Unavailable != null; // no Steam here: it says so
        bool pass = listed && searched && fetched && marked && unavailable && boards;
        GD.Print(pass ? "hellwall-selftest: PASS workshop" : $"hellwall-selftest: FAIL workshop (listed {listed} ({browser.Rows}), searched {searched}, fetched {fetched}, marks {marked}, says Steam's needed {unavailable}, leaderboard names {boards})");
        host.GetTree().Quit();
    }

    void Reload()
    {
        _page = 1;
        foreach (var b in _sorts) b.AddThemeColorOverride("font_color", (int)b.GetMeta("sort") == (int)_sort ? UiKit.Gold : UiKit.Text);
        Load(append: false);
    }

    void Load(bool append)
    {
        if (!append) { foreach (var c in _list.GetChildren()) c.QueueFree(); Rows = 0; }
        _more.Visible = false;
        if (Source.Unavailable is { } why) { _status.Text = why; return; }
        _status.Text = "Looking...";
        Source.Query(_sort, _search.Text.Trim(), _tag, _page, (items, total, error) =>
        {
            if (!IsInstanceValid(this)) return;
            if (items == null) { _status.Text = error ?? "The Workshop didn't answer"; return; }
            foreach (var item in items) _list.AddChild(Row(item));
            Rows += items.Count;
            _status.Text = Rows == 0 ? "Nothing found." : $"{Rows} of {total}";
            _more.Visible = Rows < total && items.Count > 0;
        });
    }

    Control Row(WorkshopItem item)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var picture = new TextureRect { CustomMinimumSize = new Vector2(88, 88), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        row.AddChild(picture);
        Source.Preview(item, this, image => { if (image != null && IsInstanceValid(picture)) picture.Texture = ImageTexture.CreateFromImage(image); });
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 2);
        string marks = WorkshopRecord.Won(item.Id) ? "  · won" : WorkshopRecord.Played(item.Id) ? "  · played" : "";
        var title = UiKit.Label(item.Title + marks, 15, WorkshopRecord.Won(item.Id) ? UiKit.Gold : UiKit.Text);
        title.ClipText = true;
        text.AddChild(title);
        var facts = UiKit.Label($"by {item.Author}  ·  +{item.VotesUp} / -{item.VotesDown}  ·  {item.Players:N0} played  ·  {string.Join(", ", item.Tags)}", 12, UiKit.Muted);
        facts.ClipText = true;
        text.AddChild(facts);
        var about = UiKit.Label(item.Description.Length > 160 ? item.Description[..160] + "..." : item.Description, 12);
        about.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        about.MaxLinesVisible = 2;
        text.AddChild(about);
        row.AddChild(text);
        var play = UiKit.TextButton("Play", 14);
        play.Pressed += () =>
        {
            play.Disabled = true;
            _status.Text = $"Getting '{item.Title}'...";
            Source.Fetch(item, (map, error) =>
            {
                if (!IsInstanceValid(this)) return;
                play.Disabled = false;
                if (map == null) { _status.Text = error ?? "Couldn't get it"; return; }
                WorkshopRecord.Mark(item.Id, won: false);
                Play(map);
            });
        };
        row.AddChild(play);
        return row;
    }
}

/// <summary>The player's own maps, dressed as Workshop items: for the browser's self-test and screenshots without Steam.</summary>
public sealed class FakeWorkshopSource : IWorkshopSource
{
    readonly List<(WorkshopItem Item, ScenarioDef Map)> _maps;

    public FakeWorkshopSource(IEnumerable<ScenarioDef> maps) =>
        _maps = maps.Select((m, i) => (new WorkshopItem((ulong)(1000 + i), m.Name, m.Briefing.Length > 0 ? m.Briefing : "A hand-made map.", "a map-maker", (uint)(40 - i * 3), (uint)i, [.. Workshop.Tags(m)], (ulong)(900 - i * 40), ""), m)).ToList();

    public string? Unavailable => null;

    /// <summary>A handful of made-up maps and missions (generated terrain), for the self-test and screenshots when there are no maps of one's own.</summary>
    public static List<ScenarioDef> Demo()
    {
        (string Name, MapKind Kind, bool Mission, string Brief)[] specs =
        [
            ("The Ford at Ashmere", MapKind.Lakes, true, "Hold the only ford for twenty days. The river guards the rest."),
            ("Three Passes", MapKind.Highlands, true, "Three ways down from the peaks, and not enough stone for all of them."),
            ("Greenwood", MapKind.Wildwood, false, ""),
            ("The Long Causeway", MapKind.Causeway, true, "Push the wall out to the far gaps before the Convergence."),
            ("Open Country", MapKind.Plains, false, ""),
        ];
        return specs.Select((s, i) =>
        {
            var world = World.Create(new WorldOptions((uint)(40 + i), 128, 0, Rules.Default, Map: s.Kind));
            return new ScenarioDef
            {
                Id = $"demo-{i}", Name = s.Name, Briefing = s.Brief, Seed = (uint)(40 + i), Map = s.Kind, MapSize = 128, IsMission = s.Mission,
                Difficulty = i % 2 == 0 ? Difficulty.Normal : Difficulty.Hard, Tiles = ScenarioDef.EncodeTiles(world.Terrain.Tiles),
                Objectives = s.Mission ? [new ObjectiveDef { Kind = ObjectiveKind.Survive }] : [],
            };
        }).ToList();
    }

    public void Query(WorkshopSort sort, string search, string? tag, uint page, Action<List<WorkshopItem>?, uint, string?> done)
    {
        var found = _maps.Select(p => p.Item).Where(i => (tag == null || i.Tags.Contains(tag)) && (search.Length == 0 || i.Title.Contains(search, StringComparison.OrdinalIgnoreCase)));
        found = sort switch
        {
            WorkshopSort.Newest => found.Reverse(),
            WorkshopSort.TopRated => found.OrderByDescending(i => i.VotesUp - (long)i.VotesDown),
            WorkshopSort.MostPlayed => found.OrderByDescending(i => i.Players),
            _ => found,
        };
        var all = found.ToList();
        done(all.Skip((int)(page - 1) * 50).Take(50).ToList(), (uint)all.Count, null);
    }

    public void Fetch(WorkshopItem item, Action<ScenarioDef?, string?> done)
    {
        var map = _maps.First(p => p.Item.Id == item.Id).Map;
        done(map with { Id = $"ws-{item.Id}", WorkshopId = item.Id, Version = Leaderboards.VersionOf(map.ToJson()) }, null);
    }

    public void Preview(WorkshopItem item, Node host, Action<Image?> done) => done(Workshop.Preview(_maps.First(p => p.Item.Id == item.Id).Map));
    public void Vote(ulong id, bool up) { }
    public void Poll() { }
}

/// <summary>
/// Steam's Workshop, through ISteamUGC: queries (a page of 50), subscribing and downloading a map
/// (polled until installed), its preview (fetched over HTTP), and votes. Every Steamworks type is kept
/// in the nested Live class and the *Core methods, so nothing here loads Steamworks.NET until Steam is
/// up (see Steam.cs).
/// </summary>
public sealed class SteamWorkshopSource : IWorkshopSource
{
    public string? Unavailable => Steam.Running ? null : "The Workshop needs Steam: start Hellwall from Steam to browse and play the maps others have made. (Maps sent as files go in through Import... in the map editor.)";

    public void Query(WorkshopSort sort, string search, string? tag, uint page, Action<List<WorkshopItem>?, uint, string?> done)
    {
        try { QueryCore(sort, search, tag, page, done); }
        catch (Exception e) { done(null, 0, $"The Workshop couldn't be asked ({e.GetType().Name})"); }
    }

    // A fetch under way: its item, and who's waiting.
    ulong _fetching;
    Action<ScenarioDef?, string?>? _fetched;
    double _fetchStarted;

    public void Fetch(WorkshopItem item, Action<ScenarioDef?, string?> done)
    {
        _fetching = item.Id;
        _fetched = done;
        _fetchStarted = Time.GetTicksMsec() / 1000.0;
        try { SubscribeCore(item.Id); }
        catch (Exception e) { _fetching = 0; done(null, $"Couldn't subscribe ({e.GetType().Name})"); }
    }

    public void Poll()
    {
        if (_fetching == 0 || _fetched == null) return;
        string? folder;
        try { folder = InstalledCore(_fetching); }
        catch (Exception) { folder = null; }
        if (folder == null)
        {
            if (Time.GetTicksMsec() / 1000.0 - _fetchStarted > 120) { var d = _fetched; _fetching = 0; _fetched = null; d(null, "The download didn't finish (two minutes): try again"); }
            return;
        }
        ulong id = _fetching;
        var done = _fetched;
        _fetching = 0;
        _fetched = null;
        string file = System.IO.Path.Combine(folder, "map.json");
        if (!System.IO.File.Exists(file)) { done(null, "That Workshop item has no map in it"); return; }
        ScenarioDef map;
        string text = System.IO.File.ReadAllText(file);
        try { map = ScenarioDef.FromJson(text); }
        catch (Exception) { done(null, "That map's file is damaged"); return; }
        if (map.Problem() is { } problem) { done(null, $"The game can't play it: {problem}"); return; }
        done(map with { Id = $"ws-{id}", WorkshopId = id, Version = Leaderboards.VersionOf(text) }, null);
    }

    public void Preview(WorkshopItem item, Node host, Action<Image?> done)
    {
        if (item.PreviewUrl.Length == 0) { done(null); return; }
        var http = new HttpRequest();
        host.AddChild(http);
        http.RequestCompleted += (result, code, headers, body) =>
        {
            http.QueueFree();
            if (result != (long)HttpRequest.Result.Success || code != 200) { done(null); return; }
            var image = new Image();
            bool ok = image.LoadPngFromBuffer(body) == Error.Ok || image.LoadJpgFromBuffer(body) == Error.Ok;
            done(ok ? image : null);
        };
        http.Request(item.PreviewUrl);
    }

    public void Vote(ulong id, bool up)
    {
        try { VoteCore(id, up); } catch (Exception) { }
    }

    /// <summary>Your own published maps' best thumbs up (for Well Received), when Steam answers.</summary>
    public void BestOfMine(Action<uint> done)
    {
        try { MineCore(done); } catch (Exception) { }
    }

#if NO_STEAMWORKS
    static void QueryCore(WorkshopSort sort, string search, string? tag, uint page, Action<List<WorkshopItem>?, uint, string?> done) => throw new PlatformNotSupportedException();
    static void SubscribeCore(ulong id) => throw new PlatformNotSupportedException();
    static string? InstalledCore(ulong id) => null;
    static void VoteCore(ulong id, bool up) { }
    static void MineCore(Action<uint> done) { }
#else
    static class Live
    {
        public static CallResult<SteamUGCQueryCompleted_t>? Queried, Mine;
        public static CallResult<RemoteStorageSubscribePublishedFileResult_t>? Subscribed;
        public static CallResult<SetUserItemVoteResult_t>? Voted;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void QueryCore(WorkshopSort sort, string search, string? tag, uint page, Action<List<WorkshopItem>?, uint, string?> done)
    {
        var app = SteamUtils.GetAppID();
        var type = search.Length > 0 ? EUGCQuery.k_EUGCQuery_RankedByTextSearch : sort switch
        {
            WorkshopSort.Newest => EUGCQuery.k_EUGCQuery_RankedByPublicationDate,
            WorkshopSort.TopRated => EUGCQuery.k_EUGCQuery_RankedByVote,
            WorkshopSort.MostPlayed => EUGCQuery.k_EUGCQuery_RankedByTotalUniqueSubscriptions,
            _ => EUGCQuery.k_EUGCQuery_RankedByTrend,
        };
        var query = SteamUGC.CreateQueryAllUGCRequest(type, EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items_ReadyToUse, app, app, page);
        if (search.Length > 0) SteamUGC.SetSearchText(query, search);
        if (tag != null) SteamUGC.AddRequiredTag(query, tag);
        if (type == EUGCQuery.k_EUGCQuery_RankedByTrend) SteamUGC.SetRankedByTrendDays(query, 30);
        Live.Queried = CallResult<SteamUGCQueryCompleted_t>.Create((r, failed) =>
        {
            if (failed || r.m_eResult != EResult.k_EResultOK) { SteamUGC.ReleaseQueryUGCRequest(r.m_handle); done(null, 0, $"The Workshop didn't answer ({(failed ? "no reply" : r.m_eResult.ToString())})"); return; }
            done(Items(r.m_handle, r.m_unNumResultsReturned), r.m_unTotalMatchingResults, null);
            SteamUGC.ReleaseQueryUGCRequest(r.m_handle);
        });
        Live.Queried.Set(SteamUGC.SendQueryUGCRequest(query));
    }

    static List<WorkshopItem> Items(UGCQueryHandle_t query, uint count)
    {
        var items = new List<WorkshopItem>();
        for (uint i = 0; i < count; i++)
        {
            if (!SteamUGC.GetQueryUGCResult(query, i, out var d) || d.m_eResult != EResult.k_EResultOK) continue;
            SteamUGC.GetQueryUGCPreviewURL(query, i, out string url, 1024);
            SteamUGC.GetQueryUGCStatistic(query, i, EItemStatistic.k_EItemStatistic_NumUniqueSubscriptions, out ulong players);
            var owner = new CSteamID(d.m_ulSteamIDOwner);
            SteamFriends.RequestUserInformation(owner, true);
            string author = SteamFriends.GetFriendPersonaName(owner);
            items.Add(new WorkshopItem(d.m_nPublishedFileId.m_PublishedFileId, d.m_rgchTitle, d.m_rgchDescription,
                string.IsNullOrEmpty(author) || author == "[unknown]" ? "a Steam player" : author,
                d.m_unVotesUp, d.m_unVotesDown, d.m_rgchTags.Split(',', StringSplitOptions.RemoveEmptyEntries), players, url ?? ""));
        }
        return items;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void SubscribeCore(ulong id)
    {
        var file = new PublishedFileId_t(id);
        Live.Subscribed = CallResult<RemoteStorageSubscribePublishedFileResult_t>.Create((_, _) => SteamUGC.DownloadItem(file, true));
        Live.Subscribed.Set(SteamUGC.SubscribeItem(file));
        SteamUGC.DownloadItem(file, true); // already subscribed: straight to the download
    }

    /// <summary>The item's folder once it's downloaded and up to date, else null.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static string? InstalledCore(ulong id)
    {
        var file = new PublishedFileId_t(id);
        var state = (EItemState)SteamUGC.GetItemState(file);
        if (!state.HasFlag(EItemState.k_EItemStateInstalled) || state.HasFlag(EItemState.k_EItemStateNeedsUpdate) || state.HasFlag(EItemState.k_EItemStateDownloading)) return null;
        return SteamUGC.GetItemInstallInfo(file, out _, out string folder, 1024, out _) ? folder : null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void VoteCore(ulong id, bool up)
    {
        Live.Voted = CallResult<SetUserItemVoteResult_t>.Create((_, _) => { });
        Live.Voted.Set(SteamUGC.SetUserItemVote(new PublishedFileId_t(id), up));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void MineCore(Action<uint> done)
    {
        var app = SteamUtils.GetAppID();
        var query = SteamUGC.CreateQueryUserUGCRequest(SteamUser.GetSteamID().GetAccountID(), EUserUGCList.k_EUserUGCList_Published,
            EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items_ReadyToUse, EUserUGCListSortOrder.k_EUserUGCListSortOrder_VoteScoreDesc, app, app, 1);
        Live.Mine = CallResult<SteamUGCQueryCompleted_t>.Create((r, failed) =>
        {
            if (!failed && r.m_eResult == EResult.k_EResultOK)
                done(Items(r.m_handle, r.m_unNumResultsReturned).Where(i => i.IsMission).Select(i => i.VotesUp).DefaultIfEmpty(0u).Max());
            SteamUGC.ReleaseQueryUGCRequest(r.m_handle);
        });
        Live.Mine.Set(SteamUGC.SendQueryUGCRequest(query));
    }
#endif
}
