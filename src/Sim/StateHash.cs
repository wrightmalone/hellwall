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
        h = Mix(h, (uint)world.Rules.Hash);
        h = Mix(h, (uint)(world.Rules.Hash >> 32));
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
            h = Mix(h, Bits(b.Hp));
            h = Mix(h, Bits(b.Built));
            h = MixByte(h, (byte)((b.Complete ? 1 : 0) | (b.OnGround ? 2 : 0) | (b.Staffed ? 4 : 0)));
            h = Mix(h, Bits(b.Cooldown));
            h = Mix(h, Bits(b.TrainProgress));
            h = MixByte(h, b.Possessed ? (byte)1 : (byte)0);
            h = Mix(h, (uint)b.Occupants);
            h = Mix(h, Bits(b.PossessTimer));
            h = Mix(h, (uint)(b.Researching?.Length ?? 0));
            foreach (char ch in b.Researching ?? "") h = MixByte(h, (byte)ch);
            h = Mix(h, Bits(b.ResearchProgress));
            h = Mix(h, (uint)b.Queue.Count);
            foreach (var q in b.Queue) h = MixByte(h, (byte)q);
        }

        h = Mix(h, (uint)world.Tech.Researched.Count);
        foreach (var id in world.Tech.Researched)
            foreach (char ch in id) h = MixByte(h, (byte)ch);

        var colony = world.Colony;
        foreach (var stock in colony.Stock) h = Mix64(h, BitConverter.DoubleToInt64Bits(stock));
        h = MixByte(h, colony.Starving ? (byte)1 : (byte)0);

        h = Mix(h, (uint)world.Units.Count);
        foreach (var u in world.Units)
        {
            h = Mix(h, (uint)u.Id);
            h = MixByte(h, (byte)u.Kind);
            h = Mix(h, Bits(u.X));
            h = Mix(h, Bits(u.Y));
            h = Mix(h, Bits(u.Hp));
            h = Mix(h, Bits(u.Cooldown));
            h = MixByte(h, (byte)u.Order);
            h = Mix(h, (uint)u.DestX);
            h = Mix(h, (uint)u.DestY);
        }

        var horde = world.Horde;
        h = Mix(h, (uint)horde.Count);
        for (int i = 0; i < horde.Count; i++)
        {
            h = Mix(h, Bits(horde.X[i]));
            h = Mix(h, Bits(horde.Y[i]));
            h = Mix(h, Bits(horde.VX[i]));
            h = Mix(h, Bits(horde.VY[i]));
            h = MixByte(h, (byte)horde.Kind[i]);
            h = Mix(h, Bits(horde.Hp[i]));
            h = Mix(h, Bits(horde.Cooldown[i]));
        }

        h = Mix(h, (uint)world.Packs.Count);
        foreach (var p in world.Packs)
        {
            h = Mix(h, (uint)p.Id);
            h = Mix(h, (uint)p.X);
            h = Mix(h, (uint)p.Y);
            h = Mix(h, (uint)p.Count);
            h = MixByte(h, (byte)p.Kind);
            h = MixByte(h, p.Awake ? (byte)1 : (byte)0);
        }

        foreach (var level in world.Noise.Level) h = Mix(h, Bits(level));

        foreach (var g in world.Gates)
        {
            h = Mix(h, (uint)g.Id);
            h = Mix(h, Bits(g.Hp));
            h = Mix(h, Bits(g.SpawnTimer));
        }

        if (world.Survival is { } s)
        {
            h = MixByte(h, s.FinalLanded ? (byte)1 : (byte)0);
            h = Mix(h, (uint)s.FinalLandedTick);
            foreach (var w in s.Waves)
            {
                h = MixByte(h, (byte)((w.Announced ? 1 : 0) | (w.Landed ? 2 : 0)));
                h = Mix(h, (uint)w.Size);
                foreach (var side in w.Sides) h = MixByte(h, (byte)side);
            }
            h = MixByte(h, s.Endless ? (byte)1 : (byte)0);
            foreach (var id in s.Corruptions) h = MixString(h, id);
            h = MixString(h, s.PendingCorruption ?? "");
            h = Mix(h, (uint)s.NextCorruptionTick);
        }
        // Flow field and spatial hash are pure functions of the above, so they aren't hashed.
        return h;
    }

    public static string Hex(World world) => Compute(world).ToString("x16");

    static uint Bits(float f) => (uint)BitConverter.SingleToInt32Bits(f);

    static ulong Mix64(ulong h, long v) => Mix(Mix(h, (uint)v), (uint)(v >> 32));

    static ulong Mix(ulong h, uint value)
    {
        h = MixByte(h, (byte)value);
        h = MixByte(h, (byte)(value >> 8));
        h = MixByte(h, (byte)(value >> 16));
        return MixByte(h, (byte)(value >> 24));
    }

    static ulong MixString(ulong h, string s)
    {
        foreach (char ch in s) h = Mix(h, ch);
        return MixByte(h, 0);
    }

    static ulong MixByte(ulong h, byte b)
    {
        unchecked { return (h ^ b) * Prime; }
    }
}
