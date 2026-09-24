using System.Globalization;
using Hellwall.Sim;

namespace Hellwall.Headless;

/// <summary>
/// What a map offers near the start: rock, forest and open grass within a
/// few radii of the Keep, and how far away the nearest of each is. The seed
/// of phase 5's fairness rules: `hellwall-sim maps --seeds=3,11,5,7,19,42`.
/// </summary>
public static class MapReport
{
    public static int Run(Dictionary<string, string> args)
    {
        var seeds = args.GetValueOrDefault("seeds", "3,11,5,7,19,42").Split(',').Select(v => uint.Parse(v, CultureInfo.InvariantCulture));
        int size = int.Parse(args.GetValueOrDefault("size", "256"), CultureInfo.InvariantCulture);
        foreach (var seed in seeds)
        {
            var t = MapGen.Generate(seed, size);
            int c = size / 2;
            var row = new List<string> { $"seed {seed,3}" };
            foreach (var tile in new[] { Tile.Rock, Tile.Forest })
            {
                int nearest = int.MaxValue;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        if (t.Get(x, y) == tile) nearest = Math.Min(nearest, (int)MathF.Sqrt((x - c) * (x - c) + (y - c) * (y - c)));
                row.Add($"nearest {tile.ToString().ToLowerInvariant()} {nearest,3}");
            }
            foreach (int r in new[] { 20, 25, 30 })
            {
                int rock = 0, forest = 0;
                for (int y = c - r; y <= c + r; y++)
                    for (int x = c - r; x <= c + r; x++)
                    {
                        if ((x - c) * (x - c) + (y - c) * (y - c) > r * r) continue;
                        var tile = t.Get(x, y);
                        if (tile == Tile.Rock) rock++; else if (tile == Tile.Forest) forest++;
                    }
                row.Add($"r{r}: rock {rock,4} forest {forest,4}");
            }
            Console.WriteLine(string.Join("   ", row));
        }
        return 0;
    }
}
