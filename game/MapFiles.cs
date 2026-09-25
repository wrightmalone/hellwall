using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>Hand-made maps: one scenario JSON each (terrain inside), in user://maps/. The map editor writes them; the skirmish menu lists them.</summary>
public static class MapFiles
{
    const string Dir = "user://maps";

    public static string Folder => ProjectSettings.GlobalizePath(Dir);

    public static IEnumerable<ScenarioDef> All()
    {
        if (!System.IO.Directory.Exists(Folder)) yield break;
        foreach (var path in System.IO.Directory.GetFiles(Folder, "*.json").Order())
        {
            ScenarioDef? map = null;
            try { map = ScenarioDef.FromJson(System.IO.File.ReadAllText(path)); }
            catch (Exception e) { GD.PushWarning($"skipping map {path}: {e.Message}"); }
            if (map != null) yield return map;
        }
    }

    /// <summary>A file-safe id from a name: lower case, letters and digits, dashes between.</summary>
    public static string IdFor(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var id = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return id.Length == 0 ? "untitled" : id;
    }

    public static string Save(ScenarioDef map)
    {
        System.IO.Directory.CreateDirectory(Folder);
        string path = System.IO.Path.Combine(Folder, map.Id + ".json");
        System.IO.File.WriteAllText(path, map.ToJson());
        return path;
    }
}
