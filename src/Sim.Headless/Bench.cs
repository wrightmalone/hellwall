using System.Diagnostics;
using System.Globalization;
using Hellwall.Sim;

namespace Hellwall.Headless;

/// <summary>
/// The phase 1 gate: a Keep inside a wall ring with two 3-tile gaps, on a 256
/// map, and 20k demons streaming in from eight edge points. Every tick is
/// timed, and the behaviour the timing is supposed to cover is checked too.
///
/// Gates (exit 1 on any failure):
///   perf      p95 tick time within --gate-ms (default 12)
///   flow      a warm full flow-field rebuild within --flow-gate-ms (default 5),
///             since a rebuild happens inside a tick whenever a building changes
///   walls     no demon ever inside a wall, building, rock or water
///   crowding  no tile ever holds more than --max-density demons (default 12)
///   stuck     at most --max-stuck (default 1%) of demons made no progress
///             over the final 30s while the tile their flow points into had
///             room. Waiting behind a full tile is a queue, which is correct;
///             pressing against rock with open ground ahead is a bug.
///
/// "Arrived" can't be a gate: two 3-tile gaps can't admit 20k demons in any
/// sensible time, and a queue at the gate is the correct behaviour.
/// </summary>
public static class Bench
{
    /// <summary>Path cost within which a demon counts as at the colony: in the crowd around it, not queueing for it.</summary>
    const int AtColony = 400;

    const int StallWindowTicks = 30 * Balance.TickHz;

