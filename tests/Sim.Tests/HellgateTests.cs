using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class HellgateTests
{
    static World Survival(Rules? rules = null)
    {
        var world = World.Create(new WorldOptions(7, 256, 0, (rules ?? Rules.Default).WithStartingResources(Plenty), Survival: true));
        world.DrainEvents();
        return world;
    }

    [Fact]
    public void GatesStandFarOutOnlyInSurvivalRuns()
    {
        var world = Survival();
        Assert.Equal(Rules.Default.Hellgates.Count, world.Gates.Count);
        foreach (var g in world.Gates)
        {
            float dx = g.CentreX - 128, dy = g.CentreY - 128;
            Assert.True(MathF.Sqrt(dx * dx + dy * dy) >= Rules.Default.Hellgates.MinDistance - 2);
            Assert.False(world.IsWalkable(g.X + 1, g.Y + 1));
        }
        Assert.Empty(World.Create(new WorldOptions(7, 256)).Gates);
    }

    [Fact]
    public void AGateSendsBandsAtTheColony()
    {
        var world = Survival(Rules.Default.WithHellgates(g => g with { FirstBandDay = 0 }));
        var events = RunSeconds(world, Rules.Default.Hellgates.SpawnSeconds + 1);
        Assert.True(world.Horde.Count >= Rules.Default.Hellgates.BandSize * world.Gates.Count / 2,
            $"after a spawn interval there should be bands out, found {world.Horde.Count}");
    }

    [Fact]
    public void ASurvivalRunStartsWithAGarrisonAndQuietGates()
    {
        var world = Survival();
        Assert.Equal(Rules.Default.StartingUnits.Sum(s => s.Count), world.Units.Count);
        RunSeconds(world, 60 * 2);
        Assert.Equal(0, world.Horde.Count); // no bands before FirstBandDay
    }

    [Fact]
    public void ClosingGatesShrinksTheWaves()
    {
        var world = Survival();
        Assert.Equal(1.0, HellgateSystem.WaveScale(world), 6);
        foreach (var g in world.Gates.Take(2)) world.DamageGate(g, 1e9f);
        Assert.Equal(0.4 + 0.6 * 0.5, HellgateSystem.WaveScale(world), 6);
        foreach (var g in world.Gates) world.DamageGate(g, 1e9f);
        Assert.Equal(Rules.Default.Hellgates.WaveFloor, HellgateSystem.WaveScale(world), 6);
        Assert.All(world.Gates, g => Assert.True(world.IsWalkable(g.X + 1, g.Y + 1), "a closed gate's ground is open again"));
    }

    [Fact]
    public void SoldiersSentAtAGateCloseIt()
    {
        // No bands, no packs on the way, and a soft gate: this is about soldiers attacking it.
        var rules = OpenWoods.WithHellgates(g => g with { Hp = 300, SpawnSeconds = 1e6f }).WithWilds(w => w with { Packs = 0, Strays = 0, Ruins = 0 });
        var world = Survival(rules);
        var gate = world.Gates[0];
        Built(world, BuildingKind.House, 132, 126);
        var barracks = Built(world, BuildingKind.Barracks, 123, 130);
        int garrison = world.Units.Count; // the starting Militia
        for (int i = 0; i < 4; i++) world.Enqueue(new TrainUnit(barracks.Id, UnitKind.Militia));
        RunSeconds(world, 4 * 6 + 1);
        Assert.Equal(garrison + 4, world.Units.Count);

        Run(world, new OrderUnits(world.Units.Select(u => u.Id).ToArray(), OrderKind.AttackMove, (int)gate.CentreX, gate.Y + Hellgate.Size + 1));
        var events = RunSeconds(world, 150);
        Assert.Contains(events, e => e is HellgateClosed c && c.GateId == gate.Id);
        Assert.False(gate.Alive);
    }

    [Fact]
    public void GatesSurviveASaveAndLoad()
    {
        var world = Survival();
        world.DamageGate(world.Gates[0], 1e9f);
        world.DamageGate(world.Gates[1], 500);
        RunSeconds(world, 30);
        var loaded = World.Load(world.Save(), world.Rules);
        Assert.Equal(StateHash.Hex(world), StateHash.Hex(loaded));
        Assert.False(loaded.Gates[0].Alive);
        Assert.True(loaded.IsWalkable(world.Gates[0].X + 1, world.Gates[0].Y + 1));
        Assert.False(loaded.IsWalkable(world.Gates[1].X + 1, world.Gates[1].Y + 1));
        RunSeconds(world, 40);
        RunSeconds(loaded, 40);
        Assert.Equal(StateHash.Hex(world), StateHash.Hex(loaded));
    }
}
