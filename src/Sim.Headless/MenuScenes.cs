using System.Globalization;
using System.Text.Json;
using Hellwall.Sim;

namespace Hellwall.Headless;

/// <summary>
/// The scenes behind the main menu: the bot plays a run to a chosen moment (a town growing, a
/// wave breaking on the walls, the Convergence), and the world is saved there with where the
/// camera should look. The game loads one, plays it on for 15 to 20 seconds, and cuts to the
/// next. A save loads only under the rules it was made with, so export.sh makes these afresh
/// for every build (the backdrop's rules: the defaults with fog off).
///   hellwall-sim menuscenes --out=game/menu [--only=wave-plains]
/// </summary>
public static class MenuScenes
{
    /// <summary>
    /// What to make: a name, the map, its seed, and the moment: a time, a wave (its number, or 0
    /// for the Convergence) some seconds after it lands, or (Fall) the last half-minute before the
    /// Keep falls, so some scenes are the town losing. At a difficulty: waves are Hard, so the
    /// fights are close; falls are Nightmare, so they come.
    /// </summary>
    sealed record Spec(string Name, MapKind Map, uint Seed, double AtSeconds = 0, int Wave = -1, double AfterLanding = 20, string Plan = "fortress",
        Difficulty Difficulty = Difficulty.Normal, bool Fall = false);

    /// <summary>A falling scene starts this long before the Keep falls: long enough to see the breach spread.</summary>
    const double FallLead = 25;

    static readonly Spec[] Specs =
    [
        new("town-plains", MapKind.Plains, 4711, AtSeconds: 560),
        new("town-lakes", MapKind.Lakes, 812, AtSeconds: 900),
        new("town-wildwood", MapKind.Wildwood, 2024, AtSeconds: 1250, Plan: "pyre"),
        new("wave-plains", MapKind.Plains, 31, Wave: 8, AfterLanding: 22, Difficulty: Difficulty.Hard),
        new("wave-causeway", MapKind.Causeway, 911, Wave: 10, AfterLanding: 25, Plan: "legion", Difficulty: Difficulty.Hard),
        new("wave-gorge", MapKind.Gorge, 505, Wave: 9, AfterLanding: 30, Difficulty: Difficulty.Hard),
        new("wave-twofronts", MapKind.TwoFronts, 912, Wave: 14, AfterLanding: 25, Plan: "pyre", Difficulty: Difficulty.Hard),
        new("convergence-plains", MapKind.Plains, 77, Wave: 0, AfterLanding: 18, Difficulty: Difficulty.Hard),
        new("fall-plains", MapKind.Plains, 606, Plan: "legion", Difficulty: Difficulty.Nightmare, Fall: true),
        new("fall-lakes", MapKind.Lakes, 17, Difficulty: Difficulty.Nightmare, Fall: true),
        new("fall-wildwood", MapKind.Wildwood, 88, Plan: "pyre", Difficulty: Difficulty.Nightmare, Fall: true),
    ];

    public static Rules BackdropRules => Rules.Default.WithFog(f => f with { Enabled = false });

