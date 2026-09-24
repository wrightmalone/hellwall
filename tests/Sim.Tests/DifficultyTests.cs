namespace Hellwall.Sim.Tests;

public class DifficultyTests
{
    static World Survival(Difficulty d) =>
        World.Create(new WorldOptions(11, 256, 0, Rules.Default, Survival: true, Difficulty: d));

    [Fact]
    public void NormalIsTheRulesAsWritten()
    {
        Assert.Same(Rules.Default, Rules.Default.ForDifficulty(Difficulty.Normal));
    }

    [Fact]
    public void HarderMeansMoreDemonsAndEasierMeansMoreToStartWith()
    {
        var easy = Survival(Difficulty.Easy);
        var normal = Survival(Difficulty.Normal);
        var nightmare = Survival(Difficulty.Nightmare);
        int Total(World w) => w.Survival!.Waves.Sum(v => v.Size);
        Assert.True(Total(easy) < Total(normal) && Total(normal) < Total(nightmare));
        Assert.True(easy.Packs.Sum(p => p.Count) < normal.Packs.Sum(p => p.Count));
        Assert.True(easy.Colony[Resource.Gold] > normal.Colony[Resource.Gold]);
        Assert.Equal(normal.Terrain.Tiles, nightmare.Terrain.Tiles); // the same map, only the horde changes
    }

    [Fact]
    public void ADifferentDifficultyIsADifferentRuleset()
    {
        Assert.NotEqual(Rules.Default.Hash, Rules.Default.ForDifficulty(Difficulty.Hard).Hash);
        Assert.NotEqual(Rules.Default.ForDifficulty(Difficulty.Easy).Hash, Rules.Default.ForDifficulty(Difficulty.Hard).Hash);
    }

    [Fact]
    public void ASaveRemembersItsDifficulty()
    {
        var world = Survival(Difficulty.Hard);
        for (int t = 0; t < 200; t++) world.Step();
        var loaded = World.Load(world.Save(), Rules.Default);
        Assert.Equal(Difficulty.Hard, loaded.Rules.Difficulty);
        for (int t = 0; t < 200; t++) { world.Step(); loaded.Step(); }
        Assert.Equal(StateHash.Compute(world), StateHash.Compute(loaded));
    }
}
