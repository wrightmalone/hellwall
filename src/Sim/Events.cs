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
