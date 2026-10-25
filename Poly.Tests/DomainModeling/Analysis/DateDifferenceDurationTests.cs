using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Analysis;

public class DateDifferenceDurationTests {
    private static EvolutionResult Parse(string dsl) {
        var parser = new PolyDslParser(dsl);
        var changes = parser.Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).Apply(changes);
    }

    /// <summary>Duration literals (<c>1 Days</c>) need the temporal pack at parse time.</summary>
    private static EvolutionResult ParseTemporal(string dsl) {
        var session = DomainSession.ForExtensions(ExtensionCatalog.ProductLanguage);
        var changes = new PolyDslParser(dsl, session).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(changes, session: session);
    }

    private static string PrintTemporal(string dsl) {
        var result = ParseTemporal(dsl);
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        var types = new DomainToCSharpExporter().Export(result.Root!, analysis);
        return new CSharpGenerator().Generate(types);
    }

    private static List<string> Errors(EvolutionResult result) =>
        result.Analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Message)
            .ToList();

    private static bool HasError(EvolutionResult result, string messageContains) =>
        Errors(result).Any(m => m.Contains(messageContains, StringComparison.Ordinal));

    [Test]
    public async Task DateTime_Minus_DateTime_Assigns() {
        var result = Parse("""
            domain Span
            uses temporal
            Job: entity {
              Started: DateTime
              Finished: DateTime
              Elapsed: Duration optional
              Close: action { assign Elapsed to Finished - Started }
            }
            """);
        await Assert.That(Errors(result)).IsEmpty();
    }

    [Test]
    public async Task Date_Minus_Date_Assigns() {
        var result = Parse("""
            domain Span
            uses temporal
            Job: entity {
              Started: Date
              Finished: Date
              Elapsed: Duration optional
              Close: action { assign Elapsed to Finished - Started }
            }
            """);
        await Assert.That(Errors(result)).IsEmpty();
    }

    [Test]
    public async Task MixedFamily_StillErrors() {
        var result = Parse("""
            domain Span
            uses temporal
            Job: entity {
              Started: Date
              Finished: DateTime
              Elapsed: Duration optional
              Close: action { assign Elapsed to Finished - Started }
            }
            """);
        await Assert.That(HasError(result, "arithmetic operand is not numeric")).IsTrue();
    }

    [Test]
    public async Task PlusDuration_StillFolds() {
        var result = ParseTemporal("""
            domain Span
            uses temporal
            Job: entity {
              Finished: DateTime
              Extend: action { assign Finished to Finished + 1 Days }
            }
            """);
        await Assert.That(Errors(result)).IsEmpty();
    }

    [Test]
    public async Task Number_Minus_Number_Stays() {
        var result = Parse("""
            domain Budget
            Job: entity {
              Count: Number
              Limit: Number
              Net: action { assign Count to Count - Limit }
            }
            """);
        await Assert.That(Errors(result)).IsEmpty();
    }

    [Test]
    public async Task Close_Elapsed_IsTheDifference() {
        var result = ParseTemporal("""
            domain Span
            uses temporal
            Job: entity {
              Started: DateTime
              Finished: DateTime
              Elapsed: Duration optional
              Close: action { assign Elapsed to Finished - Started }
            }
            """);
        var domain = result.Root!;
        var started = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var job = DomainEntityInstance.Create(
            domain.Types.OfType<Entity>().Single(e => e.Name == "Job"),
            new Dictionary<string, object?> {
                ["Started"] = started,
                ["Finished"] = started.AddHours(1)
            },
            domain);

        var close = job.InvokeAction("Close");

        await Assert.That(close.Succeeded).IsTrue();
        await Assert.That(job.GetProperty<object>("Elapsed")).IsEqualTo(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task Close_PrintsSubtract() {
        var csharp = PrintTemporal("""
            domain Span
            uses temporal
            Job: entity {
              Started: DateTime
              Finished: DateTime
              Elapsed: Duration optional
              Close: action { assign Elapsed to Finished - Started }
            }
            """);

        await Assert.That(csharp).Contains(".Subtract(");
    }

    [Test]
    public async Task PlusDay_StillAddDays() {
        var csharp = PrintTemporal("""
            domain Span
            uses temporal
            Job: entity {
              Finished: DateTime
              Extend: action { assign Finished to Finished + 1 Days }
            }
            """);

        await Assert.That(csharp).Contains(".AddDays(");
    }
}