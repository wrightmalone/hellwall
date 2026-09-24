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
public sealed record UnitDied(int Tick, int UnitId, UnitKind Kind, float X, float Y, bool Rose) : SimEvent(Tick);

/// <summary>A demon reached an inhabited building; its Occupants will come out as Thralls.</summary>
public sealed record BuildingPossessed(int Tick, int BuildingId, BuildingKind Kind, int Occupants) : SimEvent(Tick);

/// <summary>One per tick with kills, not one per demon: a Bombard volley can kill dozens.</summary>
public sealed record DemonsKilled(int Tick, int Count) : SimEvent(Tick);

/// <summary>
/// A shot the player's side fired, so the client can draw it. Damage is
/// resolved instantly; there are no projectiles in the sim.
/// </summary>
public sealed record ShotFired(int Tick, float FromX, float FromY, float ToX, float ToY, float Splash, bool FromUnit) : SimEvent(Tick);

public sealed record OutcomeChanged(int Tick, Outcome Outcome) : SimEvent(Tick);
