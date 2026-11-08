using Poly.Analysis;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Platform;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.Grammar;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// #424 (epic #427, spike #413 decisions 3 and 7): the host target is a session fact.
/// <c>uses posix</c> imports the package; it never chooses the target. Analysis refuses the
/// package when the host target is absent or not one the package declares, and the surface
/// digest stays the header only.
/// </summary>
public sealed class PlatformTargetTests {
    private const string ShopWithPosix = """
        domain Shop
        uses posix

        Order: entity { Total: Number }
        """;

    private const string ShopWithBilling = """
        domain Shop
        uses billing

        Order: entity { Total: Number }
        """;

    private const string ShopWithStorage = """
        domain Shop
        uses storage

        Order: entity { Total: Number }
        """;

    private static ExtensionCatalog PlatformCatalog(params IDomainLibrary[] extra) {
        var catalog = ExtensionCatalog.Core.With(new PosixLibrary());
        foreach (var library in extra)
            catalog = catalog.With(library);
        return catalog;
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(
        string poly, ExtensionCatalog catalog, string? target) {
        var tree = DslSyntaxTree.Parse(poly);
        var session = DomainSession.ForDocument(tree, [], catalog).WithTarget(target);
        var applied = new DomainEvolution(new Domain("_", []))
            .ApplyAndAnalyze(new DomainResolver(tree, session).Resolve(), session: session);
        return (applied.Root ?? new Domain("_", []), applied.Analysis, session);
    }

    private static IReadOnlyList<Diagnostic> Errors(AnalysisResult analysis, string code) =>
        analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == code)
            .ToList();

    [Test]
    public async Task UsesPosix_WindowsTarget_ReportsDmcon004NamingTheTarget() {
        var (_, analysis, session) = Evolve(ShopWithPosix, PlatformCatalog(), "windows");

        var errors = Errors(analysis, DomainModelDiagnosticCodes.PlatformTargetMismatch);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message)
            .Contains("posix")
            .And.Contains("windows");

