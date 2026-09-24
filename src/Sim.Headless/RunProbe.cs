using System.Diagnostics;
using System.Globalization;
using Hellwall.Sim;

namespace Hellwall.Headless;

/// <summary>
/// The phase 3 exit criterion: a full survival run, under the default rules
/// and starting resources, is winnable and losable.
///
/// --win-seeds (default 7, the designated map, and 3): the Full bot must win.
/// --lose-seeds (default 7,3,11,19,42): the Passive bot (economy, no defense) must lose.
/// --seeds plays the Full bot on other maps without gating, to see how far
/// the doctrine carries: it's a scripted player, and some terrain beats it.
/// </summary>
public static class RunProbe
{
    public static int Run(Dictionary<string, string> args)
    {
        List<uint> Seeds(string key, string fallback) =>
            args.GetValueOrDefault(key, fallback).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => uint.Parse(v, CultureInfo.InvariantCulture)).ToList();
        bool trace = args.ContainsKey("trace");
        double snapAt = args.TryGetValue("snapshot-at", out var at) ? double.Parse(at, CultureInfo.InvariantCulture) : -1;

        if (args.ContainsKey("seeds"))
        {
            // Informational: the Full bot on whatever maps were asked for.
            var styles = args.TryGetValue("bot", out var only) ? [Enum.Parse<Bot.Style>(only, ignoreCase: true)] : new[] { Bot.Style.Full };
            foreach (var seed in Seeds("seeds", ""))
                foreach (var style in styles)
                    Console.WriteLine(Play(seed, style, trace, snapAt));
            return 0;
        }

        bool ok = true;
        foreach (var seed in Seeds("win-seeds", "7,3"))
        {
            var r = Play(seed, Bot.Style.Full, trace, snapAt);
            Console.WriteLine(r);
            if (r.Outcome != Outcome.Won) { Console.WriteLine($"FAIL: seed {seed}: the full bot didn't win"); ok = false; }
        }
        foreach (var seed in Seeds("lose-seeds", "7,3,11,19,42"))
        {
            var r = Play(seed, Bot.Style.Passive, trace, snapAt);
            Console.WriteLine(r);
            if (r.Outcome != Outcome.Lost) { Console.WriteLine($"FAIL: seed {seed}: the passive bot didn't lose"); ok = false; }
        }
        return ok ? 0 : 1;
    }

    public sealed record Result(uint Seed, Bot.Style Style, Outcome Outcome, int Day, int Buildings, int Units, int Killed, int Lost, int Possessed, double WallSeconds)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"seed {Seed,-3} {Style,-8} {Outcome,-7} day {Day,2}  buildings {Buildings,3}  units {Units,2}  demons killed {Killed,6}  buildings lost {Lost,4}  possessed {Possessed,3}  ({WallSeconds:F0}s)");
    }

    public static Result Play(uint seed, Bot.Style style, bool trace, double snapshotAt = -1)
    {
        var clock = Stopwatch.StartNew();
        var world = World.Create(new WorldOptions(seed, 256, 40, Rules.Default, Survival: true));
        var bot = new Bot(world, style) { Verbose = trace };
        int possessed = 0;
        int limit = (world.Rules.Survival.Days + 5) * (int)(world.Rules.Survival.DaySeconds * Balance.TickHz);
        while (world.Outcome == Outcome.Running && world.Tick < limit)
        {
            if (world.Tick % Balance.TickHz == 0) bot.Act();
            world.Step();
            var events = world.DrainEvents();
            bot.See(events);
            foreach (var e in events)
            {
                if (e is BuildingPossessed) possessed++;
                if (trace && e is WaveLanded wl) Console.WriteLine($"    day {world.Day,2}: wave {wl.Number} landed ({wl.Spawned}{(wl.Final ? ", CONVERGENCE" : "")})");
            }
            if (trace && world.Tick % (5 * 60 * Balance.TickHz) == 0) Trace(world);
            if (snapshotAt >= 0 && world.Tick == (int)(snapshotAt * Balance.TickHz))
            {
                string path = $"out/run-{seed}-{style}-{snapshotAt:0}s.ppm";
                Snapshot.Write(world, path);
                Console.WriteLine($"    snapshot: {path}");
            }
        }
        if (trace) Trace(world);
        return new Result(seed, style, world.Outcome, world.Day, world.Buildings.Count, world.Units.Count, world.Stats.DemonsKilled,
            world.Stats.BuildingsLost, possessed, clock.Elapsed.TotalSeconds);
    }

    static void Trace(World w)
    {
        var c = w.Colony;
        string Count(BuildingKind k) => w.Buildings.Count(b => b.Kind == k).ToString();
        var food = w.Buildings.Where(b => b.Def.Produces == Resource.Food).ToList();
        string farming = string.Join(",", food.Select(b => $"{(b.Kind == BuildingKind.Farm ? "F" : "H")}{(b.Active ? "" : b.Staffed ? "!" : "?")}{b.Rate * w.Colony.Power:0.00}"));
        Console.WriteLine($"      food producers [{farming}] eaten {c.Colonists * w.Rules.ColonistFoodPerSecond:0.00}/s power {c.Power:0.00}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"    day {w.Day,2}  gold {c[Resource.Gold],5:F0} ({c.NetPerSecond[0]:+0.0;-0.0}) wood {c[Resource.Wood],4:F0} ({c.NetPerSecond[1]:+0.0;-0.0}) stone {c[Resource.Stone],4:F0} ({c.NetPerSecond[2]:+0.0;-0.0}) food {c[Resource.Food],4:F0} ({c.NetPerSecond[3]:+0.00;-0.00})  " +
            $"colonists {c.WorkersUsed}/{c.Colonists} sanct {c.SanctityDemand:0}/{c.SanctitySupply:0}  " +
            $"house {Count(BuildingKind.House)} farm {Count(BuildingKind.Farm)} hunt {Count(BuildingKind.Hunter)} wood {Count(BuildingKind.Woodcutter)} quar {Count(BuildingKind.Quarry)} shrine {Count(BuildingKind.Shrine)} ward {Count(BuildingKind.Wardstone)} " +
            $"wall {Count(BuildingKind.Wall)} tower {Count(BuildingKind.Watchtower)} bomb {Count(BuildingKind.Bombard)} units {w.Units.Count}  demons {w.Horde.Count} (asleep {w.Packs.Where(p => !p.Awake).Sum(p => p.Count)})  keep {w.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Keep)?.Hp ?? 0:0}"));
    }
}
