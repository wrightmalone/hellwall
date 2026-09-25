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
/// <param name="Difficulty">Scales Rules (which must be unscaled, Normal) by the level's multipliers.</param>
/// <param name="Endless">With Survival: no Convergence, no win; waves and corruptions until the Keep falls.</param>
/// <param name="Map">Which kind of terrain the seed grows.</param>
/// <param name="Scenario">A campaign mission: its locks and goals (Rules should already be its RulesFrom; ScenarioDef.Options does both).</param>
public readonly record struct WorldOptions(uint Seed, int MapSize = Balance.DefaultMapSize, int DormantPacks = 0, Rules? Rules = null, bool Survival = false, Difficulty Difficulty = Difficulty.Normal, bool Endless = false, MapKind Map = MapKind.Plains, ScenarioDef? Scenario = null);

public sealed class WorldStats
{
    public int DemonsKilled;
    public int BuildingsLost;
    public int UnitsLost;
    /// <summary>The run over time, sampled every half day: for the end panel's chart.</summary>
    public readonly List<HistorySample> History = new();
}

public readonly record struct HistorySample(int Tick, int Colonists, int Soldiers, int Horde);

/// <summary>
/// The whole simulation state and its fixed-rate step. Plain data plus the
/// systems that advance it; Godot and the headless runner are both just
/// consumers that enqueue commands and read state and events.
/// </summary>
public sealed partial class World
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

    public TechState Tech { get; } = new();

    /// <summary>A building's definition as it stands now: base rules plus everything researched.</summary>
    public BuildingDef Def(BuildingKind kind) => Tech.Buildings[(int)kind];

    /// <summary>A unit's definition as it stands now: base rules plus everything researched.</summary>
    public UnitDef Def(UnitKind kind) => Tech.Units[(int)kind];

    /// <summary>
    /// Demons as they are in this run: the rules' definitions with every
    /// corruption so far applied (endless mode). Derived from the rules and the
    /// corruptions taken, so not saved.
    /// </summary>
    public DemonDef[] Demons { get; internal set; } = [];

    public MapKind Map { get; }

    /// <summary>The campaign mission being played, if any.</summary>
    public ScenarioDef? Scenario { get; }
    public ObjectiveDef[] Goals { get; }
    public bool[] GoalsDone { get; }
    public bool[] TriggersFired { get; }

    public DemonDef Def(DemonKind kind) => Demons[(int)kind];

    /// <summary>1-based; counts on even without a survival schedule.</summary>
    public int Day => Tick / (int)(Rules.Survival.DaySeconds * Balance.TickHz) + 1;

    /// <summary>Ordered by id, ascending. Hashing and iteration rely on that.</summary>
    public IReadOnlyList<Building> Buildings => _buildings;

    /// <summary>Ordered by id, ascending.</summary>
    public IReadOnlyList<Unit> Units => _units;

    public Horde Horde { get; } = new();
    public FlowField Flow { get; }
    public NoiseGrid Noise { get; }
    public Vision Vision { get; }

    /// <summary>Ordered by id, ascending.</summary>
    public IReadOnlyList<Pack> Packs => _packs;
    internal List<Pack> PackList => _packs;

    /// <summary>Every Hellgate the map started with, standing or not. Ordered by id.</summary>
    public IReadOnlyList<Hellgate> Gates => _gates;
    internal List<Hellgate> GateList => _gates;

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
    readonly List<Hellgate> _gates = new();
    readonly List<Ruin> _ruins = new();
    /// <summary>The blessings on offer now (empty: none), and how many milestones have been answered.</summary>
    public string[] PatronOffer { get; internal set; } = [];
    public int PatronsTaken { get; internal set; }
    public IReadOnlyList<Ruin> Ruins => _ruins;
    internal List<Ruin> RuinList => _ruins;

    internal Pack AddPack(int x, int y, int count, DemonKind kind)
    {
        var pack = new Pack { Id = _nextId++, X = x, Y = y, Count = Math.Max(1, count), Kind = kind };
        _packs.Add(pack);
        return pack;
    }

    /// <summary>Per tile: a standing Hellgate is here. Blocks walkers like rock.</summary>
    readonly bool[] _gateTile;

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

    /// <summary>Per tile, a tree's hit points (0 where there's no tree), and which woodsman has claimed it.</summary>
    internal readonly float[] TreeHp;
    internal readonly int[] TreeClaim;
    readonly List<Woodsman> _woodsmen = new();
    public IReadOnlyList<Woodsman> Woodsmen => _woodsmen;
    internal List<Woodsman> WoodsmanList => _woodsmen;

    /// <summary>Forest is a wall: no one walks through it, the horde hacks through it.</summary>
    public bool ForestBlocks => Rules.Woods.Blocks;

    bool Blocked(Tile tile) => !Terrain.IsWalkable(tile) || (tile == Tile.Forest && ForestBlocks);

    /// <summary>A standing tree the horde can hack through (only when the woods block).</summary>
    public bool IsTree(int x, int y) => ForestBlocks && Terrain.InBounds(x, y) && Terrain.Get(x, y) == Tile.Forest;

    internal int NextId() => _nextId++;

    /// <summary>A tree comes down: the tile becomes open ground, and routes and gathering are recomputed.</summary>
    public int TreesFelled { get; internal set; }

    /// <summary>Tests and tools: turn a rectangle (inclusive) into standing forest at full health, leaving buildings alone.</summary>
    internal void PlantForest(int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!Terrain.InBounds(x, y) || BuildingIdAt(x, y) != 0) continue;
                Terrain.Set(x, y, Tile.Forest);
                TreeHp[Terrain.Index(x, y)] = Rules.Woods.TreeHp;
            }
        OnLayoutChanged();
        MarkNetworkDirty();
    }

    internal void Fell(int tile)
    {
        if (Terrain.Tiles[tile] != Tile.Forest) return;
        TreesFelled++;
        Terrain.Tiles[tile] = Tile.Grass;
        TreeHp[tile] = 0;
        TreeClaim[tile] = 0;
        OnLayoutChanged();
        MarkNetworkDirty();
        _events.Add(new TreeFelled(Tick, tile % Terrain.Width, tile / Terrain.Width));
    }

    /// <summary>A tile beside a building a person can stand on: where its woodsmen come and go.</summary>
    internal (int X, int Y)? DoorOf(Building b) => FindTileNear((int)b.CentreX, b.Y + b.H, 4, IsHumanWalkable);

    /// <summary>Per tile, the speed factor Belfries impose (1 where none reach). Derived, rebuilt every tick.</summary>
    internal readonly float[] Slow;
    internal readonly List<int> SlowedTiles = new();

    public float SlowAt(float x, float y) => Slow[Math.Clamp((int)y, 0, Terrain.Height - 1) * Terrain.Width + Math.Clamp((int)x, 0, Terrain.Width - 1)];

    World(WorldOptions options)
    {
        Seed = options.Seed;
        Rng = new Rng(options.Seed);
        Rules = (options.Rules ?? Rules.Default).ForDifficulty(options.Difficulty);
        Terrain = options.Scenario?.DecodeTiles() is { } painted ? Terrain.From(options.MapSize, painted) : MapGen.Generate(options.Seed, options.MapSize, options.Map);
        Map = options.Map;
        _occupancy = new int[Terrain.Width * Terrain.Height];
        _gateTile = new bool[Terrain.Width * Terrain.Height];
        Slow = new float[Terrain.Width * Terrain.Height];
        Array.Fill(Slow, 1f);
        TreeHp = new float[Terrain.Width * Terrain.Height];
        TreeClaim = new int[Terrain.Width * Terrain.Height];
        for (int i = 0; i < TreeHp.Length; i++)
            if (Terrain.Tiles[i] == Tile.Forest) TreeHp[i] = Rules.Woods.TreeHp;
        Colony = new Colony(Terrain.Width * Terrain.Height, Rules.StartingResources);
        Flow = new FlowField(Terrain.Width, Terrain.Height);
        Noise = new NoiseGrid(Terrain.Width, Terrain.Height);
        Vision = new Vision(Terrain.Width, Terrain.Height, Rules.Fog.Enabled && (options.Survival || options.Scenario != null));
        Vision.Reveal(Terrain.Width / 2f, Terrain.Height / 2f, Rules.Fog.StartReveal);
        Spatial = new SpatialHash(Terrain.Width, Terrain.Height);
        if (options.Survival) Survival = new Survival(Rules.Survival, options.Endless);
        Scenario = options.Scenario;
        // What winning takes: the mission's goals, or for a plain survival run, surviving.
        Goals = Scenario?.Goals ?? (options.Survival ? [new ObjectiveDef { Kind = ObjectiveKind.Survive }] : []);
        GoalsDone = new bool[Goals.Length];
        TriggersFired = new bool[Scenario?.Triggers.Length ?? 0];
        Tech.Recompute(Rules);
        Demons = Rules.Demons;
    }

    public static World Create(WorldOptions options)
    {
        var world = new World(options);
        var def = world.Def(BuildingKind.Keep);
        int centre = options.MapSize / 2;
        var keep = world.AddBuilding(BuildingKind.Keep, centre - def.W / 2, centre - def.H / 2);
        keep.Complete = true;
        keep.Built = def.BuildSeconds;
        ColonySystem.RecomputeNetwork(world);
        world.EnsureFlow();
        if (options.Survival)
        {
            // The survival scenario: Hellgates out on the map and a garrison at home.
            world.PlaceGates();
            foreach (var start in world.Rules.StartingUnits)
                for (int i = 0; i < start.Count; i++) world.TrySpawnUnit(start.Kind, keep);
        }
        world.ScatterPacks(options.DormantPacks > 0 ? options.DormantPacks : options.Survival ? world.Rules.Wilds.Packs : 0);
        if (options.Survival && options.DormantPacks == 0) world.ScatterStrays(world.Rules.Wilds.Strays);
        if (options.Survival && options.DormantPacks == 0) RuinSystem.Place(world);
        if (options.Scenario is { } s)
            foreach (var p in s.PlacedPacks)
                if (world.Terrain.InBounds(p.X, p.Y) && p.Count > 0)
                    world._packs.Add(new Pack { Id = world._nextId++, X = p.X, Y = p.Y, Count = p.Count, Kind = p.Kind });
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
        HellgateSystem.Step(this, dt);
        SurvivalSystem.Step(this);
        CorruptionSystem.Step(this);
        ObjectiveSystem.Step(this);

        Spatial.Build(Horde);
        _spatialStale = false;
        UnitSystem.MarkChase(this);
        Abilities.StampSlow(this);
        HordeSystem.Move(this);
        Abilities.Howl(this);
        BurnOnHolyGround(dt);
        UnitSystem.TakeHits(this);
        Combat.DemonsAttackBuildings(this);
        UnitSystem.Step(this, dt);
        WoodsSystem.Step(this, dt);
        Vision.Step(this);
        RepairSystem.Step(this, dt);
        RuinSystem.Step(this);
        StepUpgrades(dt);
        PatronSystem.Step(this);
        if (Survival != null && Tick % (int)(Rules.Survival.DaySeconds * Balance.TickHz / 2) == 0)
            Stats.History.Add(new HistorySample(Tick, Colony.Colonists, _units.Count, Horde.Count));
        Abilities.Heal(this, dt);
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

    /// <summary>A mission ran out of time: the Convergence broke on the walls with its goals still unmet.</summary>
    internal void Lose()
    {
        if (Outcome != Outcome.Running) return;
        Outcome = Outcome.Lost;
        _events.Add(new OutcomeChanged(Tick, Outcome));
    }

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
        Terrain.InBounds(x, y) && !Blocked(Terrain.Get(x, y)) && _occupancy[Terrain.Index(x, y)] == 0 && !_gateTile[Terrain.Index(x, y)];

    /// <summary>Soldiers can stand here: like demons, but they pass through gates.</summary>
    public bool IsHumanWalkable(int x, int y)
    {
        if (!Terrain.InBounds(x, y) || Blocked(Terrain.Get(x, y)) || _gateTile[Terrain.Index(x, y)]) return false;
        int id = _occupancy[Terrain.Index(x, y)];
        return id == 0 || _buildingById[id].IsGate;
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
        if (Def(kind).UpgradeOnly) return "upgrade a building to get one";
        if (Scenario?.Locks(kind) == true) return "not in this mission";
        var def = Def(kind);
        if (def.RequiresTech is { } needs && !Tech.Has(needs)) return $"needs {Rules.Tech(needs).Name}";
        if (x < 0 || y < 0 || x + def.W > Terrain.Width || y + def.H > Terrain.Height) return "out of bounds";
        if (!Vision.IsExplored(x, y) || !Vision.IsExplored(x + def.W - 1, y + def.H - 1)) return "unexplored ground";
        for (int ty = y; ty < y + def.H; ty++)
        {
            for (int tx = x; tx < x + def.W; tx++)
            {
                if (!Terrain.IsBuildable(Terrain.Get(tx, ty))) return "terrain not buildable";
                if (_occupancy[Terrain.Index(tx, ty)] != 0) return "tile occupied";
            }
        }
        if (!FootprintConsecrated(x, y, def.W, def.H)) return "not on consecrated ground";
        if (PackNear(x, y, def.W, def.H) != null) return "demons sleep nearby: clear them first";
        if (DemonsInRect(x, y, def.W, def.H)) return "demons in the way";
        return Colony.Shortfall(def.Cost);
    }

    /// <summary>What a gatherer placed here would collect per second at full sanctity, for the placement preview.</summary>
    public double EstimateGathering(BuildingKind kind, int x, int y)
    {
        var def = Def(kind);
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

    /// <summary>With Holy Fire researched, consecrated ground burns every demon standing (or hovering) on it.</summary>
    void BurnOnHolyGround(float dt)
    {
        float dps = Tech.HolyGroundDps;
        if (dps <= 0) return;
        var holy = Colony.Consecrated;
        int width = Terrain.Width;
        for (int i = 0; i < Horde.Count; i++)
            if (holy[(int)Horde.Y[i] * width + (int)Horde.X[i]]) Horde.Hp[i] -= dps * dt;
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
        Horde.Add(kind, x, y, Def(kind).Hp);
        _spatialStale = true;
    }

    /// <summary>Put a trained unit on the nearest open tile beside its Barracks. False if there's no room yet.</summary>
    internal bool TrySpawnUnit(UnitKind kind, Building barracks)
    {
        // A free tile beside the Barracks, not one another soldier is already standing on.
        var tile = FindTileNear((int)barracks.CentreX, barracks.Y + barracks.H, 6,
            (x, y) => IsHumanWalkable(x, y) && !_units.Any(u => (int)u.X == x && (int)u.Y == y));
        if (tile == null) return false;
        var def = Def(kind);
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
        // Off to the rally point, fighting anything on the way.
        if (barracks.RallyX >= 0 && TryOrder(new OrderUnits([u.Id], OrderKind.AttackMove, barracks.RallyX, barracks.RallyY)) is { } why)
            _events.Add(new CommandRejected(Tick, why, new SetRally(barracks.Id, barracks.RallyX, barracks.RallyY)));
        return true;
    }

    void EnsureFlow()
    {
        if (!_flowDirty) return;
        Flow.Build(this);
        _flowDirty = false;
    }

    bool _humanFieldsDirty;
    readonly HashSet<FlowField> _fieldsInUse = new();
    readonly List<int> _fieldKeys = new();

    void RebuildHumanFieldsIfNeeded()
    {
        // Release fields nobody is using any more.
        if (_humanFields.Count > 0)
        {
            // Reused sets, not new ones: with a patrol out, fields are live all game, and this runs every tick.
            _fieldsInUse.Clear();
            foreach (var u in _units)
            {
                if (u.Field != null) _fieldsInUse.Add(u.Field);
                // A patrol's other end: it'll be wanted again at the turn, so keep it rather than rebuild the whole map then.
                if (u.Order == OrderKind.Patrol && _humanFields.TryGetValue(Terrain.Index(u.PatrolX, u.PatrolY), out var back)) _fieldsInUse.Add(back);
            }
            _fieldKeys.Clear();
            foreach (var (key, field) in _humanFields) if (!_fieldsInUse.Contains(field)) _fieldKeys.Add(key);
            foreach (var key in _fieldKeys)
            {
                _spareFields.Add(_humanFields[key]);
                _humanFields.Remove(key);
            }
        }
        if (!_humanFieldsDirty) return;
        _humanFieldsDirty = false;
        foreach (var (key, field) in _humanFields) field.BuildHuman(this, key % Terrain.Width, key / Terrain.Width);
    }

    internal FlowField HumanFieldTo(int x, int y)
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
            CancelTraining c => TryCancel(c),
            SetRally r => TrySetRally(r),
            Research r => TryResearch(r),
            UpgradeBuilding u => TryUpgrade(u.BuildingId),
            ChoosePatron p => PatronSystem.Choose(this, p.TechId),
            OrderUnits o => TryOrder(o),
            _ => "unknown command",
        };
        if (reason != null) _events.Add(new CommandRejected(Tick, reason, command));
    }

    string? TryPlace(BuildingKind kind, int x, int y)
    {
        string? reason = CheckPlacement(kind, x, y);
        if (reason != null) return reason;

        var def = Def(kind);
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
        var def = Def(kind);
        var building = new Building { Id = _nextId++, Kind = kind, Def = def, X = x, Y = y, W = def.W, H = def.H, Hp = def.Hp };
        _buildings.Add(building);
        _buildingById[building.Id] = building;
        Stamp(building, building.Id);
        OnLayoutChanged();
        _events.Add(new BuildingPlaced(Tick, building.Id, kind, x, y));
        return building;
    }

    string? TryUpgrade(int id)
    {
        if (!_buildingById.TryGetValue(id, out var b)) return "no such building";
        if (b.Def.UpgradesTo is not { } to) return $"{b.Kind} has no upgrade";
        if (!b.Complete || b.Possessed) return "not while it's being built or possessed";
        if (b.Upgrading) return "already upgrading";
        if (Scenario?.Locks(to) == true) return "not in this mission";
        var def = Def(to);
        if (def.RequiresTech is { } needs && !Tech.Has(needs)) return $"needs {Rules.Tech(needs).Name}";
        if (Colony.Shortfall(def.Cost) is { } shortfall) return shortfall;
        Colony.Pay(def.Cost);
        b.Upgrading = true;
        b.UpgradeProgress = 0;
        return null;
    }

    /// <summary>Upgrades under way: the building works as it was until the new one is finished.</summary>
    void StepUpgrades(float dt)
    {
        foreach (var b in _buildings)
        {
            if (!b.Upgrading) continue;
            if (b.Possessed) { CancelUpgrade(b); continue; }
            var to = b.Def.UpgradesTo!.Value;
            var def = Def(to);
            b.UpgradeProgress += dt;
            if (b.UpgradeProgress < def.BuildSeconds) continue;
            var from = b.Kind;
            float share = b.Hp / b.Def.Hp;
            b.Kind = to;
            b.Def = def;
            b.Hp = def.Hp * share;
            b.WatchedHp = b.Hp;
            b.Upgrading = false;
            b.UpgradeProgress = 0;
            MarkNetworkDirty();
            _events.Add(new BuildingUpgraded(Tick, b.Id, from, to));
        }
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

    /// <summary>An upgrade that won't finish (its building demolished, lost or possessed): what was paid for it comes back.</summary>
    void CancelUpgrade(Building b)
    {
        if (!b.Upgrading) return;
        Colony.Refund(Def(b.Def.UpgradesTo!.Value).Cost, 1);
        b.Upgrading = false;
        b.UpgradeProgress = 0;
    }

    void RefundQueue(Building building)
    {
        CancelUpgrade(building);
        foreach (var kind in building.Queue) Colony.Refund(Def(kind).Cost, 1);
        building.Queue.Clear();
        if (building.Researching is { } id)
        {
            Colony.Refund(Rules.Tech(id).Cost, 1);
            building.Researching = null;
            building.ResearchProgress = 0;
        }
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
        if (Def(t.Kind).RequiresTech is { } needs && !Tech.Has(needs)) return $"needs {Rules.Tech(needs).Name}";
        if (Scenario?.Locks(t.Kind) == true) return "not in this mission";
        if (b.Queue.Count >= Balance.QueueLimit) return "the queue is full";
        var cost = Def(t.Kind).Cost;
        string? shortfall = Colony.Shortfall(cost);
        if (shortfall != null) return shortfall;
        Colony.Pay(cost);
        b.Queue.Add(t.Kind);
        return null;
    }

    /// <summary>Why a tech can't be started now, or null if it can (at some Scriptorium, cost aside).</summary>
    public string? CheckResearch(string id)
    {
        var tech = Rules.Techs.FirstOrDefault(t => t.Id == id);
        if (tech == null) return "no such tech";
        if (tech.Patron) return "a patron's blessing is chosen, not researched";
        if (tech.KeepLevel > 0) return "raised at the Keep";
        if (Tech.Has(id)) return "already researched";
        if (Scenario?.Locks(id) == true) return "not in this mission";
        if (_buildings.Any(b => b.Researching == id)) return "already being researched";
        if (tech.ExclusiveWith is { } other && (Tech.Has(other) || _buildings.Any(b => b.Researching == other)))
            return $"locked out by {Rules.Tech(other).Name}";
        foreach (var need in tech.Requires)
            if (!Tech.Has(need)) return $"needs {Rules.Tech(need).Name}";
        return null;
    }

    /// <summary>The Keep's next level, if there is one to raise: the lowest KeepLevel tech not yet had.</summary>
    public TechDef? NextKeepLevel() => Rules.Techs.Where(t => t.KeepLevel > 0 && !Tech.Has(t.Id)).OrderBy(t => t.KeepLevel).FirstOrDefault();

    string? TryRaiseKeep(Building keep, string id)
    {
        if (keep.Researching != null) return "the Keep is already being raised";
        if (NextKeepLevel() is not { } next || next.Id != id) return "that isn't the Keep's next level";
        if (Scenario?.Locks(id) == true) return "not in this mission";
        if (Colony.Shortfall(next.Cost) is { } shortfall) return shortfall;
        Colony.Pay(next.Cost);
        keep.Researching = id;
        keep.ResearchProgress = 0;
        return null;
    }

    string? TryResearch(Research r)
    {
        if (!_buildingById.TryGetValue(r.BuildingId, out var b)) return "no such building";
        if (b.Kind == BuildingKind.Keep) return TryRaiseKeep(b, r.TechId);
        if (!b.Def.Researches) return $"{b.Kind} can't research";
        if (!b.Complete) return "still under construction";
        if (b.Researching != null) return "already researching";
        string? why = CheckResearch(r.TechId);
        if (why != null) return why;
        var cost = Rules.Tech(r.TechId).Cost;
        string? shortfall = Colony.Shortfall(cost);
        if (shortfall != null) return shortfall;
        Colony.Pay(cost);
        b.Researching = r.TechId;
        b.ResearchProgress = 0;
        return null;
    }

    /// <summary>
    /// A tech finishes: recompute every effective definition and hand the new
    /// ones to what's already built and trained, keeping each one's hit
    /// points in proportion.
    /// </summary>
    internal void CompleteResearch(Building at)
    {
        string id = at.Researching!;
        at.Researching = null;
        at.ResearchProgress = 0;
        Grant(id);
    }

    /// <summary>A tech takes effect: from research, or a patron's blessing.</summary>
    internal void Grant(string id)
    {
        Tech.Researched.Add(id);
        Tech.Recompute(Rules);
        foreach (var b in _buildings)
        {
            var def = Def(b.Kind);
            if (def.Hp != b.Def.Hp) b.Hp *= def.Hp / b.Def.Hp;
            b.Def = def;
        }
        foreach (var u in _units)
        {
            var def = Def(u.Kind);
            if (def.Hp != u.Def.Hp) u.Hp *= def.Hp / u.Def.Hp;
            u.Def = def;
        }
        MarkNetworkDirty(); // radii and gather rates may have changed
        _events.Add(new TechResearched(Tick, id));
    }

    string? TryCancel(CancelTraining c)
    {
        if (!_buildingById.TryGetValue(c.BarracksId, out var b)) return "no such building";
        if (c.Index < 0 || c.Index >= b.Queue.Count) return "nothing queued there";
        Colony.Refund(Def(b.Queue[c.Index]).Cost, 1);
        b.Queue.RemoveAt(c.Index);
        if (c.Index == 0) b.TrainProgress = 0;
        return null;
    }

    string? TrySetRally(SetRally r)
    {
        if (!_buildingById.TryGetValue(r.BuildingId, out var b)) return "no such building";
        if (b.Def.Trains.Length == 0) return $"{b.Kind} trains no one";
        if (r.X < 0)
        {
            b.RallyX = -1;
            return null;
        }
        if (!Terrain.InBounds(r.X, r.Y)) return "out of bounds";
        b.RallyX = r.X;
        b.RallyY = r.Y;
        return null;
    }

    string? TryOrder(OrderUnits o)
    {
        if (o.UnitIds.Length == 0) return "no units";
        FlowField? field = null;
        int dx = o.X, dy = o.Y;
        if (o.Order is OrderKind.Move or OrderKind.AttackMove or OrderKind.Patrol)
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
            u.PatrolX = (int)u.X;
            u.PatrolY = (int)u.Y;
            ordered++;
        }
        return ordered == 0 ? "no such units" : null;
    }

    /// <summary>
    /// The nearest point on the nearest building a flier would go for
    /// (anything but walls, gates and what the horde already holds).
    /// Recomputed per flier per tick rather than cached: fliers are few, and
    /// it keeps them free of state.
    /// </summary>
    internal (float X, float Y, bool Found) NearestFlierTarget(float x, float y)
    {
        float best = float.MaxValue, bx = 0, by = 0;
        foreach (var b in _buildings)
        {
            if (!b.IsDemonTarget) continue;
            float px = Math.Clamp(x, b.X, b.X + b.W), py = Math.Clamp(y, b.Y, b.Y + b.H);
            float dx = px - x, dy = py - y;
            float d2 = dx * dx + dy * dy;
            if (d2 < best) { best = d2; bx = px; by = py; }
        }
        return (bx, by, best < float.MaxValue);
    }

    /// <summary>A building whose footprint lies within reach of a point, checking the tile under it and its neighbours.</summary>
    /// <summary>Whether any building stands within about `radius` tiles: a coarse scan of every other tile, for rare checks.</summary>
    internal bool BuildingInSight(float x, float y, int radius)
    {
        int cx = (int)x, cy = (int)y, r2 = radius * radius;
        for (int oy = -radius; oy <= radius; oy += 2)
            for (int ox = -radius; ox <= radius; ox += 2)
                if (ox * ox + oy * oy <= r2 && BuildingIdAt(cx + ox, cy + oy) != 0) return true;
        return false;
    }

    internal int BuildingWithinReach(float x, float y, float reach, bool skipWalls)
    {
        int cx = (int)x, cy = (int)y;
        for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int id = BuildingIdAt(cx + ox, cy + oy);
                if (id == 0) continue;
                var b = _buildingById[id];
                if (skipWalls && !b.IsDemonTarget) continue;
                float ex = MathF.Max(MathF.Max(b.X - x, 0), x - (b.X + b.W));
                float ey = MathF.Max(MathF.Max(b.Y - y, 0), y - (b.Y + b.H));
                if (ex * ex + ey * ey <= reach * reach) return id;
            }
        return 0;
    }

    /// <summary>Remove everything that died this tick, report it, and check for defeat.</summary>
    void ResolveDeaths()
    {
        // Bloaters burst as they die, however they died, before anything else is counted.
        // Broodmothers' broods crawl out after the dead are cleared, so they don't count as killed.
        List<(DemonKind Kind, float X, float Y, int Count)>? broods = null;
        for (int i = 0; i < Horde.Count; i++)
        {
            if (Horde.Hp[i] > 0) continue;
            var def = Def(Horde.Kind[i]);
            if (def.ExplodeDamage > 0) Combat.Explode(this, Horde.X[i], Horde.Y[i], def);
            if (def.BroodCount > 0) (broods ??= new()).Add((def.BroodKind, Horde.X[i], Horde.Y[i], def.BroodCount));
        }

        int killed = Horde.RemoveDead();
        if (broods != null)
            foreach (var (kind, x, y, count) in broods)
            {
                int n = SpawnCluster(kind, (int)x, (int)y, count);
                if (n > 0) _events.Add(new DemonsSpawned(Tick, kind, n));
            }
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
    /// <summary>A pack wakes where it slept: each demon at its spot (or a nearby walkable one, if a building has gone up there).</summary>
    int SpawnPack(Pack pack)
    {
        float density = Rules.Wilds.SleepDensity, hp = Def(pack.Kind).Hp;
        int spawned = 0;
        for (int i = 0; i < pack.Count; i++)
            for (int k = 0; k < 4; k++)
            {
                var (x, y, _) = pack.Spot(i + k * pack.Count, density);
                if (x < 0 || y < 0 || !IsWalkable((int)x, (int)y)) continue;
                Horde.Add(pack.Kind, x, y, hp);
                spawned++;
                break;
            }
        if (spawned > 0) _spatialStale = true;
        return spawned;
    }

    int SpawnCluster(DemonKind kind, int cx, int cy, int count)
    {
        float radius = MathF.Sqrt(count / (MathF.PI * Balance.SpawnDensity)) + 1;
        float hp = Def(kind).Hp;
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

    /// <summary>
    /// Put the map's Hellgates on open ground far from the Keep, spread apart,
    /// each able to reach the colony.
    /// </summary>
    void PlaceGates()
    {
        var rules = Rules.Hellgates;
        if (Scenario is { PlacedGates.Length: > 0 } s)
        {
            foreach (var p in s.PlacedGates)
            {
                if (p.X < 0 || p.Y < 0 || p.X + Hellgate.Size > Terrain.Width || p.Y + Hellgate.Size > Terrain.Height) continue;
                var placed = new Hellgate { Id = _nextId++, X = p.X, Y = p.Y, Hp = rules.Hp, SpawnTimer = Rng.NextInt((int)rules.SpawnSeconds) };
                _gates.Add(placed);
                StampGate(placed, true);
            }
            _flowDirty = true;
            return;
        }
        int centre = Terrain.Width / 2;
        int min2 = rules.MinDistance * rules.MinDistance;
        for (int n = 0, attempts = 0; n < rules.Count && attempts < rules.Count * 400; attempts++)
        {
            int x = Rng.NextInt(Terrain.Width - Hellgate.Size), y = Rng.NextInt(Terrain.Height - Hellgate.Size);
            int dx = x - centre, dy = y - centre;
            if (dx * dx + dy * dy < min2) continue;
            if (_gates.Any(g => Math.Abs(g.X - x) + Math.Abs(g.Y - y) < 40)) continue;
            bool clear = true;
            for (int ty = y; ty < y + Hellgate.Size && clear; ty++)
                for (int tx = x; tx < x + Hellgate.Size && clear; tx++)
                    clear = Terrain.IsWalkable(Terrain.Get(tx, ty)) && Flow.DistAt(tx, ty) != FlowField.Unreachable;
            if (!clear) continue;
            var gate = new Hellgate { Id = _nextId++, X = x, Y = y, Hp = rules.Hp, SpawnTimer = Rng.NextInt((int)rules.SpawnSeconds) };
            _gates.Add(gate);
            StampGate(gate, true);
            n++;
        }
        if (_gates.Count > 0) _flowDirty = true;
    }

    void StampGate(Hellgate g, bool on)
    {
        for (int ty = g.Y; ty < g.Y + Hellgate.Size; ty++)
            for (int tx = g.X; tx < g.X + Hellgate.Size; tx++)
                _gateTile[Terrain.Index(tx, ty)] = on;
    }

    /// <summary>A Hellgate's band: a small mixed group that heads straight for the colony.</summary>
    internal void SpawnBand(int x, int y, int count)
    {
        int hounds = count / 4;
        int spawned = SpawnCluster(DemonKind.Imp, x, y, count - hounds) + SpawnCluster(DemonKind.Hound, x, y, hounds);
        if (spawned > 0) _events.Add(new DemonsSpawned(Tick, DemonKind.Imp, spawned));
    }

    /// <summary>A standing gate whose footprint lies within range of a point, or null.</summary>
    internal Hellgate? GateNear(float x, float y, float range)
    {
        foreach (var g in _gates)
        {
            if (!g.Alive) continue;
            float ex = MathF.Max(MathF.Max(g.X - x, 0), x - (g.X + Hellgate.Size));
            float ey = MathF.Max(MathF.Max(g.Y - y, 0), y - (g.Y + Hellgate.Size));
            if (ex * ex + ey * ey <= range * range) return g;
        }
        return null;
    }

    /// <summary>A soldier's shot at a gate.</summary>
    internal void DamageGate(Hellgate gate, float damage)
    {
        if (!gate.Alive) return;
        gate.Hp -= damage;
        if (gate.Alive) return;
        StampGate(gate, false);
        OnLayoutChanged();
        _events.Add(new HellgateClosed(Tick, gate.Id, gate.X, gate.Y));
    }

    /// <summary>
    /// Spread sleeping packs over the map: none inside MinDistance, none
    /// closer together than Spacing, each on ground that can reach the
    /// colony, and sized by distance from the Keep: small near home, large far out.
    /// </summary>
    /// <summary>Stragglers: a few demons at a time, all over the map past MinDistance, a little apart from everything else asleep.</summary>
    void ScatterStrays(int count)
    {
        var wilds = Rules.Wilds;
        int centre = Terrain.Width / 2;
        int minD2 = wilds.StrayMinDistance * wilds.StrayMinDistance;
        for (int n = 0, attempts = 0; n < count && attempts < count * 50; attempts++)
        {
            int x = Rng.NextInt(Terrain.Width);
            int y = Rng.NextInt(Terrain.Height);
            int dx = x - centre, dy = y - centre;
            if (dx * dx + dy * dy < minD2) continue;
            if (!IsWalkable(x, y) || Flow.DistAt(x, y) == FlowField.Unreachable) continue;
            if (_packs.Any(p => (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) < 25)) continue;
            if (GuardsHomeIron(x, y)) continue;
            var kind = Rng.Chance(wilds.HoundChance) ? DemonKind.Hound : DemonKind.Imp;
            _packs.Add(new Pack { Id = _nextId++, X = x, Y = y, Count = 1 + Rng.NextInt(wilds.StrayMax), Kind = kind, Stray = true });
            n++;
        }
    }

    void ScatterPacks(int count)
    {
        var wilds = Rules.Wilds;
        int centre = Terrain.Width / 2;
        int minD2 = wilds.MinDistance * wilds.MinDistance;
        int spacing2 = wilds.Spacing * wilds.Spacing;
        float farthest = MathF.Sqrt(2) * centre;
        for (int n = 0, attempts = 0; n < count && attempts < count * 300; attempts++)
        {
            int x = Rng.NextInt(Terrain.Width);
            int y = Rng.NextInt(Terrain.Height);
            int dx = x - centre, dy = y - centre;
            if (dx * dx + dy * dy < minD2) continue;
            if (!IsWalkable(x, y) || Flow.DistAt(x, y) == FlowField.Unreachable) continue;
            if (_packs.Any(p => (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) < spacing2)) continue;
            if (GuardsHomeIron(x, y)) continue;
            var kind = Rng.Chance(wilds.HoundChance) ? DemonKind.Hound : DemonKind.Imp;
            float far = Math.Clamp((MathF.Sqrt(dx * dx + dy * dy) - wilds.MinDistance) / (farthest - wilds.MinDistance), 0, 1);
            int size = (int)(wilds.NearCount + (wilds.FarCount - wilds.NearCount) * far * (0.7 + 0.6 * Rng.NextDouble()));
            _packs.Add(new Pack { Id = _nextId++, X = x, Y = y, Count = Math.Max(1, size), Kind = kind });
            n++;
        }
    }

    /// <summary>
    /// The iron and stone nearest home are never guarded: no pack sits within
    /// clearing range of ore or rock that lies within 32 tiles of the Keep.
    /// Iron needs expansion, expansion needs clearing, clearing needs an army
    /// and an army needs iron; the first deposits are what break that loop.
    /// </summary>
    bool GuardsHomeIron(int x, int y)
    {
        int c = Terrain.Width / 2;
        int r = (int)Rules.Wilds.ClearRadius + 2;
        for (int ty = Math.Max(0, y - r); ty <= Math.Min(Terrain.Height - 1, y + r); ty++)
            for (int tx = Math.Max(0, x - r); tx <= Math.Min(Terrain.Width - 1, x + r); tx++)
            {
                if (Terrain.Get(tx, ty) is not (Tile.Ore or Tile.Rock)) continue;
                if ((tx - x) * (tx - x) + (ty - y) * (ty - y) > r * r) continue;
                if ((tx - c) * (tx - c) + (ty - c) * (ty - c) <= 32 * 32) return true;
            }
        return false;
    }

    void WakePacks()
    {
        bool checkUnits = Tick % 10 == 0 && _units.Count > 0; // soldiers walk slowly; twice a second is plenty
        foreach (var pack in _packs)
        {
            if (pack.Awake) continue;
            float wake = Rules.Wilds.WakeRadius + Rules.Wilds.WakeSpread * pack.Spread(Rules.Wilds.SleepDensity), wake2 = wake * wake;
            bool woken = Noise.LevelAtTile(pack.X, pack.Y) >= Balance.WakeThreshold;
            if (!woken && checkUnits)
                foreach (var u in _units)
                {
                    float dx = u.X - pack.X, dy = u.Y - pack.Y;
                    if (dx * dx + dy * dy <= wake2) { woken = true; break; }
                }
            if (!woken) continue;
            pack.Awake = true;
            int spawned = SpawnPack(pack);
            _events.Add(new PackWoke(Tick, pack.Id, pack.X, pack.Y, spawned));
        }
    }

    /// <summary>The nearest sleeping pack within ClearRadius of a footprint, if any: its ground can't be built on yet.</summary>
    public Pack? PackNear(int x, int y, int w, int h)
    {
        float density = Rules.Wilds.SleepDensity;
        foreach (var p in _packs)
        {
            if (p.Awake) continue;
            float r = p.Stray ? p.Spread(density) + 2 : MathF.Max(Rules.Wilds.ClearRadius, p.Spread(density) + 2);
            float ex = MathF.Max(MathF.Max(x - (p.X + 0.5f), 0), p.X + 0.5f - (x + w));
            float ey = MathF.Max(MathF.Max(y - (p.Y + 0.5f), 0), p.Y + 0.5f - (y + h));
            if (ex * ex + ey * ey <= r * r) return p;
        }
        return null;
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
