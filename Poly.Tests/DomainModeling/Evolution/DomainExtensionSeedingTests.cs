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
        var compiler = new PolyCompiler(ExtensionCatalog.Core.With(new WidgetsLibrary()), new List<string>());

        var result = compiler.Analyze("domain D\nuses widgets\n", "w.poly");

        await Assert.That(result.Domain).IsNotNull();
        await Assert.That(result.Domain!.Types.OfType<PrimitiveType>().Any(p => p.Name == "Gizmo")).IsTrue();
    }
}