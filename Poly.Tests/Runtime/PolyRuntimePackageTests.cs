using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Runtime;

/// <summary>
/// Guards the R-01 assembly move: <see cref="IStoragePort"/> and
/// <see cref="ConstraintFailureException"/> ship in their own BCL-only assembly,
/// <c>Poly.Runtime</c>, while their namespace stays
/// <c>Poly.DomainModeling.Runtime</c> so the emitted C# surface is byte-identical.
/// The namespace move belongs to R-05/R-07, not here.
/// </summary>
public class PolyRuntimePackageTests {
    [Test]
    public async Task StoragePortAndFailureException_LiveInPolyRuntimeAssembly() {
        var portAssembly = typeof(IStoragePort).Assembly.GetName().Name;
        await Assert.That(portAssembly).IsEqualTo("Poly.Runtime");

        var failureAssembly = typeof(ConstraintFailureException).Assembly.GetName().Name;
        await Assert.That(failureAssembly).IsEqualTo("Poly.Runtime");
    }

    [Test]
    public async Task PolyRuntime_ReferencesNothingBeyondTheBcl() {
        var runtime = typeof(IStoragePort).Assembly;

        // Assert the move first: on the pre-move tree this test would otherwise be
        // green against Poly's own reference set and prove nothing.
        await Assert.That(runtime.GetName().Name).IsEqualTo("Poly.Runtime");

        var offenders = runtime.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !IsBclName(name))
            .ToArray();

        await Assert.That(offenders).IsEmpty();

        static bool IsBclName(string name) =>
            name is "System.Runtime" or "mscorlib" or "netstandard"
            || (name.StartsWith("System.", StringComparison.Ordinal)
                && !name.StartsWith("System.Private", StringComparison.Ordinal));
    }

    [Test]
    public async Task Poly_ReferencesPolyRuntime() {
        var poly = typeof(ModuleStoragePort).Assembly;
        var runtime = typeof(IStoragePort).Assembly;

        await Assert.That(poly.GetName().Name).IsEqualTo("Poly");
        await Assert.That(poly).IsNotSameReferenceAs(runtime);

        var referencesRuntime = poly.GetReferencedAssemblies()
            .Any(reference => string.Equals(reference.Name, "Poly.Runtime", StringComparison.Ordinal));
        await Assert.That(referencesRuntime).IsTrue();
    }

    [Test]
    public async Task GetTypeDefinition_ResolvesStoragePortByFullName() {
        // Regression guard, not isolation: Type.GetType(name) resolves against the
        // calling assembly and CoreLib only, so this leans on the loaded-assembly
        // fallback once the type leaves Poly. Inside one test process it may pass
        // for the wrong reason; the assembly-name assertions above are the guard
        // that cannot.
        var resolved = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition("Poly.DomainModeling.Runtime.IStoragePort");

        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.Name).IsEqualTo(nameof(IStoragePort));
        await Assert.That(resolved!.Namespace).IsEqualTo("Poly.DomainModeling.Runtime");
    }
}