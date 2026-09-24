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

/// <param name="DormantPacks">How many sleeping packs to scatter at creation. Zero keeps tests quiet.</param>
public readonly record struct WorldOptions(uint Seed, int MapSize = Balance.DefaultMapSize, int DormantPacks = 0);

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

    public Horde Horde { get; } = new();
    public FlowField Flow { get; }
    public NoiseGrid Noise { get; }

    /// <summary>Ordered by id, ascending.</summary>
    public IReadOnlyList<Pack> Packs => _packs;

    internal SpatialHash Spatial { get; }

    readonly List<Building> _buildings = new();
    readonly List<Pack> _packs = new();

    /// <summary>Building id per tile, 0 for empty.</summary>
    readonly int[] _occupancy;

    readonly List<Command> _pending = new();
    readonly List<SimEvent> _events = new();
    int _nextId = 1;
    bool _flowDirty = true;

    /// <summary>Demons were added since the spatial hash was last built.</summary>
    bool _spatialStale;

    World(WorldOptions options)
    {
        Seed = options.Seed;
        Rng = new Rng(options.Seed);
        Terrain = MapGen.Generate(options.Seed, options.MapSize);
        _occupancy = new int[Terrain.Width * Terrain.Height];
        Flow = new FlowField(Terrain.Width, Terrain.Height);
        Noise = new NoiseGrid(Terrain.Width, Terrain.Height);
        Spatial = new SpatialHash(Terrain.Width, Terrain.Height);
    }

    public static World Create(WorldOptions options)
    {
        var world = new World(options);
        var (w, h) = Balance.Footprint(BuildingKind.Keep);
        int centre = options.MapSize / 2;
        string? reason = world.TryPlace(BuildingKind.Keep, centre - w / 2, centre - h / 2, makeNoise: false);
        if (reason != null) throw new InvalidOperationException($"Keep could not be placed: {reason}");
        world.EnsureFlow();
        world.ScatterPacks(options.DormantPacks);
        return world;
    }

    public void Enqueue(Command command) => _pending.Add(command);

    /// <summary>Advance one tick: drain commands, then run systems.</summary>
    public void Step()
    {
        if (Outcome != Outcome.Running) return;
        ApplyCommands();
        EnsureFlow();
        Noise.Decay();
        WakePacks();
        HordeSystem.Step(this);
        _spatialStale = false;
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

    /// <summary>Demons can stand here: walkable terrain with no building on it.</summary>
    public bool IsWalkable(int x, int y) =>
        Terrain.InBounds(x, y) && Terrain.IsWalkable(Terrain.Get(x, y)) && _occupancy[Terrain.Index(x, y)] == 0;

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
        if (DemonsInRect(x, y, w, h)) return "demons in the way";
        return null;
    }

    /// <summary>
    /// The nearest tile to (x, y), searching outward ring by ring up to
    /// maxRadius, that a demon could stand on and path to the colony from.
    /// Hosts use it to aim scenario spawns.
    /// </summary>
    public (int X, int Y)? FindReachableTileNear(int x, int y, int maxRadius)
    {
        EnsureFlow();
        for (int r = 0; r <= maxRadius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int tx = x + dx, ty = y + dy;
                    if (IsWalkable(tx, ty) && Flow.DistAt(tx, ty) != FlowField.Unreachable) return (tx, ty);
                }
            }
        }
        return null;
    }

    void EnsureFlow()
    {
        if (!_flowDirty) return;
        Flow.Build(this);
        _flowDirty = false;
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
            PlaceBuilding p => TryPlace(p.Kind, p.X, p.Y, makeNoise: true),
            Demolish d => TryDemolish(d.BuildingId),
            SpawnDemons s => TrySpawn(s),
            MakeNoise m => TryNoise(m.X + 0.5f, m.Y + 0.5f, m.Radius, m.Intensity),
            _ => "unknown command",
        };
        if (reason != null) _events.Add(new CommandRejected(Tick, reason, command));
    }

    string? TryPlace(BuildingKind kind, int x, int y, bool makeNoise)
    {
        string? reason = CheckPlacement(kind, x, y);
        if (reason != null) return reason;

        var (w, h) = Balance.Footprint(kind);
        var building = new Building { Id = _nextId++, Kind = kind, X = x, Y = y, W = w, H = h };
        _buildings.Add(building);
        Stamp(building, building.Id);
        _flowDirty = true;
        _events.Add(new BuildingPlaced(Tick, building.Id, kind, x, y));
        if (makeNoise) TryNoise(x + w / 2f, y + h / 2f, Balance.BuildNoiseRadius, Balance.BuildNoiseIntensity);
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
        _flowDirty = true;
        _events.Add(new BuildingRemoved(Tick, building.Id, building.Kind));
        return null;
    }

    string? TrySpawn(SpawnDemons s)
    {
        if (s.Count <= 0) return "count must be positive";
        if (!Terrain.InBounds(s.X, s.Y)) return "out of bounds";
        int spawned = SpawnCluster(s.Kind, s.X, s.Y, s.Count);
        if (spawned == 0) return "no walkable ground there";
        _events.Add(new DemonsSpawned(Tick, s.Kind, spawned));
        return null;
    }

    string? TryNoise(float x, float y, float radius, float intensity)
    {
        if (radius <= 0 || intensity <= 0) return "radius and intensity must be positive";
        Noise.Emit(x, y, radius, intensity);
        _events.Add(new NoiseMade(Tick, x, y, radius));
        return null;
    }

    /// <summary>
    /// Scatter demons over a disc around a tile centre, sized so the crowd
    /// starts at about SpawnDensity per tile. Returns how many found ground.
    /// </summary>
    int SpawnCluster(DemonKind kind, int cx, int cy, int count)
    {
        float radius = MathF.Sqrt(count / (MathF.PI * Balance.SpawnDensity)) + 1;
        int spawned = 0;
        for (int n = 0; n < count; n++)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float angle = (float)(Rng.NextDouble() * 2 * Math.PI);
                float r = radius * MathF.Sqrt((float)Rng.NextDouble());
                float x = cx + 0.5f + MathF.Cos(angle) * r;
                float y = cy + 0.5f + MathF.Sin(angle) * r;
                if (x < 0 || y < 0 || !IsWalkable((int)x, (int)y)) continue;
                Horde.Add(kind, x, y);
                spawned++;
                break;
            }
        }
        if (spawned > 0) _spatialStale = true;
        return spawned;
    }

    void ScatterPacks(int count)
    {
        int centre = Terrain.Width / 2;
        int minD2 = Balance.PackMinDistanceFromKeep * Balance.PackMinDistanceFromKeep;
        for (int n = 0, attempts = 0; n < count && attempts < count * 200; attempts++)
        {
            int x = Rng.NextInt(Terrain.Width);
            int y = Rng.NextInt(Terrain.Height);
            int dx = x - centre, dy = y - centre;
            if (dx * dx + dy * dy < minD2) continue;
            if (!IsWalkable(x, y) || Flow.DistAt(x, y) == FlowField.Unreachable) continue;
            var kind = Rng.Chance(Balance.PackHoundChance) ? DemonKind.Hound : DemonKind.Imp;
            int size = Balance.PackMinCount + Rng.NextInt(Balance.PackMaxCount - Balance.PackMinCount + 1);
            _packs.Add(new Pack { Id = _nextId++, X = x, Y = y, Count = size, Kind = kind });
            n++;
        }
    }

    void WakePacks()
    {
        foreach (var pack in _packs)
        {
            if (pack.Awake || Noise.LevelAtTile(pack.X, pack.Y) < Balance.WakeThreshold) continue;
            pack.Awake = true;
            int spawned = SpawnCluster(pack.Kind, pack.X, pack.Y, pack.Count);
            _events.Add(new PackWoke(Tick, pack.Id, pack.X, pack.Y, spawned));
        }
    }

    /// <summary>
    /// Any demon body overlapping the rectangle. Searches one tile beyond it,
    /// because the hash is built at the start of a tick and a demon moves at
    /// most ~0.2 tiles per tick, so it can't reach the footprint from further out.
    /// </summary>
    bool DemonsInRect(int x, int y, int w, int h)
    {
        if (Horde.Count == 0) return false;
        if (_spatialStale)
        {
            Spatial.Build(Horde);
            _spatialStale = false;
        }
        const float r = Balance.DemonRadius;
        for (int ty = Math.Max(0, y - 1); ty <= Math.Min(Terrain.Height - 1, y + h); ty++)
        {
            for (int tx = Math.Max(0, x - 1); tx <= Math.Min(Terrain.Width - 1, x + w); tx++)
            {
                int cell = ty * Terrain.Width + tx;
                for (int k = Spatial.CellStart[cell]; k < Spatial.CellStart[cell + 1]; k++)
                {
                    int i = Spatial.Items[k];
                    float ux = Horde.X[i], uy = Horde.Y[i];
                    if (ux > x - r && ux < x + w + r && uy > y - r && uy < y + h + r) return true;
                }
            }
        }
        return false;
    }

    void Stamp(Building b, int value)
    {
        for (int ty = b.Y; ty < b.Y + b.H; ty++)
            for (int tx = b.X; tx < b.X + b.W; tx++)
                _occupancy[Terrain.Index(tx, ty)] = value;
    }
}
