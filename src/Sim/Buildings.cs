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
    Farm,
    StoneWall,
    LanceTower,
    Scriptorium,
    Mine,
    /// <summary>No weapon: its bells slow every demon in earshot.</summary>
    Belfry,
    /// <summary>Anti-air: long range, shoots only fliers.</summary>
    Skyspire,
    /// <summary>Short range, rapid, quiet: burns whatever is at the wall.</summary>
    Censer,
    /// <summary>Works silver veins, which lie only near the map's edge.</summary>
    SilverMine,
    /// <summary>Food from the water around it: makes a lakeshore worth building on.</summary>
    Fishery,
    /// <summary>A House rebuilt in stone: twice the room on the same ground. Only by upgrading a House.</summary>
    Cottage,
    /// <summary>The third tier: a Cottage raised again, after Masonry. Only by upgrading a Cottage.</summary>
    Manor,
    /// <summary>A gate in stone: a stone wall your people can walk through. After Masonry.</summary>
    StoneGate,
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
    /// <summary>Put on hold by the player: takes no crew (they go to other work), does nothing; its woodsmen stay home.</summary>
    public bool Paused;
    /// <summary>A crew building (woodsmen, miners) whose crew found nothing left to work within reach: worked out.</summary>
    public bool Exhausted;
    /// <summary>Seconds its crew stay fled from demons that came too close with no wall between (FlightSystem); nothing gathered meanwhile.</summary>
    public float FleeTimer;
    public bool Fleeing => FleeTimer > 0;

    /// <summary>Gatherers: resource per second at full sanctity, from the tiles it has claimed.</summary>
    public double Rate;

    /// <summary>Seconds until the weapon can fire again.</summary>
    public float Cooldown;

    /// <summary>Barracks: units waiting to be trained, front first, and progress on the front one.</summary>
    public readonly List<UnitKind> Queue = new();
    public float TrainProgress;

    /// <summary>Barracks: where new soldiers attack-move to once they're out (RallyX -1: nowhere, they wait by the door).</summary>
    public int RallyX = -1;
    public int RallyY;

    /// <summary>A lodge whose woodsmen fell trees: wood delivered in the current window, and how far into it.</summary>
    public float WoodWindow;
    public float WoodTimer;

    /// <summary>Repair: health as of last tick, seconds since it last fell, and whether it mended this tick (for the client).</summary>
    public float WatchedHp;
    public float Calm;
    public bool Repairing;

    /// <summary>Being rebuilt as its Def.UpgradesTo (it works as it was meanwhile), and for how long so far.</summary>
    public bool Upgrading;
    public float UpgradeProgress;

    /// <summary>Scriptorium: the tech being researched here, if any, and seconds of work done on it.</summary>
    public string? Researching;
    public float ResearchProgress;

    /// <summary>
    /// Taken by the horde: its occupants are turning, one every
    /// possessionSpawnSeconds, and it falls when the last is out. It houses
    /// nobody, works for nobody, and is no longer a demon target.
    /// </summary>
    public bool Possessed;
    public int Occupants;
    public float PossessTimer;

    public float CentreX => X + W / 2f;
    public float CentreY => Y + H / 2f;

    public bool NeedsCrew => Def.Workers > 0;

    /// <summary>Doing its job: built, on holy ground, and crewed if it needs a crew.</summary>
    public bool Active => Complete && OnGround && !Possessed && (!NeedsCrew || Staffed);

    public bool IsWallLike => Kind is BuildingKind.Wall or BuildingKind.Gate or BuildingKind.StoneWall or BuildingKind.StoneGate;
    public bool IsGate => Kind is BuildingKind.Gate or BuildingKind.StoneGate;

    /// <summary>What the horde paths to: anything built except walls and gates (which it paths through) and what it already holds.</summary>
    public bool IsDemonTarget => !IsWallLike && !Possessed;

    /// <summary>
    /// People a demon would take: residents of a finished House, or the crew of
    /// a crewed building, if its kind can be possessed at all. The Keep is
    /// never taken, only destroyed.
    /// </summary>
    public int PeopleInside => !Complete || Possessed || !Def.Possessable || Kind == BuildingKind.Keep ? 0
        : Def.Housing > 0 ? Def.Housing
        : Staffed ? Def.Workers
        : 0;
}
