using Poly.Ast.Nodes;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.Grammar;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// #419: a library's <see cref="IDomainLibrary.Register"/> contributes a contract surface
/// (and optional per-endpoint AST); <c>uses &lt;id&gt; [version]</c> loads it before the
/// body parses and seeds it onto the domain as an <see cref="ImportedContract"/>.
/// </summary>
public sealed class ContractContributionTests {
    private static readonly Dictionary<string, Func<Block>> NoBodies =
        new(StringComparer.Ordinal);

    private static ImportedContract BillingSurface(string version = "v1") =>
        new(
            "Billing",
            ContractSourceKind.InternalDomain,
            "billing",
            version,
            [
                new ContractEndpoint(
                    "Charge",
                    ContractEndpointKind.Operation,
                    ContractEndpointDirection.Outbound,
                    new DomainTypeReference("ChargeRequest")),
                new ContractEndpoint(
                    "Refund",
                    ContractEndpointKind.Operation,
                    ContractEndpointDirection.Outbound,
                    new DomainTypeReference("Number")),
            ]) {
            Types = [
                new ValueType("ChargeRequest", [
                    new Property("Amount", new DomainTypeReference("Number"), []),
                ], []),
            ]
        };

    private static ExtensionCatalog Catalog(params IDomainLibrary[] libraries) {
        var catalog = new ExtensionCatalog();
        foreach (var library in libraries)
            catalog = catalog.With(library);
        return catalog;
    }

