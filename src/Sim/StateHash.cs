namespace Hellwall.Sim;

/// <summary>
/// FNV-1a 64 over everything that defines the world. Two runs that agree on
/// this hash at every sample agree on the world. Deliberately not
/// System.HashCode, which is randomized per process and would make a
/// cross-process determinism check meaningless.
///
/// Every new piece of state must be folded in here, or determinism tests
/// silently stop covering it.
/// </summary>
public static class StateHash
{
    const ulong Offset = 14695981039346656037UL;
    const ulong Prime = 1099511628211UL;

    public static ulong Compute(World world)
    {
        ulong h = Offset;
        h = Mix(h, (uint)world.Tick);
        h = Mix(h, world.Seed);
        h = Mix(h, world.Rng.State);
        h = Mix(h, (uint)world.Outcome);
        h = Mix(h, (uint)world.Terrain.Width);
        h = Mix(h, (uint)world.Terrain.Height);
        foreach (var tile in world.Terrain.Tiles) h = MixByte(h, (byte)tile);
        h = Mix(h, (uint)world.Buildings.Count);
        foreach (var b in world.Buildings)
        {
            h = Mix(h, (uint)b.Id);
            h = Mix(h, (uint)b.Kind);
            h = Mix(h, (uint)b.X);
            h = Mix(h, (uint)b.Y);
        }
        return h;
    }

    public static string Hex(World world) => Compute(world).ToString("x16");

    static ulong Mix(ulong h, uint value)
    {
        h = MixByte(h, (byte)value);
        h = MixByte(h, (byte)(value >> 8));
        h = MixByte(h, (byte)(value >> 16));
        return MixByte(h, (byte)(value >> 24));
    }

    static ulong MixByte(ulong h, byte b)
    {
        unchecked { return (h ^ b) * Prime; }
    }
}
