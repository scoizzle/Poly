using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Runtime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// R-04: every lowered failure carries its <c>Poly.Runtime.DomainFailureKind</c> and element path
/// instead of a bare string. Each case prints the lowered module and reads the failure call.
/// </summary>
public class FailureTypingTests {
    private static string Print(string dsl) {
        var session = ParitySessions.Create();
        var changes = new PolyDslParser(dsl, session).Parse();
        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);
        if (!evolved.Succeeded)
            throw new InvalidOperationException(string.Join("; ", evolved.Analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return new CSharpGenerator().Generate(session.Lower(evolved.Root!, evolved.Analysis));
    }

    [Test]
    [Arguments("Score: Number range(1, 10)", "Range(\"'Score' must be >= 1.\", \"Widget.Score\")")]
    [Arguments("Score: Number range(1, 10)", "Range(\"'Score' must be <= 10.\", \"Widget.Score\")")]
    [Arguments("Name: Text length(3, 8)", "Length(\"'Name' must be at least 3 characters.\", \"Widget.Name\")")]
    [Arguments("Name: Text length(3, 8)", "Length(\"'Name' must be at most 8 characters.\", \"Widget.Name\")")]
    [Arguments("Code: Text pattern(\"^[A-Z]+$\")", "Pattern(\"'Code' does not match the required pattern.\", \"Widget.Code\")")]
    [Arguments("Name: Text", "Required(\"'Name' is required.\", \"Widget.Name\")")]
    [Arguments("Status: Text equals(\"Active\")", "Constraint(\"'Status' must equal Active.\", \"Widget.Status\")")]
    public async Task ConstraintKinds_Create_EmitTheirOwnKindAndPath(string property, string expected) {
        var cs = Print($$"""
            domain Lab
            Widget: entity {
              {{property}}
              Poke: action (n: Number) { }
            }
            """);

        await Assert.That(cs).Contains(
            $"return DomainResult<Widget>.Failure(Poly.Runtime.DomainFailure.{expected});");
    }

    [Test]
    [Arguments("Score: Number range(1, 10) default(5)", "Number", "Range(\"'Score' must be >= 1.\", \"Widget.Score\")")]
    [Arguments("Name: Text length(3, 8) default(\"abcd\")", "Text", "Length(\"'Name' must be at least 3 characters.\", \"Widget.Name\")")]
    [Arguments("Code: Text pattern(\"^[A-Z]+$\") default(\"AB\")", "Text", "Pattern(\"'Code' does not match the required pattern.\", \"Widget.Code\")")]
    [Arguments("Name: Text default(\"x\")", "Text", "Required(\"'Name' is required.\", \"Widget.Name\")")]
    [Arguments("Status: Text equals(\"Active\") default(\"Active\")", "Text", "Constraint(\"'Status' must equal Active.\", \"Widget.Status\")")]
    public async Task ConstraintKinds_Assign_EmitTheirOwnKindAndPath(string property, string paramType, string expected) {
        var name = property[..property.IndexOf(':')];
        var cs = Print($$"""
            domain Lab
            Widget: entity {
              {{property}}
              Set: action (v: {{paramType}}) { assign {{name}} to v }
            }
            """);

        await Assert.That(cs).Contains($"return DomainResult.Failure(Poly.Runtime.DomainFailure.{expected});");
    }

    [Test]
    public async Task ConstraintKinds_Enum_EmitTheEnumKindAndPath() {
        var cs = Print("""
            domain Lab
            WidgetStatus: enum { Open, Closed }
            Widget: entity {
              Status: WidgetStatus
              Set: action (v: WidgetStatus) { assign Status to v }
            }
            """);

        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(
            cs, "Poly\\.Runtime\\.DomainFailure\\.Enum\\([^;]*, \"Widget\\.Status\"\\)\\)")).IsTrue();
        await Assert.That(cs).Contains("Poly.Runtime.DomainFailure.Required(\"'Status' is required.\", \"Widget.Status\")");
    }

    [Test]
    public async Task ConstraintKinds_Unique_EmitTheUniqueKindAndPath_OnCreateAndAssign() {
        var cs = Print("""
            domain Lab
            uses persistence

            Widget: entity {
              Sku: Text unique
              Rename: action (v: Text) { assign Sku to v }
            }
            """);

        const string call = "Poly.Runtime.DomainFailure.Unique(\"Unique constraint violated: 'Sku' value is already used on another 'Widget'.\", \"Widget.Sku\")";
        await Assert.That(cs).Contains($"return DomainResult<Widget>.Failure({call});");
        await Assert.That(cs).Contains($"return DomainResult.Failure({call});");
    }

    [Test]
    [Arguments("Required", DomainFailureKind.Required)]
    [Arguments("RangeMinimum", DomainFailureKind.Range)]
    [Arguments("RangeMaximum", DomainFailureKind.Range)]
    [Arguments("LengthMinimum", DomainFailureKind.Length)]
    [Arguments("LengthMaximum", DomainFailureKind.Length)]
    [Arguments("Pattern", DomainFailureKind.Pattern)]
    [Arguments("Equality", DomainFailureKind.Constraint)]
    [Arguments("EnumRequiredSlot", DomainFailureKind.Required)]
    [Arguments("EnumMember", DomainFailureKind.Enum)]
    public async Task FailureSpec_PerKind_HasTheFailureKindAndNoGuard(
        string kindName, DomainFailureKind expected) {
        var kind = Enum.Parse<ConstraintCheckWriter.Kind>(kindName);
        var check = new ConstraintCheckWriter.Check(kind, new Constant(true), null, new Constant("m"));

        var spec = ConstraintCheckWriter.FailureSpec(check, "Widget", "Score");

        await Assert.That(spec.Kind).IsEqualTo(expected);
        await Assert.That(spec.ElementPath).IsEqualTo("Widget.Score");
        await Assert.That(spec.Guard).IsNull();
    }

