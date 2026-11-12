using Microsoft.Extensions.Logging.Abstractions;

using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Libraries.Platform;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Mcp;
using Poly.Mcp.Sessions;
using Poly.Packs.Product;
using Poly.Packs.Sqlite;
using Poly.Packs.SqlServer;
using Poly.Tests.TestHelpers;

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
        var document = DomainSession.Create(tree.Uses, SessionOptions.Bare(ExtensionCatalog.Core));
        var authoring = LibrarySelection.Load(tree, SessionOptions.Bare(ExtensionCatalog.Core) with { Authoring = true });

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
        var seeded = DomainSession.Create(TreeWithoutUses().Uses, new SessionOptions(catalog, ["x"]));
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
        var session = LibrarySelection.Load(
            TreeWithoutUses(), SessionOptions.Bare(ExtensionCatalog.Core) with { Authoring = true });
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
            .Throws<InvalidOperationException>();
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
            DomainSession.Create(tree.Uses, new SessionOptions(catalog, ["x"])),
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

    // ---- C-03: one implicit library set. ----

    private static McpSessionStore NewMcpStore() => new(
        TimeProvider.System,
        Microsoft.Extensions.Options.Options.Create(new McpSessionOptions()),
        NullLogger<McpSessionStore>.Instance);

    /// <summary>
    /// C-03 R3 (A4): the implicit set is exactly <c>[storage, persistence]</c> for the
    /// constant, the polyc factory, an MCP session, and the DslCompiler's own seed path.
    /// </summary>
    [Test]
    public async Task ImplicitIds_AreExactlyStorageAndPersistence_ForMcpPolycAndDslCompiler() {
        string[] expected = ["storage", "persistence"];

        await Assert.That(ProductCatalog.ImplicitIds).IsEquivalentTo(expected);

        var polyc = global::Polyc.Options.CreateCompiler(null)
            .Compile("domain D\nA: entity {\n  X: Text\n}\n", "x.poly");
        await Assert.That(polyc.Session!.Extensions).IsEquivalentTo(expected);

        var store = NewMcpStore();
        var (_, state) = store.Create("D");
        await Assert.That(state.Domain.Extensions).IsEquivalentTo(expected);

        var dsl = new Poly.DslCompiler.DslCompiler().Compile(
            "domain Holder\n\nItem: entity {\n  Email: Text unique\n}\n", CompileMode.All, DbmsPack.Generic);
        await Assert.That(dsl.Success).IsTrue();
        var source = string.Join("\n", dsl.Files!.Select(file => file.Source));
        await Assert.That(source).Contains("ExistsWithEmail");
    }

    /// <summary>
    /// C-03 R4 (A2): polyc and an MCP session load the same libraries for one document —
    /// the implicit set when it lists no <c>uses</c>, and the document's own list when it does.
    /// </summary>
    [Test]
    public async Task PolycAndMcpSessions_LoadIdenticalLibrariesForOneDocument() {
        const string NoUses = "domain Doc\nA: entity {\n  X: Text\n}\n";
        var polyc = global::Polyc.Options.CreateCompiler(null).Compile(NoUses, "doc.poly");
        await Assert.That(polyc.Succeeded).IsTrue();

        var (id, _) = McpTestTools.Store.Create("Doc");
        await Assert.That(McpTestTools.Store.TryGet(id, out var created)).IsTrue();
        await Assert.That(created.Modeling.Extensions).IsEquivalentTo(polyc.Session!.Extensions);
        await Assert.That(created.Domain.Extensions).IsEquivalentTo(polyc.Session!.Extensions);

        const string UsesSqlite = "domain S\nuses sqlite\n\nA: entity {\n  X: Text\n}\n";
        var polycSqlite = global::Polyc.Options.CreateCompiler(null).Compile(UsesSqlite, "s.poly");
        await Assert.That(polycSqlite.Succeeded).IsTrue();
        await Assert.That(McpTestTools.Dsl.ApplyDsl(id, UsesSqlite).Success).IsTrue();
        await Assert.That(McpTestTools.Store.TryGet(id, out var applied)).IsTrue();
        await Assert.That(applied.Domain.Extensions).IsEquivalentTo(new[] { "sqlite" });
        await Assert.That(applied.Modeling.Extensions).IsEquivalentTo(polycSqlite.Session!.Extensions);
    }

    /// <summary>C-03 R5: a bare session loads no implicit libraries (C-05's entry point).</summary>
    [Test]
    public async Task BareSession_LoadsNoImplicitLibraries() {
        var session = DomainSession.ForExtensions([]);

        await Assert.That(session.Extensions).IsEmpty();
        await Assert.That(session.Options.ImplicitIds).IsEmpty();
    }

    /// <summary>
    /// C-03 R6 (A7): one rule per input kind, whatever the entry point. A blank id is an
    /// <see cref="ArgumentException"/>; a duplicate or unknown id is an
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    [Test]
    public async Task IdValidation_OneRulePerInputKind() {
        var catalog = ProductCatalog.Model;
        var options = new SessionOptions(catalog, []);

        await Assert.That(() => new SessionOptions(catalog, ["a", "a"]))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("'a'");
        await Assert.That(() => LibrarySelection.Load(["a", "a"], options))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("'a'");
        await Assert.That(() => LibrarySelection.Resolve(["a", "a"], options))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("'a'");

        await Assert.That(() => new SessionOptions(catalog, [" "]))
            .Throws<ArgumentException>();
        await Assert.That(() => LibrarySelection.Load([" "], options))
            .Throws<ArgumentException>();
        await Assert.That(() => LibrarySelection.Resolve([" "], options))
            .Throws<ArgumentException>();

        await Assert.That(() => LibrarySelection.Resolve(["nope"], options))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("nope");
    }

    /// <summary>
    /// C-03 R10 (A7): C-01's headline reload change — a domain reloaded against
    /// <see cref="ProductCatalog.Model"/> resolves the new id against Model, not Core.
    /// </summary>
    [Test]
    public async Task Model_Reload_KeepsTheModelCatalog() {
        var domain = new Domain("D", []) { Extensions = ["storage"] };
        var session = DomainSession.Open(domain, ProductCatalog.Model);

        var reloaded = session.WithDomain(domain with { Extensions = ["storage", "sqlite"] });

        await Assert.That(reloaded.Extensions).IsEquivalentTo(new[] { "storage", "sqlite" });
        await Assert.That(ReferenceEquals(reloaded.Options.Catalog, ProductCatalog.Model)).IsTrue();
    }

    // ---- C-02 carry-overs, tests only (A8/B9). ----

    /// <summary>
    /// C-02 carry-over 2: <c>ProvidersOf</c> reports the catalog key even when the
    /// compiler's own <see cref="IConstraintCompiler.LibraryId"/> differs.
    /// </summary>
    [Test]
    public async Task ProvidersOf_ReportsTheCatalogKey_NotTheCompilerLibraryId() {
        var catalog = new ExtensionCatalog().With(new AliasedCompilerLibrary("alias", "unique-alias"));

        var providers = catalog.ProvidersOf(new UniqueConstraint());

        await Assert.That(string.Join(",", providers)).IsEqualTo("alias");
    }

    /// <summary>
    /// C-02 carry-over 3: the probe registers once under contention, and a catalog from
    /// <c>With</c> re-probes. This is the red the B9 mutation
    /// (<c>ExecutionAndPublication</c> → <c>PublicationOnly</c>) produces.
    /// </summary>
    [Test]
    public async Task Probe_RegistersOnceUnderContention_AndWithChildReprobes() {
        var counting = new SlowCountingLibrary("counted");
        var catalog = new ExtensionCatalog().With(counting);

        Parallel.For(0, 32, i => { _ = catalog.Info("counted"); });
        await Assert.That(counting.Registrations).IsEqualTo(1);

        var child = catalog.With(new SlowCountingLibrary("other"));
        _ = child.Info("counted");
        await Assert.That(counting.Registrations).IsEqualTo(2);
    }

    /// <summary>
    /// C-02 carry-over 3 / FX3: a throwing <c>Register</c> propagates, the failure is cached
    /// (one registration), and <c>ProvidersOf</c> re-runs its own <c>Applies</c> per call.
    /// </summary>
    [Test]
    public async Task Probe_ThrowingRegister_PropagatesAndIsCached() {
        var throwing = new ThrowingLibrary("boom");
        var catalog = new ExtensionCatalog().With(throwing);

        await Assert.That(() => catalog.Info("boom"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("boom-probe");
        await Assert.That(() => catalog.Info("boom"))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("boom-probe");
        await Assert.That(throwing.Registrations).IsEqualTo(1);
    }

    private static ExtensionCatalog VendorCatalog() =>
        ProductCatalog.Catalog.With(new ProbeLibrary("x"));

    private static IReadOnlyList<string> ResolveWithVendor(
        string vendor, string[] uses, string[]? host = null) =>
        LibrarySelection.Resolve(uses, new SessionOptions(VendorCatalog(), ProductCatalog.ImplicitIds) {
            StorageVendor = vendor,
            HostIds = host ?? [],
        });

    /// <summary>
    /// C-04 A2: the host's storage vendor replaces <c>persistence</c> in place, is appended only
    /// when no storage compiler is listed, and never doubles a listed storage library.
    /// </summary>
    [Test]
    [Arguments("sqlite", new string[0], new string[0], new[] { "storage", "sqlite" })]
    [Arguments("sqlite", new[] { "temporal", "persistence" }, new string[0], new[] { "temporal", "sqlite" })]
    [Arguments("sqlite", new[] { "http" }, new string[0], new[] { "http", "sqlite" })]
    [Arguments("sqlite", new[] { "sqlserver" }, new string[0], new[] { "sqlserver" })]
    [Arguments("sqlite", new[] { "persistence", "sqlite" }, new string[0], new[] { "sqlite" })]
    [Arguments("sqlite", new[] { "sqlserver", "persistence" }, new string[0], new[] { "sqlserver" })]
    [Arguments("sqlite", new[] { "http" }, new[] { "x" }, new[] { "http", "x", "sqlite" })]
    [Arguments("persistence", new string[0], new string[0], new[] { "storage", "persistence" })]
    [Arguments("persistence", new[] { "http" }, new string[0], new[] { "http", "persistence" })]
    [Arguments("persistence", new[] { "temporal", "sqlite" }, new string[0], new[] { "temporal", "sqlite" })]
    [Arguments("persistence", new string[0], new[] { "sqlite" }, new[] { "storage", "sqlite" })]
    [Arguments("persistence", new string[0], new[] { "http" }, new[] { "storage", "persistence", "http" })]
    public async Task StorageVendor_ReplacesPersistenceInPlace_AppendsOnlyWhenNoStorageCompilerListed(
        string vendor, string[] uses, string[] host, string[] expected) {
        var ids = ResolveWithVendor(vendor, uses, host);

        await Assert.That(ids.SequenceEqual(expected)).IsTrue().Because(string.Join(",", ids));
    }

    /// <summary>C-04 A1: no vendor means the selection is unchanged.</summary>
    [Test]
    public async Task NoStorageVendor_LeavesTheSelectionUnchanged() {
        var options = new SessionOptions(VendorCatalog(), ProductCatalog.ImplicitIds);

        await Assert.That(LibrarySelection.Resolve([], options).SequenceEqual(["storage", "persistence"])).IsTrue();
        await Assert.That(LibrarySelection.Resolve(["http"], options).SequenceEqual(["http"])).IsTrue();
    }

    /// <summary>
    /// C-04 A1: host libraries join once, after the selection, and are validated like the
    /// implicit ids (a blank or repeated id is rejected; <c>with</c> is re-checked by Resolve).
    /// </summary>
    [Test]
    public async Task HostIds_AreAppendedOnce_AfterTheSelection() {
        var options = new SessionOptions(VendorCatalog(), []) { HostIds = ["x"] };

        await Assert.That(LibrarySelection.Resolve(["http"], options).SequenceEqual(["http", "x"])).IsTrue();
        await Assert.That(LibrarySelection.Resolve(["x", "http"], options).SequenceEqual(["x", "http"])).IsTrue();

        Assert.Throws<ArgumentException>(() => _ = new SessionOptions(VendorCatalog(), []) { HostIds = [" "] });
        Assert.Throws<InvalidOperationException>(() => _ = new SessionOptions(VendorCatalog(), []) { HostIds = ["x", "x"] });
        Assert.Throws<ArgumentException>(() =>
            LibrarySelection.Resolve([], options with { HostIds = [""] }));
        Assert.Throws<InvalidOperationException>(() =>
            LibrarySelection.Resolve([], options with { HostIds = ["x", "x"] }));
        await Assert.That(() => LibrarySelection.Resolve([], options with { HostIds = ["nope"] }))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// C-03 carry: <c>Applies</c> results are never cached, so a throwing <c>Applies</c> throws
    /// on every <c>ProvidersOf</c> call, and a vendor <c>Resolve</c> surfaces it too.
    /// </summary>
    [Test]
    public async Task ProvidersOf_ThrowingApplies_ThrowsOnEveryCall() {
        var catalog = new ExtensionCatalog().With(new ThrowingAppliesLibrary("angry"));

        for (var i = 0; i < 2; i++) {
            await Assert.That(() => catalog.ProvidersOf(new UniqueConstraint()))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("applies-boom");
        }
        var options = new SessionOptions(catalog, []) { StorageVendor = "angry" };
        await Assert.That(() => LibrarySelection.Resolve([], options))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("applies-boom");
    }

    /// <summary>C-04 F4: Resolve over its own output changes nothing, for every A2 row.</summary>
    [Test]
    [Arguments("sqlite", new string[0], new string[0])]
    [Arguments("sqlite", new[] { "temporal", "persistence" }, new string[0])]
    [Arguments("sqlite", new[] { "http" }, new string[0])]
    [Arguments("sqlite", new[] { "sqlserver" }, new string[0])]
    [Arguments("sqlite", new[] { "persistence", "sqlite" }, new string[0])]
    [Arguments("sqlite", new[] { "sqlserver", "persistence" }, new string[0])]
    [Arguments("sqlite", new[] { "http" }, new[] { "x" })]
    [Arguments("persistence", new string[0], new string[0])]
    [Arguments("persistence", new[] { "http" }, new string[0])]
    [Arguments("persistence", new[] { "temporal", "sqlite" }, new string[0])]
    [Arguments("persistence", new string[0], new[] { "sqlite" })]
    [Arguments("persistence", new string[0], new[] { "http" })]
    public async Task Resolve_IsIdempotent_OverItsOwnOutput(string vendor, string[] uses, string[] host) {
        var options = new SessionOptions(VendorCatalog(), ProductCatalog.ImplicitIds) {
            StorageVendor = vendor,
            HostIds = host,
        };
        var once = LibrarySelection.Resolve(uses, options);
        var twice = LibrarySelection.Resolve(once, options);

        await Assert.That(twice.SequenceEqual(once)).IsTrue().Because(string.Join(",", once) + " vs " + string.Join(",", twice));
    }

    /// <summary>C-04 F5: a storage vendor must be a storage library; anything else fails closed.</summary>
    [Test]
    public async Task StorageVendor_NotAStorageLibrary_FailsClosed() {
        var options = new SessionOptions(VendorCatalog(), ProductCatalog.ImplicitIds) { StorageVendor = "http" };

        await Assert.That(() => LibrarySelection.Resolve([], options))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("not a storage library");
    }

    /// <summary>C-04 F4: with no storage vendor, Resolve never probes the catalog's compilers.</summary>
    [Test]
    public async Task HostIds_WithoutAStorageVendor_DoNotProbeApplies() {
        var catalog = new ExtensionCatalog().With(new ThrowingAppliesLibrary("angry"));
        var options = new SessionOptions(catalog, []) { HostIds = ["angry"] };

        await Assert.That(LibrarySelection.Resolve([], options).SequenceEqual(["angry"])).IsTrue();
    }

    /// <summary>C-04 F8: a repeated host id names HostIds, not the implicit set.</summary>
    [Test]
    public async Task HostIds_RepeatedId_NamesTheProperty() {
        await Assert.That(() => _ = new SessionOptions(VendorCatalog(), []) { HostIds = ["x", "x"] })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("HostIds");
    }

    /// <summary>FX3: a throwing <c>Applies</c> propagates out of <c>ProvidersOf</c>.</summary>
    [Test]
    public async Task ProvidersOf_ThrowingApplies_Propagates() {
        var catalog = new ExtensionCatalog().With(new ThrowingAppliesLibrary("angry"));

        await Assert.That(() => catalog.ProvidersOf(new UniqueConstraint()))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("applies-boom");
    }

    /// <summary>
    /// FX7: the implicit set is read-only by type, not an array a caller can cast and mutate.
    /// </summary>
    [Test]
    public async Task ImplicitIds_IsNotACastableStringArray() {
        await Assert.That(ProductCatalog.ImplicitIds is string[]).IsFalse();
    }

    /// <summary>
    /// FX2 (C-05 B1): the replace rule applies to authoring too. MCP no longer adds
    /// <c>persistence</c> to the applied document, so the document names both libraries and the
    /// export still guards <c>unique</c> through the storage port. <c>uses storage</c> alone is
    /// <c>DMLIB001</c> at define time (pinned by
    /// <c>ConstraintCoverageTests.PolycMcp_PolyDefine_UsesStorageUnique_IsRejectedWithDMLIB001_AndKeepsTheRevision</c>).
    /// </summary>
    [Test]
    public async Task UsesStoragePlusPersistence_McpCSharpExport_GuardsUnique() {
        var (sessionId, _) = McpTestTools.Store.Create("StorageUnique");
        var applied = McpTestTools.Dsl.ApplyDsl(
            sessionId,
            "domain StorageUnique\nuses storage\nuses persistence\n\nAgent: entity {\n  Handle: Text unique\n}\n");
        await Assert.That(applied.Success).IsTrue();

        var first = McpTestTools.Runtime.CreateInstance(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(first.Success).IsTrue();
        var exported = McpTestTools.Oracle.ExportDomainToCSharp(sessionId);
        await Assert.That(exported.Success).IsTrue();

        var duplicate = McpTestTools.Runtime.CreateInstance(sessionId, "Agent", """{"Handle":"sam"}""");
        await Assert.That(duplicate.Success).IsFalse();
        await Assert.That(duplicate.Message).Contains("Unique constraint");
    }

    private sealed class AliasedCompilerLibrary(string id, string compilerLibraryId) : IDomainLibrary {
        public string Id => id;

        public void Register(SessionBuilder builder) =>
            builder.AddConstraintCompiler(new AliasedUniqueCompiler(compilerLibraryId));
    }

    private sealed class AliasedUniqueCompiler(string libraryId) : IConstraintCompiler {
        public string LibraryId => libraryId;

        public bool Applies(Constraint constraint) => constraint is UniqueConstraint;

        ConstraintContribution IConstraintCompiler.Compile(ConstraintCompileRequest request) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingAppliesLibrary(string id) : IDomainLibrary {
        public string Id => id;

        public void Register(SessionBuilder builder) =>
            builder.AddConstraintCompiler(new ThrowingAppliesCompiler());
    }

    private sealed class ThrowingAppliesCompiler : IConstraintCompiler {
        public string LibraryId => "throwing-applies";

        public bool Applies(Constraint constraint) =>
            throw new InvalidOperationException("applies-boom");

        ConstraintContribution IConstraintCompiler.Compile(ConstraintCompileRequest request) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// A library whose <c>Register</c> takes long enough that a non-serialized
    /// <c>Lazy</c> (the B9 <c>PublicationOnly</c> mutation) lets several concurrent
    /// callers run it.
    /// </summary>
    private sealed class SlowCountingLibrary(string id) : IDomainLibrary {
        private int _registrations;

        public string Id => id;

        public int Registrations => Volatile.Read(ref _registrations);

        public void Register(SessionBuilder builder) {
            _ = Interlocked.Increment(ref _registrations);
            Thread.Sleep(5);
        }
    }

    private sealed class ThrowingLibrary(string id) : IDomainLibrary {
        private int _registrations;

        public string Id => id;

        public int Registrations => Volatile.Read(ref _registrations);

        public void Register(SessionBuilder builder) {
            _ = Interlocked.Increment(ref _registrations);
            throw new InvalidOperationException("boom-probe");
        }
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