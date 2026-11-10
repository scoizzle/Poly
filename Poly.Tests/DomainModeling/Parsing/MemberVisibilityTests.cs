using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Parsing;

public class MemberVisibilityTests {
    private static Domain Apply(string poly, DomainSession? session = null) {
        var parser = session is null ? new PolyDslParser(poly) : new PolyDslParser(poly, session);
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(parser.Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary ?? "evolution failed");
        return result.Root!;
    }

    private static Entity EntityOf(Domain d, string name) =>
        d.Types.OfType<Entity>().Single(e => e.Name == name);

    [Test]
    public async Task UnmarkedDocument_RoundTrip_MembersStayInternal() {
        var poly = """
            domain Test

            Money: value {
              Amount: Number
            }

            Order: entity {
              Total: Number
              Open: stage {
                Close: action {
                }
              }
              Pay: action (bill: Money) {
              }
            }
            """;

        var first = Apply(poly);
        var order = EntityOf(first, "Order");
        await Assert.That(order.Visibility).IsEqualTo(Visibility.Internal);
        await Assert.That(first.Types.OfType<ValueType>().Single().Visibility).IsEqualTo(Visibility.Internal);
        await Assert.That(order.Actions.Single().Visibility).IsEqualTo(Visibility.Internal);
        await Assert.That(order.Stages.Single().Actions.Single().Visibility).IsEqualTo(Visibility.Internal);

        var printed = new DomainDslPrinter().Print(first);
        await Assert.That(printed.Contains(" public", StringComparison.Ordinal)).IsFalse();

        var second = Apply(printed);
        await Assert.That(EntityOf(second, "Order").Visibility).IsEqualTo(Visibility.Internal);
        await Assert.That(second.Types.OfType<ValueType>().Single().Visibility).IsEqualTo(Visibility.Internal);
        await Assert.That(EntityOf(second, "Order").Actions.Single().Visibility).IsEqualTo(Visibility.Internal);
        await Assert.That(EntityOf(second, "Order").Stages.Single().Actions.Single().Visibility).IsEqualTo(Visibility.Internal);
    }

