namespace Hellwall.Sim;

/// <summary>
/// Every tunable number lives here, not scattered through systems.
///
/// Phase 0 keeps it as C#. It moves to data files in phase 2, once there are
/// enough numbers to tune; endless mode and horde mutations need it that way.
/// </summary>
public static class Balance
{
    public const int TickHz = 20;
    public const int DefaultMapSize = 128;

    /// <summary>Radius of guaranteed grass around the map centre, where the Keep goes.</summary>
    public const int KeepClearRadius = 12;

    public static (int W, int H) Footprint(BuildingKind kind) => kind switch
    {
        BuildingKind.Keep => (3, 3),
        BuildingKind.House => (2, 2),
        BuildingKind.Wall => (1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    // --- Horde movement ---

    /// <summary>Tiles per second.</summary>
    public static float DemonSpeed(DemonKind kind) => kind switch
    {
        DemonKind.Imp => 2.0f,
        DemonKind.Hound => 3.6f,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Body radius in tiles; two demons closer than twice this push apart.</summary>
    public const float DemonRadius = 0.3f;

    /// <summary>Push speed, in tiles/s, per tile of overlap.</summary>
    public const float SeparationStrength = 6f;

    /// <summary>Cap on neighbours considered for separation, so a packed crowd stays O(n).</summary>
    public const int MaxNeighbours = 10;

    /// <summary>Demons per tile before crowd pressure starts spreading them out.</summary>
    public const int ComfortDensity = 3;

    /// <summary>Hard cap: a tile holding this many demons blocks entry like a wall.</summary>
    public const int MaxDensity = 8;

    /// <summary>Drift speed, in tiles/s, per demon of difference between neighbouring tiles.</summary>
    public const float PressureStrength = 0.6f;

    // --- Flow field ---

    public const int CostStraight = 10;
    public const int CostDiagonal = 14;
    public const int ForestCostMultiplier = 2;

    /// <summary>Flow cost at or below which a demon is in striking distance of a building (adjacent).</summary>
    public const int ArriveDistance = 14;

    // --- Noise and dormant packs ---

    public const int NoiseCellSize = 8;
    public const float NoiseHalfLifeSeconds = 4f;
    public const float WakeThreshold = 1f;

    /// <summary>Placing a building is loud: this wakes packs within ~half the radius.</summary>
    public const float BuildNoiseRadius = 24f;
    public const float BuildNoiseIntensity = 2f;

    public const int PackMinDistanceFromKeep = 40;
    public const int PackMinCount = 80;
    public const int PackMaxCount = 400;
    public const double PackHoundChance = 0.15;

    /// <summary>Spawned demons are scattered over a disc holding about this many per tile.</summary>
    public const float SpawnDensity = 1.2f;
}
