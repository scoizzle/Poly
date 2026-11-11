using System.Xml.Linq;

using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Runtime;

/// <summary>
/// Guards the R-01 assembly move: <see cref="IStoragePort"/> and
/// <see cref="ConstraintFailureException"/> ship in their own BCL-only, separately
/// packable assembly <c>Poly.Runtime</c>, while their namespace stays
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

        // Assert the move first: on the pre-move tree the rest of this test would run
        // against Poly's own reference set and prove nothing.
        await Assert.That(runtime.GetName().Name).IsEqualTo("Poly.Runtime");

        // Name-prefix allowlists admit System.CommandLine and friends. Require every
        // reference to be an assembly the shared framework actually ships.
        var framework = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var foreign = runtime.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !File.Exists(Path.Combine(framework, name + ".dll")))
            .ToArray();

        await Assert.That(foreign).IsEmpty();

        // The reference set above only proves what the compiler emitted. The project file
        // is what stops a package or project edge from being added and silently followed.
        var project = XDocument.Load(Path.Combine(RepoRoot(), "Poly.Runtime", "Poly.Runtime.csproj"));
        await Assert.That(project.Descendants("PackageReference").Any()).IsFalse();
        await Assert.That(project.Descendants("ProjectReference").Any()).IsFalse();
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
        // Must be run on its own (--treenode-filter for this method) to mean anything:
        // the registry resolves a bare name through Type.GetType (calling assembly and
        // CoreLib only) and then the loaded-assembly scan, so it only exercises the
        // fallback that loads Poly's referenced assemblies when nothing has yet touched
        // a Poly.Runtime type in this process. Inside the full suite another test may
        // have loaded that assembly already, and then this passes for the wrong reason.
        var resolved = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition("Poly.DomainModeling.Runtime.IStoragePort");

        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.Name).IsEqualTo(nameof(IStoragePort));
        await Assert.That(resolved!.Namespace).IsEqualTo("Poly.DomainModeling.Runtime");
    }

    [Test]
    public async Task PolyRuntime_IsPackable_WhilePolyDependsOnIt() {
        var root = RepoRoot();
        var poly = XDocument.Load(Path.Combine(root, "Poly", "Poly.csproj"));
        var runtime = XDocument.Load(Path.Combine(root, "Poly.Runtime", "Poly.Runtime.csproj"));

        var reference = poly.Descendants("ProjectReference")
            .Single(element => ((string?)element.Attribute("Include"))?.Replace('\\', '/')
                .EndsWith("Poly.Runtime/Poly.Runtime.csproj", StringComparison.Ordinal) == true);

        // pack writes every ProjectReference into the nuspec as a dependency, whether or
        // not the referenced project is packable, so a private-absent, unpackable
        // Poly.Runtime is an NU1101 for every consumer. It must be one or the other.
        var privateAssetsAll = string.Equals(
            (string?)reference.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase);
        var runtimePackable = !string.Equals(
            ProjectProperty(runtime, "IsPackable"), "false", StringComparison.OrdinalIgnoreCase);

        await Assert.That(privateAssetsAll || runtimePackable).IsTrue();
        await Assert.That(ProjectProperty(runtime, "PackageId")).IsEqualTo("Poly.Runtime");
        await Assert.That(ProjectProperty(runtime, "Version")).IsEqualTo(ProjectProperty(poly, "Version"));
    }

    private static string? ProjectProperty(XDocument project, string name) =>
        project.Descendants(name).Select(element => element.Value.Trim()).FirstOrDefault();

    private static string RepoRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Poly.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root with Poly.slnx not found.");
    }
}