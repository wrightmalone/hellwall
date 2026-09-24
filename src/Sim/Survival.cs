namespace Hellwall.Sim;

/// <summary>The survival mode's clock and wave schedule, from rules.json.</summary>
public sealed record SurvivalRules
{
    public float DaySeconds { get; init; } = 60;

    /// <summary>The Convergence lands at the end of this day; survive it to win.</summary>
    public int Days { get; init; } = 60;

    /// <summary>The first wave lands at the end of this day.</summary>
    public int FirstWaveDay { get; init; } = 3;
    public int WaveEveryDays { get; init; } = 3;

    /// <summary>A wave is announced, with its size and sides, this long before it lands.</summary>
    public float TelegraphSeconds { get; init; } = 60;

    public int FirstWaveSize { get; init; } = 30;

    /// <summary>Each wave is this many times the size of the one before.</summary>
    public double WaveGrowth { get; init; } = 1.2;

    /// <summary>Every this many waves, waves come from one more side, up to MaxSides.</summary>
    public int WavesPerExtraSide { get; init; } = 5;
    public int MaxSides { get; init; } = 3;

    /// <summary>The final wave, from every side at once.</summary>
    public int ConvergenceSize { get; init; } = 4000;

    /// <summary>
    /// After the Convergence lands, the run is won when no demon is left, or
    /// when the Keep still stands this long after: the Convergence has broken
    /// on the walls. (A few stragglers stuck out on the map shouldn't hold a
    /// won run hostage.)
    /// </summary>
    public float ConvergenceHoldSeconds { get; init; } = 240;

    /// <summary>
    /// What each wave is made of, beyond Imps: a kind joins from a given wave
    /// number and takes that share of every wave after. Whatever's left over
    /// is Imps.
    /// </summary>
    public WaveMixEntry[] Mix { get; init; } = [new() { Kind = DemonKind.Hound, FromWave = 1, Share = 0.2 }];
}

public sealed record WaveMixEntry
{
    public DemonKind Kind { get; init; }
    public int FromWave { get; init; } = 1;
    public double Share { get; init; }
}

public sealed class PlannedWave
{
    public int Number;
    public int LandsAtTick;
    public int Size;
    public bool Final;

    /// <summary>Chosen when the wave is announced, not before.</summary>
    public Side[] Sides = [];
    public bool Announced;
    public bool Landed;

    public int AnnounceTick(SurvivalRules rules) => LandsAtTick - (int)(rules.TelegraphSeconds * Balance.TickHz);
}

/// <summary>
/// Survival state: the planned waves and where the run stands. The schedule
/// (when and how big) is fixed at creation; each wave's sides are drawn from
/// the world's Rng when it's announced, so the player learns the direction a
/// minute ahead and not before.
/// </summary>
public sealed class Survival
{
    public readonly SurvivalRules Rules;
    public readonly List<PlannedWave> Waves = new();
    public bool FinalLanded;
    public int FinalLandedTick;

    public Survival(SurvivalRules rules)
    {
        Rules = rules;
        int ticksPerDay = (int)(rules.DaySeconds * Balance.TickHz);
        int n = 1;
        for (int day = rules.FirstWaveDay; day < rules.Days; day += rules.WaveEveryDays, n++)
        {
            Waves.Add(new PlannedWave
            {
                Number = n,
                LandsAtTick = day * ticksPerDay,
                Size = (int)Math.Round(rules.FirstWaveSize * Math.Pow(rules.WaveGrowth, n - 1)),
            });
        }
        Waves.Add(new PlannedWave { Number = n, LandsAtTick = rules.Days * ticksPerDay, Size = rules.ConvergenceSize, Final = true });
    }

    public int TicksPerDay => (int)(Rules.DaySeconds * Balance.TickHz);

    /// <summary>1-based day number at a tick.</summary>
    public int DayAt(int tick) => tick / TicksPerDay + 1;

    /// <summary>The next wave that hasn't landed, announced or not.</summary>
    public PlannedWave? Next => Waves.FirstOrDefault(w => !w.Landed);
}

internal static class SurvivalSystem
{
    public static void Step(World world)
    {
        var s = world.Survival;
        if (s == null) return;

        foreach (var wave in s.Waves)
        {
            if (wave.Landed) continue;
            if (!wave.Announced && world.Tick >= wave.AnnounceTick(s.Rules))
            {
                wave.Sides = wave.Final ? Enum.GetValues<Side>() : DrawSides(world, SidesFor(s.Rules, wave.Number));
                wave.Announced = true;
                world.Emit(new WaveAnnounced(world.Tick, wave.Number, wave.LandsAtTick, wave.Sides, wave.Size, wave.Final));
            }
            if (wave.Announced && world.Tick >= wave.LandsAtTick)
            {
                // Waves are fed by the Hellgates: fewer standing, smaller waves.
                wave.Size = (int)Math.Round(wave.Size * HellgateSystem.WaveScale(world));
                int spawned = Land(world, wave, s.Rules.Mix);
                wave.Landed = true;
                if (wave.Final)
                {
                    s.FinalLanded = true;
                    s.FinalLandedTick = world.Tick;
                }
                world.Emit(new WaveLanded(world.Tick, wave.Number, spawned, wave.Final));
            }
        }

        if (s.FinalLanded && (world.Horde.Count == 0 || world.Tick - s.FinalLandedTick >= s.Rules.ConvergenceHoldSeconds * Balance.TickHz))
            world.Win();
    }

    static int SidesFor(SurvivalRules rules, int number) =>
        Math.Min(rules.MaxSides, 1 + (number - 1) / Math.Max(1, rules.WavesPerExtraSide));

    static Side[] DrawSides(World world, int count)
    {
        var sides = Enum.GetValues<Side>().ToList();
        var chosen = new List<Side>();
        for (int i = 0; i < count && sides.Count > 0; i++)
        {
            int k = world.Rng.NextInt(sides.Count);
            chosen.Add(sides[k]);
            sides.RemoveAt(k);
        }
        chosen.Sort();
        return chosen.ToArray();
    }

    static int Land(World world, PlannedWave wave, WaveMixEntry[] mix)
    {
        int spawned = 0;
        for (int i = 0; i < wave.Sides.Length; i++)
        {
            int share = wave.Size / wave.Sides.Length + (i < wave.Size % wave.Sides.Length ? 1 : 0);
            int imps = share;
            foreach (var m in mix)
            {
                if (wave.Number < m.FromWave) continue;
                int n = Math.Min(imps, (int)Math.Round(share * m.Share));
                spawned += world.SpawnAtEdge(wave.Sides[i], m.Kind, n);
                imps -= n;
            }
            spawned += world.SpawnAtEdge(wave.Sides[i], DemonKind.Imp, imps);
        }
        return spawned;
    }
}
