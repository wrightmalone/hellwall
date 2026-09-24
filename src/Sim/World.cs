namespace Hellwall.Sim;

public enum BuildingKind : byte
{
    Keep,
    House,
    Wall,
}

public enum Outcome : byte
{
    Running,
    Lost,
    Won,
}

public sealed class Building
{
    public int Id;
    public BuildingKind Kind;

    /// <summary>Top-left tile of the footprint.</summary>
    public int X;
    public int Y;
    public int W;
    public int H;
}

public readonly record struct WorldOptions(uint Seed, int MapSize = Balance.DefaultMapSize);

/// <summary>
/// The whole simulation state and its fixed-rate step. Plain data plus the
/// systems that advance it; Godot and the headless runner are both just
/// consumers that enqueue commands and read state and events.
/// </summary>
public sealed class World
{
    public int Tick { get; private set; }
    public uint Seed { get; }
    public Rng Rng { get; }
    public Terrain Terrain { get; }
    public Outcome Outcome { get; private set; } = Outcome.Running;

    /// <summary>Ordered by id, ascending. Hashing and iteration rely on that.</summary>
    public IReadOnlyList<Building> Buildings => _buildings;

    readonly List<Building> _buildings = new();

    /// <summary>Building id per tile, 0 for empty.</summary>
    readonly int[] _occupancy;

    readonly List<Command> _pending = new();
    readonly List<SimEvent> _events = new();
    int _nextId = 1;

    World(WorldOptions options)
    {
        Seed = options.Seed;
        Rng = new Rng(options.Seed);
        Terrain = MapGen.Generate(options.Seed, options.MapSize);
        _occupancy = new int[Terrain.Width * Terrain.Height];
    }

    public static World Create(WorldOptions options)
    {
        var world = new World(options);
        var (w, h) = Balance.Footprint(BuildingKind.Keep);
        int centre = options.MapSize / 2;
        string? reason = world.TryPlace(BuildingKind.Keep, centre - w / 2, centre - h / 2);
        if (reason != null) throw new InvalidOperationException($"Keep could not be placed: {reason}");
        return world;
    }

    public void Enqueue(Command command) => _pending.Add(command);

    /// <summary>Advance one tick: drain commands, then run systems.</summary>
    public void Step()
    {
        if (Outcome != Outcome.Running) return;
        ApplyCommands();
        Tick++;
    }

    /// <summary>
    /// Apply queued commands without advancing time, so building works while
    /// paused. Deterministic: with no ticks elapsing, applying a command now is
    /// identical to applying it at the top of the next tick, and a replay that
    /// stamps it at the current tick reproduces the same state.
    /// </summary>
    public void FlushCommands()
    {
        if (Outcome != Outcome.Running) return;
        ApplyCommands();
    }

    /// <summary>Hand every event since the last drain to the caller, and forget them.</summary>
    public List<SimEvent> DrainEvents()
    {
        var drained = new List<SimEvent>(_events);
        _events.Clear();
        return drained;
    }

    public int BuildingIdAt(int x, int y) => Terrain.InBounds(x, y) ? _occupancy[Terrain.Index(x, y)] : 0;

    /// <summary>Why a building can't go here, or null if it can. Lets the client preview placement.</summary>
    public string? CheckPlacement(BuildingKind kind, int x, int y)
    {
        var (w, h) = Balance.Footprint(kind);
        if (kind == BuildingKind.Keep && _buildings.Count > 0) return "only one Keep";
        if (x < 0 || y < 0 || x + w > Terrain.Width || y + h > Terrain.Height) return "out of bounds";
        for (int ty = y; ty < y + h; ty++)
        {
            for (int tx = x; tx < x + w; tx++)
            {
                if (!Terrain.IsBuildable(Terrain.Get(tx, ty))) return "terrain not buildable";
                if (_occupancy[Terrain.Index(tx, ty)] != 0) return "tile occupied";
            }
        }
        return null;
    }

    void ApplyCommands()
    {
        if (_pending.Count == 0) return;
        var commands = _pending.ToArray();
        _pending.Clear();
        foreach (var command in commands) Apply(command);
    }

    void Apply(Command command)
    {
        string? reason = command switch
        {
            PlaceBuilding p => TryPlace(p.Kind, p.X, p.Y),
            Demolish d => TryDemolish(d.BuildingId),
            _ => "unknown command",
        };
        if (reason != null) _events.Add(new CommandRejected(Tick, reason, command));
    }

    string? TryPlace(BuildingKind kind, int x, int y)
    {
        string? reason = CheckPlacement(kind, x, y);
        if (reason != null) return reason;

        var (w, h) = Balance.Footprint(kind);
        var building = new Building { Id = _nextId++, Kind = kind, X = x, Y = y, W = w, H = h };
        _buildings.Add(building);
        Stamp(building, building.Id);
        _events.Add(new BuildingPlaced(Tick, building.Id, kind, x, y));
        return null;
    }

    string? TryDemolish(int id)
    {
        int index = _buildings.FindIndex(b => b.Id == id);
        if (index < 0) return "no such building";
        var building = _buildings[index];
        if (building.Kind == BuildingKind.Keep) return "the Keep cannot be demolished";

        _buildings.RemoveAt(index);
        Stamp(building, 0);
        _events.Add(new BuildingRemoved(Tick, building.Id, building.Kind));
        return null;
    }

    void Stamp(Building b, int value)
    {
        for (int ty = b.Y; ty < b.Y + b.H; ty++)
            for (int tx = b.X; tx < b.X + b.W; tx++)
                _occupancy[Terrain.Index(tx, ty)] = value;
    }
}
