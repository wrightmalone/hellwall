using System.Globalization;
using Hellwall.Sim;

namespace Hellwall.Headless;

/// <summary>
/// A picture of a map as a survival run starts it: terrain, sleeping packs (bigger dots for
/// bigger packs, violet with elites), Hellgates, the Keep, and where each side's waves come on.
/// One pixel a tile, scaled up; a BMP (no dependencies), for looking at new map kinds.
///   hellwall-sim mapimage --map=causeway --seed=11 --out=out/causeway.bmp [--scale=3]
/// </summary>
public static class MapImage
{
    public static int Run(Dictionary<string, string> args)
    {
        var kind = Enum.Parse<MapKind>(args.GetValueOrDefault("map", "plains"), ignoreCase: true);
        uint seed = uint.Parse(args.GetValueOrDefault("seed", "11"), CultureInfo.InvariantCulture);
        int scale = int.Parse(args.GetValueOrDefault("scale", "3"), CultureInfo.InvariantCulture);
        string output = args.GetValueOrDefault("out", $"out/map-{kind}-{seed}.bmp");
        var difficulty = Enum.Parse<Difficulty>(args.GetValueOrDefault("difficulty", "normal"), ignoreCase: true);
        var world = World.Create(new WorldOptions(seed, 256, 0, Rules.Default, Survival: true, Difficulty: difficulty, Map: kind));
        var t = world.Terrain;
        int n = t.Width;
        var px = new (byte R, byte G, byte B)[n * n];
        for (int i = 0; i < px.Length; i++)
            px[i] = t.Tiles[i] switch
            {
                Tile.Water => ((byte)60, (byte)110, (byte)190),
                Tile.Rock => ((byte)120, (byte)115, (byte)110),
                Tile.Forest => ((byte)35, (byte)95, (byte)40),
                Tile.Ore => ((byte)200, (byte)120, (byte)60),
                Tile.Silver => ((byte)220, (byte)220, (byte)235),
                _ => ((byte)120, (byte)170, (byte)80),
            };
        void Dot(int x, int y, int r, (byte, byte, byte) colour)
        {
            for (int yy = y - r; yy <= y + r; yy++)
                for (int xx = x - r; xx <= x + r; xx++)
                    if (t.InBounds(xx, yy) && (xx - x) * (xx - x) + (yy - y) * (yy - y) <= r * r) px[yy * n + xx] = colour;
        }
        foreach (var p in world.Packs) Dot(p.X, p.Y, p.Count > 30 ? 2 : p.Count > 12 ? 1 : 0, p.EliteCount > 0 ? ((byte)170, (byte)40, (byte)190) : ((byte)200, (byte)30, (byte)30));
        foreach (var g in world.Gates) Dot(g.X + 1, g.Y + 1, 3, ((byte)255, (byte)0, (byte)120));
        foreach (var r in world.Ruins) Dot(r.X, r.Y, 2, ((byte)230, (byte)190, (byte)40));
        Dot(n / 2, n / 2, 4, ((byte)255, (byte)255, (byte)255));
        foreach (var side in MapGen.WaveSides(kind))
            foreach (var (x, y) in MapGen.Entries(kind, side, n, seed: seed)) Dot(x, y, 4, ((byte)255, (byte)230, (byte)0));
        Write(output, px, n, scale);
        var budget = MapGen.Measure(t);
        Console.WriteLine($"{kind} seed {seed}: {world.Packs.Count} packs ({world.Packs.Sum(p => p.Count)} demons), {world.Gates.Count} gates, {world.Ruins.Count} ruins; start {budget}; wrote {output}");
        return 0;
    }

    static void Write(string path, (byte R, byte G, byte B)[] px, int n, int scale)
    {
        int w = n * scale, row = (w * 3 + 3) & ~3;
        using var f = new BinaryWriter(File.Create(path));
        f.Write((byte)'B'); f.Write((byte)'M'); f.Write(54 + row * w); f.Write(0); f.Write(54);
        f.Write(40); f.Write(w); f.Write(w); f.Write((short)1); f.Write((short)24); f.Write(0); f.Write(row * w); f.Write(2835); f.Write(2835); f.Write(0); f.Write(0);
        var line = new byte[row];
        for (int y = w - 1; y >= 0; y--) // bottom-up
        {
            for (int x = 0; x < w; x++)
            {
                var (r, g, b) = px[(y / scale) * n + x / scale];
                line[x * 3] = b; line[x * 3 + 1] = g; line[x * 3 + 2] = r;
            }
            f.Write(line);
        }
    }
}
