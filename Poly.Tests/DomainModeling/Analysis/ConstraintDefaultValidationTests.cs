using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #367: a property default that violates its own range/length is rejected at analysis for every
/// literal type, not just <c>long</c>. A default the analyzer cannot evaluate (a non-literal) beside
/// a range/length is an explicit DMCS003 warning, never a silent pass.
/// </summary>
public class ConstraintDefaultValidationTests {
    private static EvolutionResult Parse(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
    }

    private static EvolutionResult ParseTemporal(string dsl) {
        var session = SessionBuilder.CreateEmpty().LoadCore().Load(new TemporalLibrary()).Build();
        var changes = new PolyDslParser(dsl, session).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
    }

    private static List<string> Messages(EvolutionResult r, DiagnosticSeverity severity) =>
        r.Analysis.Diagnostics.Where(d => d.Severity == severity).Select(d => d.Message).ToList();

    [Test]
    public async Task DecimalDefault_BelowRange_IsError() {
        var r = Parse("""
            domain Test
            Item: entity {
              Price: Decimal range(10, 20) default(5.5)
            }
            """);
        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(Messages(r, DiagnosticSeverity.Error)
            .Any(m => m.Contains("Price") && m.Contains("5.5") && m.Contains("range"))).IsTrue();
    }

    [Test]
    public async Task DecimalDefault_AboveRange_IsError() {
        var r = Parse("""
            domain Test
            Item: entity {
              Price: Decimal range(10, 20) default(25.5)
            }
            """);
        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(Messages(r, DiagnosticSeverity.Error)
            .Any(m => m.Contains("Price") && m.Contains("25.5") && m.Contains("range"))).IsTrue();
    }

    [Test]
    public async Task DecimalDefault_WithinRange_IsClean() {
        var r = Parse("""
            domain Test
            Item: entity {
              Price: Decimal range(10, 20) default(15.5)
            }
            """);
        await Assert.That(r.Succeeded).IsTrue();
        await Assert.That(Messages(r, DiagnosticSeverity.Warning)
            .Any(m => m.Contains("Price"))).IsFalse();
    }

    private static List<string> ApiErrors(object defaultValue) {
        var entity = new Entity("Item",
            [new Property("Price", new DomainTypeReference("Number"),
                [new DefaultValueConstraint(DomainExpression.Literal(defaultValue)),
                 new RangeConstraint(10L, 20L)])],
            [], [], []);
        var analysis = DomainModelAnalyzer.Analyze(DomainTestFactory.Create("Test", [entity], []));
        return analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Message).ToList();
    }

    [Test]
    public async Task DoubleDefault_BelowRange_IsError() {
        await Assert.That(ApiErrors(5.5d).Any(m => m.Contains("Price") && m.Contains("5.5"))).IsTrue();
    }

    [Test]
    public async Task ApiDecimalDefault_BelowRange_IsError() {
        await Assert.That(ApiErrors(5.5m).Any(m => m.Contains("Price") && m.Contains("5.5"))).IsTrue();
    }

    [Test]
    public async Task DoubleDefault_WithinRange_IsClean() {
        await Assert.That(ApiErrors(15.5d).Any(m => m.Contains("default value"))).IsFalse();
    }

    [Test]
    public async Task LongDefault_BelowRange_StillError() {
        var r = Parse("""
            domain Test
            Item: entity {
              Qty: Number range(10, 20) default(5)
            }
            """);
        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(Messages(r, DiagnosticSeverity.Error)
            .Any(m => m.Contains("Qty") && m.Contains("5"))).IsTrue();
    }

    [Test]
    public async Task StringDefault_OutsideLength_IsError() {
        var r = Parse("""
            domain Test
            Item: entity {
              Code: Text length(3, 5) default("ab")
            }
            """);
        await Assert.That(r.Succeeded).IsFalse();
        await Assert.That(Messages(r, DiagnosticSeverity.Error)
            .Any(m => m.Contains("Code") && m.Contains("length"))).IsTrue();
    }

    [Test]
    public async Task StringDefault_WithinLength_IsClean() {
        var r = Parse("""
            domain Test
            Item: entity {
              Code: Text length(3, 5) default("abcd")
            }
            """);
        await Assert.That(r.Succeeded).IsTrue();
    }

    [Test]
    public async Task NonLiteralDefault_BesideRange_IsExplicitlyWarnedNotSilent() {
        var r = ParseTemporal("""
            domain Test
            uses temporal
            Event: entity {
              At: Date range(2020, 2030) default(Today)
            }
            """);
        await Assert.That(Messages(r, DiagnosticSeverity.Warning)
            .Any(m => m.Contains("At") && m.Contains("default") && m.Contains("not validated"))).IsTrue();
    }

    [Test]
    public async Task NonLiteralDefault_WithoutRangeOrLength_NoDefaultWarning() {
        var r = ParseTemporal("""
            domain Test
            uses temporal
            Event: entity {
              At: Date default(Today)
            }
            """);
        await Assert.That(Messages(r, DiagnosticSeverity.Warning)
            .Any(m => m.Contains("not validated"))).IsFalse();
    }
}