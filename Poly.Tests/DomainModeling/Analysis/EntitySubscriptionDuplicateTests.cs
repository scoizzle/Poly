using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Entity-level <c>when</c> subscriptions dispatch whether or not the entity is in a
/// stage, so two with the same relationship + stage set are as ambiguous as two
/// stage-scoped ones. The stage-scoped duplicate check exists; the entity-level loop
/// did not run it (analysis-coverage finding F4/T8). These pin the entity-level warning.
/// </summary>
public sealed class EntitySubscriptionDuplicateTests {
    private static EvolutionResult Apply(string dsl) =>
        new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(new PolyDslParser(dsl).Parse());

    private static bool HasDuplicateWarning(EvolutionResult result) =>
        result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SubscriptionContractMismatch
            && d.Message.Contains("Duplicate entity-level subscription key", StringComparison.Ordinal));

    [Test]
    public async Task DuplicateEntityLevelSubscriptionKey_Warns() {
        var result = Apply("""
            domain Dup
            Parent: entity {
              Counter: Number default(0)
              kids: many Kid
              when kids Active { assign Counter to Counter + 1 }
              when kids Active { assign Counter to Counter + 1 }
            }
            Kid: entity {
              Active: stage { }
              Done: stage { }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(HasDuplicateWarning(result)).IsTrue();
    }

    [Test]
    public async Task DistinctEntityLevelSubscriptionKeys_DoNotWarn() {
        var result = Apply("""
            domain Dup
            Parent: entity {
              Counter: Number default(0)
              kids: many Kid
              when kids Active { assign Counter to Counter + 1 }
              when kids Done { assign Counter to Counter + 1 }
            }
            Kid: entity {
              Active: stage { }
              Done: stage { }
            }
            """);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(HasDuplicateWarning(result)).IsFalse();
    }
}