    private static Domain ParseWith(string poly, ExtensionCatalog catalog) {
        var tree = DslSyntaxTree.Parse(poly);
        var session = DomainSession.Create(tree.Uses, SessionOptions.Bare(catalog));
        var changes = new DomainResolver(tree, session).Resolve();
        var result = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded) {
            throw new InvalidOperationException(
                "Test domain failed to apply: " +
                string.Join("; ", result.Analysis.Diagnostics.Select(d => d.Message)));
        }
        return result.Root!;
    }

    [Test]
    public async Task AddContract_OutsideRegister_FailsClosed() {
        await Assert.That(() => SessionBuilder.CreateEmpty().AddContract(BillingSurface()))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Register");
    }

    [Test]
    public async Task AddContract_DuplicateSurfaceNameFromSecondLibrary_FailsClosed() {
        var catalog = Catalog(
            new SurfaceLibrary("one", BillingSurface()),
            new SurfaceLibrary("two", BillingSurface("v2")));

        await Assert.That(() => DomainSession.ForExtensions(["one", "two"], catalog))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("already contributed");
    }

    [Test]
    public async Task AddContract_BodyForUnknownEndpoint_FailsClosed() {
        var bodies = new Dictionary<string, Func<Block>>(StringComparer.Ordinal) {
            ["Nope"] = () => new Block([Return.True]),
        };
        var library = new SurfaceLibrary("stripe", BillingSurface(), bodies);

        await Assert.That(() => DomainSession.ForExtensions(["stripe"], Catalog(library)))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Nope");
    }

    [Test]
    public async Task AddContract_DuplicateEndpoint_FailsClosed() {
        var surface = BillingSurface() with {
            Endpoints = [
                new ContractEndpoint(
                    "Charge", ContractEndpointKind.Operation, ContractEndpointDirection.Outbound,
                    new DomainTypeReference("Number")),
                new ContractEndpoint(
                    "Charge", ContractEndpointKind.Event, ContractEndpointDirection.Inbound,
                    new DomainTypeReference("Number")),
            ],
        };

        await Assert.That(() => DomainSession.ForExtensions(
                ["stripe"], Catalog(new SurfaceLibrary("stripe", surface))))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("more than once");
    }

    [Test]
    public async Task WithSeed_SeedLibraryContributesSurface_SeedsIt() {
        var session = DomainSession.ForExtensions(
            ["billing"], Catalog(new SurfaceLibrary("billing", BillingSurface())));
        var changes = DomainCompilation.WithSeed(
            new PolyDslParser("domain Shop\n\nOrder: entity { Total: Number }\n", session).Parse(),
            ["billing"],
            session);

        var result = new DomainEvolution(new Domain("_", [])).ApplyAndAnalyze(changes, session: session);

        await Assert.That(result.Succeeded).IsTrue();
        var contract = result.Root!.ImportedContracts.Single();
        await Assert.That(contract.UsesId).IsEqualTo("billing");
        await Assert.That(contract.Pin is null).IsTrue();
    }

    [Test]
    public async Task Register_ContributedSurfaceAndBodies_SessionHoldsThemBeforeTheBody() {
        var block = new Block([new Return(new Constant(1))]);
        var bodies = new Dictionary<string, Func<Block>>(StringComparer.Ordinal) {
            ["Charge"] = () => block,
        };
        var library = new SurfaceLibrary("stripe", BillingSurface(), bodies);

        var session = DomainSession.ForExtensions(["stripe"], Catalog(library));

        var contribution = session.ContractContributions.Single();
        await Assert.That(contribution.LibraryId).IsEqualTo("stripe");
        await Assert.That(contribution.Surface.Name).IsEqualTo("Billing");
        await Assert.That(contribution.Bodies["Charge"]()).IsSameReferenceAs(block);
        await Assert.That(session.FindContractContribution("stripe")).IsSameReferenceAs(contribution);
        await Assert.That(session.FindContractContribution("billing") is null).IsTrue();
    }

    [Test]
    public async Task Uses_SeedsContributedSurface_WithUsesIdAndNoPin() {
        var domain = ParseWith("""
            domain Shop
            uses billing

            Order: entity { Total: Number }
            """, Catalog(new SurfaceLibrary("billing", BillingSurface())));

        var contract = domain.ImportedContracts.Single();
        await Assert.That(contract.Name).IsEqualTo("Billing");
        await Assert.That(contract.UsesId).IsEqualTo("billing");
        await Assert.That(contract.Pin is null).IsTrue();
        await Assert.That(contract.Version).IsEqualTo("v1");
        await Assert.That(contract.Endpoints.Select(e => e.Name)).IsEquivalentTo(new[] { "Charge", "Refund" });
        await Assert.That(contract.Types.Select(t => t.Name)).IsEquivalentTo(new[] { "ChargeRequest" });
    }

    [Test]
    public async Task Uses_WithPin_RoundTripsAndDoesNotPrintTheSeededBlock() {
        const string poly = """
            domain Shop
            uses billing 2024-11-20

            Order: entity { Total: Number }
            """;
        var catalog = Catalog(new SurfaceLibrary("billing", BillingSurface(version: "2024-11-20")));
        var domain = ParseWith(poly, catalog);

        var contract = domain.ImportedContracts.Single();
        await Assert.That(contract.Pin).IsEqualTo("2024-11-20");
        await Assert.That(contract.Version).IsEqualTo("2024-11-20");

        var printed = new DomainDslPrinter(DomainSession.Open(domain, catalog)).Print(domain);
        await Assert.That(printed.Contains("uses billing 2024-11-20", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains("Billing: contract", StringComparison.Ordinal)).IsFalse();

        var reparsed = ParseWith(printed, catalog);
        var again = reparsed.ImportedContracts.Single();
        await Assert.That(again.Pin).IsEqualTo("2024-11-20");
        await Assert.That(again.Version).IsEqualTo("2024-11-20");
        await Assert.That(new DomainDslPrinter(DomainSession.Open(reparsed, catalog)).Print(reparsed))
            .IsEqualTo(printed);
    }

    [Test]
    public async Task Uses_WithoutPin_PrintsWithoutVersion() {
        const string poly = """
            domain Shop
            uses billing

            Order: entity { Total: Number }
            """;
        var catalog = Catalog(new SurfaceLibrary("billing", BillingSurface()));
        var domain = ParseWith(poly, catalog);

        var printed = new DomainDslPrinter(DomainSession.Open(domain, catalog)).Print(domain);
        var usesLine = printed.Split('\n').Single(line => line.StartsWith("uses ", StringComparison.Ordinal)).TrimEnd('\r');
        await Assert.That(usesLine).IsEqualTo("uses billing");
    }

    [Test]
    public async Task Uses_ContractBlockBesideSeed_FailsClosed() {
        const string poly = """
            domain Shop
            uses billing

            Billing: contract internal billing v1 {}

            Order: entity { Total: Number }
            """;

        var ex = Assert.Throws<GrammarException>(() =>
            ParseWith(poly, Catalog(new SurfaceLibrary("billing", BillingSurface()))));
        await Assert.That(ex.Message.Contains("contributed by", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Uses_SecondAuthoredContractBlockWithSameName_FailsClosed() {
        const string poly = """
            domain Shop

            Billing: contract external billing v1 {}
            Billing: contract external billing v2 {}

            Order: entity { Total: Number }
            """;

        var ex = Assert.Throws<GrammarException>(() => ParseWith(poly, Catalog()));
        await Assert.That(ex.Message.Contains("already declared", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Uses_PinOnIdWithoutContribution_FailsClosed() {
        var session = DomainSession.ForExtensions([ExtensionCatalog.TemporalId]);

        var ex = Assert.Throws<GrammarException>(() =>
            new PolyDslParser("domain Shop\nuses temporal 2024-11-20\n", session).Parse());
        await Assert.That(ex.Message.Contains("contributed no contract", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task Uses_TwoDomainsUsingEachOther_LoadAndResolve() {
        var alpha = new SurfaceLibrary("alpha", new ImportedContract(
            "Alpha", ContractSourceKind.InternalDomain, "alpha", "v1",
            [new ContractEndpoint("Do", ContractEndpointKind.Operation, ContractEndpointDirection.Outbound, new DomainTypeReference("Number"))]));
        var beta = new SurfaceLibrary("beta", new ImportedContract(
            "Beta", ContractSourceKind.InternalDomain, "beta", "v1",
            [new ContractEndpoint("Ping", ContractEndpointKind.Operation, ContractEndpointDirection.Outbound, new DomainTypeReference("Text"))]));
        var catalog = Catalog(alpha, beta);

        var a = ParseWith("""
            domain A
            uses beta

            AThing: entity { N: Number }
            """, catalog);
        var b = ParseWith("""
            domain B
            uses alpha

            BThing: entity { T: Text }
            """, catalog);

        await Assert.That(a.ImportedContracts.Single().Name).IsEqualTo("Beta");
        await Assert.That(a.ImportedContracts.Single().UsesId).IsEqualTo("beta");
        await Assert.That(b.ImportedContracts.Single().Name).IsEqualTo("Alpha");
        await Assert.That(b.ImportedContracts.Single().UsesId).IsEqualTo("alpha");
    }

    [Test]
    public async Task Uses_VersionRun_ReadsBareAndPunctuatedTokens() {
        var tree = DslSyntaxTree.Parse("""
            domain Shop
            uses billing 2024-11-20
            uses stripe v1
            uses acme 1.2.3

            Order: entity { Total: Number }
            """);

        await Assert.That(tree.Uses).IsEquivalentTo(new[] { "billing", "stripe", "acme" });
        await Assert.That(tree.UsesVersions.Select(version => version ?? "<none>"))
            .IsEquivalentTo(new[] { "2024-11-20", "v1", "1.2.3" });

        var unpinned = DslSyntaxTree.Parse("""
            domain Shop
            uses billing

            Order: entity { Total: Number }
            """);
        await Assert.That(unpinned.UsesVersions.Single() is null).IsTrue();
    }

    private sealed class SurfaceLibrary(
        string id,
        ImportedContract surface,
        IReadOnlyDictionary<string, Func<Block>>? bodies = null) : IDomainLibrary {
        public string Id => id;

        public void Register(SessionBuilder builder) => builder.AddContract(surface, bodies ?? NoBodies);
    }
}