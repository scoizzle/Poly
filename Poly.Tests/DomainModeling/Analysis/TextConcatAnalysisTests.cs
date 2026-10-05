using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Analysis;

public class TextConcatAnalysisTests {
    static string Dsl(string target, string expression) => $$"""
        domain Cat
        Item: entity {
          Status: Text
          Suffix: Text
          Count: Number
          Total: Number
          Code: Text
          Tag: action { assign {{target}} to {{expression}} }
        }
        """;

    // The error messages Analyze gives the domain; empty when it is valid.
    static IReadOnlyList<string> Errors(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(new PolyDslParser(dsl).Parse());
        return [.. result.Analysis.Diagnostics
            .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
            .Select(d => d.Message)];
    }

    // `+` on two Texts is concatenation, however the operands are written.
    [Test]
    [Arguments("\"S\" + Status")]
    [Arguments("Status + Suffix")]
    [Arguments("Status + \"-\" + Suffix")]
    public async Task Add_WhenBothOperandsAreText_IsAccepted(string expression) =>
        await Assert.That(Errors(Dsl("Code", expression))).IsEmpty();

    [Test]
    public async Task Add_WhenBothOperandsAreNumbers_IsStillAccepted() =>
        await Assert.That(Errors(Dsl("Total", "Count + Total"))).IsEmpty();

    // Text and Number do not mix in either order.
    [Test]
    [Arguments("Status + Count")]
    [Arguments("Count + Status")]
    public async Task Add_WhenTextMeetsNumber_IsRejected(string expression) {
        var errors = Errors(Dsl("Code", expression));
        await Assert.That(errors.Any(e => e.StartsWith("arithmetic operand is not numeric"))).IsTrue();
    }

    // Only `+` concatenates.
    [Test]
    [Arguments("Status - Suffix")]
    [Arguments("Status * Suffix")]
    [Arguments("Status / Suffix")]
    public async Task OtherArithmetic_WhenOperandsAreText_IsRejected(string expression) {
        var errors = Errors(Dsl("Code", expression));
        await Assert.That(errors.Any(e => e.StartsWith("arithmetic operand is not numeric"))).IsTrue();
    }

    // A concatenation is Text, so it cannot be stored in a Number property.
    [Test]
    public async Task Add_WhenTextConcatenationIsAssignedToNumber_IsRejected() {
        var errors = Errors(Dsl("Total", "Status + Suffix"));
        await Assert.That(errors).Contains("type mismatch in assign to property 'Total': cannot assign 'Text' to 'Number'");
    }
}
