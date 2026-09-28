using System.Runtime.CompilerServices;
using Godot;
using Hellwall.Sim;
#if !NO_STEAMWORKS
using Steamworks;
#endif

namespace Hellwall.Game;

/// <summary>
/// Publishing a hand-made map or mission to the Steam Workshop (docs/plans/steam.md, step 5). The map
/// is staged in user://workshop/[id]/ (its JSON as map.json, the folder the Workshop item carries),
/// with a preview drawn from its tiles beside it; then Steam's UGC calls create the item (the first
/// time), set its title, description (the briefing), tags, content and preview, and upload. The item's
/// id goes back into the map file, so publishing again updates the same item. Only with Steam up:
/// the editor's button says so otherwise. Staging and the preview need no Steam (the editor's self-test
/// checks them).
/// </summary>
public static class Workshop
{
    /// <summary>What a publish is doing, for the editor to show (empty when nothing is).</summary>
    public static string Status { get; private set; } = "";
    public static bool Busy { get; private set; }

    /// <summary>Tags the Workshop browser (step 6) filters by: mission or map, the kind of map, its size, a mission's difficulty.</summary>
    public static List<string> Tags(ScenarioDef map)
    {
        var tags = new List<string> { map.IsMission ? "Mission" : "Map", map.Map.ToString(), $"{map.MapSize}" };
        if (map.IsMission) tags.Add(map.Difficulty.ToString());
        return tags;
    }

    /// <summary>The folder a map is staged in, with map.json in it; and its preview, beside the folder (the Workshop keeps the preview apart from the content).</summary>
    public static (string Folder, string Preview) Stage(ScenarioDef map)
    {
        string root = ProjectSettings.GlobalizePath($"user://workshop/{map.Id}");
        string folder = System.IO.Path.Combine(root, "content");
        if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "map.json"), map.ToJson());
        string preview = System.IO.Path.Combine(root, "preview.png");
        Preview(map).SavePng(preview);
        return (folder, preview);
    }

    /// <summary>
    /// The map from above, for the Workshop page: its tiles in the terrain's colours, the Keep in
    /// gold, Hellgates in red, packs as dark red dots, standing buildings in blue. 512 px square
    /// (Steam wants previews under 1 MB).
    /// </summary>
    public static Image Preview(ScenarioDef map)
    {
        const int Size = 512;
        int n = map.MapSize;
        var tiles = map.DecodeTiles() ?? new Tile[n * n];
        float k = Size / (float)n;
        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                image.SetPixel(x, y, Palette.Tiles[(int)tiles[Math.Min(n - 1, (int)(y / k)) * n + Math.Min(n - 1, (int)(x / k))]]);
        void Box(float x, float y, float w, float h, Color c)
        {
            int x0 = (int)(x * k), y0 = (int)(y * k), x1 = (int)((x + w) * k), y1 = (int)((y + h) * k);
            image.FillRect(new Rect2I(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0)), c);
        }
        foreach (var b in map.PlacedBuildings) Box(b.X, b.Y, b.Turned ? 1 : 2, b.Turned ? 3 : 2, new Color(0.45f, 0.7f, 1f));
        foreach (var p in map.PlacedPacks) Box(p.X - 1.5f, p.Y - 1.5f, 3, 3, new Color(0.55f, 0.08f, 0.08f));
        foreach (var g in map.PlacedGates) Box(g.X, g.Y, Hellgate.Size, Hellgate.Size, new Color(0.95f, 0.15f, 0.2f));
        int kx = map.KeepX >= 0 ? map.KeepX : n / 2, ky = map.KeepY >= 0 ? map.KeepY : n / 2;
        Box(kx - 2, ky - 2, 5, 5, new Color(0.94f, 0.76f, 0.36f));
        return image;
    }

    /// <summary>
    /// Publish (or update) a map: staged, then sent. Done, when it finishes, gets the map as published
    /// (its WorkshopId set) or null if it failed; Status says why, either way. Steam must be up.
    /// </summary>
    public static void Publish(ScenarioDef map, string changeNote, Action<ScenarioDef?> done)
    {
        if (!Steam.Running) { Status = "Steam isn't running: the Workshop needs it"; done(null); return; }
        if (Busy) { Status = "Already publishing"; return; }
        if (map.Problem() is { } problem) { Status = $"Can't publish it: {problem}"; done(null); return; }
        var (folder, preview) = Stage(map);
        Busy = true;
        Status = map.WorkshopId == 0 ? "Creating the Workshop item..." : "Updating the Workshop item...";
        try
        {
            PublishCore(map, folder, preview, changeNote, result =>
            {
                Busy = false;
                if (result is { } published) Diagnostics.Note($"workshop: published '{map.Name}' as {published.WorkshopId}");
                done(result);
            });
        }
        catch (Exception e)
        {
            Busy = false;
            Status = $"Couldn't reach the Workshop ({e.GetType().Name}: {e.Message})";
            done(null);
        }
    }

    /// <summary>While uploading: how far, for the editor's status line (called each frame).</summary>
    public static void Poll()
    {
        if (!Busy) return;
        try { PollCore(); } catch (Exception) { }
    }