    public static int Run(Dictionary<string, string> args)
    {
        string dir = args.GetValueOrDefault("out", "game/menu");
        Directory.CreateDirectory(dir);
        var chosen = args.TryGetValue("only", out var only) ? Specs.Where(s => s.Name == only).ToArray() : Specs;
        var made = new List<object>();
        // Each scene is its own long run: in parallel.
        var results = new (Spec Spec, object? Entry)[chosen.Length];
        Parallel.For(0, chosen.Length, i => results[i] = (chosen[i], Make(chosen[i], dir)));
        foreach (var (spec, entry) in results)
            if (entry != null) made.Add(entry);
            else Console.WriteLine($"menuscenes: {spec.Name} never reached its moment (the run ended first); left out");
        // The index is rewritten only for a full run, so --only keeps the others.
        if (!args.ContainsKey("only"))
            File.WriteAllText(Path.Combine(dir, "scenes.json"), JsonSerializer.Serialize(new { scenes = made }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"menuscenes: {made.Count} scenes in {dir}");
        return 0;
    }

    static object? Make(Spec spec, string dir)
    {
        var world = World.Create(new WorldOptions(spec.Seed, 256, 0, BackdropRules, Survival: true, Difficulty: spec.Difficulty, Map: spec.Map));
        var bot = new Bot(world, Bot.Style.Full, spec.Plan);
        int? stopAt = spec.AtSeconds > 0 ? (int)(spec.AtSeconds * Balance.TickHz) : null;
        // A fall is only known once it's happened: keep the last minute of snapshots, ten seconds apart.
        var snapshots = new Queue<(int Tick, byte[] Save, (float, float) Focus)>();
        while (world.Outcome == Outcome.Running)
        {
            if (spec.Fall && world.Tick % (10 * Balance.TickHz) == 0)
            {
                world.FlushCommands();
                snapshots.Enqueue((world.Tick, world.Save(), Focus(world)));
                if (snapshots.Count > 7) snapshots.Dequeue();
            }
            if (world.Tick % Balance.TickHz == 0) bot.Act();
            world.Step();
            bot.See(world.DrainEvents());
            if (stopAt is { } t && world.Tick >= t) break;
            if (spec.Wave >= 0 && world.Survival is { } s)
            {
                var wave = spec.Wave == 0 ? s.Waves.LastOrDefault(w => w.Final) : s.Waves.FirstOrDefault(w => w.Number == spec.Wave);
                if (wave is { Landed: true } && world.Tick >= wave.LandsAtTick + spec.AfterLanding * Balance.TickHz) break;
            }
        }
        string file = spec.Name + ".hwsave";
        if (spec.Fall)
        {
            if (world.Outcome != Outcome.Lost || snapshots.Count == 0) return null;
            int fell = world.Tick;
            var (tick, save, (fx, fy)) = snapshots.Where(p => p.Tick <= fell - FallLead * Balance.TickHz).DefaultIfEmpty(snapshots.Peek()).Last();
            File.WriteAllBytes(Path.Combine(dir, file), save);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"menuscenes: {spec.Name}: {(fell - tick) / (double)Balance.TickHz:0} s before the Keep falls on day {fell / (Balance.TickHz * 60) + 1}, looking at {fx:0},{fy:0}"));
            return new { file, x = Math.Round(fx, 1), y = Math.Round(fy, 1), zoom = 0.85, action = true, fall = true };
        }
        if (world.Outcome != Outcome.Running) return null;
        world.FlushCommands();
        File.WriteAllBytes(Path.Combine(dir, file), world.Save());
        var (x, y) = Focus(world);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"menuscenes: {spec.Name}: day {world.Day}, {world.Horde.Count} demons awake, looking at {x:0},{y:0}"));
        return new { file, x = Math.Round(x, 1), y = Math.Round(y, 1), zoom = spec.Wave >= 0 ? 0.85 : 0.95, action = spec.Wave >= 0, fall = false };
    }

    /// <summary>Where to look: between the Keep and the thick of the demons in the open, if there's a fight; the Keep otherwise.</summary>
    static (float X, float Y) Focus(World world)
    {
        var keep = world.Buildings.FirstOrDefault(b => b.Kind == BuildingKind.Keep);
        float kx = keep?.CentreX ?? world.Terrain.Width / 2f, ky = keep?.CentreY ?? world.Terrain.Height / 2f;
        var h = world.Horde;
        if (h.Count < 20) return (kx, ky);
        // The wave's demons nearest the town: the mean of the closest fifth.
        var near = Enumerable.Range(0, h.Count).OrderBy(i => (h.X[i] - kx) * (h.X[i] - kx) + (h.Y[i] - ky) * (h.Y[i] - ky)).Take(Math.Max(20, h.Count / 5)).ToList();
        float hx = near.Average(i => h.X[i]), hy = near.Average(i => h.Y[i]);
        return (kx + (hx - kx) * 0.65f, ky + (hy - ky) * 0.65f);
    }
}
