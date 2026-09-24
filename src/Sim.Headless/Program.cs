using System.Diagnostics;
using System.Globalization;
using System.Text;
using Hellwall.Headless;
using Hellwall.Sim;

// Headless tuning harness: replay a command script with no renderer, sample
// the world to CSV, and print the final state hash. Two runs that print the
// same hash played the same game; scripts/verify.sh relies on that.

if (args.Length > 0 && args[0] == "bench") return Bench.Run(ParseArgs(args[1..]));
if (args.Length > 0 && args[0] == "town") return TownProbe.Run(ParseArgs(args[1..]));
if (args.Length > 0 && args[0] == "run") return RunProbe.Run(ParseArgs(args[1..]));
if (args.Length > 0 && args[0] == "paths") return RunProbe.Paths(ParseArgs(args[1..]));
if (args.Length > 0 && args[0] == "maps") return MapReport.Run(ParseArgs(args[1..]));
if (args.Length > 0 && args[0] == "endless") return RunProbe.Endless(ParseArgs(args[1..]));

var args_ = ParseArgs(args);
if (args_.ContainsKey("help"))
{
    Console.WriteLine("""
        hellwall-sim bench [--units=20000 --size=256 --ticks=2400 --gate-ms=12 --seed=7]
          the phase 1 performance gate; exits 1 on failure

        hellwall-sim town [--wave=200 --seconds=240 --seeds=3,7,11,19,42 --trace]
          the phase 2 gate: a walled town holds a wave an undefended one doesn't

        hellwall-sim run [--win-seeds=7,3 --lose-seeds=7,3,11,19,42 --trace --snapshot-at=<sec>]
          the phase 3 gate: a bot wins a full survival run; a passive one loses
        hellwall-sim run --seeds=11,19 [--bot=full|passive --plan=fortress|pyre|legion --difficulty=normal]
          play the bot on other maps, ungated

        hellwall-sim paths [--seeds=7 --trace --difficulty=normal]
          the phase 4 gate: fortress, pyre and legion research plans all win

        hellwall-sim endless [--seeds=3,5,7 --plan=fortress --difficulty=normal --max-days=200 --trace]
          endless mode: how long the bot lasts on each seed, and what the horde became

        hellwall-sim maps [--seeds=3,11 --size=256]
          what each start offers within reach

        hellwall-sim [options]
          --seed=<n>        world seed (default 7)
          --ticks=<n>       ticks to run (default 1200 = 60s)
          --size=<n>        map size in tiles (default 128)
          --packs=<n>       dormant packs to scatter (default 0)
          --script=<path>   tick-stamped command script (JSON)
          --out=<path>      write samples as CSV
          --sample=<sec>    sample interval in seconds (default 10)
          --events          print every sim event
        """);
    return 0;
}

uint seed = uint.Parse(args_.GetValueOrDefault("seed", "7"), CultureInfo.InvariantCulture);
int ticks = int.Parse(args_.GetValueOrDefault("ticks", "1200"), CultureInfo.InvariantCulture);
int size = int.Parse(args_.GetValueOrDefault("size", Balance.DefaultMapSize.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
double sampleSeconds = double.Parse(args_.GetValueOrDefault("sample", "10"), CultureInfo.InvariantCulture);
bool printEvents = args_.ContainsKey("events");

var script = args_.TryGetValue("script", out var scriptPath)
    ? Script.Load(scriptPath)
    : ("none", new List<ScriptedCommand>());

int packs = int.Parse(args_.GetValueOrDefault("packs", "0"), CultureInfo.InvariantCulture);
var world = World.Create(new WorldOptions(seed, size, packs));
int sampleEvery = Math.Max(1, (int)Math.Round(sampleSeconds * Balance.TickHz));
var csv = new StringBuilder("tick,seconds,buildings,houses,walls,demons,packs_asleep,rejected,hash\n");
int next = 0;
int rejected = 0;
int eventCount = 0;
var clock = Stopwatch.StartNew();

for (int t = 0; t <= ticks; t++)
{
    // Commands stamped for tick t are applied at the top of step t.
    while (next < script.Item2.Count && script.Item2[next].Tick <= world.Tick)
        world.Enqueue(script.Item2[next++].Command);

    if (t % sampleEvery == 0 || t == ticks) Sample();
    if (t == ticks) break;
    world.Step();

    foreach (var e in world.DrainEvents())
    {
        eventCount++;
        if (e is CommandRejected) rejected++;
        if (printEvents) Console.WriteLine(e);
    }
}

double ms = clock.Elapsed.TotalMilliseconds;
if (args_.TryGetValue("out", out var outPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    File.WriteAllText(outPath, csv.ToString());
}

Console.WriteLine($"script={script.Item1} seed={seed} ticks={world.Tick} buildings={world.Buildings.Count} demons={world.Horde.Count} events={eventCount} rejected={rejected}");
Console.WriteLine($"sim_ms={ms.ToString("F1", CultureInfo.InvariantCulture)} ms_per_tick={(ms / Math.Max(1, world.Tick)).ToString("F4", CultureInfo.InvariantCulture)}");
Console.WriteLine($"hash={StateHash.Hex(world)}");
return 0;

void Sample()
{
    int houses = world.Buildings.Count(b => b.Kind == BuildingKind.House);
    int walls = world.Buildings.Count(b => b.Kind == BuildingKind.Wall);
    double seconds = world.Tick / (double)Balance.TickHz;
    int asleep = world.Packs.Count(p => !p.Awake);
    csv.Append(CultureInfo.InvariantCulture,
        $"{world.Tick},{seconds:F1},{world.Buildings.Count},{houses},{walls},{world.Horde.Count},{asleep},{rejected},{StateHash.Hex(world)}\n");
}

static Dictionary<string, string> ParseArgs(string[] argv)
{
    var result = new Dictionary<string, string>();
    foreach (var a in argv)
    {
        if (!a.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"unexpected argument '{a}'");
        var body = a[2..];
        int eq = body.IndexOf('=');
        if (eq < 0) result[body] = "true";
        else result[body[..eq]] = body[(eq + 1)..];
    }
    return result;
}
