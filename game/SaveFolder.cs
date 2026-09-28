using Godot;

namespace Hellwall.Game;

/// <summary>
/// Where the game keeps a player's things (user://): its own folder, not Godot's generic one, so
/// Steam Cloud's paths never change after release (docs/plans/steam.md). project.godot sets it:
///   Windows  %APPDATA%\Hellwall
///   macOS    ~/Library/Application Support/Hellwall
///   Linux    ~/.local/share/Hellwall
/// Builds up to 0.29 kept everything in Godot's app_userdata\Hellwall: the first launch of a newer
/// build copies it across once (saves, campaign, settings, scores, maps, crash reports), never over
/// anything already in the new folder, and leaves the old one as it was, as a backup. Logs and
/// Godot's caches aren't copied: they rebuild themselves. Then saves loose in it (up to 0.31) are
/// moved into saves/ and autosaves/ (Tidy), where Steam Cloud syncs the one and not the other.
/// </summary>
public static class SaveFolder
{
    const string Marker = ".moved-from-godot-folder";
    static readonly string[] Skip = ["logs", "shader_cache", "vulkan", "objectdb_snapshots"];

    /// <summary>What the first launch copied, for the diagnostics trail (null: nothing to do).</summary>
    public static string? Migrated { get; private set; }

    /// <summary>The folder builds up to 0.29 used: Godot's own, under the OS's data folder.</summary>
    public static string OldFolder => System.IO.Path.Combine(OS.GetDataDir(), "Godot", "app_userdata", "Hellwall");

    /// <summary>First thing at startup, before anything reads a setting or a save.</summary>
    public static void Migrate()
    {
        Migrated = Migrate(OldFolder, OS.GetUserDataDir());
        if (Tidy(OS.GetUserDataDir()) is { } tidied) Migrated = Migrated == null ? tidied : Migrated + "; " + tidied;
    }

