namespace Hellwall.Sim;

public enum BuildingKind : byte
{
    Keep,
    House,
    Woodcutter,
    Quarry,
    Hunter,
    Shrine,
    Wardstone,
    Wall,
    Gate,
    Watchtower,
    Bombard,
    Barracks,
}

public sealed class Building
{
    public int Id;
    public BuildingKind Kind;
    public BuildingDef Def = null!;

    /// <summary>Top-left tile of the footprint.</summary>
    public int X;
    public int Y;
    public int W;
    public int H;

    public float Hp;

    /// <summary>Seconds of construction done; complete once it reaches Def.BuildSeconds.</summary>
    public float Built;
    public bool Complete;

    /// <summary>Its whole footprint is on consecrated ground connected to the Keep.</summary>
    public bool OnGround;

    /// <summary>Has its full crew. Buildings are idle, not slower, when short-staffed.</summary>
    public bool Staffed;

    /// <summary>Gatherers: resource per second at full sanctity, from the tiles it has claimed.</summary>
    public double Rate;

    /// <summary>Seconds until the weapon can fire again.</summary>
    public float Cooldown;

    /// <summary>Barracks: units waiting to be trained, front first, and progress on the front one.</summary>
    public readonly List<UnitKind> Queue = new();
    public float TrainProgress;

    public float CentreX => X + W / 2f;
    public float CentreY => Y + H / 2f;

    public bool NeedsCrew => Def.Workers > 0;

    /// <summary>Doing its job: built, on holy ground, and crewed if it needs a crew.</summary>
    public bool Active => Complete && OnGround && (!NeedsCrew || Staffed);

    public bool IsWallLike => Kind is BuildingKind.Wall or BuildingKind.Gate;
}
