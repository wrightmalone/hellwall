using Godot;

namespace Hellwall.Game;

/// <summary>Per-player preferences in user://settings.cfg: survives runs and relaunches, never touches the sim.</summary>
public static class Settings
{
    const string Path = "user://settings.cfg";
    static ConfigFile? _file;

    static ConfigFile File
    {
        get
        {
            if (_file != null) return _file;
            _file = new ConfigFile();
            _file.Load(Path); // missing on first run: defaults apply
            return _file;
        }
    }

    public static bool Get(string key, bool fallback) => (bool)File.GetValue("game", key, fallback);
    public static float Get(string key, float fallback) => (float)File.GetValue("game", key, fallback);
    public static int Get(string key, int fallback) => (int)File.GetValue("game", key, fallback);

    public static void Set(string key, Variant value)
    {
        File.SetValue("game", key, value);
        File.Save(Path);
    }
}
