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

    /// <summary>The extension a shared map goes by (the same JSON the maps folder holds).</summary>
    public const string Extension = "hwmap";

    /// <summary>Write a map where someone chose, to send to someone else.</summary>
    public static void Export(ScenarioDef map, string path) => System.IO.File.WriteAllText(path, map.ToJson());

    /// <summary>
    /// Add a map someone sent: read it, check it (Problem), and save it among the hand-made maps
    /// under a name of its own (a number on the end if it would replace one). The map, or why not.
    /// </summary>
    public static (ScenarioDef? Map, string? Error) Import(string path)
    {
        ScenarioDef map;
        try
        {
            if (new System.IO.FileInfo(path).Length > 4_000_000) return (null, "the file is too big to be a map");
            map = ScenarioDef.FromJson(System.IO.File.ReadAllText(path));
        }
        catch (Exception e) { return (null, $"it isn't a map this build can read ({e.Message})"); }
        if (map.Problem() is { } problem) return (null, problem);
        string name = map.Name.Length > 0 ? map.Name : System.IO.Path.GetFileNameWithoutExtension(path);
        string id = IdFor(name);
        var taken = All().Select(m => m.Id).ToHashSet();
        for (int n = 2; taken.Contains(id); n++) id = $"{IdFor(name)}-{n}";
        map = map with { Id = id, Name = name };
        Save(map);
        return (map, null);
    }

    public static string Save(ScenarioDef map)
    {
        System.IO.Directory.CreateDirectory(Folder);
        string path = System.IO.Path.Combine(Folder, map.Id + ".json");
        System.IO.File.WriteAllText(path, map.ToJson());
        return path;
    }
}
