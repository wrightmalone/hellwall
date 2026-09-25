namespace Hellwall.Sim;

/// <summary>
/// Save and load: a full snapshot of the world between ticks.
///
/// A snapshot, not a replay, because replaying an hour's commands would mean
/// re-simulating 72,000 ticks to load a save. The risk with snapshots is a
/// forgotten field, so SaveTests checks that a loaded world has the same
/// StateHash as the one saved and then stays identical, tick for tick, as
/// both play on. Derived state (flow fields, spatial hash, soldiers' route
/// maps) is rebuilt rather than stored.
///
/// The sim still does no IO: Save returns bytes, Load takes them, and the
/// host decides where they live. A save only loads under the rules it was
/// made with; the rules' hash is in the header.
/// </summary>
public sealed partial class World
{
    const uint Magic = 0x56535748; // "HWSV"
    const int FormatVersion = 8;

    public byte[] Save()
    {
        if (_pending.Count > 0) throw new InvalidOperationException("flush or step before saving: commands are pending");
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(Rules.Hash);
            w.Write(Seed);
            w.Write(Terrain.Width);
            w.Write(Survival != null);
            w.Write(Survival?.Endless ?? false);
            w.Write((byte)Rules.Difficulty);
            w.Write((byte)Map);
            w.Write(Scenario?.Id ?? "");
            // A skirmish or a hand-made map isn't in the campaign: it travels with the save.
            bool inline = Scenario != null && Campaign.Default.Find(Scenario.Id) != Scenario;
            w.Write(inline);
            if (inline) w.Write(Scenario!.ToJson());
            w.Write(Rules.Woods.Blocks); // a run option (living woods), so the save carries it

            w.Write(Tick);
            w.Write((byte)Outcome);
            w.Write(Rng.State);
            w.Write(_nextId);
            w.Write(NetworkDirty);
            foreach (var t in Terrain.Tiles) w.Write((byte)t);

            w.Write(Tech.Researched.Count);
            foreach (var id in Tech.Researched) w.Write(id);

            w.Write(Stats.DemonsKilled);
            w.Write(Stats.BuildingsLost);
            w.Write(Stats.UnitsLost);

            foreach (var v in Colony.Stock) w.Write(v);
            foreach (var v in Colony.NetPerSecond) w.Write(v);
            w.Write(Colony.Colonists);
            w.Write(Colony.WorkersUsed);
            w.Write(Colony.SanctitySupply);
            w.Write(Colony.SanctityDemand);
            w.Write(Colony.Power);
            w.Write(Colony.Starving);
            foreach (var c in Colony.Consecrated) w.Write(c);

            w.Write(_buildings.Count);
            foreach (var b in _buildings)
            {
                w.Write(b.Id);
                w.Write((byte)b.Kind);
                w.Write(b.X);
                w.Write(b.Y);
                w.Write(b.Hp);
                w.Write(b.Built);
                w.Write(b.Complete);
                w.Write(b.OnGround);
                w.Write(b.Staffed);
                w.Write(b.Rate);
                w.Write(b.Cooldown);
                w.Write(b.TrainProgress);
                w.Write(b.Queue.Count);
                foreach (var q in b.Queue) w.Write((byte)q);
                w.Write(b.RallyX);
                w.Write(b.RallyY);
                w.Write(b.WoodWindow);
                w.Write(b.WoodTimer);
                w.Write(b.Possessed);
                w.Write(b.Occupants);
                w.Write(b.PossessTimer);
                w.Write(b.Researching ?? "");
                w.Write(b.ResearchProgress);
            }

            w.Write(_units.Count);
            foreach (var u in _units)
            {
                w.Write(u.Id);
                w.Write((byte)u.Kind);
                w.Write(u.X);
                w.Write(u.Y);
                w.Write(u.PrevX);
                w.Write(u.PrevY);
                w.Write(u.Hp);
                w.Write(u.Cooldown);
                w.Write((byte)u.Order);
                w.Write(u.DestX);
                w.Write(u.DestY);
                w.Write(u.Kills);
                w.Write(u.Field != null);
            }

            var h = Horde;
            w.Write(h.Count);
            for (int i = 0; i < h.Count; i++)
            {
                w.Write(h.X[i]);
                w.Write(h.Y[i]);
                w.Write(h.PrevX[i]);
                w.Write(h.PrevY[i]);
                w.Write(h.VX[i]);
                w.Write(h.VY[i]);
                w.Write((byte)h.Kind[i]);
                w.Write(h.Hp[i]);
                w.Write(h.Cooldown[i]);
            }

            w.Write(_packs.Count);
            foreach (var p in _packs)
            {
                w.Write(p.Id);
                w.Write(p.X);
                w.Write(p.Y);
                w.Write(p.Count);
                w.Write((byte)p.Kind);
                w.Write(p.Awake);
                w.Write(p.Stray);
            }

            foreach (var level in Noise.Level) w.Write(level);

            w.Write(_gates.Count);
            foreach (var g in _gates)
            {
                w.Write(g.Id);
                w.Write(g.X);
                w.Write(g.Y);
                w.Write(g.Hp);
                w.Write(g.SpawnTimer);
            }

            if (Survival is { } s)
            {
                w.Write(s.FinalLanded);
                w.Write(s.FinalLandedTick);
                w.Write(s.ConvergenceSpent);
                w.Write(s.Waves.Count);
                foreach (var wave in s.Waves)
                {
                    w.Write(wave.Announced);
                    w.Write(wave.Landed);
                    w.Write(wave.Size);
                    w.Write(wave.Sides.Length);
                    foreach (var side in wave.Sides) w.Write((byte)side);
                }
                w.Write(s.Corruptions.Count);
                foreach (var id in s.Corruptions) w.Write(id);
                w.Write(s.PendingCorruption ?? "");
                w.Write(s.NextCorruptionTick);
            }
            foreach (bool done in GoalsDone) w.Write(done);
            foreach (bool fired in TriggersFired) w.Write(fired);
            SaveWoods(w);
            foreach (bool seen in Vision.Explored) w.Write(seen);
        }
        return stream.ToArray();
    }

    /// <summary>Trees part felled (only those; the rest stand at full), how many have come down, and the woodsmen, who carry their paths.</summary>
    void SaveWoods(BinaryWriter w)
    {
        w.Write(TreesFelled);
        var hurt = new List<int>();
        for (int i = 0; i < TreeHp.Length; i++)
            if (Terrain.Tiles[i] == Tile.Forest && TreeHp[i] != Rules.Woods.TreeHp) hurt.Add(i);
        w.Write(hurt.Count);
        foreach (int i in hurt) { w.Write(i); w.Write(TreeHp[i]); }
        w.Write(_woodsmen.Count);
        foreach (var m in _woodsmen)
        {
            w.Write(m.Id); w.Write(m.HomeId);
            w.Write(m.X); w.Write(m.Y); w.Write(m.PrevX); w.Write(m.PrevY);
            w.Write((byte)m.State);
            w.Write(m.Path.Length);
            foreach (int t in m.Path) w.Write(t);
            w.Write(m.Step); w.Write(m.Tree); w.Write(m.Carry); w.Write(m.Wait);
        }
    }

    void LoadWoods(BinaryReader r)
    {
        TreesFelled = r.ReadInt32();
        for (int i = 0; i < TreeHp.Length; i++) TreeHp[i] = Terrain.Tiles[i] == Tile.Forest ? Rules.Woods.TreeHp : 0;
        for (int n = r.ReadInt32(); n > 0; n--) { int i = r.ReadInt32(); TreeHp[i] = r.ReadSingle(); }
        Array.Clear(TreeClaim);
        _woodsmen.Clear();
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var m = new Woodsman { Id = r.ReadInt32(), HomeId = r.ReadInt32(), X = r.ReadSingle(), Y = r.ReadSingle(), PrevX = r.ReadSingle(), PrevY = r.ReadSingle(), State = (WoodsmanState)r.ReadByte() };
            m.Path = new int[r.ReadInt32()];
            for (int i = 0; i < m.Path.Length; i++) m.Path[i] = r.ReadInt32();
            m.Step = r.ReadInt32(); m.Tree = r.ReadInt32(); m.Carry = r.ReadSingle(); m.Wait = r.ReadSingle();
            if (m.Tree >= 0) TreeClaim[m.Tree] = m.Id;
            _woodsmen.Add(m);
        }
    }

    /// <summary>Rebuild a world from Save's bytes. The rules must be the ones it was saved under (at Normal or at the save's own difficulty, which the save records).</summary>
    public static World Load(byte[] data, Rules rules)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        if (r.ReadUInt32() != Magic) throw new FormatException("not a Hellwall save");
        int version = r.ReadInt32();
        if (version != FormatVersion) throw new FormatException($"save format {version}, this build reads {FormatVersion}");
        ulong rulesHash = r.ReadUInt64();
        uint seed = r.ReadUInt32();
        int size = r.ReadInt32();
        bool survival = r.ReadBoolean();
        bool endless = r.ReadBoolean();
        var difficulty = (Difficulty)r.ReadByte();
        var map = (MapKind)r.ReadByte();
        string scenarioId = r.ReadString();
        bool inline = r.ReadBoolean();
        var scenario = inline ? ScenarioDef.FromJson(r.ReadString())
            : scenarioId.Length == 0 ? null : Campaign.Default.Find(scenarioId) ?? throw new FormatException($"this save is from a mission this build doesn't have ('{scenarioId}')");
        if (scenario != null) rules = scenario.RulesFrom(rules);
        if (r.ReadBoolean() && !rules.Woods.Blocks) rules = rules.WithWoods(w => w with { Blocks = true });
        if (rulesHash != rules.ForDifficulty(difficulty).Hash) throw new FormatException("this save was made under different rules");

        var world = new World(new WorldOptions(seed, size, 0, rules, survival, difficulty, endless, map, scenario));
        world.Tick = r.ReadInt32();
        world.Outcome = (Outcome)r.ReadByte();
        world.Rng.State = r.ReadUInt32();
        world._nextId = r.ReadInt32();
        world.NetworkDirty = r.ReadBoolean();
        var tiles = world.Terrain.Tiles;
        for (int i = 0; i < tiles.Length; i++) tiles[i] = (Tile)r.ReadByte();

        int researched = r.ReadInt32();
        for (int i = 0; i < researched; i++) world.Tech.Researched.Add(r.ReadString());
        world.Tech.Recompute(rules);

        world.Stats.DemonsKilled = r.ReadInt32();
        world.Stats.BuildingsLost = r.ReadInt32();
        world.Stats.UnitsLost = r.ReadInt32();

        var colony = world.Colony;
        for (int i = 0; i < colony.Stock.Length; i++) colony.Stock[i] = r.ReadDouble();
        for (int i = 0; i < colony.NetPerSecond.Length; i++) colony.NetPerSecond[i] = r.ReadDouble();
        colony.Colonists = r.ReadInt32();
        colony.WorkersUsed = r.ReadInt32();
        colony.SanctitySupply = r.ReadSingle();
        colony.SanctityDemand = r.ReadSingle();
        colony.Power = r.ReadSingle();
        colony.Starving = r.ReadBoolean();
        for (int i = 0; i < colony.Consecrated.Length; i++) colony.Consecrated[i] = r.ReadBoolean();

        int buildings = r.ReadInt32();
        for (int n = 0; n < buildings; n++)
        {
            int id = r.ReadInt32();
            var kind = (BuildingKind)r.ReadByte();
            var def = world.Def(kind);
            var b = new Building
            {
                Id = id, Kind = kind, Def = def, X = r.ReadInt32(), Y = r.ReadInt32(), W = def.W, H = def.H,
                Hp = r.ReadSingle(), Built = r.ReadSingle(), Complete = r.ReadBoolean(), OnGround = r.ReadBoolean(),
                Staffed = r.ReadBoolean(), Rate = r.ReadDouble(), Cooldown = r.ReadSingle(), TrainProgress = r.ReadSingle(),
            };
            int queued = r.ReadInt32();
            for (int q = 0; q < queued; q++) b.Queue.Add((UnitKind)r.ReadByte());
            b.RallyX = r.ReadInt32();
            b.RallyY = r.ReadInt32();
            b.WoodWindow = r.ReadSingle();
            b.WoodTimer = r.ReadSingle();
            b.Possessed = r.ReadBoolean();
            b.Occupants = r.ReadInt32();
            b.PossessTimer = r.ReadSingle();
            string researching = r.ReadString();
            b.Researching = researching.Length == 0 ? null : researching;
            b.ResearchProgress = r.ReadSingle();
            world._buildings.Add(b);
            world._buildingById[b.Id] = b;
            world.Stamp(b, b.Id);
        }

        int units = r.ReadInt32();
        var needsField = new List<Unit>();
        for (int n = 0; n < units; n++)
        {
            int id = r.ReadInt32();
            var kind = (UnitKind)r.ReadByte();
            var u = new Unit
            {
                Id = id, Kind = kind, Def = world.Def(kind), X = r.ReadSingle(), Y = r.ReadSingle(), PrevX = r.ReadSingle(), PrevY = r.ReadSingle(),
                Hp = r.ReadSingle(), Cooldown = r.ReadSingle(), Order = (OrderKind)r.ReadByte(), DestX = r.ReadInt32(), DestY = r.ReadInt32(), Kills = r.ReadInt32(),
            };
            if (r.ReadBoolean()) needsField.Add(u);
            world._units.Add(u);
        }

        int demons = r.ReadInt32();
        var h = world.Horde;
        for (int i = 0; i < demons; i++)
        {
            float x = r.ReadSingle(), y = r.ReadSingle(), px = r.ReadSingle(), py = r.ReadSingle(), vx = r.ReadSingle(), vy = r.ReadSingle();
            var kind = (DemonKind)r.ReadByte();
            h.Add(kind, x, y, r.ReadSingle());
            h.PrevX[i] = px;
            h.PrevY[i] = py;
            h.VX[i] = vx;
            h.VY[i] = vy;
            h.Cooldown[i] = r.ReadSingle();
        }

        int packs = r.ReadInt32();
        for (int n = 0; n < packs; n++)
            world._packs.Add(new Pack { Id = r.ReadInt32(), X = r.ReadInt32(), Y = r.ReadInt32(), Count = r.ReadInt32(), Kind = (DemonKind)r.ReadByte(), Awake = r.ReadBoolean(), Stray = r.ReadBoolean() });

        var noise = world.Noise.Level;
        for (int i = 0; i < noise.Length; i++) noise[i] = r.ReadSingle();

        int gates = r.ReadInt32();
        for (int n = 0; n < gates; n++)
        {
            var g = new Hellgate { Id = r.ReadInt32(), X = r.ReadInt32(), Y = r.ReadInt32(), Hp = r.ReadSingle(), SpawnTimer = r.ReadSingle() };
            world._gates.Add(g);
            if (g.Alive) world.StampGate(g, true);
        }

        if (world.Survival is { } s)
        {
            s.FinalLanded = r.ReadBoolean();
            s.FinalLandedTick = r.ReadInt32();
            s.ConvergenceSpent = r.ReadBoolean();
            int waves = r.ReadInt32();
            if (s.Endless) s.Extend(waves); // planned as the run went on
            if (waves != s.Waves.Count) throw new FormatException("wave schedule doesn't match the rules");
            foreach (var wave in s.Waves)
            {
                wave.Announced = r.ReadBoolean();
                wave.Landed = r.ReadBoolean();
                wave.Size = r.ReadInt32();
                var sides = new Side[r.ReadInt32()];
                for (int i = 0; i < sides.Length; i++) sides[i] = (Side)r.ReadByte();
                wave.Sides = sides;
            }
            int taken = r.ReadInt32();
            for (int i = 0; i < taken; i++) s.Corruptions.Add(r.ReadString());
            string pending = r.ReadString();
            s.PendingCorruption = pending.Length == 0 ? null : pending;
            s.NextCorruptionTick = r.ReadInt32();
            CorruptionSystem.Recompute(world);
        }

        for (int i = 0; i < world.GoalsDone.Length; i++) world.GoalsDone[i] = r.ReadBoolean();
        for (int i = 0; i < world.TriggersFired.Length; i++) world.TriggersFired[i] = r.ReadBoolean();
        world.LoadWoods(r);
        var explored = new bool[world.Vision.Explored.Length];
        for (int i = 0; i < explored.Length; i++) explored[i] = r.ReadBoolean();
        world.Vision.Load(explored);

        // Derived state: rebuilt, not stored.
        world._flowDirty = true;
        world._spatialStale = true;
        world.EnsureFlow();
        foreach (var u in needsField) u.Field = world.HumanFieldTo(u.DestX, u.DestY);
        return world;
    }
}
