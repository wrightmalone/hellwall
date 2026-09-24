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

/// <summary>Debug and scenario: scatter Count demons over a disc centred on tile (X, Y).</summary>
public sealed record SpawnDemons(DemonKind Kind, int X, int Y, int Count) : Command;

/// <summary>Debug and scenario: a noise pulse, as a stand-in for combat until phase 2.</summary>
public sealed record MakeNoise(int X, int Y, float Radius, float Intensity) : Command;

/// <summary>Queue a unit at a Barracks. Paid for when queued; refunded if the Barracks is lost.</summary>
public sealed record TrainUnit(int BarracksId, UnitKind Kind) : Command;

/// <summary>Start researching a tech at a Scriptorium. Paid for up front; refunded if the building is lost.</summary>
public sealed record Research(int BuildingId, string TechId) : Command;

/// <summary>Order soldiers to a tile. They share one route map for the order.</summary>
public sealed record OrderUnits(int[] UnitIds, OrderKind Order, int X, int Y) : Command;
