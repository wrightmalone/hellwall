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
/// Godot's caches aren't copied: they rebuild themselves.
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
    public static void Migrate() => Migrated = Migrate(OldFolder, OS.GetUserDataDir());

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
            bool pass = first && copied && skipped && kept && backup && once;
            GD.Print(pass ? "hellwall-selftest: PASS migrate" : $"hellwall-selftest: FAIL migrate (first {first}, copied {copied}, logs skipped {skipped}, new kept {kept}, old left {backup}, once {once})");
            return pass;
        }
        finally
        {
            try { System.IO.Directory.Delete(root, true); } catch (System.IO.IOException) { }
        }
    }
}
