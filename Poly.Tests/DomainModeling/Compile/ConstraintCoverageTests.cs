using System.Text.Json.Nodes;

using Poly.Compile;
using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;
using Poly.Tests.DomainModeling.Lowering;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// C-05 (#684): a constraint no loaded library compiles is a positioned analysis error
/// (<c>DMLIB001</c>), not a silent drop. <c>unique</c> is the one shipped record: its
/// guard is the storage port, so a document that lists <c>uses</c> without
/// <c>persistence</c> or a vendor library is refused at define time in <c>polyc</c>,
/// <c>poly_define</c> and the <c>DslCompiler</c> facade.
/// </summary>
public class ConstraintCoverageTests {
    private const string Document = """
        domain Holder

        Holder: entity {
          Email: Text unique
        }
        """;

    private static Domain Domain(out Property property) {
        property = new Property("Email", new DomainTypeReference("Text"), [new UniqueConstraint()]);
        var entity = new Entity("Holder", [property], [], [], []);
        // A bare builder: core's passes and compilers only, the catalog the session reports.
        return ValidDomain.Create("Holder", [entity]);
    }

    private static DomainSession BareSession() {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        return builder.Build();
    }

    [Test]
    public async Task UniqueWithoutPersistence_IsPositionedDMLIB001_NamingTheLibrary() {
        var domain = Domain(out var property);
        var analysis = BareSession().Analyze(domain);

        var errors = analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Code).IsEqualTo("DMLIB001");
        await Assert.That(errors[0].Message).IsEqualTo(
            "Constraint 'unique' on Holder.Email has no compiler: add `uses persistence`.");
        await Assert.That(ReferenceEquals(errors[0].Node, property)).IsTrue();
    }

    [Test]
    public async Task NoProviderInCatalog_ReportsNoLibraryHint() {
        var domain = Domain(out _);
        // A catalog with no library at all: no probe can name a fix, so the message says so.
        var session = LibrarySelection.Load([], SessionOptions.Bare(new ExtensionCatalog()));

        var analysis = session.Analyze(domain);

        var errors = analysis.Diagnostics.Where(d => d.Code == "DMLIB001").ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).IsEqualTo(
            "Constraint 'unique' on Holder.Email has no compiler: "
            + "no library in this session's catalog compiles it.");
    }

    [Test]
    public async Task ThrowingProbeCatalog_ReportsHintlessDMLIB001() {
        var domain = Domain(out _);
        var catalog = new ExtensionCatalog().With(new ThrowingRegisterLibrary("boom"));
        var session = LibrarySelection.Load([], SessionOptions.Bare(catalog));

        AnalysisResult analysis = null!;
        await Assert.That(() => analysis = session.Analyze(domain)).ThrowsNothing();

        var errors = analysis.Diagnostics.Where(d => d.Code == "DMLIB001").ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).IsEqualTo(
            "Constraint 'unique' on Holder.Email has no compiler.");
    }

    [Test]
    public async Task EveryRegisteredConstraintKind_IsClaimedMarkerOrCompiledBySomeLibrary() {
        var records = ConstraintSamples.Records();
        await Assert.That(records.Count).IsGreaterThan(0);

        foreach (var record in records) {
            var (_, sample) = ConstraintSamples.For(record);
            var kinds = ConstraintCheckWriter.KindsFor(sample);
            ConstraintCheckWriter.CoverageByConstraint.TryGetValue(record, out var coverage);
            var covered = kinds.Count > 0
                || coverage is { Marker: true }
                || ProductCatalog.Catalog.ProvidersOf(sample).Count > 0;

            await Assert.That(covered).IsTrue()
                .Because($"'{record.Name}' emits no writer kind, is not a marker, and no catalog library "
                    + "compiles it: Lower would drop it. Add a CoverageByConstraint row marked "
                    + "'marker: <why no guard exists>' or a library compiler.");
        }
    }

    [Test]
    public async Task SameDocument_IsCleanUnderImplicitIds_AndDMLIB001InABareSession() {
        var withImplicit = new PolyCompiler(ProductCatalog.Catalog, ProductCatalog.ImplicitIds).Analyze(Document);
        var bare = new PolyCompiler(ExtensionCatalog.Core, []).Analyze(Document);

        await Assert.That(ErrorCodes(withImplicit)).IsEmpty();

        var red = ErrorCodes(bare);
        await Assert.That(red.Count).IsEqualTo(1);
        await Assert.That(red[0]).IsEqualTo("DMLIB001");
    }

    [Test]
    public async Task LowerWithoutAnalysis_Throws() {
        var property = new Property("Email", new DomainTypeReference("Text"), [new UniqueConstraint()]);
        var entity = new Entity("Holder", [property], [], [], []);
        var domain = ValidDomain.Create("Holder", [entity], extensions: ["storage", "persistence"]);
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        builder.Load(ExtensionCatalog.Core.Resolve("storage"));
        builder.Load(new PersistenceEmitLibrary());
        var session = builder.Build();
        var analysis = session.Analyze(domain);
        var module = DomainProgramProjection.ToSyntax(domain, analysis);

        var thrown = Assert.Throws<InvalidOperationException>(() => ConstraintCompilation.Apply(
            module, domain, analysis, [new BuiltinConstraintCompiler()]));
        await Assert.That(thrown!.Message).Contains("'unique'");
        await Assert.That(thrown.Message).Contains("no compiler");

        // The control: the same module and analysis, with a compiler that applies.
        var applied = ConstraintCompilation.Apply(
            module, domain, analysis, [new BuiltinConstraintCompiler(), new UniqueConstraintCompiler("persistence")]);
        await Assert.That(applied.Module).IsNotEmpty();
    }

    [Test]
    public async Task TypeLevelConstraint_IsPositionedDMLIB001OnTheType() {
        var money = new ValueType("Money", [], [new UniqueConstraint()]);
        var domain = ValidDomain.Create("Holder", [money]);
        var analysis = BareSession().Analyze(domain);

        var errors = analysis.Diagnostics.Where(d => d.Code == "DMLIB001").ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(errors[0].Node, money)).IsTrue();
        await Assert.That(errors[0].Message).IsEqualTo(
            "Constraint 'unique' on Money has no compiler: add `uses persistence`.");
    }

    [Test]
    public async Task PolycMcp_PolyDefine_UsesStorageUnique_IsRejectedWithDMLIB001_AndKeepsTheRevision() {
        const string Rejected = """
            domain StorageOnly
            uses storage

            Agent: entity {
              Email: Text unique
            }
            """;
        const string Accepted = """
            domain StorageOnly
            uses storage
            uses persistence

            Agent: entity {
              Email: Text unique
            }
            """;

        using var host = await PolycProcess.McpAsync();
        await host.InitializeAsync();
        var opened = await host.ToolAsync("poly_workspace", new JsonObject { ["name"] = "StorageOnly" });
        var workspace = opened["state"]!["workspace"]!.GetValue<string>();
        var before = opened["state"]!["revision"]!.GetValue<long>();

        var rejected = await host.ToolAsync("poly_define", new JsonObject { ["workspace"] = workspace, ["poly"] = Rejected });
        await Assert.That(rejected["ok"]!.GetValue<bool>()).IsFalse();
        var findings = rejected["findings"]!.AsArray();
        var refusal = findings.FirstOrDefault(f =>
            string.Equals(f!["code"]?.GetValue<string>(), "DMLIB001", StringComparison.Ordinal)
            && string.Equals(f!["severity"]!.GetValue<string>(), "Error", StringComparison.OrdinalIgnoreCase));
        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!["message"]!.GetValue<string>()).Contains("uses persistence");
        await Assert.That(rejected["state"]!["revision"]!.GetValue<long>()).IsEqualTo(before);

        var accepted = await host.ToolAsync("poly_define", new JsonObject { ["workspace"] = workspace, ["poly"] = Accepted });
        await Assert.That(accepted["ok"]!.GetValue<bool>()).IsTrue();
        await Assert.That(accepted["state"]!["revision"]!.GetValue<long>()).IsEqualTo(before + 1);
    }

    [Test]
    public async Task Polyc_UsesHttpAlone_Unique_IsPlacedDMLIB001() {
        const string Text = "domain HttpOnly\nuses http\n\nAccount: entity {\n  Email: Text unique\n}\n";

        var result = new PolyCompiler(ProductCatalog.Catalog, ProductCatalog.ImplicitIds).Compile(Text, "http-only.poly");

        var errors = result.Diagnostics.Where(d => d.IsError).ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Code).IsEqualTo("DMLIB001");
        await Assert.That(errors[0].Placed).IsTrue();
        var expectedLine = Array.FindIndex(Text.Split('\n'), line => line.Contains("Email:", StringComparison.Ordinal)) + 1;
        await Assert.That(errors[0].Line).IsEqualTo(expectedLine);
        await Assert.That(errors[0].Column).IsEqualTo(3);
    }

    [Test]
    public async Task DslCompiler_Entities_UniqueWithoutPersistence_IsDMLIB001() {
        var compiler = new DslCompiler.DslCompiler();

        var rejected = compiler.Compile("domain Lab\n\nA: entity {\n  X: Text unique\n}\n");
        await Assert.That(rejected.Success).IsFalse();
        await Assert.That(rejected.Errors).IsNotNull();
        // The facade reports messages, not codes (it never has); the code is DMLIB001 at the
        // PolyCompiler seam, which test Polyc_UsesHttpAlone_Unique_IsPlacedDMLIB001 pins.
        await Assert.That(rejected.Errors!.Any(e => e.Contains("has no compiler", StringComparison.Ordinal))).IsTrue();
        await Assert.That(rejected.Errors.Any(e => e.Contains("uses persistence", StringComparison.Ordinal))).IsTrue();

        var accepted = compiler.Compile("domain Lab\nuses persistence\n\nA: entity {\n  X: Text unique\n}\n");
        await Assert.That(accepted.Success).IsTrue();
    }

    private static List<string> ErrorCodes(CompileResult result) =>
        [.. result.Diagnostics.Where(d => d.IsError).Select(d => d.Code)];

    /// <summary>A library whose probe throws: the hint cannot be computed, so the finding is hintless.</summary>
    private sealed class ThrowingRegisterLibrary(string id) : IDomainLibrary {
        public string Id => id;

        public void Register(SessionBuilder builder) =>
            throw new InvalidOperationException("probe-boom");
    }
}