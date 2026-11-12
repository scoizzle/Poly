using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Libraries.Platform;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;
using Poly.Packs.Sqlite;
using Poly.Packs.SqlServer;

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
        await Assert.That(ReferenceEquals(reloaded.Options.Catalog, catalog)).IsTrue();
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
        await Assert.That(emptied.Options.ImplicitIds).IsEquivalentTo(new[] { "x" });

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

    /// <summary>
    /// T2b: the explicit-ids, document and wrapper entry points select the same
    /// implicit set and accept the same annotation. Compile-red (API absent) before C-01 step 3.
    /// </summary>
    [Test]
    public async Task ImplicitSetParity_AcrossLoadEntryPoints() {
        var catalog = ProbeCatalog();
        var options = new SessionOptions(catalog, ["x"]);
        var tree = TreeWithoutUses();

        var sessions = new[] {
            LibrarySelection.Load(tree, options),
            LibrarySelection.Load(["x"], options),
            DomainSession.ForDocument(tree, ["x"], catalog),
            DomainSession.Create([], options),
        };

        foreach (var session in sessions) {
            await Assert.That(session.Extensions).IsEquivalentTo(new[] { "x" });
            await Assert.That(session.Annotations.CanAccept("xmark")).IsTrue();
        }
    }

    /// <summary>The target lives only in <see cref="DomainSession.Options"/>; a reload keeps it.</summary>
    [Test]
    public async Task WithTarget_ThenReload_KeepsTargetInOptions() {
        var session = DomainSession.ForExtensions([], ProbeCatalog()).WithTarget("host");
        var reloaded = session.WithDomain(new Domain("D", []) { Extensions = ["x"] });

        await Assert.That(session.Options.Target).IsEqualTo("host");
        await Assert.That(reloaded.Target).IsEqualTo("host");
        await Assert.That(ReferenceEquals(reloaded.Options.Catalog, session.Options.Catalog)).IsTrue();
    }

    // ---- C-02: the catalog's read-only facts about the libraries it holds. ----

    private static readonly string[] ProductLikeIds = [
        "http", "persistence", "posix", "sqlite", "sqlserver", "storage", "temporal",
    ];

    /// <summary>
    /// The product catalog's library set, built locally so the assertions do not
    /// move when P-04 adds a vendor pack to <see cref="ProductCatalog"/>.
    /// </summary>
    private static ExtensionCatalog ProductLikeCatalog() => new ExtensionCatalog()
        .With(new TemporalLibrary())
        .With(new StorageFacetLibrary())
        .With(new PersistenceEmitLibrary())
        .With(new SqliteLibrary())
        .With(new SqlServerLibrary())
        .With(new HttpLibrary())
        .With(new PosixLibrary());

    /// <summary>
    /// The row's named test: one probe of the catalog answers targets, ids and
    /// which libraries compile a constraint kind.
    /// </summary>
    [Test]
    public async Task Info_ReportsTargetsAndConstraintProviders() {
        var catalog = ProductLikeCatalog();

        await Assert.That(string.Join(",", catalog.KnownTargets)).IsEqualTo("posix");
        await Assert.That(string.Join(",", catalog.KnownIds)).IsEqualTo(string.Join(",", ProductLikeIds));

        var providers = catalog.ProvidersOf(new UniqueConstraint());
        await Assert.That(providers).IsEquivalentTo(new[] { "persistence", "sqlite", "sqlserver" });
        await Assert.That(string.Join(",", providers)).IsEqualTo("persistence,sqlite,sqlserver");
    }

    /// <summary>T1: <c>Info</c> reports the targets, contract names and seeds of one library.</summary>
    [Test]
    public async Task Info_ReportsHostTargetsContractNamesAndSeeds() {
        var catalog = ProductLikeCatalog();

        var posix = catalog.Info("posix");
        await Assert.That(posix.Id).IsEqualTo("posix");
        await Assert.That(string.Join(",", posix.HostTargets)).IsEqualTo("posix");
        await Assert.That(string.Join(",", posix.ContractNames)).IsEqualTo("Posix");

        var sqlite = catalog.Info("sqlite");
        await Assert.That(sqlite.HostTargets).IsEmpty();
        await Assert.That(sqlite.ContractNames).IsEmpty();
        await Assert.That(sqlite.PrimitiveSeeds).IsEmpty();
    }

    /// <summary>T6: targets and contract names are distinct and ordinal-sorted; seeds keep library order.</summary>
    [Test]
    public async Task Info_SortsAndDeduplicatesTargetsAndContracts() {
        var catalog = new ExtensionCatalog()
            .With(new SortedProbeLibrary())
            .With(new SeedProbeLibrary());

        var sorted = catalog.Info("sorted");
        await Assert.That(string.Join(",", sorted.HostTargets)).IsEqualTo("posix,windows");
        await Assert.That(string.Join(",", sorted.ContractNames)).IsEqualTo("Alpha,Zeta");
        await Assert.That(string.Join(",", catalog.KnownTargets)).IsEqualTo("posix,windows");

        var seeded = catalog.Info("seeded");
        await Assert.That(string.Join(",", seeded.PrimitiveSeeds.Select(seed => seed.Name)))
            .IsEqualTo("Zeta,Alpha");
    }

    /// <summary>T2: an unknown id fails through <see cref="ExtensionCatalog.Resolve"/> and the message is shared.</summary>
    [Test]
    public async Task Info_UnknownId_ThrowsTheResolveMessage() {
        var catalog = ProductLikeCatalog();

        string? resolveMessage = null;
        try {
            catalog.Resolve("nope");
        }
        catch (InvalidOperationException ex) {
            resolveMessage = ex.Message;
        }
        await Assert.That(resolveMessage).IsNotNull();

        string? infoMessage = null;
        try {
            _ = catalog.Info("nope");
        }
        catch (InvalidOperationException ex) {
            infoMessage = ex.Message;
        }
        await Assert.That(infoMessage).IsEqualTo(resolveMessage);

        await Assert.That(() => catalog.Info(" ")).Throws<ArgumentException>();
        await Assert.That(() => catalog.ProvidersOf(null!)).Throws<ArgumentNullException>();
    }

    /// <summary>
    /// T4: <c>KnownIds</c> is the dictionary keys — it never probes. <c>KnownTargets</c>
    /// probes each library once, and the second read reuses the cache.
    /// </summary>
    [Test]
    public async Task KnownIds_NeverProbes() {
        var counting = new CountingLibrary("counted");
        var catalog = new ExtensionCatalog().With(counting);

        await Assert.That(string.Join(",", catalog.KnownIds)).IsEqualTo("counted");
        await Assert.That(counting.Registrations).IsEqualTo(0);

        await Assert.That(catalog.KnownTargets).IsEmpty();
        await Assert.That(counting.Registrations).IsEqualTo(1);

        _ = catalog.Info("counted");
        _ = catalog.KnownTargets;
        await Assert.That(counting.Registrations).IsEqualTo(1);
    }

    /// <summary>The Core library set's one constraint provider is <c>persistence</c>.</summary>
    [Test]
    public async Task CoreProvidersOf_Unique_IsPersistence() {
        var providers = ExtensionCatalog.Core.ProvidersOf(new UniqueConstraint());

        await Assert.That(string.Join(",", providers)).IsEqualTo("persistence");
    }

    /// <summary>A5: the product catalog is covered by containment only, so a new pack does not move it.</summary>
    [Test]
    public async Task ProductCatalogModel_ReportsContainedFacts() {
        var catalog = ProductCatalog.Model;

        foreach (var id in ProductLikeIds)
            await Assert.That(catalog.KnownIds.Contains(id)).IsTrue();
        await Assert.That(catalog.KnownTargets.Contains("posix")).IsTrue();
        await Assert.That(catalog.ProvidersOf(new UniqueConstraint()).Contains("persistence")).IsTrue();
    }

    private sealed class CountingLibrary(string id) : IDomainLibrary {
        public int Registrations { get; private set; }

        public string Id => id;

        public void Register(SessionBuilder builder) => Registrations++;
    }

    private sealed class SortedProbeLibrary : IDomainLibrary {
        public string Id => "sorted";

        public void Register(SessionBuilder builder) {
            builder.AddContract(
                new ImportedContract(
                    "Zeta", ContractSourceKind.ExternalProvider, "sorted", "1", []),
                targets: ["windows", "posix"]);
            builder.AddContract(
                new ImportedContract(
                    "Alpha", ContractSourceKind.ExternalProvider, "sorted", "1", []),
                targets: ["posix", "windows"]);
        }
    }

    private sealed class SeedProbeLibrary : IDomainLibrary {
        public string Id => "seeded";

        public IReadOnlyList<(string Name, PrimitiveSemantics Semantics)> PrimitiveSeeds =>
            [("Zeta", PrimitiveSemantics.Text), ("Alpha", PrimitiveSemantics.Number)];

        public void Register(SessionBuilder builder) { }
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