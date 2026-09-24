namespace Hellwall.Sim.Tests;

public class RngTests
{
    /// <summary>
    /// Golden values produced by Ashfield's TypeScript mulberry32 for seed 7.
    /// If this fails, the port has drifted from the original bit-for-bit.
    /// </summary>
    [Fact]
    public void MatchesAshfieldMulberry32()
    {
        var rng = new Rng(7);
        uint[] expected = [50271532u, 266108690u, 4195786334u, 3002305430u, 2239590375u];
        foreach (var value in expected) Assert.Equal(value, rng.NextUInt());
        Assert.Equal(567894480u, rng.State);
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var a = new Rng(42);
        a.NextUInt();
        var b = a.Clone();
        Assert.Equal(a.NextUInt(), b.NextUInt());
        a.NextUInt();
        Assert.NotEqual(a.State, b.State);
    }

    [Fact]
    public void NextIntStaysInRange()
    {
        var rng = new Rng(1);
        for (int i = 0; i < 10_000; i++)
        {
            int v = rng.NextInt(6);
            Assert.InRange(v, 0, 5);
        }
    }
}
