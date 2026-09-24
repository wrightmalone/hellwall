using System.Text;
using Hellwall.Sim;

namespace Hellwall.Headless;

/// <summary>
/// Writes the world as a binary PPM image, one pixel per tile: terrain,
/// buildings, and demon density as red intensity. PPM needs no image library;
/// on macOS, `sips -s format png in.ppm --out out.png` converts it.
/// </summary>
public static class Snapshot
{
    public static void Write(World world, string path)
    {
        var t = world.Terrain;
        var density = new int[t.Width * t.Height];
        var h = world.Horde;
        for (int i = 0; i < h.Count; i++) density[(int)h.Y[i] * t.Width + (int)h.X[i]]++;

        var pixels = new byte[t.Width * t.Height * 3];
        for (int y = 0; y < t.Height; y++)
        {
            for (int x = 0; x < t.Width; x++)
            {
                int i = y * t.Width + x;
                var (r, g, b) = t.Get(x, y) switch
                {
                    Tile.Grass => (92, 133, 71),
                    Tile.Forest => (41, 82, 46),
                    Tile.Rock => (117, 112, 107),
                    _ => (46, 77, 128),
                };
                int id = world.BuildingIdAt(x, y);
                if (id != 0)
                {
                    var kind = world.Buildings.First(bl => bl.Id == id).Kind;
                    (r, g, b) = kind == BuildingKind.Wall ? (230, 230, 240) : (240, 200, 60);
                }
                if (density[i] > 0)
                {
                    // 1 demon = dark red, 3+ = full red, 20+ = hot pink so pile-ups stand out.
                    (r, g, b) = density[i] >= 20 ? (255, 60, 220) : (Math.Min(255, 120 + density[i] * 45), 20, 20);
                }
                pixels[i * 3] = (byte)r;
                pixels[i * 3 + 1] = (byte)g;
                pixels[i * 3 + 2] = (byte)b;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var f = File.Create(path);
        f.Write(Encoding.ASCII.GetBytes($"P6\n{t.Width} {t.Height}\n255\n"));
        f.Write(pixels);
    }
}
