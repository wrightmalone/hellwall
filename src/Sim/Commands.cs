namespace Hellwall.Sim;

/// <summary>
/// Commands are the ONLY way into the simulation.
///
/// Input never mutates sim state directly: it enqueues serializable Commands,
/// which World drains at the top of the next tick (or immediately via
/// FlushCommands while paused). That buys replay, the headless harness and the
/// Godot boundary in one move: a harness script is a list of these with tick
/// stamps.
/// </summary>
public abstract record Command;

/// <summary>Place a building with its top-left corner at (X, Y).</summary>
public sealed record PlaceBuilding(BuildingKind Kind, int X, int Y) : Command;

public sealed record Demolish(int BuildingId) : Command;

/// <summary>A tick-stamped command: the serialized form of a script or replay.</summary>
public readonly record struct ScriptedCommand(int Tick, Command Command);
