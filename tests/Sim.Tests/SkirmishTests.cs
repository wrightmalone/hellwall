using static Hellwall.Sim.Tests.TestWorlds;

namespace Hellwall.Sim.Tests;

/// <summary>Skirmishes and hand-made maps: scenarios that aren't in the campaign, and travel inside their saves.</summary>
public class SkirmishTests
{
    static ScenarioDef Custom(string tiles = "") => new()
    {
        Id = "skirmish", Name = "Skirmish", Seed = 23, Map = MapKind.Highlands, MapSize = 192, Days = 30,
        Waves = 1.5, Hellgates = 2, Packs = 40, Strays = 10, LivingWoods = true, Fog = false, Tiles = tiles,
    };

    [Fact]
    public void ASkirmishPlaysUnderItsOwnSettings()
    {
        var world = World.Create(Custom().Options(Rules.Default));
        Assert.Equal(192, world.Terrain.Width);
        Assert.Equal(2, world.Gates.Count);
        Assert.Equal(40, world.Packs.Count(p => !p.Stray));
        Assert.Equal(10, world.Packs.Count(p => p.Stray));
        Assert.True(world.ForestBlocks);
        Assert.False(world.Vision.Enabled);
        Assert.Equal(30, world.Rules.Survival.Days);
    }

    [Fact]
    public void ASkirmishSaveCarriesItsScenario()
    {
        var world = World.Create(Custom().Options(Rules.Default));
        RunSeconds(world, 20);
        var loaded = World.Load(world.Save(), Rules.Default);
        Assert.Equal("skirmish", loaded.Scenario!.Id);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
        RunSeconds(world, 10);
        RunSeconds(loaded, 10);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }

    [Fact]
    public void AHandMadeMapIsPlayedAsPainted()
    {
        // All grass, a lake in one corner and a band of rock: nothing like what Highlands would generate.
        var tiles = new Tile[192 * 192];
        for (int y = 0; y < 192; y++)
            for (int x = 0; x < 192; x++)
                tiles[y * 192 + x] = x < 30 && y < 30 ? Tile.Water : x == 120 ? Tile.Rock : Tile.Grass;
        tiles[96 * 192 + 96] = Tile.Water; // under the Keep: forced back to grass
        var world = World.Create(Custom(ScenarioDef.EncodeTiles(tiles)).Options(Rules.Default));
        Assert.Equal(Tile.Water, world.Terrain.Get(5, 5));
        Assert.Equal(Tile.Rock, world.Terrain.Get(120, 10));
        Assert.Equal(Tile.Grass, world.Terrain.Get(60, 150));
        Assert.Equal(Tile.Grass, world.Terrain.Get(96, 96));
        var loaded = World.Load(world.Save(), Rules.Default);
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }
}
