using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

public class EntityLevelActionHintTests {
    private static EvolutionResult Parse(string dsl) {
        var parser = new PolyDslParser(dsl);
        var changes = parser.Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).Apply(changes);
    }

    private static bool HasError(EvolutionResult result, string messageContains) =>
        result.Analysis.Diagnostics.Any(d => d.Message.Contains(messageContains, StringComparison.Ordinal));

    [Test]
    public async Task EntityLevel_ActionBesideStage_Hints() {
        var result = Parse("""
            domain Test
            Cart: entity {
              Items: Number
              CheckedOut: stage {
                Pay: action { assign Items to 0 }
              }
              AddItem: action { assign Items to Items + 1 }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(HasError(result, "AddItem")).IsTrue();
        await Assert.That(result.Analysis.Diagnostics.Count(d =>
            d.Code == DomainModelDiagnosticCodes.EntityLevelAction)).IsEqualTo(1);
    }

    [Test]
    public async Task StageAction_Only_NoEntityLevelHint() {
        var result = Parse("""
            domain Test
            Cart: entity {
              Items: Number
              CheckedOut: stage {
                AddItem: action { assign Items to Items + 1 }
              }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EntityLevelAction)).IsFalse();
    }
}