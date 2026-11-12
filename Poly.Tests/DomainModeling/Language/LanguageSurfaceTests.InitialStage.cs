using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// #675 / Q21: an entity may designate its initial stage in the header,
/// <c>Order: entity initial(Active) { … }</c>. With no designation the first declared stage is
/// initial, unchanged. The designation is a header clause printed before <c>key(...)</c>, and a
/// second one, an empty one, or one placed after <c>key(...)</c> fails closed at parse.
/// </summary>
public sealed partial class LanguageSurfaceTests {
    /// <summary>
    /// The guide's §5 example, character for character: the printed form of the document and the
    /// fence in <c>Poly.Mcp/Docs/poly-dsl-guide.md</c> are the same text.
    /// </summary>
    internal const string DesignatedDocument = """
        domain Orders

        Order: entity initial(Active) {
          Number: Text
          Status: Text optional
          Draft: stage {
            Activate: action {
              transition to Active
            }
          }
          Active: stage {
            entry {
              assign Status to "entered_active"
            }
          }
        }
        """;

    private static (Domain Domain, AnalysisResult Analysis) InitialStageEvolve(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        return (result.Root!, result.Analysis);
    }

    private static string InitialStagePrint(Domain domain) => new DomainDslPrinter().Print(domain);

    private static GrammarException InitialStageParseFails(string poly) =>
        Assert.Throws<GrammarException>(() => new PolyDslParser(poly).Parse());

    [Test]
    public async Task InitialStage_Designated_RoundTripsIdempotent() {
        var (domain, analysis) = InitialStageEvolve(DesignatedDocument);
        await Assert.That(analysis.HasErrors).IsFalse();
        var printed = InitialStagePrint(domain);
        await Assert.That(printed).Contains("Order: entity initial(Active) {");

        var (reparsed, reanalysis) = InitialStageEvolve(printed);
        await Assert.That(reanalysis.HasErrors).IsFalse();
        await Assert.That(InitialStagePrint(reparsed)).IsEqualTo(printed);
    }

    [Test]
    public async Task InitialStage_Duplicate_IsPositionedParseError() {
        var ex = InitialStageParseFails(
            "domain Orders\nOrder: entity initial(Draft) initial(Active) {\n  Draft: stage {}\n  Active: stage {}\n}\n");

        await Assert.That(ex.Detail).Contains("more than one 'initial(...)' clause");
        await Assert.That(ex.Line).IsEqualTo(2);
        await Assert.That(ex.Column).IsEqualTo(30).Because("the second 'initial' starts the failure");
    }

    [Test]
    public async Task InitialStage_EmptyOrNonIdentifier_IsPositionedParseError() {
        var empty = InitialStageParseFails("domain Orders\nOrder: entity initial() {\n  Draft: stage {}\n}\n");
        await Assert.That(empty.Line).IsEqualTo(2);
        await Assert.That(empty.Column).IsGreaterThan(0);

        var number = InitialStageParseFails("domain Orders\nOrder: entity initial(1) {\n  Draft: stage {}\n}\n");
        await Assert.That(number.Line).IsEqualTo(2);
        await Assert.That(number.Column).IsGreaterThan(0);
    }

    [Test]
    public async Task InitialStage_AfterKeyClause_IsPositionedParseError() {
        var ex = InitialStageParseFails(
            "domain Orders\nOrder: entity key(Code) initial(Active) {\n  Code: Text\n  Active: stage {}\n}\n");

        await Assert.That(ex.Detail).Contains("must come before 'key(...)'");
        await Assert.That(ex.Line).IsEqualTo(2);
        await Assert.That(ex.Column).IsEqualTo(25).Because("'initial' follows the key clause");
    }

    [Test]
    public async Task InitialStage_StageNamedInitial_StillParses() {
        // 'initial' is a stage body member here, not a header clause: header position only, and
        // only when followed by '('.
        var (domain, analysis) = InitialStageEvolve("domain Orders\nOrder: entity {\n  initial: stage { }\n}\n");
        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(InitialStageEntity(domain, "Order").Stages.Single().Name).IsEqualTo("initial");
        await Assert.That(InitialStagePrint(domain)).Contains("initial: stage");
    }

    [Test]
    public async Task InitialStage_SingleImplicitStage_IsPrintedAndAccepted() {
        const string poly = """
            domain Orders
            Order: entity initial(Ready) {
              Number: Text
              Ready: stage { }
            }
            """;
        var (domain, analysis) = InitialStageEvolve(poly);
        await Assert.That(analysis.HasErrors).IsFalse();
        var printed = InitialStagePrint(domain);
        await Assert.That(printed).Contains("initial(Ready)");
        await Assert.That(InitialStagePrint(InitialStageEvolve(printed).Domain)).IsEqualTo(printed);
    }

    private static Entity InitialStageEntity(Domain domain, string name) =>
        domain.Types.OfType<Entity>().Single(e => e.Name == name);

