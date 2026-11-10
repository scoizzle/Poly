using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>Review repros for PR #455 (general for). Each asserts the expected-correct (fail-closed) behaviour.</summary>
public class ReviewPr455Tests {
    private static IReadOnlyList<string> Errors(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        var errors = result.Analysis.Diagnostics
            .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
            .Select(d => $"{d.Code}: {d.Message}").ToList();
        if (result.Succeeded && result.Root is not null) {
            errors.AddRange(DomainModelAnalyzer.Analyze(result.Root).Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => $"{d.Code}: {d.Message}"));
        }
        return errors;
    }

    private static string Domain(string forEffect) => $$"""
        domain Billing
        Owner: entity {
          Name: Text default("o")
        }
        Line: entity {
          Qty: Number
          Marks: Number default(0)
          Open: stage {
            Mark: action { assign Marks to Marks + 1 }
          }
        }
        Invoice: entity {
          Note: Text default("none")
          lines: many Line
          owner: one Owner
          Open: stage {
            Touch: action {
              {{forEffect}}
            }
          }
        }
        """;

    // Baseline: the shape the PR ships analyzes clean, so the repros below isolate one defect each.
    [Test]
    public async Task F0_GeneralFor_ValidShape_AnalyzesClean() {
        var errors = Errors(Domain("for line in lines where line Qty > 1 { invoke line.Mark }"));
        await Assert.That(errors).IsEmpty();
    }

    // F1: the relationship of a general for is never resolved.
    [Test]
    public async Task F1_GeneralFor_UnknownRelationship_IsAnalyzeError() {
        var errors = Errors(Domain("for line in nosuch { assign Note to \"x\" }"));
        await Assert.That(errors).IsNotEmpty();
    }

    // F2: a singular relationship is iterated (the fan-out sibling rejects this: OneToMany required).
    [Test]
    public async Task F2_GeneralFor_SingularRelationship_IsAnalyzeError() {
        var errors = Errors(Domain("for o in owner { assign Note to \"x\" }"));
        await Assert.That(errors).IsNotEmpty();
    }

    // F3: `invoke binder.Action` is never validated against the target entity.
    [Test]
    public async Task F3_GeneralFor_InvokeUnknownActionOnBinder_IsAnalyzeError() {
        var errors = Errors(Domain("for line in lines { invoke line.NoSuchAction }"));
        await Assert.That(errors).IsNotEmpty();
    }

    // F4: the `where` of a general for is never type-checked.
    [Test]
    public async Task F4_GeneralFor_NonBooleanWhere_IsAnalyzeError() {
        var errors = Errors(Domain("for line in lines where line Qty { invoke line.Mark }"));
        await Assert.That(errors).IsNotEmpty();
    }
}