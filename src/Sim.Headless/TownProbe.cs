using System.Globalization;
using Hellwall.Sim;

namespace Hellwall.Headless;

/// <summary>
/// The phase 2 exit criterion: a walled town holds a scripted wave. Run
/// twice against the identical wave: a defended town (a ring of walls with a
/// gated gap, towers, Bombards, a Barracks and its soldiers) must hold, and
/// an undefended one (the Keep and two Houses) must fall. The second half is
/// what makes the first mean anything: if both hold, combat decides nothing.
///
/// Resources are topped up so the probe measures defense, not economy; the
/// economy has its own tests.
/// </summary>
public static class TownProbe
{
    const int C = 64;

    public static int Run(Dictionary<string, string> args)
    {
        // Five seeds, because terrain moves the breaking point: at the time of
        // writing a wave of 250 breaks seeds 3 and 42 but not the others, so a
        // single-seed gate would pass or fail by luck of the map.
        var seeds = args.GetValueOrDefault("seeds", "3,7,11,19,42").Split(',').Select(v => uint.Parse(v, CultureInfo.InvariantCulture));
        int wave = int.Parse(args.GetValueOrDefault("wave", "200"), CultureInfo.InvariantCulture);
        int seconds = int.Parse(args.GetValueOrDefault("seconds", "240"), CultureInfo.InvariantCulture);
        bool trace = args.ContainsKey("trace");
        // --extra=Belfry: one more of that building inside each attacked wall, to measure what it adds.
        BuildingKind? extra = args.TryGetValue("extra", out var e) ? Enum.Parse<BuildingKind>(e) : null;

        bool ok = true;
        foreach (var seed in seeds)
        {
            var defended = Play(seed, wave, seconds, defend: true, trace, extra);
            var bare = Play(seed, wave, seconds, defend: false);
            Console.WriteLine($"seed {seed}");
            Console.WriteLine($"  {defended}");
            Console.WriteLine($"  {bare}");
            if (defended.Outcome != Outcome.Running) { Console.WriteLine($"FAIL: seed {seed}: the defended town fell"); ok = false; }
            if (bare.Outcome != Outcome.Lost) { Console.WriteLine($"FAIL: seed {seed}: the undefended town survived, so defense decided nothing"); ok = false; }
            if (defended.Rejected > 0) { Console.WriteLine($"FAIL: seed {seed}: town setup had rejected commands"); ok = false; }
        }
        return ok ? 0 : 1;
    }

