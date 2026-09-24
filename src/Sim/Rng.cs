namespace Hellwall.Sim;

/// <summary>
/// mulberry32, ported bit-for-bit from Ashfield's packages/sim/src/rng.ts.
///
/// Owned by the World and never global. The whole generator is one uint, so it
/// serializes with the world and a save or replay resumes byte-identically.
/// Same seed + same commands = identical outcome, which is what the headless
/// harness and every probe rely on.
/// </summary>
public sealed class Rng
{
    public uint State;

    public Rng(uint seed) => State = seed;

    public uint NextUInt()
    {
        unchecked
        {
            State += 0x6D2B79F5u;
            uint t = State;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return t ^ (t >> 14);
        }
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => NextUInt() / 4294967296.0;

    /// <summary>Integer in [0, n).</summary>
    public int NextInt(int n) => (int)(NextDouble() * n);

    /// <summary>Uniform in [min, max).</summary>
    public double Range(double min, double max) => min + NextDouble() * (max - min);

    public bool Chance(double probability) => NextDouble() < probability;

    public Rng Clone() => new(State);
}
