namespace Hellwall.Sim.Tests;

public class MapGenTests
{
    public static IEnumerable<object[]> Kinds() => Enum.GetValues<MapKind>().Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryStartHasStoneIronAndWoodWithinReach(MapKind kind)
    {
        // Forest is allowed a little short on the tightest lake maps; stone and iron never are.
        foreach (uint seed in Enumerable.Range(1, 16).Select(i => (uint)(i * 7 + 3)))
        {
            var have = MapGen.Measure(MapGen.Generate(seed, 256, kind));
            Assert.True(have.Rock >= MapGen.Minimum.Rock, $"{kind} seed {seed}: {have}");
            Assert.True(have.Ore >= MapGen.Minimum.Ore, $"{kind} seed {seed}: {have}");
            Assert.True(have.Forest >= MapGen.Minimum.Forest * 0.4, $"{kind} seed {seed}: {have}");
        }
    }

    [Fact]
    public void MapKindsReallyDiffer()
    {
        var plains = MapGen.Measure(MapGen.Generate(11, 256, MapKind.Plains));
        var lakes = MapGen.Measure(MapGen.Generate(11, 256, MapKind.Lakes));
        var highlands = MapGen.Measure(MapGen.Generate(11, 256, MapKind.Highlands));
        var wildwood = MapGen.Measure(MapGen.Generate(11, 256, MapKind.Wildwood));
        Assert.True(lakes.Grass < plains.Grass);
        Assert.True(highlands.Rock > plains.Rock);
        Assert.True(wildwood.Forest > plains.Forest);
    }

    [Fact]
    public void TheSameSeedAndKindAlwaysGrowTheSameMap()
    {
        Assert.Equal(MapGen.Generate(42, 256, MapKind.Lakes).Tiles, MapGen.Generate(42, 256, MapKind.Lakes).Tiles);
    }

    [Theory]
    [InlineData(MapKind.Plains)]
    [InlineData(MapKind.Lakes)]
    [InlineData(MapKind.Highlands)]
    [InlineData(MapKind.Wildwood)]
    public void SilverLiesOnlyNearTheEdge(MapKind kind)
    {
        foreach (uint seed in new uint[] { 3, 7, 11 })
        {
            var t = MapGen.Generate(seed, 256, kind);
            int c = 128, silver = 0;
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    if (t.Get(x, y) != Tile.Silver) continue;
                    silver++;
                    float d = MathF.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    Assert.True(d > c * 0.7f, $"{kind} seed {seed}: silver at ({x},{y}) is only {d:0} from the Keep");
                }
            Assert.True(silver >= 30, $"{kind} seed {seed}: only {silver} silver tiles");
        }
    }
}