#if NO_STEAMWORKS
    static void PublishCore(ScenarioDef map, string folder, string preview, string note, Action<ScenarioDef?> done) => throw new PlatformNotSupportedException("this build has no Steam");
    static void PollCore() { }
#else
    // Steam answers later, through these (kept alive here: a collected CallResult never fires). In a
    // class of their own: fields of Steamworks' types on Workshop itself would load Steamworks.NET the
    // moment anything touched Workshop (its Busy, say, every frame in the editor), and on a machine
    // that can't load it (an Apple-silicon Mac) that throws every frame.
    static class Live
    {
        public static CallResult<CreateItemResult_t>? Created;
        public static CallResult<SubmitItemUpdateResult_t>? Submitted;
        public static UGCUpdateHandle_t Update;
        public static bool Updating;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void PublishCore(ScenarioDef map, string folder, string preview, string note, Action<ScenarioDef?> done)
    {
        var app = SteamUtils.GetAppID();
        void Update(PublishedFileId_t id)
        {
            var item = map with { WorkshopId = id.m_PublishedFileId };
            Live.Update = SteamUGC.StartItemUpdate(app, id);
            SteamUGC.SetItemTitle(Live.Update, map.Name.Length > 0 ? map.Name : "Untitled");
            string about = map.Briefing.Length > 0 ? map.Briefing : map.IsMission ? "A Hellwall mission." : "A Hellwall map.";
            SteamUGC.SetItemDescription(Live.Update, about.Length > 7900 ? about[..7900] : about);
            SteamUGC.SetItemTags(Live.Update, Tags(map));
            SteamUGC.SetItemContent(Live.Update, folder);
            SteamUGC.SetItemPreview(Live.Update, preview);
            SteamUGC.SetItemVisibility(Live.Update, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic);
            Live.Updating = true;
            Live.Submitted = CallResult<SubmitItemUpdateResult_t>.Create((r, failed) =>
            {
                Live.Updating = false;
                if (failed || r.m_eResult != EResult.k_EResultOK)
                {
                    Status = $"The upload failed ({(failed ? "no answer from Steam" : r.m_eResult.ToString())})";
                    done(null);
                    return;
                }
                Status = r.m_bUserNeedsToAcceptWorkshopLegalAgreement
                    ? "Published, but hidden until you accept the Workshop agreement (the Steam overlay has it open)"
                    : "Published to the Workshop";
                if (r.m_bUserNeedsToAcceptWorkshopLegalAgreement) SteamFriends.ActivateGameOverlayToWebPage($"steam://url/CommunityFilePage/{id.m_PublishedFileId}");
                done(item);
            });
            Live.Submitted.Set(SteamUGC.SubmitItemUpdate(Live.Update, note.Length > 0 ? note : null));
        }
        if (map.WorkshopId != 0) { Update(new PublishedFileId_t(map.WorkshopId)); return; }
        Live.Created = CallResult<CreateItemResult_t>.Create((r, failed) =>
        {
            if (failed || r.m_eResult != EResult.k_EResultOK)
            {
                Busy = false;
                Status = $"Couldn't create the Workshop item ({(failed ? "no answer from Steam" : r.m_eResult.ToString())})";
                done(null);
                return;
            }
            Update(r.m_nPublishedFileId);
        });
        Live.Created.Set(SteamUGC.CreateItem(app, EWorkshopFileType.k_EWorkshopFileTypeCommunity));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void PollCore()
    {
        if (!Live.Updating) return;
        var stage = SteamUGC.GetItemUpdateProgress(Live.Update, out ulong sent, out ulong total);
        string what = stage switch
        {
            EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig => "Preparing",
            EItemUpdateStatus.k_EItemUpdateStatusPreparingContent => "Preparing the map",
            EItemUpdateStatus.k_EItemUpdateStatusUploadingContent => "Uploading the map",
            EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile => "Uploading the preview",
            EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges => "Finishing",
            _ => "Publishing",
        };
        Status = total > 0 ? $"{what}: {100 * sent / total}%" : $"{what}...";
    }
#endif
}