    /// <summary>
    /// Saves from builds up to 0.31 sat loose in the folder: into saves/ (synced by Steam Cloud) and
    /// autosaves/ (not) they go, where Main.SlotPath now looks. Never over one already there.
    /// </summary>
    public static string? Tidy(string dir)
    {
        var moves = new List<(string From, string To)> { ("quicksave.hwsave", "saves/quicksave.hwsave") };
        foreach (int n in new[] { 1, 2, 3, 9 }) moves.Add(($"slot{n}.hwsave", $"saves/slot{n}.hwsave"));
        for (int n = 1; n <= 5; n++) moves.Add(($"slot{10 + n}.hwsave", $"autosaves/autosave{n}.hwsave"));
        int moved = 0;
        try
        {
            foreach (var (from, to) in moves)
                foreach (var ext in new[] { "", ".txt" })
                {
                    string src = System.IO.Path.Combine(dir, from + ext), dst = System.IO.Path.Combine(dir, to + ext);
                    if (!System.IO.File.Exists(src) || System.IO.File.Exists(dst)) continue;
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst)!);
                    System.IO.File.Move(src, dst);
                    if (ext == "") moved++;
                }
        }
        catch (Exception e) { return $"couldn't move the saves into their folders ({e.Message})"; }
        return moved > 0 ? $"{moved} saves moved into saves/ and autosaves/" : null;
    }

    /// <summary>Copy an old folder's contents into a new one, once (a marker says it's done). Returns what was done, or null.</summary>
    public static string? Migrate(string oldDir, string newDir)
    {
        try
        {
            System.IO.Directory.CreateDirectory(newDir);
            string marker = System.IO.Path.Combine(newDir, Marker);
            if (System.IO.File.Exists(marker)) return null;
            string? note = null;
            if (System.IO.Directory.Exists(oldDir) && System.IO.Path.GetFullPath(oldDir).TrimEnd('/', '\\') != System.IO.Path.GetFullPath(newDir).TrimEnd('/', '\\'))
            {
                int copied = 0, kept = 0;
                foreach (var file in System.IO.Directory.GetFiles(oldDir, "*", System.IO.SearchOption.AllDirectories))
                {
                    string relative = System.IO.Path.GetRelativePath(oldDir, file);
                    string top = relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)[0];
                    if (Skip.Contains(top)) continue;
                    string to = System.IO.Path.Combine(newDir, relative);
                    if (System.IO.File.Exists(to)) { kept++; continue; } // the new folder's own wins
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
                    System.IO.File.Copy(file, to);
                    copied++;
                }
                note = $"moved to its own folder: {copied} files copied from {oldDir}{(kept > 0 ? $", {kept} left as the new folder had them" : "")}; the old folder is left as a backup";
            }
            System.IO.File.WriteAllText(marker, (note ?? "nothing to move") + "\n" + DateTime.Now.ToString("O") + "\n");
            return note;
        }
        catch (Exception e)
        {
            // Better to start with an empty folder than not to start: the old one is still there.
            return $"couldn't move the old folder across ({e.Message}); it's still at {oldDir}";
        }
    }

    /// <summary>--selftest=migrate: a made-up old folder moved into a made-up new one, the rules checked.</summary>
    public static bool SelfTest()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hellwall-migrate-" + Guid.NewGuid().ToString("N"));
        string oldDir = System.IO.Path.Combine(root, "old"), newDir = System.IO.Path.Combine(root, "new");
        try
        {
            void Write(string dir, string path, string text)
            {
                string full = System.IO.Path.Combine(dir, path);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
                System.IO.File.WriteAllText(full, text);
            }
            Write(oldDir, "campaign.cfg", "old campaign");
            Write(oldDir, "slot1.hwsave", "old save");
            Write(oldDir, "maps/ford.json", "old map");
            Write(oldDir, "logs/godot.log", "old log");
            Write(oldDir, "settings.cfg", "old settings");
            Write(newDir, "settings.cfg", "new settings");
            string Read(string path) => System.IO.File.Exists(System.IO.Path.Combine(newDir, path)) ? System.IO.File.ReadAllText(System.IO.Path.Combine(newDir, path)) : "";
            bool first = Migrate(oldDir, newDir) != null;
            bool copied = Read("campaign.cfg") == "old campaign" && Read("slot1.hwsave") == "old save" && Read("maps/ford.json") == "old map";
            bool skipped = Read("logs/godot.log") == "";
            bool kept = Read("settings.cfg") == "new settings";
            bool backup = System.IO.File.Exists(System.IO.Path.Combine(oldDir, "campaign.cfg"));
            // Once only: a second launch, even with the old folder changed since, leaves the new one alone.
            Write(oldDir, "campaign.cfg", "changed later");
            bool once = Migrate(oldDir, newDir) == null && Read("campaign.cfg") == "old campaign";
            // Saves loose in the folder (up to 0.31) go into saves/ and autosaves/, never over one there.
            Write(newDir, "quicksave.hwsave", "quick");
            Write(newDir, "slot12.hwsave", "auto 2");
            Write(newDir, "slot12.hwsave.txt", "auto 2 summary");
            Write(newDir, "slot2.hwsave", "loose two");
            Write(newDir, "saves/slot2.hwsave", "already there");
            Tidy(newDir);
            bool tidied = Read("saves/quicksave.hwsave") == "quick" && Read("autosaves/autosave2.hwsave") == "auto 2" && Read("autosaves/autosave2.hwsave.txt") == "auto 2 summary"
                && Read("saves/slot2.hwsave") == "already there" && Read("quicksave.hwsave") == "";
            bool pass = first && copied && skipped && kept && backup && once && tidied;
            GD.Print(pass ? "hellwall-selftest: PASS migrate" : $"hellwall-selftest: FAIL migrate (first {first}, copied {copied}, logs skipped {skipped}, new kept {kept}, old left {backup}, once {once}, saves into folders {tidied})");
            return pass;
        }
        finally
        {
            try { System.IO.Directory.Delete(root, true); } catch (System.IO.IOException) { }
        }
    }
}
