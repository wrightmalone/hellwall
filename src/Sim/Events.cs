namespace Hellwall.Sim;

/// <summary>
/// Sim to outside world is a one-way event stream. The sim appends; render,
/// audio and the harness consume. Nothing calls back in, so the sim runs with
/// no consumer attached.
/// </summary>
public abstract record SimEvent(int Tick);

public sealed record BuildingPlaced(int Tick, int BuildingId, BuildingKind Kind, int X, int Y) : SimEvent(Tick);

public sealed record BuildingRemoved(int Tick, int BuildingId, BuildingKind Kind) : SimEvent(Tick);

/// <summary>Carries the whole command: a rejection you cannot locate is not a useful log line.</summary>
public sealed record CommandRejected(int Tick, string Reason, Command Command) : SimEvent(Tick);

public sealed record DemonsSpawned(int Tick, DemonKind Kind, int Count) : SimEvent(Tick);

public sealed record PackWoke(int Tick, int PackId, int X, int Y, int Count) : SimEvent(Tick);

/// <summary>For the client and, later, audio: where something was loud.</summary>
public sealed record NoiseMade(int Tick, float X, float Y, float Radius) : SimEvent(Tick);

public sealed record BuildingCompleted(int Tick, int BuildingId, BuildingKind Kind) : SimEvent(Tick);

public sealed record BuildingDestroyed(int Tick, int BuildingId, BuildingKind Kind, int X, int Y) : SimEvent(Tick);

/// <summary>The holy grid changed shape; the client should repaint consecrated ground.</summary>
public sealed record ConsecrationChanged(int Tick) : SimEvent(Tick);

public sealed record UnitTrained(int Tick, int UnitId, UnitKind Kind, int BarracksId) : SimEvent(Tick);

/// <param name="Rose">Came back as a Thrall.</param>
/// <summary>A soldier's kills earned a new rank (1 Veteran, 2 Elite, 3 Champion).</summary>
public sealed record UnitPromoted(int Tick, int UnitId, UnitKind Kind, int Rank, float X, float Y) : SimEvent(Tick);

public sealed record UnitDied(int Tick, int UnitId, UnitKind Kind, float X, float Y, bool Rose) : SimEvent(Tick);

/// <summary>A demon reached an inhabited building; its Occupants will come out as Thralls.</summary>
public sealed record BuildingPossessed(int Tick, int BuildingId, BuildingKind Kind, int Occupants) : SimEvent(Tick);

/// <summary>One per tick with kills, not one per demon: a Bombard volley can kill dozens.</summary>
public sealed record DemonsKilled(int Tick, int Count) : SimEvent(Tick);

/// <summary>
/// A shot the player's side fired, so the client can draw it. Damage is
/// resolved instantly; there are no projectiles in the sim.
/// </summary>
/// <summary>The colony reached a milestone: choose one of these blessings.</summary>
public sealed record PatronOffered(int Tick, string[] TechIds) : SimEvent(Tick);
public sealed record PatronChosen(int Tick, string TechId) : SimEvent(Tick);

public sealed record BuildingUpgraded(int Tick, int BuildingId, BuildingKind From, BuildingKind To) : SimEvent(Tick);

/// <summary>Soldiers reached a cleared ruin and took what was there.</summary>
public sealed record RuinLooted(int Tick, int RuinId, int X, int Y, string Loot) : SimEvent(Tick);

/// <summary>A Spitter spat at a soldier or a building.</summary>
public sealed record DemonSpat(int Tick, float FromX, float FromY, float ToX, float ToY) : SimEvent(Tick);

public sealed record ShotFired(int Tick, float FromX, float FromY, float ToX, float ToY, float Splash, bool FromUnit) : SimEvent(Tick);

public sealed record OutcomeChanged(int Tick, Outcome Outcome) : SimEvent(Tick);

/// <summary>A wave is coming: where from, how many, and when.</summary>
public sealed record WaveAnnounced(int Tick, int Number, int LandsAtTick, Side[] Sides, int Size, bool Final) : SimEvent(Tick);

/// <summary>Endless: a corruption will take hold of the horde at LandsAtTick.</summary>
public sealed record CorruptionAnnounced(int Tick, string Id, string Name, string Description, int LandsAtTick) : SimEvent(Tick);

public sealed record CorruptionTook(int Tick, string Id, string Name) : SimEvent(Tick);

/// <summary>A tree came down (felled by woodsmen or hacked through by the horde): the tile is open ground now.</summary>
public sealed record TreeFelled(int Tick, int X, int Y) : SimEvent(Tick);
/// <summary>Miners took the last of a rock or ore tile; it is open ground now.</summary>
public sealed record DepositWorn(int Tick, int X, int Y, Tile Was) : SimEvent(Tick);

/// <summary>A mission trigger fired: its message, and how many demons it brought (from Side).</summary>
/// <summary>A mission's trigger speaks; Spawned demons (of Kind) will come from Side RaidLeadSeconds later.</summary>
public sealed record ScenarioMessage(int Tick, int Index, string Text, int Spawned, Side Side, string Speaker = "", DemonKind Kind = DemonKind.Imp) : SimEvent(Tick);
/// <summary>A mission's announced raid has come onto the map.</summary>
public sealed record RaidLanded(int Tick, int Index, int Spawned, Side Side, DemonKind Kind) : SimEvent(Tick);

/// <summary>A mission goal was met (index into the world's Goals).</summary>
public sealed record ObjectiveCompleted(int Tick, int Index, ObjectiveKind Kind) : SimEvent(Tick);

public sealed record WaveLanded(int Tick, int Number, int Spawned, bool Final) : SimEvent(Tick);

/// <summary>An Observatory charted a patch of the fog: X, Y its top-left tile, Size its side.</summary>
public sealed record GroundCharted(int Tick, int BuildingId, int X, int Y, int Size) : SimEvent(Tick);

public sealed record TechResearched(int Tick, string TechId) : SimEvent(Tick);

public sealed record HellgateClosed(int Tick, int GateId, int X, int Y) : SimEvent(Tick);

/// <summary>A Bloater burst here.</summary>
public sealed record DemonBurst(int Tick, float X, float Y, float Radius) : SimEvent(Tick);

/// <summary>A Howler howled: noise of Radius tiles.</summary>
public sealed record DemonHowled(int Tick, float X, float Y, float Radius) : SimEvent(Tick);
