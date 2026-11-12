using Poly.Compile;
using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Evolution;

/// <summary>
/// #464: extension primitive seeding consults the session that loaded the extension,
/// not <see cref="ExtensionCatalog.Core"/>.
/// </summary>
public class DomainExtensionSeedingTests {
    private sealed class WidgetsLibrary : IDomainLibrary {
        public string Id => "widgets";

        public IReadOnlyList<(string Name, PrimitiveSemantics Semantics)> PrimitiveSeeds =>
            [("Gizmo", PrimitiveSemantics.Text)];

        public void Register(SessionBuilder builder) { }
    }

    private static DomainSession WidgetsSession() {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        builder.Load(new WidgetsLibrary());
        return builder.Build();
    }

    [Test]
    public async Task Apply_SessionLibraryOutsideCore_SeedsItsPrimitive() {
        var session = WidgetsSession();

        var result = new DomainEvolution(new Domain("_", []))
            .Apply([new AddDomainExtensionChange("widgets")], session: session);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root!.Types.OfType<PrimitiveType>().Any(p => p.Name == "Gizmo")).IsTrue();
    }

    [Test]
    public async Task ApplyAndAnalyze_SessionLibraryOutsideCore_SeedsItsPrimitive() {
        var session = WidgetsSession();

        var result = new DomainEvolution(new Domain("_", []))
            .ApplyAndAnalyze([new AddDomainExtensionChange("widgets")], session: session);

        await Assert.That(result.Root!.Types.OfType<PrimitiveType>().Any(p => p.Name == "Gizmo")).IsTrue();
    }

    [Test]
    public async Task Apply_NoSession_StillSeedsCoreExtensionPrimitives() {
        var result = new DomainEvolution(new Domain("_", []))
            .Apply([new AddDomainExtensionChange("temporal")]);

        await Assert.That(result.Root!.Types.OfType<PrimitiveType>().Any(p => p.Name == "Date")).IsTrue();
    }

    [Test]
    public async Task Compile_UsesLibraryOutsideCore_SeedsItsPrimitive() {
        var catalog = ExtensionCatalog.Core.With(new WidgetsLibrary());
        var compiler = new PolyCompiler(catalog, SessionOptions.Bare(catalog));

        var result = compiler.Analyze("domain D\nuses widgets\n", "w.poly");

        await Assert.That(result.Domain).IsNotNull();
        await Assert.That(result.Domain!.Types.OfType<PrimitiveType>().Any(p => p.Name == "Gizmo")).IsTrue();
    }

    /// <summary>
    /// C-04 F1: a host-loaded library whose id the document does not list is stamped on the
    /// domain after the resolver ran. Its primitive must still be seeded, exactly once.
    /// </summary>
    [Test]
    public async Task Compile_HostLibraryWithSeed_UsesDocument_SeedsItsPrimitiveOnce() {
        var catalog = ExtensionCatalog.Core.With(new WidgetsLibrary());
        var compiler = new PolyCompiler(catalog, new SessionOptions(catalog, []) { HostIds = ["widgets"] });

        var result = compiler.Analyze("domain D\nuses temporal\n", "w.poly");

        await Assert.That(result.Succeeded).IsTrue()
            .Because(string.Join("; ", result.Diagnostics.Select(d => d.Code + " " + d.Message)));
        await Assert.That(result.Domain!.Types.OfType<PrimitiveType>().Count(p => p.Name == "Gizmo")).IsEqualTo(1);
    }

    /// <summary>
    /// C-04 F3: a host-loaded library's contract surface is seeded on the domain whether the
    /// document lists <c>uses</c> or not.
    /// </summary>
    [Test]
    [Arguments("domain D\n")]
    [Arguments("domain D\nuses temporal\n")]
    public async Task Compile_HostLibraryContract_IsSeededOnBothPaths(string text) {
        var catalog = ExtensionCatalog.Core.With(TestHelpers.ContractContributions.StripeLibrary());
        var compiler = new PolyCompiler(catalog, new SessionOptions(catalog, []) { HostIds = ["stripe"] });

        var result = compiler.Analyze(text, "c.poly");

        await Assert.That(result.Succeeded).IsTrue()
            .Because(string.Join("; ", result.Diagnostics.Select(d => d.Code + " " + d.Message)));
        await Assert.That(result.Domain!.ImportedContracts.Any(c => c.Name == "Stripe")).IsTrue();
        await Assert.That(result.Domain.Extensions.SequenceEqual(result.Session!.Extensions)).IsTrue();
    }
}