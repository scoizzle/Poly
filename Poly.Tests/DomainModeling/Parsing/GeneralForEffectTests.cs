using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// #409 slice 4: the general <c>for x in Rel [where cond] { effects }</c> effect parses,
/// prints and round-trips. Slices 5 (effect legality) and 6 (lower + runtime) are not
/// shipped, so Analyze rejects the form with a positioned error rather than passing it.
/// </summary>
public class GeneralForEffectTests {
    private const string Dsl = """
        domain Billing
        Line: entity {
          Qty: Number
          Open: stage {
            Mark: action { assign Qty to Qty + 1 }
          }
        }
        Invoice: entity {
          Note: Text
          lines: many Line
          Open: stage {
            Touch: action {
              for line in lines where line Qty > 1 {
                assign Note to "x"
                invoke line.Mark
              }
            }
          }
        }
        """;

    private static ForEachEffect ParseFor(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var add = changes.OfType<AddEffectToActionChange>().Single(c => c.Effect is ForEachEffect);
        return (ForEachEffect)add.Effect;
    }

    // Mutation only: Analyze rejects the general `for` until slices 5-6, so the printed
    // round trip cannot go through ApplyAndAnalyze.
    private static Domain Apply(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return result.Root;
    }

    [Test]
    public async Task GeneralFor_Parses_IntoForEachEffect() {
        var effect = ParseFor(Dsl);

        await Assert.That(effect.BinderName).IsEqualTo("line");
        await Assert.That(effect.RelationshipName).IsEqualTo("lines");
        await Assert.That(effect.Where).IsNotNull();
        await Assert.That(effect.Body.Count).IsEqualTo(2);
        await Assert.That(effect.Body[0]).IsTypeOf<AssignEffect>();
        await Assert.That(effect.Body[1]).IsTypeOf<InvokeActionEffect>();
    }

    [Test]
    public async Task GeneralFor_WithoutWhere_Parses() {
        var effect = ParseFor(Dsl.Replace(" where line Qty > 1", ""));

        await Assert.That(effect.Where).IsNull();
    }

    [Test]
    public async Task GeneralFor_PrintIsIdempotent() {
        var printer = new DomainDslPrinter();
        var first = printer.Print(Apply(Dsl));
        var second = printer.Print(Apply(first));

        await Assert.That(first).Contains("for line in lines where");
        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    public async Task GeneralFor_Analyze_FailsClosed_UntilLowerable() {
        var analysis = DomainModelAnalyzer.Analyze(Apply(Dsl));

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectForNotLowerable
            && d.Severity == DiagnosticSeverity.Error)).IsTrue();
    }

    [Test]
    [Arguments("for line lines { assign Note to \"x\" }", "'in'")]
    [Arguments("for line in where line Qty > 1 { assign Note to \"x\" }", "relationship name")]
    [Arguments("for line in lines assign Note to \"x\"", "'{'")]
    [Arguments("for line in lines where { assign Note to \"x\" }", "")]
    [Arguments("for line in lines { assign Note to \"x\"", "")]
    [Arguments("for line in lines { for l2 in lines { assign Note to \"x\" } }", "nested")]
    public async Task GeneralFor_Malformed_IsPositionedParseError(string forText, string expected) {
        var poly = "domain Billing\n"
            + "Line: entity { Qty: Number Open: stage { Mark: action { assign Qty to Qty + 1 } } }\n"
            + "Invoice: entity { Note: Text lines: many Line Open: stage { Touch: action {\n"
            + forText + "\n"
            + "} } }\n";

        GrammarException? caught = null;
        try { new PolyDslParser(poly).Parse(); }
        catch (GrammarException ex) { caught = ex; }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Line).IsGreaterThan(0);
        await Assert.That(caught.Detail).Contains(expected);
    }
}