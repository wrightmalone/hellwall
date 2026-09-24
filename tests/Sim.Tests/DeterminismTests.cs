namespace Hellwall.Sim.Tests;

/// <summary>
/// In-process determinism. The cross-process half (which is what catches
/// per-process randomized hashing) lives in scripts/verify.sh, which runs the
/// headless harness twice and compares hashes.
/// </summary>
public class DeterminismTests
{
    static readonly ScriptedCommand[] Script =
    [
        new(0, new PlaceBuilding(BuildingKind.House, 68, 62)),
        new(0, new PlaceBuilding(BuildingKind.House, 68, 62)), // rejected: occupied
        new(15, new PlaceBuilding(BuildingKind.Wall, 60, 58)),
        new(40, new Demolish(2)),
        new(40, new PlaceBuilding(BuildingKind.House, 58, 62)),
    ];

    static List<string> Replay(uint seed, int ticks)
    {
        var world = World.Create(new WorldOptions(seed));
        var hashes = new List<string>();
        int next = 0;
        for (int t = 0; t < ticks; t++)
        {
            while (next < Script.Length && Script[next].Tick <= world.Tick) world.Enqueue(Script[next++].Command);
            world.Step();
            world.DrainEvents();
            hashes.Add(StateHash.Hex(world));
        }
        return hashes;
    }

    [Fact]
    public void SameSeedAndScriptGiveIdenticalHashEveryTick()
    {
        Assert.Equal(Replay(7, 200), Replay(7, 200));
    }

    [Fact]
    public void DifferentSeedsGiveDifferentWorlds()
    {
        Assert.NotEqual(Replay(7, 1)[0], Replay(8, 1)[0]);
    }

    [Fact]
    public void FlushingWhilePausedEqualsApplyingAtNextTick()
    {
        var paused = World.Create(new WorldOptions(7));
        var live = World.Create(new WorldOptions(7));
        for (int i = 0; i < 10; i++) { paused.Step(); live.Step(); }

        paused.Enqueue(new PlaceBuilding(BuildingKind.House, 68, 62));
        paused.FlushCommands();
        paused.Step();

        live.Enqueue(new PlaceBuilding(BuildingKind.House, 68, 62));
        live.Step();

        Assert.Equal(StateHash.Hex(live), StateHash.Hex(paused));
    }
}
