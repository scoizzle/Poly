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

            Money: value public {
              Amount: Number
            }

            Order: entity public {
              Total: Number
              Pay: action (bill: Money) public {
              }
            }
            """;

        var first = Apply(poly);
        var order = EntityOf(first, "Order");
        await Assert.That(order.Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(first.Types.OfType<ValueType>().Single().Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(order.Actions.Single().Visibility).IsEqualTo(Visibility.Public);

        var printed = new DomainDslPrinter().Print(first);
        await Assert.That(printed.Contains("Order: entity public {", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains("Money: value public {", StringComparison.Ordinal)).IsTrue();
        await Assert.That(printed.Contains("Pay: action (bill: Money) public {", StringComparison.Ordinal)).IsTrue();

        var second = Apply(printed);
        await Assert.That(EntityOf(second, "Order").Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(second.Types.OfType<ValueType>().Single().Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(EntityOf(second, "Order").Actions.Single().Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(new DomainDslPrinter().Print(second)).IsEqualTo(printed);
    }

    [Test]
    public async Task ActionWithReturn_PrintsPublicAfterArrow() {
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
        await Assert.That(printed.Contains("Pay: action (bill: Money) -> Order public", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ActionReturn_Public_RoundTripsAfterArrow() {
        var poly = """
            domain Test

            Customer: entity {
              orders: many Order
              Place: action -> Order public {
                create in orders { }
              }
            }

            Order: entity {
              Draft: stage {}
            }
            """;

        var first = Apply(poly);
        await Assert.That(EntityOf(first, "Customer").Actions.Single().Visibility).IsEqualTo(Visibility.Public);
        var printed = new DomainDslPrinter().Print(first);
        await Assert.That(printed.Contains("Place: action -> Order public", StringComparison.Ordinal)).IsTrue();
        var second = Apply(printed);
        await Assert.That(EntityOf(second, "Customer").Actions.Single().Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(new DomainDslPrinter().Print(second)).IsEqualTo(printed);
    }

    [Test]
    public async Task StageShell_CopiesPublicFromEarlierEntityAction() {
        // Plan: the stage shell must not drop a mark already stored on the same-named entity action.
        var poly = """
            domain Test

            Order: entity {
              Pay: action public {
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
    public async Task UnmarkedAction_IsNotExported_MarkedActionIs() {
        var poly = """
            domain Test

            Order: entity {
              Total: Number
              Hidden: action {
              }
              Shown: action public {
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

            Order: entity {
              Total: Number
              Open: stage {
                Close: action public {
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
    public async Task AnnotationThenPublic_PrintsCanonical() {
        var poly = """
            domain Test

            Item: entity table("INVENTORY") public {
              Total: Number
            }
            """;

        var session = ExtensionCatalog.Core.Authoring;
        var domain = Apply(poly, session);
        await Assert.That(EntityOf(domain, "Item").Visibility).IsEqualTo(Visibility.Public);

        var printed = new DomainDslPrinter(ExtensionCatalog.Core.Authoring.Annotations).Print(domain);
        await Assert.That(printed.Contains("Item: entity public table(\"INVENTORY\") {", StringComparison.Ordinal)).IsTrue();

        var again = Apply(printed, session);
        await Assert.That(EntityOf(again, "Item").Visibility).IsEqualTo(Visibility.Public);
    }

    [Test]
    public async Task ParameterNamedPublic_Parses() {
        var poly = """
            domain Test

            Order: entity {
              Total: Number
              Tag: action (public: Text) {
              }
            }
            """;

        var action = EntityOf(Apply(poly), "Order").Actions.Single();
        await Assert.That(action.Visibility).IsEqualTo(Visibility.Internal);
        await Assert.That(action.Parameters.Single().Name).IsEqualTo("public");
    }

    [Test]
    public async Task PropertyNamedPublic_Parses() {
        var poly = """
            domain Test

            Order: entity {
              public: Text
            }
            """;

        var order = EntityOf(Apply(poly), "Order");
        await Assert.That(order.Visibility).IsEqualTo(Visibility.Internal);
        await Assert.That(order.Properties.Single().Name).IsEqualTo("public");
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
    public async Task PublicAction_WithInternalParameterType_Parses() {
        var poly = """
            domain Test

            Money: value {
              Amount: Number
            }

            Order: entity {
              Total: Number
              Pay: action (bill: Money) public {
              }
            }
            """;

        var domain = Apply(poly);
        await Assert.That(EntityOf(domain, "Order").Actions.Single().Visibility).IsEqualTo(Visibility.Public);
        await Assert.That(domain.Types.OfType<ValueType>().Single().Visibility).IsEqualTo(Visibility.Internal);
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
    public async Task SecondPublic_IsRejected() {
        var poly = """
            domain Test

            Order: entity public public {
              Total: Number
            }
            """;

        await Assert.That(() => new PolyDslParser(poly).Parse()).Throws<FormatException>();
    }
}