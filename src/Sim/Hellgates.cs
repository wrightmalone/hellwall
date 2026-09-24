namespace Hellwall.Sim;

public sealed record HellgateRules
{
    public int Count { get; init; }
    /// <summary>Tiles from the Keep a gate may stand, at the least.</summary>
    public int MinDistance { get; init; } = 70;
    public float Hp { get; init; } = 4000;
    public float SpawnSeconds { get; init; } = 60;
    /// <summary>Gates are quiet until the end of this day: the opening belongs to building.</summary>
    public int FirstBandDay { get; init; } = 4;
    /// <summary>Demons per band at tier 1; each tier adds this many again.</summary>
    public int BandSize { get; init; } = 6;
    /// <summary>Days at which a gate reaches tier 2 and tier 3.</summary>
    public int[] TierDays { get; init; } = [15, 35];
    /// <summary>
    /// Waves scale with the share of gates still standing, but never below
    /// this fraction: closing every gate makes the end easier, not trivial.
    /// </summary>
    public double WaveFloor { get; init; } = 0.4;
}

/// <summary>
/// A tear in the world the horde comes through. Gates stand far out on the
/// map, send a band at the colony every so often, grow as the days pass,
/// and feed every wave: the fewer that stand, the smaller the waves. This is
/// what makes going out and fighting worth the risk; the dormant packs are a
/// finite pool, the gates are not.
/// </summary>
public sealed class Hellgate
{
    public const int Size = 3;

    public int Id;
    /// <summary>Top-left tile of its 3x3 footprint.</summary>
    public int X;
    public int Y;
    public float Hp;
    public float SpawnTimer;

    public float CentreX => X + Size / 2f;
    public float CentreY => Y + Size / 2f;
    public bool Alive => Hp > 0;
}

internal static class HellgateSystem
{
    public static int Tier(World world)
    {
        int tier = 1, today = world.Day;
        var days = world.Rules.Hellgates.TierDays;
        foreach (int day in days)
            if (today > day) tier++;
        // Endless: past the last tier day, a tier every so many days more.
        if (world.Survival is { Endless: true } s && days.Length > 0 && today > days[^1])
            tier += (today - days[^1]) / Math.Max(1, s.Rules.EndlessGateTierDays);
        return tier;
    }

    public static void Step(World world, float dt)
    {
        if (world.GateList.Count == 0) return;
        var rules = world.Rules.Hellgates;
        if (world.Day <= rules.FirstBandDay) return;
        // The gates pour everything into the Convergence; after it, nothing more comes through.
        if (world.Survival is { FinalLanded: true }) return;
        int band = (int)Math.Round(rules.BandSize * Tier(world) * CorruptionSystem.GateMultiplier(world));
        foreach (var gate in world.GateList)
        {
            if (!gate.Alive) continue;
            gate.SpawnTimer += dt;
            if (gate.SpawnTimer < rules.SpawnSeconds) continue;
            gate.SpawnTimer -= rules.SpawnSeconds;
            var tile = world.FindReachableTileNear((int)gate.CentreX, gate.Y + Hellgate.Size, 4);
            if (tile != null) world.SpawnBand(tile.Value.X, tile.Value.Y, band);
        }
    }

    /// <summary>The factor waves are scaled by: 1 with every gate standing, down to the floor with none.</summary>
    public static double WaveScale(World world)
    {
        var gates = world.Gates;
        if (gates.Count == 0) return 1;
        double alive = gates.Count(g => g.Alive) / (double)gates.Count;
        double floor = world.Rules.Hellgates.WaveFloor;
        return floor + (1 - floor) * alive;
    }
}
