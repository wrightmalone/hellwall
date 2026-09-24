namespace Hellwall.Sim;

public enum Outcome : byte
{
    Running,
    Lost,
    Won,
}

/// <param name="DormantPacks">How many sleeping packs to scatter at creation. Zero keeps tests quiet.</param>
/// <param name="Rules">Content numbers; null for the defaults shipped in rules.json.</param>
/// <param name="Survival">Run the day clock and wave schedule. Off for tests and probes that script their own waves.</param>
public readonly record struct WorldOptions(uint Seed, int MapSize = Balance.DefaultMapSize, int DormantPacks = 0, Rules? Rules = null, bool Survival = false);

public sealed class WorldStats
{
    public int DemonsKilled;
    public int BuildingsLost;
    public int UnitsLost;
}

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
    public Rules Rules { get; }
    public Terrain Terrain { get; }
    public Outcome Outcome { get; private set; } = Outcome.Running;
    public Colony Colony { get; }
    public WorldStats Stats { get; } = new();

    /// <summary>The day clock and wave schedule, or null when the run isn't a survival run.</summary>
    public Survival? Survival { get; }

    /// <summary>1-based; counts on even without a survival schedule.</summary>
    public int Day => Tick / (int)(Rules.Survival.DaySeconds * Balance.TickHz) + 1;

    /// <summary>Ordered by id, ascending. Hashing and iteration rely on that.</summary>
    public IReadOnlyList<Building> Buildings => _buildings;

    /// <summary>Ordered by id, ascending.</summary>
    public IReadOnlyList<Unit> Units => _units;

    public Horde Horde { get; } = new();
    public FlowField Flow { get; }
    public NoiseGrid Noise { get; }

    /// <summary>Ordered by id, ascending.</summary>
    public IReadOnlyList<Pack> Packs => _packs;

    /// <summary>
    /// The concrete lists, for the systems. foreach over an IReadOnlyList
    /// boxes its enumerator, which would allocate every tick.
    /// </summary>
    internal List<Building> BuildingList => _buildings;
    internal List<Unit> UnitList => _units;

    internal SpatialHash Spatial { get; }
    internal bool NetworkDirty { get; private set; } = true;

    readonly List<Building> _buildings = new();
    readonly Dictionary<int, Building> _buildingById = new();
    readonly List<Unit> _units = new();
    readonly List<Pack> _packs = new();

    /// <summary>Soldier route maps by destination tile, shared by every unit sent there; rebuilt when buildings change.</summary>
    readonly Dictionary<int, FlowField> _humanFields = new();
    readonly List<FlowField> _spareFields = new();

    /// <summary>Building id per tile, 0 for empty.</summary>
    readonly int[] _occupancy;

    readonly List<Command> _pending = new();
    readonly List<SimEvent> _events = new();
    int _nextId = 1;
    bool _flowDirty = true;

    /// <summary>The horde changed (spawns, deaths) since the spatial hash was last built.</summary>
    bool _spatialStale;

    World(WorldOptions options)
    {
        Seed = options.Seed;
        Rng = new Rng(options.Seed);
        Rules = options.Rules ?? Rules.Default;
        Terrain = MapGen.Generate(options.Seed, options.MapSize);
        _occupancy = new int[Terrain.Width * Terrain.Height];
        Colony = new Colony(Terrain.Width * Terrain.Height, Rules.StartingResources);
        Flow = new FlowField(Terrain.Width, Terrain.Height);
        Noise = new NoiseGrid(Terrain.Width, Terrain.Height);
        Spatial = new SpatialHash(Terrain.Width, Terrain.Height);
        if (options.Survival) Survival = new Survival(Rules.Survival);
    }

    public static World Create(WorldOptions options)
    {
        var world = new World(options);
        var def = world.Rules[BuildingKind.Keep];
        int centre = options.MapSize / 2;
        var keep = world.AddBuilding(BuildingKind.Keep, centre - def.W / 2, centre - def.H / 2);
        keep.Complete = true;
        keep.Built = def.BuildSeconds;
        ColonySystem.RecomputeNetwork(world);
        world.EnsureFlow();
        world.ScatterPacks(options.DormantPacks);
        return world;
    }

    public void Enqueue(Command command) => _pending.Add(command);

    /// <summary>Advance one tick.</summary>
    public void Step()
    {
        if (Outcome != Outcome.Running) return;
        const float dt = 1f / Balance.TickHz;

        ApplyCommands();
        ColonySystem.Step(this, dt);
        EnsureFlow();
        RebuildHumanFieldsIfNeeded();
        Noise.Decay();
        WakePacks();
        SurvivalSystem.Step(this);

        Spatial.Build(Horde);
        _spatialStale = false;
        UnitSystem.MarkChase(this);
        HordeSystem.Move(this);
        UnitSystem.TakeHits(this);
        Combat.DemonsAttackBuildings(this);
        UnitSystem.Step(this, dt);
        Combat.TowersFire(this, dt);
        StepPossessed(dt);

        ResolveDeaths();
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

    internal void Emit(SimEvent e) => _events.Add(e);

    internal void Win()
    {
        if (Outcome != Outcome.Running) return;
        Outcome = Outcome.Won;
        _events.Add(new OutcomeChanged(Tick, Outcome));
    }

    /// <summary>Spawn a crowd at the middle of one map edge, snapped to ground that can reach the colony.</summary>
    internal int SpawnAtEdge(Side side, DemonKind kind, int count, int inset = 6)
    {
        if (count <= 0) return 0;
        int size = Terrain.Width, c = size / 2;
        var (x, y) = side switch
        {
            Side.North => (c, inset),
            Side.South => (c, size - 1 - inset),
            Side.West => (inset, c),
            _ => (size - 1 - inset, c),
        };
        var tile = FindReachableTileNear(x, y, maxRadius: 40);
        if (tile == null) return 0;
        int spawned = SpawnCluster(kind, tile.Value.X, tile.Value.Y, count);
        if (spawned > 0) _events.Add(new DemonsSpawned(Tick, kind, spawned));
        return spawned;
    }

    internal void MarkNetworkDirty() => NetworkDirty = true;

    internal void ClearNetworkDirty() => NetworkDirty = false;

    public int BuildingIdAt(int x, int y) => Terrain.InBounds(x, y) ? _occupancy[Terrain.Index(x, y)] : 0;

    public Building? BuildingById(int id) => _buildingById.GetValueOrDefault(id);

    public Unit? UnitById(int id)
    {
        foreach (var u in _units) if (u.Id == id) return u;
        return null;
    }

    /// <summary>Demons can stand here: walkable terrain with no building on it.</summary>
    public bool IsWalkable(int x, int y) =>
        Terrain.InBounds(x, y) && Terrain.IsWalkable(Terrain.Get(x, y)) && _occupancy[Terrain.Index(x, y)] == 0;

    /// <summary>Soldiers can stand here: like demons, but they pass through gates.</summary>
    public bool IsHumanWalkable(int x, int y)
    {
        if (!Terrain.InBounds(x, y) || !Terrain.IsWalkable(Terrain.Get(x, y))) return false;
        int id = _occupancy[Terrain.Index(x, y)];
        return id == 0 || _buildingById[id].Kind == BuildingKind.Gate;
    }

    internal bool IsWallAt(int x, int y)
    {
        int id = BuildingIdAt(x, y);
        return id != 0 && _buildingById[id].IsWallLike;
    }

    public bool FootprintConsecrated(int x, int y, int w, int h)
    {
        if (x < 0 || y < 0 || x + w > Terrain.Width || y + h > Terrain.Height) return false;
        for (int ty = y; ty < y + h; ty++)
            for (int tx = x; tx < x + w; tx++)
                if (!Colony.Consecrated[Terrain.Index(tx, ty)]) return false;
        return true;
    }

    /// <summary>Why a building can't go here, or null if it can. Lets the client preview placement.</summary>
    public string? CheckPlacement(BuildingKind kind, int x, int y)
    {
        if (kind == BuildingKind.Keep) return "only one Keep";
        var def = Rules[kind];
        if (x < 0 || y < 0 || x + def.W > Terrain.Width || y + def.H > Terrain.Height) return "out of bounds";
        for (int ty = y; ty < y + def.H; ty++)
        {
            for (int tx = x; tx < x + def.W; tx++)
            {
                if (!Terrain.IsBuildable(Terrain.Get(tx, ty))) return "terrain not buildable";
                if (_occupancy[Terrain.Index(tx, ty)] != 0) return "tile occupied";
            }
        }
        if (!FootprintConsecrated(x, y, def.W, def.H)) return "not on consecrated ground";
        if (DemonsInRect(x, y, def.W, def.H)) return "demons in the way";
        return Colony.Shortfall(def.Cost);
    }

    /// <summary>What a gatherer placed here would collect per second at full sanctity, for the placement preview.</summary>
    public double EstimateGathering(BuildingKind kind, int x, int y)
    {
        var def = Rules[kind];
        if (def.Produces == null) return 0;
        var claimed = new bool[Terrain.Width * Terrain.Height];
        foreach (var b in _buildings)
            if (b.Complete && b.Def.Produces == def.Produces)
                ColonySystem.CountGatherable(this, b.Def, b.CentreX, b.CentreY, claimed, claim: true);
        // Once built it stands on its own footprint, which then can't be gathered.
        for (int ty = y; ty < y + def.H; ty++)
            for (int tx = x; tx < x + def.W; tx++)
                if (Terrain.InBounds(tx, ty)) claimed[Terrain.Index(tx, ty)] = true;
        return ColonySystem.CountGatherable(this, def, x + def.W / 2f, y + def.H / 2f, claimed, claim: false) * def.PerTile;
    }

    /// <summary>
    /// The nearest tile to (x, y), searching outward ring by ring up to
    /// maxRadius, that a demon could stand on and path to the colony from.
    /// Hosts use it to aim scenario spawns.
    /// </summary>
    public (int X, int Y)? FindReachableTileNear(int x, int y, int maxRadius)
    {
        EnsureFlow();
        return FindTileNear(x, y, maxRadius, (tx, ty) => IsWalkable(tx, ty) && Flow.DistAt(tx, ty) != FlowField.Unreachable);
    }

    static (int X, int Y)? FindTileNear(int x, int y, int maxRadius, Func<int, int, bool> ok)
    {
        for (int r = 0; r <= maxRadius; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (ok(x + dx, y + dy)) return (x + dx, y + dy);
                }
        return null;
    }

    /// <summary>
    /// A demon's blow. An inhabited building isn't damaged but taken: the first
    /// hit possesses it. Everything else loses hit points.
    /// </summary>
    internal void DemonHitsBuilding(int id, float damage)
    {
        if (!_buildingById.TryGetValue(id, out var b)) return;
        int people = b.PeopleInside;
        if (people > 0)
        {
            b.Possessed = true;
            b.Occupants = people;
            b.PossessTimer = 0;
            b.Queue.Clear(); // soldiers not yet out of a possessed Barracks are lost with it
            OnLayoutChanged();
            MarkNetworkDirty();
            _events.Add(new BuildingPossessed(Tick, b.Id, b.Kind, people));
            return;
        }
        b.Hp -= damage;
    }

    /// <summary>Possessed buildings release their occupants as Thralls, one at a time, and fall when empty.</summary>
    void StepPossessed(float dt)
    {
        float every = Rules.PossessionSpawnSeconds;
        foreach (var b in _buildings)
        {
            if (!b.Possessed || b.Hp <= 0) continue;
            b.PossessTimer += dt;
            while (b.PossessTimer >= every && b.Occupants > 0)
            {
                b.PossessTimer -= every;
                b.Occupants--;
                var tile = FindTileNear((int)b.CentreX, (int)b.CentreY, 4, IsWalkable);
                if (tile != null) SpawnOne(DemonKind.Thrall, tile.Value.X + 0.5f, tile.Value.Y + 0.5f);
            }
            if (b.Occupants == 0) b.Hp = 0;
        }
    }

    void SpawnOne(DemonKind kind, float x, float y)
    {
        Horde.Add(kind, x, y, Rules[kind].Hp);
        _spatialStale = true;
    }

    /// <summary>Put a trained unit on the nearest open tile beside its Barracks. False if there's no room yet.</summary>
    internal bool TrySpawnUnit(UnitKind kind, Building barracks)
    {
        // A free tile beside the Barracks, not one another soldier is already standing on.
        var tile = FindTileNear((int)barracks.CentreX, barracks.Y + barracks.H, 6,
            (x, y) => IsHumanWalkable(x, y) && !_units.Any(u => (int)u.X == x && (int)u.Y == y));
        if (tile == null) return false;
        var def = Rules[kind];
        var u = new Unit
        {
            Id = _nextId++,
            Kind = kind,
            Def = def,
            X = tile.Value.X + 0.5f,
            Y = tile.Value.Y + 0.5f,
            Hp = def.Hp,
            Order = OrderKind.Idle,
        };
        u.PrevX = u.X;
        u.PrevY = u.Y;
        _units.Add(u);
        Emit(new UnitTrained(Tick, u.Id, kind, barracks.Id));
        return true;
    }

    void EnsureFlow()
    {
        if (!_flowDirty) return;
        Flow.Build(this);
        _flowDirty = false;
    }

    bool _humanFieldsDirty;

    void RebuildHumanFieldsIfNeeded()
    {
        // Release fields nobody is using any more.
        if (_humanFields.Count > 0)
        {
            var inUse = new HashSet<FlowField>();
            foreach (var u in _units) if (u.Field != null) inUse.Add(u.Field);
            foreach (var key in _humanFields.Keys.ToList())
            {
                if (inUse.Contains(_humanFields[key])) continue;
                _spareFields.Add(_humanFields[key]);
                _humanFields.Remove(key);
            }
        }
        if (!_humanFieldsDirty) return;
        _humanFieldsDirty = false;
        foreach (var (key, field) in _humanFields) field.BuildHuman(this, key % Terrain.Width, key / Terrain.Width);
    }

    FlowField HumanFieldTo(int x, int y)
    {
        int key = Terrain.Index(x, y);
        if (_humanFields.TryGetValue(key, out var field)) return field;
        if (_spareFields.Count > 0)
        {
            field = _spareFields[^1];
            _spareFields.RemoveAt(_spareFields.Count - 1);
        }
        else
        {
            field = new FlowField(Terrain.Width, Terrain.Height);
        }
        field.BuildHuman(this, x, y);
        _humanFields[key] = field;
        return field;
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
            SpawnDemons s => TrySpawn(s),
            MakeNoise m => TryNoise(m.X + 0.5f, m.Y + 0.5f, m.Radius, m.Intensity),
            TrainUnit t => TryTrain(t),
            OrderUnits o => TryOrder(o),
            _ => "unknown command",
        };
        if (reason != null) _events.Add(new CommandRejected(Tick, reason, command));
    }

    string? TryPlace(BuildingKind kind, int x, int y)
    {
        string? reason = CheckPlacement(kind, x, y);
        if (reason != null) return reason;

        var def = Rules[kind];
        Colony.Pay(def.Cost);
        var building = AddBuilding(kind, x, y);
        if (def.BuildSeconds <= 0)
        {
            building.Complete = true;
            MarkNetworkDirty();
        }
        TryNoise(x + def.W / 2f, y + def.H / 2f, Balance.BuildNoiseRadius, Balance.BuildNoiseIntensity);
        return null;
    }

    Building AddBuilding(BuildingKind kind, int x, int y)
    {
        var def = Rules[kind];
        var building = new Building { Id = _nextId++, Kind = kind, Def = def, X = x, Y = y, W = def.W, H = def.H, Hp = def.Hp };
        _buildings.Add(building);
        _buildingById[building.Id] = building;
        Stamp(building, building.Id);
        OnLayoutChanged();
        _events.Add(new BuildingPlaced(Tick, building.Id, kind, x, y));
        return building;
    }

    string? TryDemolish(int id)
    {
        if (!_buildingById.TryGetValue(id, out var building)) return "no such building";
        if (building.Kind == BuildingKind.Keep) return "the Keep cannot be demolished";
        // Purging a possessed building: whoever's still inside is lost with it, and there's nothing to salvage.
        // Otherwise a full refund for something not yet finished, a fraction once it's standing.
        if (!building.Possessed) Colony.Refund(building.Def.Cost, building.Complete ? Rules.RefundFraction : 1);
        RefundQueue(building);
        RemoveBuilding(building);
        _events.Add(new BuildingRemoved(Tick, building.Id, building.Kind));
        return null;
    }

    void RemoveBuilding(Building building)
    {
        _buildings.Remove(building);
        _buildingById.Remove(building.Id);
        Stamp(building, 0);
        OnLayoutChanged();
        MarkNetworkDirty();
    }

    void RefundQueue(Building building)
    {
        foreach (var kind in building.Queue) Colony.Refund(Rules[kind].Cost, 1);
        building.Queue.Clear();
    }

    void OnLayoutChanged()
    {
        _flowDirty = true;
        _humanFieldsDirty = true;
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

    string? TryTrain(TrainUnit t)
    {
        if (!_buildingById.TryGetValue(t.BarracksId, out var b)) return "no such building";
        if (Array.IndexOf(b.Def.Trains, t.Kind) < 0) return $"{b.Kind} can't train {t.Kind}";
        if (!b.Complete) return "still under construction";
        var cost = Rules[t.Kind].Cost;
        string? shortfall = Colony.Shortfall(cost);
        if (shortfall != null) return shortfall;
        Colony.Pay(cost);
        b.Queue.Add(t.Kind);
        return null;
    }

    string? TryOrder(OrderUnits o)
    {
        if (o.UnitIds.Length == 0) return "no units";
        FlowField? field = null;
        int dx = o.X, dy = o.Y;
        if (o.Order is OrderKind.Move or OrderKind.AttackMove)
        {
            // Aim at the nearest tile a soldier can actually stand on.
            var tile = FindTileNear(o.X, o.Y, 6, IsHumanWalkable);
            if (tile == null) return "no walkable ground there";
            (dx, dy) = tile.Value;
            field = HumanFieldTo(dx, dy);
        }
        int ordered = 0;
        foreach (var id in o.UnitIds)
        {
            var u = UnitById(id);
            if (u == null) continue;
            u.Order = o.Order;
            u.DestX = dx;
            u.DestY = dy;
            u.Field = field;
            ordered++;
        }
        return ordered == 0 ? "no such units" : null;
    }

    /// <summary>Remove everything that died this tick, report it, and check for defeat.</summary>
    void ResolveDeaths()
    {
        int killed = Horde.RemoveDead();
        if (killed > 0)
        {
            Stats.DemonsKilled += killed;
            _spatialStale = true;
            _events.Add(new DemonsKilled(Tick, killed));
        }

        for (int i = _units.Count - 1; i >= 0; i--)
        {
            var u = _units[i];
            if (u.Hp > 0) continue;
            _units.RemoveAt(i);
            Stats.UnitsLost++;
            // Killed by the horde, a soldier gets up again on the other side.
            (float X, float Y)? at = IsWalkable((int)u.X, (int)u.Y) ? (u.X, u.Y)
                : FindTileNear((int)u.X, (int)u.Y, 3, IsWalkable) is { } t ? (t.X + 0.5f, t.Y + 0.5f)
                : null;
            bool rose = at != null;
            if (at is { } p) SpawnOne(DemonKind.Thrall, p.X, p.Y);
            _events.Add(new UnitDied(Tick, u.Id, u.Kind, u.X, u.Y, rose));
        }

        // Iterate a snapshot: removing a Wardstone can darken buildings but never kills them.
        for (int i = _buildings.Count - 1; i >= 0; i--)
        {
            var b = _buildings[i];
            if (b.Hp > 0) continue;
            RefundQueue(b);
            RemoveBuilding(b);
            Stats.BuildingsLost++;
            _events.Add(new BuildingDestroyed(Tick, b.Id, b.Kind, b.X, b.Y));
            if (b.Kind == BuildingKind.Keep)
            {
                Outcome = Outcome.Lost;
                _events.Add(new OutcomeChanged(Tick, Outcome));
            }
        }
    }

    /// <summary>
    /// Scatter demons over a disc around a tile centre, sized so the crowd
    /// starts at about SpawnDensity per tile. Returns how many found ground.
    /// </summary>
    int SpawnCluster(DemonKind kind, int cx, int cy, int count)
    {
        float radius = MathF.Sqrt(count / (MathF.PI * Balance.SpawnDensity)) + 1;
        float hp = Rules[kind].Hp;
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
                Horde.Add(kind, x, y, hp);
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
