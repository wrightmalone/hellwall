using System.Text.Json;
using Hellwall.Sim;

namespace Hellwall.Headless;

/// <summary>
/// Loads a tick-stamped command script. The file format is flat JSON so it
/// stays hand-editable:
///
///   { "name": "smoke", "commands": [
///       { "tick": 0,   "type": "place", "kind": "House", "x": 68, "y": 62 },
///       { "tick": 200, "type": "demolish", "id": 2 } ] }
/// </summary>
public static class Script
{
    sealed record Entry(int Tick, string Type, string? Kind, int X, int Y, int Id);

    sealed record File(string Name, List<Entry> Commands);

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static (string Name, List<ScriptedCommand> Commands) Load(string path)
    {
        var file = JsonSerializer.Deserialize<File>(System.IO.File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"{path}: empty script");

        var commands = new List<ScriptedCommand>();
        foreach (var e in file.Commands)
        {
            Command command = e.Type switch
            {
                "place" => new PlaceBuilding(ParseKind(e.Kind, path), e.X, e.Y),
                "demolish" => new Demolish(e.Id),
                _ => throw new InvalidDataException($"{path}: unknown command type '{e.Type}' at tick {e.Tick}"),
            };
            commands.Add(new ScriptedCommand(e.Tick, command));
        }

        // Stable sort: commands stamped on the same tick keep file order.
        var ordered = commands.Select((c, i) => (c, i)).OrderBy(p => p.c.Tick).ThenBy(p => p.i).Select(p => p.c).ToList();
        return (file.Name, ordered);
    }

    static BuildingKind ParseKind(string? kind, string path) =>
        Enum.TryParse<BuildingKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : throw new InvalidDataException($"{path}: unknown building kind '{kind}'");
}