    [Test]
    public async Task FailureKindOf_IsTotalOverEveryCheckKind() {
        foreach (var kind in Enum.GetValues<ConstraintCheckWriter.Kind>())
            await Assert.That(() => ConstraintCheckWriter.FailureKindOf(kind)).ThrowsNothing();
    }

    [Test]
    [Arguments("Score: Number range(1, 10)", "Score", 0L, "'Score' must be >= 1.")]
    [Arguments("Score: Number range(1, 10)", "Score", 11L, "'Score' must be <= 10.")]
    [Arguments("Name: Text length(3, 8)", "Name", "ab", "'Name' must be at least 3 characters.")]
    [Arguments("Name: Text length(3, 8)", "Name", "abcdefghi", "'Name' must be at most 8 characters.")]
    [Arguments("Code: Text pattern(\"^[A-Z]+$\")", "Code", "abc", "'Code' does not match the required pattern.")]
    [Arguments("Name: Text", "Name", "", "'Name' is required.")]
    [Arguments("Status: Text equals(\"Active\")", "Status", "Other", "'Status' must equal Active.")]
    public async Task ConstraintKinds_OnBothSides_KeepTheLegacyMessageText(
        string property, string name, object value, string expected) {
        var dsl = $$"""
            domain Lab
            Widget: entity {
              {{property}}
            }
            """;

        var outcomes = await ParityScenario.FromDsl(dsl, "FailureTyping" + Guid.NewGuid().ToString("N"))
            .AssertAgree(side => side.Create("Widget", (name, value)));

        await Assert.That(outcomes[0].Success).IsFalse();
        await Assert.That(outcomes[0].Message).IsEqualTo(expected);
    }

    [Test]
    public async Task ConstraintKinds_Assign_OnBothSides_KeepTheLegacyMessageText() {
        const string dsl = """
            domain Lab
            Widget: entity {
              Score: Number range(1, 10) default(5)
              Set: action (v: Number) { assign Score to v }
            }
            """;

        var outcomes = await ParityScenario.FromDsl(dsl, "FailureTypingAssign")
            .AssertAgree(side => {
                side.Create("Widget");
                side.Invoke("Set", ("v", 11L));
            });

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).IsEqualTo("'Score' must be <= 10.");
    }

    [Test]
    public async Task ConstraintKinds_Unique_PrintedSide_KeepsTheLegacyMessageText() {
        const string dsl = """
            domain Lab
            uses persistence

            Widget: entity {
              Sku: Text unique
            }
            """;

        var (_, printed) = ParityScenario.FromDsl(dsl, "FailureTypingUnique")
            .Run(side => {
                side.Create("Widget", ("Sku", "A"));
                side.Create("Widget", ("Sku", "A"));
            });

        await Assert.That(printed[1].Success).IsFalse();
        await Assert.That(printed[1].Message).IsEqualTo(
            "Unique constraint violated: 'Sku' value is already used on another 'Widget'.");
    }

    [Test]
    public async Task PropertyNamedPoly_WithAConstraint_IsAReservedNameError() {
        var session = ParitySessions.Create();
        var changes = new PolyDslParser("""
            domain Lab
            Widget: entity {
              Poly: Text length(3, 8)
            }
            """, session).Parse();

        var evolved = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes, session: session);

        await Assert.That(evolved.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ReservedMemberName
            && d.Message.Contains("would shadow the generated C# type 'Poly'"))).IsTrue();
    }

    [Test]
    public async Task ConstraintKinds_AssignLength_OnBothSides_KeepTheLegacyMessageText() {
        const string dsl = """
            domain Lab
            Widget: entity {
              Name: Text length(3, 8) default("abcd")
              Set: action (v: Text) { assign Name to v }
            }
            """;

        var outcomes = await ParityScenario.FromDsl(dsl, "FailureTypingAssignLength")
            .AssertAgree(side => {
                side.Create("Widget");
                side.Invoke("Set", ("v", "ab"));
            });

        await Assert.That(outcomes[1].Success).IsFalse();
        await Assert.That(outcomes[1].Message).IsEqualTo("'Name' must be at least 3 characters.");
    }

    [Test]
    public async Task AssignConstraintFailure_TypedResultSite_NeverRoutesThroughThrow() {
        var entity = new Entity("Widget",
            Properties: [new Property("Score", new DomainTypeReference("Number"), [new RangeConstraint(1.0, 10.0)])],
            Actions: [], Policies: [], Stages: []);
        var assign = new AssignEffect(DomainExpression.Property("Score"), DomainExpression.Property("n"));

        var typed = new EffectLoweringPass(entity, LoweringFixtures.Context(new ThisReference(), entity)
            with { ActionResultType = new NamedTypeReference("DomainResult") }).TryLowerVmNode(assign);
        var voidBody = new EffectLoweringPass(entity, LoweringFixtures.Context(new ThisReference(), entity))
            .TryLowerVmNode(assign);

        await Assert.That(Walk(typed!).Any(n => n is ThrowStatement)).IsFalse();
        await Assert.That(Walk(voidBody!).Any(n => n is ThrowStatement)).IsTrue();
    }

    private static IEnumerable<Node> Walk(Node root) {
        yield return root;
        foreach (var child in root.Children.OfType<Node>())
            foreach (var node in Walk(child))
                yield return node;
    }
}