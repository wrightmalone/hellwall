using Godot;

namespace Hellwall.Game;

/// <summary>
/// Per-player preferences: survive runs and relaunches, never touch the sim. Most are the player's
/// wherever they play, in user://settings.cfg, which Steam Cloud syncs. A few belong to the machine
/// (a laptop's screen isn't a desktop's): those are in user://display.cfg, which it doesn't
/// (docs/plans/steam.md). A machine key found in settings.cfg (as builds up to 0.32 kept it) moves
/// across the first time it's read.
/// </summary>
public static class Settings
{
    const string Path = "user://settings.cfg", MachinePath = "user://display.cfg";
    /// <summary>Kept per machine, never synced.</summary>
    static readonly HashSet<string> MachineKeys = ["ui_scale", "fullscreen"];

    static ConfigFile? _file, _machine;

    static ConfigFile Load(ref ConfigFile? file, string path)
    {
        if (file != null) return file;
        file = new ConfigFile();
        file.Load(path); // missing on first run: defaults apply
        return file;
    }

    static ConfigFile File => Load(ref _file, Path);

    static ConfigFile Machine
    {
        get
        {
            if (_machine != null) return _machine;
            var machine = Load(ref _machine, MachinePath);
            bool moved = false;
            foreach (var key in MachineKeys)
                if (!machine.HasSectionKey("game", key) && File.HasSectionKey("game", key))
                {
                    machine.SetValue("game", key, File.GetValue("game", key));
                    File.EraseSectionKey("game", key);
                    moved = true;
                }
            if (moved) { machine.Save(MachinePath); File.Save(Path); }
            return machine;
        }
    }

    static (ConfigFile File, string Path) For(string key) => MachineKeys.Contains(key) ? (Machine, MachinePath) : (File, Path);

    public static bool Get(string key, bool fallback) => (bool)For(key).File.GetValue("game", key, fallback);
    public static float Get(string key, float fallback) => (float)For(key).File.GetValue("game", key, fallback);
    public static int Get(string key, int fallback) => (int)For(key).File.GetValue("game", key, fallback);

    public static void Set(string key, Variant value)
    {
        var (file, path) = For(key);
        file.SetValue("game", key, value);
        file.Save(path);
    }
}
