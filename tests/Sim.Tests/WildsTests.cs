using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

public class WildsTests
{
    static World Survival(Rules? rules = null)
    {
        var world = World.Create(new WorldOptions(11, 256, 0, (rules ?? Rules.Default).WithStartingResources(Plenty).WithHellgates(g => g with { Count = 0 }), Survival: true));
        world.DrainEvents();
        return world;
    }

    static float Dist(Pack p) => MathF.Sqrt((p.X - 128) * (p.X - 128) + (p.Y - 128) * (p.Y - 128));

    [Fact]
    public void PacksHoldTheWholeMapSmallNearHomeAndLargeFarOut()
    {
        var world = Survival();
        var wilds = Rules.Default.Wilds;
        Assert.True(world.Packs.Count >= wilds.Packs * 0.9, $"only {world.Packs.Count} of {wilds.Packs} packs placed");
        Assert.All(world.Packs, p => Assert.True(Dist(p) >= wilds.MinDistance));
        // Every quadrant of the map is held.
        Assert.Equal(4, world.Packs.Select(p => (p.X < 128, p.Y < 128)).Distinct().Count());
        double near = world.Packs.Where(p => Dist(p) < 45).Average(p => p.Count);
        double far = world.Packs.Where(p => Dist(p) > 100).Average(p => p.Count);
        Assert.True(far > near * 2, $"near packs average {near:F0}, far ones {far:F0}");
    }

    [Fact]
    public void NothingCanBeBuiltNearASleepingPack()
    {
        var world = Survival();
        var pack = world.Packs.OrderBy(Dist).First();
        Assert.NotNull(world.PackNear(pack.X + 2, pack.Y, 1, 1)); // this pack, or a neighbour as close
        Assert.NotEqual(pack, world.PackNear(pack.X + 40, pack.Y + 40, 1, 1)); // null, or some other pack
    }

    [Fact]
    public void SoldiersWalkingUpToAPackWakeIt()
    {
        var world = Survival();
        var pack = world.Packs.OrderBy(Dist).First();
        var soldier = world.Units[0];
        Run(world, new OrderUnits([soldier.Id], OrderKind.Move, pack.X, pack.Y));
        var events = RunSeconds(world, 60);
        Assert.Contains(events, e => e is PackWoke w && w.PackId == pack.Id);
    }

    [Fact]
    public void ClearingAPackOpensItsGround()
    {
        // Harmless demons, so the garrison can't lose: this is about the ground opening up.
        var world = Survival(Rules.Default.Harmless());
        var pack = world.Packs.OrderBy(Dist).First();
        Assert.NotNull(world.PackNear(pack.X, pack.Y, 1, 1));
        Run(world, new OrderUnits(world.Units.Select(u => u.Id).ToArray(), OrderKind.AttackMove, pack.X, pack.Y));
        RunSeconds(world, 120);
        Assert.True(pack.Awake);
        Assert.NotEqual(pack, world.PackNear(pack.X, pack.Y, 1, 1)); // null, or a neighbouring pack
    }

    [Fact]
    public void TheIronNearestHomeIsNeverGuarded()
    {
        foreach (uint seed in new uint[] { 3, 7, 11, 19, 42 })
        {
            var world = World.Create(new WorldOptions(seed, 256, 0, Rules.Default, Survival: true));
            var t = world.Terrain;
            bool found = false;
            for (int y = 96; y < 160; y++)
                for (int x = 96; x < 160; x++)
                {
                    if (t.Get(x, y) != Tile.Ore || (x - 128) * (x - 128) + (y - 128) * (y - 128) > 32 * 32) continue;
                    found = true;
                    Assert.True(world.PackNear(x, y, 1, 1) == null, $"seed {seed}: home ore at ({x},{y}) is guarded");
                }
            Assert.True(found, $"seed {seed} has no ore within 32 tiles");
        }
    }
}