    public sealed record Result(string Name, Outcome Outcome, float KeepHp, int Spawned, int Killed, int BuildingsLost, int WallsLost, int UnitsTrained, int UnitsLost, int Rejected, double Seconds)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"{Name,-10} {Outcome,-7} after {Seconds,5:F0}s  keep_hp={KeepHp,6:F0}  demons {Killed}/{Spawned} killed  buildings_lost={BuildingsLost} (walls {WallsLost})  units {UnitsTrained - UnitsLost}/{UnitsTrained} alive  rejected={Rejected}");
    }

    public static Result Play(uint seed, int waveSize, int seconds, bool defend, bool trace = false, BuildingKind? extra = null)
    {
        var rules = Rules.Default.WithStartingResources(new Cost { Gold = 5000, Wood = 3000, Stone = 2000, Food = 1000, Iron = 1000 });
        if (extra is { } ek) rules = rules.WithBuilding(ek, d => d with { RequiresTech = null });
        var world = World.Create(new WorldOptions(seed, 128, 0, rules));
        var rejected = new List<CommandRejected>();
        int wallsLost = 0, trained = 0, spawned = 0, shots = 0;

        void Do(IEnumerable<Command> commands)
        {
            foreach (var c in commands) world.Enqueue(c);
            world.FlushCommands();
        }

        Do(defend ? Town() : [new PlaceBuilding(BuildingKind.House, 59, 63), new PlaceBuilding(BuildingKind.House, 68, 63)]);
        if (defend && extra is { } k) Do([new PlaceBuilding(k, 57, 61), new PlaceBuilding(k, 70, 61)]);

        int waveTick = 60 * Balance.TickHz;
        // The garrison, fed to the Barracks as its queue has room (it holds Balance.QueueLimit).
        var garrison = new Queue<UnitKind>(Enumerable.Repeat(UnitKind.Militia, 6).Concat(Enumerable.Repeat(UnitKind.Templar, 2)).Concat(Enumerable.Repeat(UnitKind.Marksman, 2)));
        for (int t = 0; t < seconds * Balance.TickHz && world.Outcome == Outcome.Running; t++)
        {
            if (defend && garrison.Count > 0 && world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Barracks && b.Complete) is { } barracks)
            {
                var queue = new List<Command>();
                for (int room = Balance.QueueLimit - barracks.Queue.Count; room > 0 && garrison.Count > 0; room--)
                    queue.Add(new TrainUnit(barracks.Id, garrison.Dequeue()));
                if (queue.Count > 0) Do(queue);
            }
            if (t == waveTick)
            {
                Do(Scenarios.Wave(world, Side.West, waveSize / 2));
                Do(Scenarios.Wave(world, Side.East, waveSize - waveSize / 2));
            }

            world.Step();
            foreach (var e in world.DrainEvents())
            {
                switch (e)
                {
                    case CommandRejected r: rejected.Add(r); break;
                    case BuildingDestroyed { Kind: BuildingKind.Wall or BuildingKind.Gate } w:
                        wallsLost++;
                        if (trace) Console.WriteLine($"  t={world.Tick / Balance.TickHz}s wall down at ({w.X},{w.Y})");
                        break;
                    case UnitTrained ut:
                        trained++;
                        // Send each soldier to the middle of town as soon as it's trained.
                        world.Enqueue(new OrderUnits([ut.UnitId], OrderKind.AttackMove, C, C - 3));
                        break;
                    case DemonsSpawned s: spawned += s.Count; break;
                    case ShotFired: shots++; break;
                    case BuildingDestroyed d when trace: Console.WriteLine($"  t={world.Tick / Balance.TickHz}s destroyed {d.Kind} at ({d.X},{d.Y})"); break;
                    case BuildingPossessed p when trace: Console.WriteLine($"  t={world.Tick / Balance.TickHz}s POSSESSED {p.Kind} ({p.Occupants} inside)"); break;
                    case UnitDied u when trace: Console.WriteLine($"  t={world.Tick / Balance.TickHz}s {u.Kind} died{(u.Rose ? ", rose" : "")} at ({u.X:F0},{u.Y:F0})"); break;
                }
            }
            if (trace && world.Tick % (5 * Balance.TickHz) == 0)
            {
                var keepNow = world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Keep);
                var towers = world.Buildings.Where(b => b.Def.Weapon != null).ToList();
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  t={world.Tick / Balance.TickHz,3}s keep={keepNow?.Hp ?? 0,5:F0} demons={world.Horde.Count,4} shots={shots,4} towers active {towers.Count(b => b.Active)}/{towers.Count} (complete {towers.Count(b => b.Complete)}, staffed {towers.Count(b => b.Staffed)}, ground {towers.Count(b => b.OnGround)}) power={world.Colony.Power:F2} colonists={world.Colony.Colonists} used={world.Colony.WorkersUsed} units={world.Units.Count}"));
            }
        }

        foreach (var r in rejected.Take(5)) Console.WriteLine($"  rejected: {r.Reason}: {r.Command}");
        var keep = world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Keep);
        return new Result(defend ? "defended" : "bare", world.Outcome, keep?.Hp ?? 0, spawned, world.Stats.DemonsKilled,
            world.Stats.BuildingsLost, wallsLost, trained, world.Stats.UnitsLost, rejected.Count, world.Tick / (double)Balance.TickHz);
    }

    /// <summary>
    /// Keep at (63..65, 63..65). A ring of walls 8 out with gates in its north
    /// gap. Inside: a Watchtower a tile in from each corner, where its range
    /// covers two wall segments and the Keep (a tower hard against the wall
    /// draws the breach to itself, since demons head for the nearest
    /// building); a Bombard north and south of the Keep; two Houses; a Barracks.
    /// </summary>
    static List<Command> Town()
    {
        var cmds = new List<Command>();
        for (int y = C - 8; y <= C + 8; y++)
            for (int x = C - 8; x <= C + 8; x++)
            {
                bool edge = x == C - 8 || x == C + 8 || y == C - 8 || y == C + 8;
                if (!edge) continue;
                bool gate = y == C - 8 && Math.Abs(x - C) <= 1;
                cmds.Add(new PlaceBuilding(gate ? BuildingKind.Gate : BuildingKind.Wall, x, y));
            }
        foreach (var (x, y) in new[] { (58, 58), (69, 58), (58, 69), (69, 69) }) cmds.Add(new PlaceBuilding(BuildingKind.Watchtower, x, y));
        cmds.Add(new PlaceBuilding(BuildingKind.Bombard, 63, 59));
        cmds.Add(new PlaceBuilding(BuildingKind.Bombard, 63, 68));
        cmds.Add(new PlaceBuilding(BuildingKind.House, 59, 63));
        cmds.Add(new PlaceBuilding(BuildingKind.House, 68, 63));
        cmds.Add(new PlaceBuilding(BuildingKind.Barracks, 59, 66));
        return cmds;
    }
}
