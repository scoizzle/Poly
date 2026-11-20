using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Runtime;

/// <summary>
/// A range on a property applies to what the property's domain word binds, whatever the spelling (#998 T7, owner ruling
/// 7): every number word (Number, Int, Int64, Int32, Decimal, Float, Double) is guarded on create and on assign. Before
/// T7 only the spellings Number, Int and Decimal were, so a write outside an Int64, Int32, Float or Double range passed.
/// The DSL's own number keywords are Number and Decimal; the other words reach a domain that declares them as primitives
/// through the API or an MCP change, so the domain is built directly.
/// </summary>
public class NumberWordRangeTests {
    [Test]
    [Arguments("Number", 20L, 5L)]
    [Arguments("Int", 20L, 5L)]
    [Arguments("Int64", 20L, 5L)]
    [Arguments("Int32", 20, 5)]
    [Arguments("Decimal", 20L, 5L)]
    [Arguments("Float", 20.0, 5.0)]
    [Arguments("Double", 20.0, 5.0)]
    public async Task Range_IsGuarded_ForEveryNumberWord(string word, object outside, object inside) {
        var executor = new ModuleInstanceExecutor(Lower(word));

        var rejected = executor.Create("Widget", new Dictionary<string, object?> { ["X"] = outside });
        await Assert.That(rejected.Succeeded).IsFalse().Because($"create: {outside} is outside {word}'s range");
        await Assert.That(rejected.Error).Contains("must be <= 10");

        var widget = executor.Create("Widget", new Dictionary<string, object?> { ["X"] = inside }).Instance!;
        var assigned = executor.Invoke(widget, "Set", new Dictionary<string, object?> { ["v"] = outside });
        await Assert.That(assigned.Succeeded).IsFalse().Because($"assign: {outside} is outside {word}'s range");
        await Assert.That(assigned.Error).Contains("must be <= 10");
    }

    [Test]
    [Arguments("Float")]
    [Arguments("Double")]
    [Arguments("Decimal")]
    [Arguments("Int32")]
    public async Task Range_WithAFractionalBound_IsGuarded_AndAValueInsideStillRuns(string word) {
        // A fractional bound is a decimal (the DSL parser and an MCP change both store one). A double property's guard
        // compares in the property's own number, so it runs: a value inside is created, one outside is refused.
        var executor = new ModuleInstanceExecutor(Lower(word, 0.5m, 9.5m));
        object Value(double d) => word is "Int32" ? (int)d : word is "Decimal" ? (decimal)d : d;

        var inside = executor.Create("Widget", new Dictionary<string, object?> { ["X"] = Value(5) });
        await Assert.That(inside.Succeeded).IsTrue().Because(inside.Error ?? "");

        var rejected = executor.Create("Widget", new Dictionary<string, object?> { ["X"] = Value(20) });
        await Assert.That(rejected.Succeeded).IsFalse();
        await Assert.That(rejected.Error).Contains("must be <= 9.5");
    }

    private static Domain Lower(string word, object? minimum = null, object? maximum = null) {
        var set = new Poly.DomainModeling.Ontology.Action("Set", InvocationResult.Void,
            [new Property("v", new DomainTypeReference(word), [])],
            [new AssignEffect(DomainExpression.Property("X"), DomainExpression.Parameter("v"))],
            []);
        var widget = new Entity("Widget",
            [new Property("X", new DomainTypeReference(word), [new RangeConstraint(minimum ?? 0L, maximum ?? 10L)])], [set], [], []);
        // The word is declared as the domain's primitive (an API or MCP change can; the DSL declares only its keywords).
        // Its name binds the definition; the semantics is the family a name with no binding falls back to.
        var semantics = word is "Decimal" ? PrimitiveSemantics.Decimal : PrimitiveSemantics.Number;
        var domain = DomainTestFactory.Create("Lab", [new PrimitiveType(word, semantics, []), widget], []);
        var session = SessionBuilder.CreateEmpty().LoadCore().Build();
        var analysis = session.Analyze(domain);
        if (analysis.HasErrors)
            throw new InvalidOperationException(string.Join("; ", analysis.Diagnostics
                .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        _ = RuntimeAnalysisCache.GetOrLower(domain, session, analysis);
        return domain;
    }
}