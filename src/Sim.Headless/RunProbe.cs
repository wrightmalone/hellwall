using System.Diagnostics;
using System.Globalization;
using Hellwall.Sim;

namespace Hellwall.Headless;

/// <summary>
/// The phase 3 exit criterion: a full survival run, under the default rules
/// and starting resources, is winnable and losable.
///
/// --win-seeds (default 11, the designated map, and 3): the Full bot must win.
/// With Hellgates in, whole maps are currently won or lost by every
/// doctrine alike (see README, "Bot results"): map fairness is phase 5's
/// problem, so the gates run on maps that are fair today.
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
        ReadDifficulty(args);
        double snapAt = args.TryGetValue("snapshot-at", out var at) ? double.Parse(at, CultureInfo.InvariantCulture) : -1;
        string plan = args.GetValueOrDefault("plan", "fortress");
        if (!Bot.Plans.ContainsKey(plan)) { Console.Error.WriteLine($"no plan '{plan}': {string.Join(", ", Bot.Plans.Keys)}"); return 2; }

        if (args.ContainsKey("seeds"))
        {
            // Informational: the Full bot on whatever maps were asked for.
            var styles = args.TryGetValue("bot", out var only) ? [Enum.Parse<Bot.Style>(only, ignoreCase: true)] : new[] { Bot.Style.Full };
            foreach (var seed in Seeds("seeds", ""))
                foreach (var style in styles)
                    Console.WriteLine(Play(seed, style, trace, snapAt, plan));
            return 0;
        }

        bool ok = true;
        foreach (var seed in Seeds("win-seeds", "11,3"))
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

    /// <summary>
    /// The phase 4 gate: at least three build paths win. Each plan in
    /// Bot.Plans plays the designated map with the full demon roster; every one
    /// has to win, and the spread of their margins shows whether one dominates.
    /// </summary>
    /// <summary>
    /// Endless mode, the phase 5 criterion: runs should vary by seed, in how
    /// long they last and in what the horde became. Plays each seed until
    /// the Keep falls (or --max-days) and prints the day and the corruptions.
    /// </summary>
    public static int Endless(Dictionary<string, string> args)
    {
        var seeds = args.GetValueOrDefault("seeds", "3,5,7,11,13,19,23,42").Split(',').Select(v => uint.Parse(v, CultureInfo.InvariantCulture)).ToList();
        string plan = args.GetValueOrDefault("plan", "fortress");
        int maxDays = int.Parse(args.GetValueOrDefault("max-days", "200"), CultureInfo.InvariantCulture);
        bool trace = args.ContainsKey("trace");
        ReadDifficulty(args);
        foreach (var seed in seeds)
        {
            var r = Play(seed, Bot.Style.Full, trace, plan: plan, endlessDays: maxDays, onEnd: w => lastCorruptions = string.Join(" > ", w.Survival!.Corruptions.Select(id => w.Rules.Corruption(id).Name)));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"endless seed {seed,-3} {plan,-9} {(r.Outcome == Outcome.Lost ? "fell" : "stands"),-6} day {r.Day,3}  killed {r.Killed,7}  [{lastCorruptions}]"));
        }
        return 0;
    }

    static string lastCorruptions = "";

    /// <summary>
    /// The campaign's difficulty curve, measured: each mission (--missions, or
    /// all) played by each research plan (--plans), with the day reached and
    /// the goals met. scripts/campaign.sh runs the missions in parallel.
    /// </summary>
    public static int Campaign(Dictionary<string, string> args)
    {
        var campaign = Hellwall.Sim.Campaign.Default;
        var ids = args.TryGetValue("missions", out var m) ? m.Split(',') : campaign.Scenarios.Select(s => s.Id).ToArray();
        var plans = args.GetValueOrDefault("plans", string.Join(",", Bot.Plans.Keys)).Split(',');
        foreach (var id in ids)
        {
            var s = campaign.Find(id) ?? throw new ArgumentException($"no mission '{id}'");
            foreach (var plan in plans)
            {
                string goals = "";
                var r = Play(s.Seed, Bot.Style.Full, args.ContainsKey("trace"), plan: plan, scenario: s, onEnd: w => goals = string.Join(" ", w.Goals.Select((g, i) => $"{g.Kind}{(w.GoalsDone[i] ? "+" : "-")}")));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"mission {id,-12} {plan,-9} {r.Outcome,-7} day {r.Day,2}/{(s.Days > 0 ? s.Days : 60)}  {s.Map,-9} {s.Difficulty,-6} [{goals}]"));
            }
        }
        return 0;
    }

    public static int Paths(Dictionary<string, string> args)
    {
        var seeds = args.GetValueOrDefault("seeds", "11").Split(',').Select(v => uint.Parse(v, CultureInfo.InvariantCulture)).ToList();
        bool trace = args.ContainsKey("trace");
        args_perf = args.ContainsKey("perf");
        ReadDifficulty(args);
        bool ok = true;
        foreach (var seed in seeds)
            foreach (var plan in Bot.Plans.Keys)
            {
                var r = Play(seed, Bot.Style.Full, trace, plan: plan);
                Console.WriteLine(r);
                if (r.Outcome != Outcome.Won) { Console.WriteLine($"FAIL: seed {seed}: the {plan} path didn't win"); ok = false; }
            }
        return ok ? 0 : 1;
    }

    public sealed record Result(uint Seed, Bot.Style Style, Outcome Outcome, int Day, int Buildings, int Units, int Killed, int Lost, int Possessed, double WallSeconds, string Plan = "", string Techs = "", float KeepHp = 0, int GatesClosed = 0)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"seed {Seed,-3} {Style,-8} {Plan,-9}{Outcome,-7} day {Day,2}  buildings {Buildings,3}  units {Units,2}  demons killed {Killed,6}  buildings lost {Lost,4}  possessed {Possessed,3}  keep {KeepHp,5:F0}  gates closed {GatesClosed}  ({WallSeconds:F0}s)  [{Techs}]");
    }

    /// <summary>Print per-tick sim cost for each run (set by --perf).</summary>
    static bool args_perf;
    static Difficulty args_difficulty = Difficulty.Normal;
    static MapKind args_map = MapKind.Plains;

    /// <summary>--difficulty and --map, for every command that plays a run.</summary>
    static void ReadDifficulty(Dictionary<string, string> args)
    {
        if (args.TryGetValue("difficulty", out var d)) args_difficulty = Enum.Parse<Difficulty>(d, ignoreCase: true);
        if (args.TryGetValue("map", out var m)) args_map = Enum.Parse<MapKind>(m, ignoreCase: true);
        args_woods = args.ContainsKey("woods");
        if (args.TryGetValue("tree-hp", out var hp)) args_treeHp = float.Parse(hp, CultureInfo.InvariantCulture);
        if (args.TryGetValue("tree-cost", out var cost)) args_treeCost = int.Parse(cost, CultureInfo.InvariantCulture);
    }

    /// <summary>--tree-hp and --tree-cost: tune the woods from the command line.</summary>
    static float args_treeHp = -1;
    static int args_treeCost = -1;

    /// <summary>--woods: forest blocks, woodsmen fell it (WoodsRules.Blocks), whatever rules.json says.</summary>
    static bool args_woods;

    public static Result Play(uint seed, Bot.Style style, bool trace, double snapshotAt = -1, string plan = "fortress", Rules? rules = null, int endlessDays = 0, Action<World>? onEnd = null, ScenarioDef? scenario = null)
    {
        var clock = Stopwatch.StartNew();
        bool endless = endlessDays > 0;
        rules ??= Rules.Default;
        if (args_woods) rules = rules.WithWoods(w => w with { Blocks = true });
        if (args_treeHp > 0) rules = rules.WithWoods(w => w with { TreeHp = args_treeHp });
        if (args_treeCost > 0) rules = rules.WithWoods(w => w with { TreeCost = args_treeCost });
        var world = scenario != null
            ? World.Create(scenario.Options(rules ?? Rules.Default))
            : World.Create(new WorldOptions(seed, 256, 0, rules ?? Rules.Default, Survival: true, Difficulty: args_difficulty, Endless: endless, Map: args_map));
        var bot = new Bot(world, style, plan) { Verbose = trace };
        int possessed = 0;
        int limit = (endless ? endlessDays : world.Rules.Survival.Days + 5) * (int)(world.Rules.Survival.DaySeconds * Balance.TickHz);
        var tickClock = new Stopwatch();
        var tickMs = new List<double>(limit);
        int peakHorde = 0;
        while (world.Outcome == Outcome.Running && world.Tick < limit)
        {
            if (world.Tick % Balance.TickHz == 0) bot.Act();
            tickClock.Restart();
            world.Step();
            tickMs.Add(tickClock.Elapsed.TotalMilliseconds);
            peakHorde = Math.Max(peakHorde, world.Horde.Count);
            var events = world.DrainEvents();
            bot.See(events);
            foreach (var e in events)
            {
                if (e is BuildingPossessed) possessed++;
                if (trace && e is WaveLanded wl) Console.WriteLine($"    day {world.Day,2}: wave {wl.Number} landed ({wl.Spawned}{(wl.Final ? ", CONVERGENCE" : "")})");
                if (trace && e is CorruptionTook ct) Console.WriteLine($"    day {world.Day,2}: CORRUPTION {ct.Name}");
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
        onEnd?.Invoke(world);
        if (args_perf)
        {
            tickMs.Sort();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"    sim ticks: {tickMs.Count}  p50 {tickMs[tickMs.Count / 2]:F2} ms  p99 {tickMs[(int)(tickMs.Count * 0.99)]:F2} ms  max {tickMs[^1]:F2} ms  peak horde {peakHorde}"));
        }
        return new Result(seed, style, world.Outcome, world.Day, world.Buildings.Count, world.Units.Count, world.Stats.DemonsKilled,
            world.Stats.BuildingsLost, possessed, clock.Elapsed.TotalSeconds, style == Bot.Style.Full ? plan : "",
            string.Join(",", world.Tech.Researched), world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Keep)?.Hp ?? 0,
            world.Gates.Count(g => !g.Alive));
    }

    static void Trace(World w)
    {
        var c = w.Colony;
        string Count(BuildingKind k) => w.Buildings.Count(b => b.Kind == k).ToString();
        var food = w.Buildings.Where(b => b.Def.Produces == Resource.Food).ToList();
        string farming = string.Join(",", food.Select(b => $"{(b.Kind == BuildingKind.Farm ? "F" : "H")}{(b.Active ? "" : b.Staffed ? "!" : "?")}{b.Rate * w.Colony.Power:0.00}"));
        Console.WriteLine($"      food producers [{farming}] eaten {c.Colonists * w.Rules.ColonistFoodPerSecond:0.00}/s power {c.Power:0.00}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"    day {w.Day,2}  gold {c[Resource.Gold],5:F0} ({c.NetPerSecond[0]:+0.0;-0.0}) wood {c[Resource.Wood],4:F0} ({c.NetPerSecond[1]:+0.0;-0.0}) stone {c[Resource.Stone],4:F0} ({c.NetPerSecond[2]:+0.0;-0.0}) food {c[Resource.Food],4:F0} ({c.NetPerSecond[3]:+0.00;-0.00}) iron {c[Resource.Iron],4:F0} ({c.NetPerSecond[4]:+0.00;-0.00})  " +
            $"colonists {c.WorkersUsed}/{c.Colonists} sanct {c.SanctityDemand:0}/{c.SanctitySupply:0}  " +
            $"house {Count(BuildingKind.House)} farm {Count(BuildingKind.Farm)} hunt {Count(BuildingKind.Hunter)} wood {Count(BuildingKind.Woodcutter)} quar {Count(BuildingKind.Quarry)} mine {Count(BuildingKind.Mine)} shrine {Count(BuildingKind.Shrine)} ward {Count(BuildingKind.Wardstone)} " +
            $"wall {Count(BuildingKind.Wall)} tower {Count(BuildingKind.Watchtower)} bomb {Count(BuildingKind.Bombard)} units {w.Units.Count}  demons {w.Horde.Count} (asleep {w.Packs.Where(p => !p.Awake).Sum(p => p.Count)} in {w.Packs.Count(p => !p.Awake)} packs)  keep {w.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Keep)?.Hp ?? 0:0}") +
            (w.ForestBlocks ? $"  woodsmen {w.Woodsmen.Count} felled {w.TreesFelled}" : ""));
    }
}
