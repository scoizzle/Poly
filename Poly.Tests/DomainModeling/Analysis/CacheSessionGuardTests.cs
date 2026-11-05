using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #398: `RuntimeAnalysisCache` keeps one compilation per <see cref="Domain"/>. Binding a session
/// that loads different libraries used to evict the module a caller was running — silently, so the
/// next consumer ran a lower without the previous session's constraint compilers. It now refuses.
/// <para>
/// The guard compares the session object, not the `uses` ids: two sessions can load the same ids
/// from different catalogs and lower differently, which was the round-3 hole. A distinct session is
/// refused even when its ids match, including the one `WithDomain` reopens from a domain.
/// </para>
/// </summary>
public class CacheSessionGuardTests {
    private const string Dsl = """
        domain Guard
        Widget: entity {
          Code: Text unique
        }
        """;

    [Test]
    public async Task BindingASessionWithDifferentLibraries_OverACachedModule_Throws() {
        var (domain, sessionA, analysisA) = Build(withPersistence: true);
        var moduleA = RuntimeAnalysisCache.GetOrLower(domain, sessionA, analysisA);
        await Assert.That(moduleA.Count).IsGreaterThan(0);

        var (_, sessionB, analysisB) = Rebuild(domain, withPersistence: false);

        await Assert.That(() => RuntimeAnalysisCache.GetOrLower(domain, sessionB, analysisB))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("already has a module");
    }

    /// <summary>
    /// The refusal must not cost the invariant it protects: the module the first session lowered is
    /// still the module consumers get afterwards.
    /// </summary>
    [Test]
    public async Task RefusedRebind_LeavesTheCachedModuleInPlace() {
        var (domain, sessionA, analysisA) = Build(withPersistence: true);
        var moduleA = RuntimeAnalysisCache.GetOrLower(domain, sessionA, analysisA);

        var (_, sessionB, analysisB) = Rebuild(domain, withPersistence: false);
        await Assert.That(() => RuntimeAnalysisCache.GetOrLower(domain, sessionB, analysisB))
            .Throws<InvalidOperationException>();

        await Assert.That(RuntimeAnalysisCache.TryGetModule(domain, out var stillThere)).IsTrue();
        await Assert.That(ReferenceEquals(stillThere, moduleA)).IsTrue()
            .Because("a refused bind must not replace or clear the module it refused to evict");
    }

    /// <summary>
    /// `WithDomain` is the shape a caller might assume is equivalent: it reopens from the domain's
    /// `uses`. It is not — the new session can load different libraries — so it is refused too.
    /// This is the case the round-3 review found: same ids, different catalog, module kept.
    /// </summary>
    [Test]
    public async Task ReopeningFromTheDomain_OverACachedModule_IsRefused() {
        var (domain, sessionA, analysisA) = Build(withPersistence: true);
        RuntimeAnalysisCache.GetOrLower(domain, sessionA, analysisA);

        var reopened = sessionA.WithDomain(domain);
        await Assert.That(() => RuntimeAnalysisCache.GetOrLower(domain, reopened, analysisA))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("already has a module");
    }

    [Test]
    public async Task RebindingTheSameSession_IsAllowed() {
        var (domain, sessionA, analysisA) = Build(withPersistence: true);
        var moduleA = RuntimeAnalysisCache.GetOrLower(domain, sessionA, analysisA);
        var again = RuntimeAnalysisCache.GetOrLower(domain, sessionA, analysisA);
        await Assert.That(ReferenceEquals(again, moduleA)).IsTrue();
    }

    private static (Domain Domain, DomainSession Session, AnalysisResult Analysis) Build(bool withPersistence) {
        var session = Session(withPersistence);
        var changes = new PolyDslParser(Dsl, session).Parse();
        var seeded = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!seeded.Succeeded)
            throw new InvalidOperationException(string.Join("; ", seeded.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return (seeded.Root!, session, seeded.Analysis);
    }

    /// <summary>Analyze without binding: this test is about what <c>Bind</c> refuses.</summary>
    private static (Domain Domain, DomainSession Session, AnalysisResult Analysis) Rebuild(
        Domain domain, bool withPersistence) {
        var session = Session(withPersistence);
        return (domain, session, session.AnalyzeWithoutBind(domain));
    }

    private static DomainSession Session(bool withPersistence) =>
        DomainSession.ForExtensions(
            withPersistence
                ? [.. ExtensionCatalog.ProductAuthoring, "persistence"]
                : [.. ExtensionCatalog.ProductAuthoring],
            ProductCatalog.Catalog);
}