    /// <summary>
    /// Mutation only, then analysis: a domain whose designation is wrong is rejected by the evolution
    /// gate, so the proposal must be analyzed before it is discarded.
    /// </summary>
    private static (Domain Domain, AnalysisResult Analysis) InitialStageAnalyze(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var mutation = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!mutation.Succeeded)
            throw new InvalidOperationException(string.Join("; ", mutation.MutationErrors));
        return (mutation.Root, DomainModelAnalyzer.Analyze(mutation.Root));
    }

    [Test]
    public async Task InitialStage_UnknownStage_IsAnalysisError() {
        const string poly = """
            domain Orders
            Order: entity initial(Shipped) {
              Number: Text
              Draft: stage { }
              Active: stage { }
            }
            """;
        var (domain, analysis) = InitialStageAnalyze(poly);

        var finding = analysis.Diagnostics.Single(d => d.Code == "DMINI001");
        await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(finding.Message).Contains("Order");
        await Assert.That(finding.Message).Contains("Shipped");
        // Placed at the entity declaration, the DMSTR007 precedent, so the error points at the clause's owner.
        await Assert.That(finding.Node).IsNotNull();
        await Assert.That(((Entity)finding.Node!).Name).IsEqualTo("Order");

        var order = InitialStageEntity(domain, "Order");
        await Assert.That(order.InitialStage()!.Name).IsEqualTo("Draft").Because("the accessor falls back to the first declared stage");
        await Assert.That(ReferenceEquals(order.InitialStage(), order.Stages[0])).IsTrue();
    }

    [Test]
    public async Task InitialStage_DesignationNamingProperty_IsAnalysisError() {
        const string poly = """
            domain Orders
            Order: entity initial(Label) {
              Label: Text
              Active: stage { }
            }
            """;
        var (_, analysis) = InitialStageAnalyze(poly);

        var finding = analysis.Diagnostics.Single(d => d.Code == "DMINI001");
        await Assert.That(finding.Message).Contains("property");
    }

    [Test]
    public async Task InitialStage_OnStagelessEntity_IsAnalysisError() {
        const string poly = """
            domain Orders
            Order: entity initial(Active) {
              Number: Text
            }
            """;
        var (_, analysis) = InitialStageAnalyze(poly);

        await Assert.That(analysis.Diagnostics.Single(d => d.Code == "DMINI001").Severity).IsEqualTo(DiagnosticSeverity.Error);
    }

    /// <summary>
    /// An evolution change can remove the designated stage, leaving a dangling name. Analysis is what
    /// makes it visible; the accessor keeps the create path working meanwhile.
    /// </summary>
    [Test]
    public async Task InitialStage_RemovedStage_IsAnalysisError() {
        var (domain, _) = InitialStageEvolve(DesignatedDocument);
        var removed = new DomainEvolution(domain).Apply([new RemoveStageChange("Order", "Active")]);
        await Assert.That(removed.Succeeded).IsTrue();

        var analysis = DomainModelAnalyzer.Analyze(removed.Root);
        var finding = analysis.Diagnostics.Single(d => d.Code == "DMINI001");
        await Assert.That(finding.Message).Contains("Active");
        await Assert.That(InitialStageEntity(removed.Root, "Order").InitialStage()!.Name).IsEqualTo("Draft");
    }

    [Test]
    public async Task InitialStage_WellFormed_ProducesNoDiagnostic() {
        var (_, analysis) = InitialStageEvolve(DesignatedDocument);
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "DMINI001")).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "DMSTR010")).IsFalse();
    }

    [Test]
    public async Task InitialStage_Designated_SimCreateStartsInDesignatedStage() {
        var (domain, _) = InitialStageEvolve(DesignatedDocument);
        var created = ModuleExec.For(domain).Create("Order", new Dictionary<string, object?> { ["Number"] = "A-1" });

        await Assert.That(created.Succeeded).IsTrue();
        await Assert.That(created.Instance!.Bag["CurrentStage"]).IsEqualTo("Active");
        await Assert.That(created.Instance!.Bag["Status"]).IsEqualTo("entered_active");
    }

    [Test]
    public async Task InitialStage_NoDesignation_SimCreateStartsInFirstDeclaredStage() {
        var poly = DesignatedDocument.Replace("entity initial(Active) {", "entity {", StringComparison.Ordinal);
        var (domain, _) = InitialStageEvolve(poly);
        var created = ModuleExec.For(domain).Create("Order", new Dictionary<string, object?> { ["Number"] = "A-1" });

        await Assert.That(created.Succeeded).IsTrue();
        await Assert.That(created.Instance!.Bag["CurrentStage"]).IsEqualTo("Draft");
    }

    [Test]
    public async Task InitialStage_Designated_PrintedCreateAssignsDesignatedStage() {
        var (domain, analysis) = InitialStageEvolve(DesignatedDocument);
        var cs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(domain, analysis));

        await Assert.That(cs).Contains("this.CurrentStage = OrderStage.Active;");
        await Assert.That(cs).Contains("entered_active").Because("the designated stage's entry effects run in the constructor");
    }

    [Test]
    public async Task InitialStage_EntryAssignedProperty_UsesDesignatedStage() {
        var (domain, analysis) = InitialStageEvolve(DesignatedDocument);
        await Assert.That(analysis.HasErrors).IsFalse();

        var order = InitialStageEntity(domain, "Order");
        await Assert.That(EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(order).Contains("Status")).IsTrue();

        var control = DesignatedDocument.Replace("entity initial(Active) {", "entity {", StringComparison.Ordinal);
        var controlEntity = InitialStageEntity(InitialStageEvolve(control).Domain, "Order");
        await Assert.That(EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(controlEntity).Count).IsEqualTo(0);
    }
}