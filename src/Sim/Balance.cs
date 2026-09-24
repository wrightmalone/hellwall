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


    // --- Horde movement (content numbers such as speed live in rules.json) ---

    /// <summary>Body radius in tiles; two demons closer than twice this push apart.</summary>
    public const float DemonRadius = 0.3f;

    /// <summary>How close, in tiles, a demon's centre must be to a building's footprint to hit it: its body is against the wall.</summary>
    public const float DemonReach = 0.45f;

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

    /// <summary>
    /// For demons, stepping through a wall or gate costs this many ordinary
    /// steps: the horde walks around a wall when a way round is shorter than
    /// ~30 tiles, and breaks through it otherwise.
    /// </summary>
    public const int WallCostMultiplier = 30;

    /// <summary>Flow cost at or below which a demon is in striking distance of a building (adjacent).</summary>
    public const int ArriveDistance = 14;

    // --- Noise and dormant packs ---

    public const int NoiseCellSize = 8;
    public const float NoiseHalfLifeSeconds = 4f;
    public const float WakeThreshold = 1f;

    /// <summary>
    /// Placing a building makes some noise: it wakes packs within about half
    /// this radius. Kept below the wilds' clear radius, since nothing can be
    /// built that close to a pack anyway: building never wakes the wilds;
    /// fighting (shots carry much further) does.
    /// </summary>
    /// <summary>Soldiers a Barracks can have waiting, the one in training included.</summary>
    public const int QueueLimit = 8;

    public const float BuildNoiseRadius = 10f;
    public const float BuildNoiseIntensity = 2f;


    /// <summary>Noise level of a shot at its source (radius comes from the weapon): enough to wake packs near it.</summary>
    public const float CombatNoiseIntensity = 2f;

    /// <summary>Spawned demons are scattered over a disc holding about this many per tile.</summary>
    public const float SpawnDensity = 1.2f;
}