    public static int Run(Dictionary<string, string> args)
    {
        uint seed = uint.Parse(args.GetValueOrDefault("seed", "7"), CultureInfo.InvariantCulture);
        int units = int.Parse(args.GetValueOrDefault("units", "20000"), CultureInfo.InvariantCulture);
        int size = int.Parse(args.GetValueOrDefault("size", "256"), CultureInfo.InvariantCulture);
        int ticks = int.Parse(args.GetValueOrDefault("ticks", "2400"), CultureInfo.InvariantCulture);
        double gateMs = double.Parse(args.GetValueOrDefault("gate-ms", "12"), CultureInfo.InvariantCulture);
        double flowGateMs = double.Parse(args.GetValueOrDefault("flow-gate-ms", "5"), CultureInfo.InvariantCulture);
        int maxDensity = int.Parse(args.GetValueOrDefault("max-density", "12"), CultureInfo.InvariantCulture);
        double maxStuck = double.Parse(args.GetValueOrDefault("max-stuck", "0.01"), CultureInfo.InvariantCulture);
        bool trace = args.ContainsKey("trace");
        if (ticks <= StallWindowTicks) throw new ArgumentException($"--ticks must exceed the {StallWindowTicks}-tick stall window");

        var world = World.Create(new WorldOptions(seed, size));
        foreach (var c in Scenarios.WallRing(world, Scenarios.BenchRingRadius, Side.East, Side.West)) world.Enqueue(c);
        world.Step();
        // A rejected wall is a hole in the ring, and the horde would stream through it
        // instead of the gaps, so the bench would no longer measure what it claims.
        var rejected = world.DrainEvents().OfType<CommandRejected>().ToList();
        if (rejected.Count > 0)
        {
            foreach (var r in rejected) Console.WriteLine($"setup rejected: {r}");
            Console.WriteLine("FAIL: scenario setup was rejected");
            return 1;
        }

        double flowMs = TimeFlowRebuild(world);

        foreach (var c in Scenarios.EdgeAssault(world, units, points: 8)) world.Enqueue(c);
        world.FlushCommands();
        int spawned = world.Horde.Count;
        world.DrainEvents();

        var samples = new double[ticks];
        var perTile = new int[size * size];
        int intrusions = 0;
        int densest = 0;
        int[]? distAtWindowStart = null;
        int gcBefore = GC.CollectionCount(0);
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var clock = new Stopwatch();

        for (int t = 0; t < ticks; t++)
        {
            if (t == ticks - StallWindowTicks) distAtWindowStart = Distances(world);

            clock.Restart();
            world.Step();
            samples[t] = clock.Elapsed.TotalMilliseconds;

            intrusions += CountIntrusions(world);
            if (t % 20 == 0) densest = Math.Max(densest, Densest(world, perTile));
            if (trace && (t + 1) % 200 == 0) Trace(world, t + 1, samples.AsSpan(t - 199, 200), perTile);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        int gcs = GC.CollectionCount(0) - gcBefore;

        var end = Distances(world);
        Densest(world, perTile);
        int atColony = 0, queued = 0, stuck = 0;
        var h = world.Horde;
        for (int i = 0; i < end.Length; i++)
        {
            if (end[i] <= AtColony) { atColony++; continue; }
            if (end[i] < distAtWindowStart![i]) continue;
            if (HasRoomAhead(world, perTile, h.X[i], h.Y[i])) stuck++;
            else queued++;
        }
        double stuckFraction = stuck / (double)Math.Max(1, spawned);

        if (args.ContainsKey("histogram")) PrintHistogram(world);
        if (args.TryGetValue("snapshot", out var snapshotPath)) Snapshot.Write(world, snapshotPath);

        var sorted = samples.OrderBy(s => s).ToArray();
        double p95 = sorted[(int)(0.95 * (ticks - 1))];

        string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
        Console.WriteLine($"bench seed={seed} size={size} spawned={spawned} ticks={ticks} ({ticks / Balance.TickHz}s sim)");
        Console.WriteLine($"tick_ms   mean={F(samples.Average())} p50={F(sorted[(ticks - 1) / 2])} p95={F(p95)} p99={F(sorted[(int)(0.99 * (ticks - 1))])} max={F(sorted[^1])}   gate p95<={F(gateMs)}");
        Console.WriteLine($"flow_ms   {F(flowMs)} warm full rebuild   gate <={F(flowGateMs)}");
        Console.WriteLine($"crowding  densest tile {densest} demons   gate <={maxDensity}");
        Console.WriteLine($"stuck     {stuck} ({F(stuckFraction * 100)}%) no progress in 30s with room ahead   gate <={F(maxStuck * 100)}%");
        Console.WriteLine($"queued    {queued} no progress in 30s, waiting behind a full tile (informational)");
        Console.WriteLine($"walls     {intrusions} demon-ticks inside blocked tiles   gate 0");
        Console.WriteLine($"at colony {atColony}/{spawned} within {AtColony / 10} path-tiles (informational)");
        Console.WriteLine($"gc        {gcs} gen0 collections, {allocated / 1024} KiB allocated during {ticks} ticks (informational)");
        Console.WriteLine($"hash={StateHash.Hex(world)}");

        bool ok = true;
        void Gate(bool pass, string what) { if (!pass) { Console.WriteLine($"FAIL: {what}"); ok = false; } }
        Gate(p95 <= gateMs, "tick p95 over budget");
        Gate(flowMs <= flowGateMs, "flow rebuild over budget");
        Gate(densest <= maxDensity, "crowd piled up");
        Gate(stuckFraction <= maxStuck, "demons stuck with room to move");
        Gate(intrusions == 0, "demons inside blocked tiles");
        return ok ? 0 : 1;
    }

    /// <summary>Best of several warm rebuilds: the first includes JIT, which a player pays once, not per wall.</summary>
    static double TimeFlowRebuild(World world)
    {
        double best = double.MaxValue;
        var clock = new Stopwatch();
        for (int i = 0; i < 6; i++)
        {
            clock.Restart();
            world.Flow.Build(world);
            best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
        }
        return best;
    }

    /// <summary>The tile this demon's flow direction points into is walkable and under the density cap.</summary>
    static bool HasRoomAhead(World world, int[] perTile, float x, float y)
    {
        var (fx, fy, _) = world.Flow.Sample(x, y);
        if (fx == 0 && fy == 0) return false;
        int ax = (int)(x + MathF.Sign(fx) * (MathF.Abs(fx) > 0.38f ? 1 : 0));
        int ay = (int)(y + MathF.Sign(fy) * (MathF.Abs(fy) > 0.38f ? 1 : 0));
        return world.IsWalkable(ax, ay) && perTile[ay * world.Terrain.Width + ax] < Balance.MaxDensity;
    }

    static int[] Distances(World world)
    {
        var h = world.Horde;
        var d = new int[h.Count];
        for (int i = 0; i < h.Count; i++) d[i] = world.Flow.DistAt((int)h.X[i], (int)h.Y[i]);
        return d;
    }

    static int Densest(World world, int[] perTile)
    {
        Array.Clear(perTile);
        var h = world.Horde;
        int densest = 0;
        for (int i = 0; i < h.Count; i++)
            densest = Math.Max(densest, ++perTile[(int)h.Y[i] * world.Terrain.Width + (int)h.X[i]]);
        return densest;
    }

    static void Trace(World world, int tick, ReadOnlySpan<double> window, int[] perTile)
    {
        int atColony = 0;
        var h = world.Horde;
        for (int i = 0; i < h.Count; i++)
            if (world.Flow.DistAt((int)h.X[i], (int)h.Y[i]) <= AtColony) atColony++;
        double mean = 0, max = 0;
        foreach (var v in window) { mean += v; max = Math.Max(max, v); }
        Console.WriteLine(FormattableString.Invariant(
            $"  t={tick / Balance.TickHz,4}s  tick_ms mean={mean / window.Length:F2} max={max:F2}  densest={Densest(world, perTile)}  at_colony={atColony}"));
    }

    /// <summary>Where the horde ended up, by path distance to the nearest building, and how many are standing still.</summary>
    static void PrintHistogram(World world)
    {
        var h = world.Horde;
        var buckets = new SortedDictionary<int, (int All, int Still)>();
        for (int i = 0; i < h.Count; i++)
        {
            int d = world.Flow.DistAt((int)h.X[i], (int)h.Y[i]);
            int b = d == FlowField.Unreachable ? -1 : d / 200;
            bool still = h.VX[i] * h.VX[i] + h.VY[i] * h.VY[i] < 0.05f;
            var (all, st) = buckets.GetValueOrDefault(b);
            buckets[b] = (all + 1, st + (still ? 1 : 0));
        }
        foreach (var (b, (all, st)) in buckets)
            Console.WriteLine($"  {(b < 0 ? "unreachable" : $"{b * 20,3}-{b * 20 + 20,3} tiles")}: {all,6}  still {st,6}");
    }

    static int CountIntrusions(World world)
    {
        int n = 0;
        var h = world.Horde;
        for (int i = 0; i < h.Count; i++)
            if (!world.IsWalkable((int)h.X[i], (int)h.Y[i])) n++;
        return n;
    }
}
