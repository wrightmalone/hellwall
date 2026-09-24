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
}
