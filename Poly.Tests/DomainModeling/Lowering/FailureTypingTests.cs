using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.Interpretation.CSharp;
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

        await Assert.That(cs).Contains("Poly.Runtime.DomainFailure.Enum(");
        await Assert.That(cs).Contains(", \"Widget.Status\")");
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
}