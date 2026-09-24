namespace Hellwall.Sim;

/// <summary>One change a corruption makes to a demon (Kind null: every demon).</summary>
public sealed record DemonModifier
{
    public DemonKind? Kind { get; init; }
    /// <summary>hp, speed, damage, explodeDamage, broodCount or howlRadius.</summary>
    public string Stat { get; init; } = "";
    public double Mul { get; init; } = 1;
    public double Add { get; init; }
}

/// <summary>
/// A mutation of the horde, drawn in endless mode every few days and kept
/// for the rest of the run. Each is telegraphed a minute before it takes
/// hold. Data, in rules.json: new ones need no code unless they touch a stat
/// the modifiers don't cover.
/// </summary>
public sealed record CorruptionDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public DemonModifier[] Demons { get; init; } = [];
    /// <summary>Kinds that join every wave from now on, at these shares.</summary>
    public WaveMixEntry[] Mix { get; init; } = [];
    /// <summary>Every wave from now on is this many times larger.</summary>
    public double Waves { get; init; } = 1;
    /// <summary>Hellgate bands are this many times larger.</summary>
    public double Gates { get; init; } = 1;
}

internal static class CorruptionSystem
{
    public static void Step(World world)
    {
        var s = world.Survival;
        if (s is not { Endless: true }) return;
        var rules = s.Rules;
        int warn = (int)(rules.CorruptionWarnSeconds * Balance.TickHz);
        if (s.PendingCorruption == null && world.Tick >= s.NextCorruptionTick - warn && world.Rules.Corruptions.Length > 0)
        {
            var c = Draw(world, s);
            s.PendingCorruption = c.Id;
            world.Emit(new CorruptionAnnounced(world.Tick, c.Id, c.Name, c.Description, s.NextCorruptionTick));
        }
        if (s.PendingCorruption is { } id && world.Tick >= s.NextCorruptionTick)
        {
            s.Corruptions.Add(id);
            s.PendingCorruption = null;
            s.NextCorruptionTick += rules.CorruptionEveryDays * s.TicksPerDay;
            Recompute(world);
            var c = world.Rules.Corruption(id);
            world.Emit(new CorruptionTook(world.Tick, c.Id, c.Name));
        }
    }

    // Its own method: a lambda capturing `s` in Step would allocate its closure every tick, taken or not.
    static CorruptionDef Draw(World world, Survival s)
    {
        var pool = world.Rules.Corruptions.Where(c => !s.Corruptions.Contains(c.Id)).ToArray();
        if (pool.Length == 0) pool = world.Rules.Corruptions; // every one taken: they stack from here
        return pool[world.Rng.NextInt(pool.Length)];
    }

    /// <summary>The run's demons: the rules' definitions with every corruption taken, in order.</summary>
    public static void Recompute(World world)
    {
        var demons = (DemonDef[])world.Rules.Demons.Clone();
        if (world.Survival is { } s)
            foreach (var id in s.Corruptions)
                foreach (var m in world.Rules.Corruption(id).Demons)
                    for (int k = 0; k < demons.Length; k++)
                        if (m.Kind == null || m.Kind == (DemonKind)k) demons[k] = Apply(demons[k], m);
        world.Demons = demons;
    }

    static DemonDef Apply(DemonDef d, DemonModifier m)
    {
        float F(float v) => (float)(v * m.Mul + m.Add);
        return m.Stat switch
        {
            "hp" => d with { Hp = F(d.Hp) },
            "speed" => d with { Speed = F(d.Speed) },
            "damage" => d with { Damage = F(d.Damage) },
            "explodeDamage" => d with { ExplodeDamage = F(d.ExplodeDamage) },
            "howlRadius" => d with { HowlRadius = F(d.HowlRadius) },
            "broodCount" => d with { BroodCount = (int)Math.Round(d.BroodCount * m.Mul + m.Add) },
            _ => throw new FormatException($"corruption stat '{m.Stat}' isn't one a demon has"),
        };
    }

    public static double WaveMultiplier(World world) => Product(world, c => c.Waves);

    public static double GateMultiplier(World world) => Product(world, c => c.Gates);

    static double Product(World world, Func<CorruptionDef, double> f)
    {
        double m = 1;
        if (world.Survival is { } s)
            foreach (var id in s.Corruptions) m *= f(world.Rules.Corruption(id));
        return m;
    }

    /// <summary>The wave mix with every corruption's additions after the rules' own.</summary>
    public static WaveMixEntry[] Mix(World world)
    {
        var s = world.Survival!;
        if (s.Corruptions.Count == 0) return s.Rules.Mix;
        return s.Rules.Mix.Concat(s.Corruptions.SelectMany(id => world.Rules.Corruption(id).Mix)).ToArray();
    }
}
