namespace Hellwall.Sim;

/// <summary>The survival mode's clock and wave schedule, from rules.json.</summary>
public sealed record SurvivalRules
{
    public float DaySeconds { get; init; } = 60;
    /// <summary>Colonists at which a patron saint offers a blessing (three to choose from, one each).</summary>
    public int[] PatronMilestones { get; init; } = [];

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
    /// The share of the Convergence that comes from its main side (the rest split over the
    /// others), and how long ahead that's known: a chokepoint to prepare, not a flood from
    /// everywhere. 0: an even split.
    /// </summary>
    public double ConvergenceLean { get; init; }
    public float ConvergenceWarnSeconds { get; init; } = 60;

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

    // Endless mode: no Convergence; waves go on, capped, with a Surge every so
    // often, and the horde takes a corruption every few days.

    /// <summary>No regular wave grows past this (the engine's budget is about 20,000 on the map).</summary>
    public int EndlessMaxWave { get; init; } = 5000;
    /// <summary>Every this many waves, a Surge: SurgeScale times the size, from every side.</summary>
    public int SurgeEveryWaves { get; init; } = 6;
    public double SurgeScale { get; init; } = 2.5;
    public int FirstCorruptionDay { get; init; } = 12;
    public int CorruptionEveryDays { get; init; } = 8;
    public float CorruptionWarnSeconds { get; init; } = 60;
    /// <summary>After the rules' last TierDay, Hellgates grow a tier every this many days.</summary>
    public int EndlessGateTierDays { get; init; } = 20;
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
    /// <summary>Endless mode's periodic swell: bigger, and from every side.</summary>
    public bool Surge;

    /// <summary>Chosen when the wave is announced, not before.</summary>
    public Side[] Sides = [];
    public bool Announced;
    public bool Landed;

    public int AnnounceTick(SurvivalRules rules) => LandsAtTick - (int)((Final ? Math.Max(rules.TelegraphSeconds, rules.ConvergenceWarnSeconds) : rules.TelegraphSeconds) * Balance.TickHz);

    /// <summary>How many come from Sides[i]: the Convergence leans on its main side (Sides[0]), any other wave splits evenly.</summary>
    public int ShareOf(int i, SurvivalRules rules)
    {
        int n = Sides.Length;
        double lean = Final && n > 1 ? rules.ConvergenceLean : 0;
        if (lean <= 0) return Size / n + (i < Size % n ? 1 : 0);
        int main = (int)Math.Round(Size * lean), rest = Size - main;
        return i == 0 ? main : rest / (n - 1) + (i - 1 < rest % (n - 1) ? 1 : 0);
    }
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
    /// <summary>No Convergence and no win: the run goes on until the Keep falls, and the score is the day.</summary>
    public readonly bool Endless;
    public readonly List<PlannedWave> Waves = new();
    public bool FinalLanded;
    public int FinalLandedTick;
    /// <summary>The Convergence has broken on the walls: no demon left, or the Keep still standing long after it landed. The Survive goal.</summary>
    public bool ConvergenceSpent;

    /// <summary>Endless: corruption ids taken so far, in order.</summary>
    public readonly List<string> Corruptions = new();
    /// <summary>Endless: the corruption announced and about to take hold, if any.</summary>
    public string? PendingCorruption;
    public int NextCorruptionTick;

    public Survival(SurvivalRules rules, bool endless = false)
    {
        Rules = rules;
        Endless = endless;
        int n = 1;
        for (int day = rules.FirstWaveDay; day < rules.Days; day += rules.WaveEveryDays, n++)
            Waves.Add(Plan(n));
        if (!endless)
            Waves.Add(new PlannedWave { Number = n, LandsAtTick = rules.Days * TicksPerDay, Size = rules.ConvergenceSize, Final = true });
        NextCorruptionTick = rules.FirstCorruptionDay * TicksPerDay;
    }

    PlannedWave Plan(int n)
    {
        int size = (int)Math.Round(Rules.FirstWaveSize * Math.Pow(Rules.WaveGrowth, n - 1));
        bool surge = Endless && Rules.SurgeEveryWaves > 0 && n % Rules.SurgeEveryWaves == 0;
        if (Endless) size = Math.Min(size, Rules.EndlessMaxWave);
        if (surge) size = (int)Math.Round(size * Rules.SurgeScale);
        return new PlannedWave { Number = n, LandsAtTick = (Rules.FirstWaveDay + (n - 1) * Rules.WaveEveryDays) * TicksPerDay, Size = size, Surge = surge };
    }

    /// <summary>Endless: keep a couple of waves planned ahead of the clock.</summary>
    internal void Extend(int count)
    {
        while (Waves.Count < count) Waves.Add(Plan(Waves.Count + 1));
    }

    internal void ExtendAhead()
    {
        if (!Endless) return;
        // Landed waves are a prefix of the list, so the unlanded ones are the tail. No LINQ: this runs every tick.
        int ahead = 0;
        for (int i = Waves.Count - 1; i >= 0 && !Waves[i].Landed; i--) ahead++;
        for (; ahead < 2; ahead++) Waves.Add(Plan(Waves.Count + 1));
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
        s.ExtendAhead();

        foreach (var wave in s.Waves)
        {
            if (wave.Landed) continue;
            if (!wave.Announced && world.Tick >= wave.AnnounceTick(s.Rules))
            {
                wave.Sides = wave.Final ? MainSideFirst(world) : wave.Surge ? Enum.GetValues<Side>() : DrawSides(world, SidesFor(s.Rules, wave.Number));
                // Corruptions that swell the tide apply from the announcement, so the size shown is the size that comes.
                wave.Size = (int)Math.Round(wave.Size * CorruptionSystem.WaveMultiplier(world));
                wave.Announced = true;
                world.Emit(new WaveAnnounced(world.Tick, wave.Number, wave.LandsAtTick, wave.Sides, wave.Size, wave.Final));
            }
            if (wave.Announced && world.Tick >= wave.LandsAtTick)
            {
                // Waves are fed by the Hellgates: fewer standing, smaller waves.
                wave.Size = (int)Math.Round(wave.Size * HellgateSystem.WaveScale(world));
                int spawned = Land(world, wave, s.Endless ? CorruptionSystem.Mix(world) : s.Rules.Mix);
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
            s.ConvergenceSpent = true; // the objectives decide whether that wins
    }

    static int SidesFor(SurvivalRules rules, int number) =>
        Math.Min(rules.MaxSides, 1 + (number - 1) / Math.Max(1, rules.WavesPerExtraSide));

    /// <summary>Every side, the one most of them come from first.</summary>
    static Side[] MainSideFirst(World world)
    {
        var sides = Enum.GetValues<Side>().ToList();
        var main = sides[world.Rng.NextInt(sides.Count)];
        sides.Remove(main);
        return [main, .. sides];
    }

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
            int share = wave.ShareOf(i, world.Survival!.Rules);
            world.SpawnColumn = Horde.ColumnOf(wave.Number, wave.Sides[i]);
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
        world.SpawnColumn = 0;
        return spawned;
    }
}
