using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// C-01 session options and library selection. T2a pins the shape C-01 must
/// preserve; T1/T4/T5 record the <see cref="DomainSession.WithDomain"/> reload
/// bugs (the reload falls back to <see cref="ExtensionCatalog.Core"/>).
/// Step 1 asserts only no-throw, <c>Extensions</c>, <c>Target</c> and
/// <c>CanAccept</c>; the <c>Options.*</c> assertions arrive with the API (B3).
/// </summary>
public sealed class LibrarySelectionTests {
    private const string DomainWithoutUses = """
        domain T
        Item: entity { Name: Text }
        """;

    private static DslSyntaxTree TreeWithoutUses() => DslSyntaxTree.Parse(DomainWithoutUses);

    private static ExtensionCatalog ProbeCatalog() =>
        new ExtensionCatalog().With(new ProbeLibrary("x", new ProbeAnnotation("xmark")));

    /// <summary>
    /// Green on master: the two document factories load the same libraries and
    /// differ only by the facet pack, which never becomes an extension id.
    /// </summary>
    [Test]
    public async Task DocumentFactories_ShareIdsAndContributions_DifferOnlyByAuthoring() {
        var tree = TreeWithoutUses();
        var document = DomainSession.ForDocument(tree, []);
        var authoring = DomainSession.ForAuthoringDocument(tree, []);

        await Assert.That(document.Extensions).IsEmpty();
        await Assert.That(authoring.Extensions).IsEmpty();
        await Assert.That(document.Contributions).IsEquivalentTo(authoring.Contributions);

        await Assert.That(document.Extensions.Contains("storage")).IsFalse();
        await Assert.That(authoring.Extensions.Contains("storage")).IsFalse();

        await Assert.That(document.Annotations.CanAccept("column")).IsFalse();
        await Assert.That(authoring.Annotations.CanAccept("column")).IsTrue();
        await Assert.That(document.Annotations.CanAccept("table")).IsFalse();
        await Assert.That(authoring.Annotations.CanAccept("table")).IsTrue();
    }

    /// <summary>
    /// Red on master: the reload resolves <c>x</c> against
    /// <see cref="ExtensionCatalog.Core"/>, which cannot, and throws.
    /// </summary>
    [Test]
    public async Task WithDomain_AfterUsesChange_KeepsTheSessionCatalog() {
        var catalog = ProbeCatalog();
        var session = DomainSession.ForExtensions([], catalog);

        var reloaded = session.WithDomain(new Domain("D", []) { Extensions = ["x"] });

        await Assert.That(reloaded.Extensions).IsEquivalentTo(new[] { "x" });
        await Assert.That(reloaded.Target).IsNull();
    }

    /// <summary>
    /// Red on master (B2): the empty-uses reload itself succeeds, but it loses the
    /// catalog, so the next uses change cannot resolve <c>x</c>. The
    /// <c>Options.ImplicitIds</c> assertion arrives with step 3 (B2/B3).
    /// </summary>
    [Test]
    public async Task WithDomain_EmptyUsesAfterSeed_LoadsNoImplicitIds() {
        var catalog = ProbeCatalog();
        var seeded = DomainSession.ForDocument(TreeWithoutUses(), ["x"], catalog);
        await Assert.That(seeded.Extensions).IsEquivalentTo(new[] { "x" });

        var emptied = seeded.WithDomain(new Domain("D", []));
        // Explicit ids: a reload never re-applies the implicit seed.
        await Assert.That(emptied.Extensions).IsEmpty();

        // The reload keeps the catalog, so this later change still resolves 'x'.
        var reloaded = emptied.WithDomain(new Domain("D", []) { Extensions = ["x"] });
        await Assert.That(reloaded.Extensions).IsEquivalentTo(new[] { "x" });
    }

    /// <summary>
    /// Red on master: the reload is a plain library load, so the facet pack is
    /// dropped and <c>column</c>/<c>table</c> stop being accepted.
    /// </summary>
    [Test]
    public async Task WithDomain_AfterUsesChange_KeepsTheAuthoringPack() {
        var session = DomainSession.ForAuthoringDocument(TreeWithoutUses(), []);
        await Assert.That(session.Annotations.CanAccept("column")).IsTrue();

        var reloaded = session.WithDomain(new Domain("D", []) { Extensions = ["temporal"] });

        await Assert.That(reloaded.Annotations.CanAccept("column")).IsTrue();
        await Assert.That(reloaded.Annotations.CanAccept("table")).IsTrue();
        await Assert.That(reloaded.Extensions).IsEquivalentTo(new[] { "temporal" });
        await Assert.That(reloaded.Extensions.Contains("storage")).IsFalse();
    }

    /// <summary>
    /// The selection rule: a non-empty <c>uses</c> list replaces the implicit set
    /// outright; an empty one falls back to it (C-01 step 2).
    /// </summary>
    [Test]
    public async Task Resolve_NonEmptyUses_ReplacesImplicitIds() {
        var catalog = new ExtensionCatalog()
            .With(new ProbeLibrary("x"))
            .With(new ProbeLibrary("y"));
        var options = new SessionOptions(catalog, ["x"]);

        await Assert.That(LibrarySelection.Resolve([], options)).IsEquivalentTo(new[] { "x" });
        await Assert.That(LibrarySelection.Resolve(["y"], options)).IsEquivalentTo(new[] { "y" });
    }

    /// <summary>
    /// Value validation for the new types: the record rejects a null catalog, a
    /// blank id and a repeated id; <c>Resolve</c> re-checks <c>documentUses</c>.
    /// </summary>
    [Test]
    public async Task SessionOptions_BlankIdOrDuplicate_Throws() {
        var catalog = new ExtensionCatalog();
        await Assert.That(() => new SessionOptions(catalog, ["storage", " "]))
            .Throws<ArgumentException>();
        await Assert.That(() => new SessionOptions(catalog, ["a", "a"]))
            .Throws<ArgumentException>();
        await Assert.That(() => new SessionOptions(null!, []))
            .Throws<ArgumentNullException>();

        var options = new SessionOptions(catalog, []);
        await Assert.That(() => LibrarySelection.Resolve(["a", "a"], options))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("'a'");
        await Assert.That(() => LibrarySelection.Resolve(["nope"], options))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("nope");
        await Assert.That(() => LibrarySelection.Resolve([" "], options))
            .Throws<ArgumentException>();
    }

    private sealed class ProbeLibrary(string id, ProbeAnnotation? annotation = null) : IDomainLibrary {
        public string Id => id;

        public void Register(SessionBuilder builder) {
            if (annotation is not null)
                builder.Annotations.Register(annotation);
        }
    }

    private sealed class ProbeAnnotation(string keyword) : IAnnotationSyntax {
        public string Keyword => keyword;

        public bool TryPrint(Facet facet, out string text) {
            text = string.Empty;
            return false;
        }
    }
}