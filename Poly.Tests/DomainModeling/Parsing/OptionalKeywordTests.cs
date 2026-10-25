using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// <c>optional</c> is the explicit opt-out of the required-by-default property rule.
/// It parses as one constraint, prints back, and is rejected on value-type members.
/// </summary>
public sealed class OptionalKeywordTests {
    private static Domain Parse(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return result.Root!;
    }

    [Test]
    public async Task Parse_OptionalProperty_IsOptionalConstraint() {
        var domain = Parse("""
            domain D
            Token: entity { Note: Text optional }
            """);
        var note = domain.Types.OfType<Entity>().Single().Properties.Single();
        await Assert.That(note.Constraints.Any(c => c is OptionalConstraint)).IsTrue();
    }

    [Test]
    public async Task RoundTrip_OptionalProperty_PrintsOptional() {
        const string poly = """
            domain D
            Token: entity { Note: Text optional }
            """;
        var printed = new DomainDslPrinter().Print(Parse(poly));
        await Assert.That(printed).Contains("Note: Text optional");

        var reparsed = Parse(printed).Types.OfType<Entity>().Single().Properties.Single();
        await Assert.That(reparsed.Constraints.Any(c => c is OptionalConstraint)).IsTrue();
    }

    [Test]
    public async Task Parse_OptionalOnValueType_FailsClosed() {
        var message = Assert.Throws<FormatException>(() => Parse("""
            domain D
            Payload: value { Note: Text optional }
            """));
        await Assert.That(message!.Message).Contains("optional");
        await Assert.That(message.Message).Contains("value-type members");
    }
}