        var ex = Assert.Throws<InvalidOperationException>(() => session.Lower(new Domain("Shop", []), analysis));
        await Assert.That(ex.Message).Contains("windows");
    }

    [Test]
    public async Task UsesPosix_PosixTarget_LoadsAndSeedsTheContribution() {
        var (domain, analysis, session) = Evolve(ShopWithPosix, PlatformCatalog(), "posix");

        await Assert.That(analysis.HasErrors).IsFalse();
        var contract = domain.ImportedContracts.Single();
        await Assert.That(contract.UsesId).IsEqualTo(PosixLibrary.LibraryId);
        await Assert.That(contract.Name).IsEqualTo("Posix");

        var contribution = session.FindContractContribution(PosixLibrary.LibraryId);
        await Assert.That(contribution is null).IsFalse();
        await Assert.That(contribution!.Targets).IsEquivalentTo(new[] { PosixLibrary.TargetId });
    }

    [Test]
    public async Task UsesPosix_NoHostTarget_ReportsDmcon004NamingTheAbsentFact() {
        var (_, analysis, _) = Evolve(ShopWithPosix, PlatformCatalog(), target: null);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.PlatformTargetMismatch);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("no target");
    }

    [Test]
    public async Task UsesLibraryWithoutDeclaredTargets_IsNeverAMismatch() {
        var billing = new SurfaceLibrary("billing", new ImportedContract(
            "Billing", ContractSourceKind.InternalDomain, "billing", "v1",
            [new ContractEndpoint(
                "Charge", ContractEndpointKind.Operation, ContractEndpointDirection.Outbound,
                new DomainTypeReference("Number"))]));

        var (_, analysis, _) = Evolve(ShopWithBilling, PlatformCatalog(billing), "windows");

        await Assert.That(analysis.HasErrors).IsFalse();
    }

    [Test]
    public async Task SurfaceDigest_IgnoresBodiesAndTargets_MovesOnASurfaceChange() {
        var withBodyA = SessionFor(
            new SurfaceLibrary(
                "posix",
                PosixCopy(addEndpoint: false),
                Bodies(new Poly.Ast.Nodes.Return(new Poly.Ast.Nodes.Constant(0))),
                targets: [PosixLibrary.TargetId]));
        var withBodyB = SessionFor(
            new SurfaceLibrary(
                "posix",
                PosixCopy(addEndpoint: false),
                Bodies(new Poly.Ast.Nodes.Return(new Poly.Ast.Nodes.Constant(1))),
                targets: ["windows"]));

        var a = withBodyA.ContractContributions.Single().Surface.Fingerprint;
        var b = withBodyB.ContractContributions.Single().Surface.Fingerprint;
        await Assert.That(b).IsEqualTo(a);

        var wider = SessionFor(
            new SurfaceLibrary("posix", PosixCopy(addEndpoint: true), targets: [PosixLibrary.TargetId]));
        await Assert.That(wider.ContractContributions.Single().Surface.Fingerprint).IsNotEqualTo(a);
    }

    [Test]
    public async Task PosixLibrary_IsHeaderOnly() {
        var session = SessionFor(new PosixLibrary());
        var contribution = session.ContractContributions.Single();

        await Assert.That(contribution.Surface.Name).IsEqualTo("Posix");
        await Assert.That(contribution.Surface.SourceKind).IsEqualTo(ContractSourceKind.ExternalProvider);
        await Assert.That(contribution.Bodies.Count).IsEqualTo(0);
        await Assert.That(contribution.Targets).IsEquivalentTo(new[] { PosixLibrary.TargetId });
        await Assert.That(contribution.Surface.Endpoints.Select(e => e.Name))
            .IsEquivalentTo(new[] { "Open", "Read", "Write", "Close" });
        await Assert.That(contribution.Surface.Types.Select(t => t.Name))
            .IsEquivalentTo(new[] { "OpenRequest", "ReadRequest", "WriteRequest", "CloseRequest" });
    }

    [Test]
    public async Task WithTarget_SurvivesBothWithDomainBranches() {
        var catalog = PlatformCatalog();
        var session = DomainSession.ForExtensions(["storage"], catalog).WithTarget("windows");

        var sameUses = Evolve(ShopWithStorage, catalog, "windows").Domain;
        await Assert.That(session.WithDomain(sameUses).Target).IsEqualTo("windows");

        var otherUses = new Domain("Other", []);
        await Assert.That(session.WithDomain(otherUses).Target).IsEqualTo("windows");
    }

    private static DomainSession SessionFor(IDomainLibrary library) =>
        DomainSession.ForExtensions([library.Id], new ExtensionCatalog().With(library));

    private static Dictionary<string, IReadOnlyList<Poly.Ast.Node>> Bodies(Poly.Ast.Node body) =>
        new(StringComparer.Ordinal) {
            ["Open"] = [body],
        };

    /// <summary>The POSIX surface, optionally with one more prototype, for digest comparisons.</summary>
    private static ImportedContract PosixCopy(bool addEndpoint) {
        var surface = new ImportedContract(
            "Posix", ContractSourceKind.ExternalProvider, PosixLibrary.LibraryId, PosixLibrary.SurfaceVersion,
            [
                new ContractEndpoint("Open", ContractEndpointKind.Operation, ContractEndpointDirection.Outbound, new DomainTypeReference("OpenRequest")),
                new ContractEndpoint("Close", ContractEndpointKind.Operation, ContractEndpointDirection.Outbound, new DomainTypeReference("CloseRequest")),
            ]) {
            Types = [
                new ValueType("OpenRequest", [
                    new Property("Path", new DomainTypeReference("Text"), []),
                ], []),
                new ValueType("CloseRequest", [
                    new Property("Descriptor", new DomainTypeReference("Number"), []),
                ], []),
            ],
        };
        return addEndpoint
            ? surface with {
                Endpoints = [
                    .. surface.Endpoints,
                    new ContractEndpoint("Read", ContractEndpointKind.Operation, ContractEndpointDirection.Outbound, new DomainTypeReference("Number")),
                ],
            }
            : surface;
    }

    private sealed class SurfaceLibrary(
        string id,
        ImportedContract surface,
        IReadOnlyDictionary<string, IReadOnlyList<Poly.Ast.Node>>? bodies = null,
        IReadOnlyList<string>? targets = null) : IDomainLibrary {
        public string Id => id;

        public void Register(SessionBuilder builder) =>
            builder.AddContract(surface, bodies, targets);
    }
}