    [Test]
    public async Task PublicEntityValueTypeAndAction_RoundTrip_KeepsTheMark() {
        var poly = """
            domain Test

            public value Money {
              Amount: Number
            }

            public entity Order {
              Total: Number
              public action Pay (bill: Money) {
              }
            }
            """;

        var first = Apply(poly);
        var order = EntityOf(first, "Order");
        await Assert.That(order.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(first.Types.OfType<ValueType>().Single().Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(order.Actions.Single().Visibility).IsEqualTo(Visibility.Public);

        var printed = new DomainDslPrinter().Print(first);
        await Assert.That(printed.Contains("public entity Order {", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains("public value Money {", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains("public action Pay (bill: Money) {", StringComparison.Ordinal)).IsTrue();

        var second = Apply(printed);
        await Assert.That(EntityOf(second, "Order").Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(second.Types.OfType<ValueType>().Single().Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(EntityOf(second, "Order").Actions.Single().Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(new DomainDslPrinter().Print(second)).IsEqualTo(printed);
    }

    [Test]
    public async Task PublicActionWithReturn_PrintsModifierFirst() {
        // A value-type return is not the product path, so this locks the print order
        // without asking analysis to accept `-> Money`.
        var pay = new Action(
            "Pay",
            new InvocationResult([
                new InvocationResult.Member("Result", new DomainTypeReference("Order"), [])
            ]),
            [new Property("bill", new DomainTypeReference("Money"), [])],
            [],
            []) { Visibility = Visibility.Public };
        var domain = new Domain("Test", [new Entity("Order", [], [pay], [], [])]);
        var printed = new DomainDslPrinter().Print(domain);
        await Assert.That(printed.Contains("public action Pay (bill: Money) -> Order", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task PublicActionWithReturn_RoundTripsModifierFirst() {
        var poly = """
            domain Test

            public entity Customer {
              orders: many Order
              public action Place -> Order {
                create in orders { }
              }
            }

            public entity Order {
              Draft: stage {}
            }
            """;

        var first = Apply(poly);
        await Assert.That(EntityOf(first, "Customer").Actions.Single().Visibility).IsEqualTo(Visibility.Public);
        var printed = new DomainDslPrinter().Print(first);
        await Assert.That(printed.Contains("public action Place -> Order", StringComparison.Ordinal)).IsTrue();
        var second = Apply(printed);
        await Assert.That(EntityOf(second, "Customer").Actions.Single().Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(new DomainDslPrinter().Print(second)).IsEqualTo(printed);
    }

    [Test]
    public async Task StageShell_CopiesPublicFromEarlierEntityAction() {
        // Plan: the stage shell must not drop a mark already stored on the same-named entity action.
        var poly = """
            domain Test

            public entity Order {
              public action Pay {
              }
              Open: stage {
                Pay: action {
                }
              }
            }
            """;

        var order = EntityOf(Apply(poly), "Order");
        await Assert.That(order.Actions.Single().Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(order.Stages.Single().Actions.Single().Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task StageShell_CopiesPublicFromLaterEntityAction() {
        // Declaration order is not meaningful: the same mark must survive when the
        // entity-level action is declared after the stage that holds its shell.
        var poly = """
            domain Test

            public entity Order {
              Open: stage {
                Pay: action {
                }
              }
              public action Pay {
              }
            }
            """;

        var order = EntityOf(Apply(poly), "Order");
        await Assert.That(order.Actions.Single().Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(order.Stages.Single().Actions.Single().Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task UnmarkedAction_IsNotExported_MarkedActionIs() {
        var poly = """
            domain Test

            public entity Order {
              Total: Number
              Hidden: action {
              }
              public action Shown {
              }
            }
            """;

        var order = EntityOf(Apply(poly), "Order");
        await Assert.That(order.Actions.Single(a => a.Name == "Hidden").Visibility).IsEqualTo(Visibility.Internal);
        await Assert.That(order.Actions.Single(a => a.Name == "Shown").Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task StageAction_Public_RoundTrips() {
        var poly = """
            domain Test

            public entity Order {
              Total: Number
              Open: stage {
                public action Close {
                }
                Keep: action {
                }
              }
            }
            """;

        var first = Apply(poly);
        var stage = EntityOf(first, "Order").Stages.Single();
        await Assert.That(stage.Actions.Single(a => a.Name == "Close").Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(stage.Actions.Single(a => a.Name == "Keep").Visibility).IsEqualTo(Visibility.Internal);

        var second = Apply(new DomainDslPrinter().Print(first));
        var stage2 = EntityOf(second, "Order").Stages.Single();
        await Assert.That(stage2.Actions.Single(a => a.Name == "Close").Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(stage2.Actions.Single(a => a.Name == "Keep").Visibility).IsEqualTo(Visibility.Internal);
    }

    [Test]
    public async Task PublicEntityWithAnnotation_PrintsCanonical() {
        var poly = """
            domain Test

            public entity Item table("INVENTORY") {
              Total: Number
            }
            """;

        var session = ExtensionCatalog.Core.Authoring;
        var domain = Apply(poly, session);
        await Assert.That(EntityOf(domain, "Item").Visibility).IsEqualTo(Visibility.Public);

        var printed = new DomainDslPrinter(ExtensionCatalog.Core.Authoring.Annotations).Print(domain);
        await Assert.That(printed.Contains("public entity Item table(\"INVENTORY\") {", StringComparison.Ordinal)).IsTrue();

        var again = Apply(printed, session);
        await Assert.That(EntityOf(again, "Item").Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParameterNamedPublic_IsRejected() {
        var poly = """
            domain Test

            Order: entity {
              Total: Number
              Tag: action (public: Text) {
              }
            }
            """;

        await Assert.That(() => new PolyDslParser(poly).Parse()).Throws<FormatException>();
    }

    [Test]
    public async Task PropertyNamedPublic_IsRejected() {
        var poly = """
            domain Test

            Order: entity {
              public: Text
            }
            """;

        await Assert.That(() => new PolyDslParser(poly).Parse()).Throws<FormatException>();
    }

    [Test]
    public async Task ContractInternal_StaysSourceKind() {
        var poly = """
            domain Test

            Billing: contract internal billing v1 {
            }
            """;

        var domain = Apply(poly);
        await Assert.That(domain.Types.OfType<Entity>().Any(e => e.Visibility == Visibility.Public)).IsFalse();
        await Assert.That(domain.Types.OfType<ValueType>().Any(v => v.Visibility == Visibility.Public)).IsFalse();

        var printed = new DomainDslPrinter().Print(domain);
        await Assert.That(printed.Contains("contract internal", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains(" public", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task WrittenInternal_OnEntity_IsRejected() {
        var poly = """
            domain Test

            Order: entity internal {
              Total: Number
            }
            """;

        await Assert.That(() => new PolyDslParser(poly).Parse()).Throws<FormatException>();
    }

    [Test]
    public async Task TrailingPublic_OnEntity_IsRejected() {
        var poly = """
            domain Test

            Order: entity public {
              Total: Number
            }
            """;

        await Assert.That(() => new PolyDslParser(poly).Parse()).Throws<FormatException>();
    }

    [Test]
    public async Task TrailingPublic_OnValue_IsRejected() {
        var poly = """
            domain Test

            Money: value public {
              Amount: Number
            }
            """;

        await Assert.That(() => new PolyDslParser(poly).Parse()).Throws<FormatException>();
    }

    [Test]
    public async Task TrailingPublic_OnAction_IsRejected() {
        var poly = """
            domain Test

            Order: entity {
              Pay: action public {
              }
            }
            """;

        await Assert.That(() => new PolyDslParser(poly).Parse()).Throws<FormatException>();
    }

    [Test]
    public async Task TrailingPublic_OnStageAction_IsRejected() {
        var poly = """
            domain Test

            Order: entity {
              Open: stage {
                Close: action public {
                }
              }
            }
            """;

        await Assert.That(() => new PolyDslParser(poly).Parse()).Throws<FormatException>();
    }
}