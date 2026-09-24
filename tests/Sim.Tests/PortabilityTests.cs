namespace Hellwall.Sim.Tests;

/// <summary>
/// The sim must be liftable into any host. Its csproj has no Godot reference,
/// and BannedSymbols.txt blocks ambient nondeterminism at compile time; this
/// guards the assembly graph itself, so a stray package or project reference
/// can't slip through.
/// </summary>
public class PortabilityTests
{
    [Fact]
    public void SimReferencesOnlyTheBaseClassLibrary()
    {
        var references = typeof(World).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        var foreign = references.Where(n => !(n == "System" || n.StartsWith("System.", StringComparison.Ordinal) || n == "netstandard")).ToList();
        Assert.True(foreign.Count == 0, $"Sim references non-BCL assemblies: {string.Join(", ", foreign)}");
    }
}
