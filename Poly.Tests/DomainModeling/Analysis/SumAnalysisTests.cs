using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

public class SumAnalysisTests {
    private static EvolutionResult Parse(string dsl) {
        var parser = new PolyDslParser(dsl);
        var changes = parser.Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).Apply(changes);
    }

    private static bool HasError(EvolutionResult result, string messageContains) =>
        result.Analysis.Diagnostics.Any(d => d.Message.Contains(messageContains, StringComparison.Ordinal));

    private const string NumberDsl = """
        domain Test
        Line: entity {
          Price: Number
          Title: Text
        }
        Order: entity {
          lines: many Line
          Total: policy { sum lines Price > 0 }
        }
        """;

    [Test]
    public async Task Sum_NumberProp_Succeeds() {
        var result = Parse(NumberDsl);
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Sum_UnknownProp_Errors() {
        var result = Parse("""
            domain Test
            Line: entity {
              Price: Number
            }
            Order: entity {
              lines: many Line
              Bad: policy { sum lines Missing > 0 }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "Missing")).IsTrue();
    }

    [Test]
    public async Task Sum_TextProp_Errors() {
        var result = Parse("""
            domain Test
            Line: entity {
              Title: Text
            }
            Order: entity {
              lines: many Line
              Bad: policy { sum lines Title > 0 }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "Title")).IsTrue();
    }

    [Test]
    public async Task Sum_UnknownRel_Errors() {
        var result = Parse("""
            domain Test
            Line: entity {
              Price: Number
            }
            Order: entity {
              lines: many Line
              Bad: policy { sum missing Price > 0 }
            }
            """);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(HasError(result, "missing")).IsTrue();
    }

    [Test]
    public async Task Count_StillSucceeds() {
        var result = Parse("""
            domain Test
            Line: entity {
              Price: Number
            }
            Order: entity {
              lines: many Line
              Total: policy { count lines > 0 }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
    }
}