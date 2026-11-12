using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// L-22b (A4): the inverse of a navigation is resolved once, in <see cref="StorageAnalyzer"/>, with the pairing rule
/// <c>StructuralDomainAnalyzer</c> enforces: the navigation's own <c>via</c>, else the peer that declares into it,
/// else the target's only unclaimed navigation back. Consumers (the pack's EF mapping) read
/// <see cref="StorageNavigation.InversePropertyName"/>; none re-derives it.
/// </summary>
public sealed class StorageNavigationInverseTests {
    private static StorageEntity Storage(string dsl, string entity) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        return new StorageAnalyzer(result.Root, result.Analysis).Analyze().Entities.Single(e => e.Name == entity);
    }

    private static string? InverseOf(StorageEntity entity, string propertyName) =>
        entity.CollectionNavigations.Concat(entity.ReferenceNavigations).Single(n => n.PropertyName == propertyName).InversePropertyName;

    [Test]
    public async Task InversePropertyName_WhenPeerDeclaresVia_IsTheDeclaringPeer() {
        const string Dsl = """
            domain Test

            Line: entity {
              Qty: Number default(1)
              order: Order via lines
            }

            Order: entity {
              lines: many Line
            }
            """;

        await Assert.That(InverseOf(Storage(Dsl, "Order"), "Lines")).IsEqualTo("Order");
        await Assert.That(InverseOf(Storage(Dsl, "Line"), "Order")).IsEqualTo("Lines");
    }

    [Test]
    public async Task InversePropertyName_WhenOwnSideDeclaresVia_IsTheDeclaredPeer() {
        const string Dsl = """
            domain Test

            Line: entity {
              Qty: Number default(1)
              order: Order
            }

            Order: entity {
              lines: many Line via order
            }
            """;

        await Assert.That(InverseOf(Storage(Dsl, "Order"), "Lines")).IsEqualTo("Order");
        await Assert.That(InverseOf(Storage(Dsl, "Line"), "Order")).IsEqualTo("Lines");
    }

    [Test]
    public async Task InversePropertyName_WhenNeitherDeclaresAndOneNavigationBackExists_IsThatNavigation() {
        const string Dsl = """
            domain Test

            Line: entity {
              Qty: Number default(1)
              order: Order
            }

            Order: entity {
              lines: many Line
            }
            """;

        await Assert.That(InverseOf(Storage(Dsl, "Order"), "Lines")).IsEqualTo("Order");
    }

    [Test]
    public async Task InversePropertyName_WhenOneSided_IsNull() {
        const string Dsl = """
            domain Test

            Line: entity {
              Qty: Number default(1)
            }

            Order: entity {
              lines: many Line
            }
            """;

        await Assert.That(InverseOf(Storage(Dsl, "Order"), "Lines")).IsNull();
    }
}
