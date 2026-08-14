using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;

namespace Poly.Tests.DomainModeling;

public class DomainModelingSurfaceCompletionTests {
    private static Domain Apply(string poly) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(poly).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary ?? "evolution failed");
        return result.Root!;
    }

    [Test]
    public async Task Parse_DecimalLiteral_IsNumber() {
        var domain = Apply("""
            domain T
            Item: entity {
              Total: Number
              Discounted: policy { Total * 0.9 > 0 }
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error)).IsFalse();
        var expr = domain.Types.OfType<Entity>().Single().Policies.Single().Expression;
        var mul = (Poly.DomainModeling.Multiply)((Poly.DomainModeling.Comparison)expr).Left;
        await Assert.That(((Literal)mul.Right).Value).IsEqualTo(0.9);
    }

    [Test]
    public async Task ConstraintPropagation_CallerLiteral_ToCalleeParam_ViaPropertyAccess() {
        var domain = Apply("""
            domain T
            Item: entity {
              Total: Number range(0, 100)
              Add: action (amount: Number) {
                assign Total to Total + amount
              }
              DoIt: action {
                invoke Add(amount: 50)
              }
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var warnings = analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();
        await Assert.That(warnings.Count).IsGreaterThanOrEqualTo(0);
        var item = domain.Types.OfType<Entity>().Single();
        var add = item.Actions.Single(a => a.Name == "Add");
        var meta = analysis.GetMetadata<DownstreamConstraintsMetadata>(add.Parameters[0]);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Constraints.OfType<RangeConstraint>().Any()).IsTrue();
    }

    [Test]
    public async Task If_NonBoolean_AnalysisRejects() {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser("""
                domain T
                Item: entity {
                  Qty: Number
                  Go: action {
                    if (Qty) {
                      assign Qty to 1
                    }
                  }
                }
                """).Parse());
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Message.Contains("if-condition", StringComparison.OrdinalIgnoreCase)
            || d.Message.Contains("Boolean", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Assign_NowToNumber_AnalysisRejects() {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser("""
                domain T
                Item: entity {
                  Qty: Number
                  Go: action {
                    assign Qty to Now
                  }
                }
                """).Parse());
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Message.Contains("Now", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task InvokeAction_UnknownArgKey_FailsClosed() {
        var entity = new Entity("Item",
            [new Property("Age", new DomainTypeReference("Number"), [])],
            [new Poly.DomainModeling.Action("Go", InvocationResult.Void, [], [], [])],
            [], []);
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Age"] = 15L });
        var result = instance.InvokeAction("Go", new Dictionary<string, object?> { ["Age"] = 40L });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Unknown argument");
        await Assert.That(instance.GetProperty<object>("Age")).IsEqualTo(15L);
    }

    [Test]
    public async Task InvokeAction_MissingDeclaredParam_FailsClosed() {
        var entity = new Entity("Item",
            [new Property("Qty", new DomainTypeReference("Number"), [])],
            [new Poly.DomainModeling.Action("Add", InvocationResult.Void,
                [new Property("amount", new DomainTypeReference("Number"), [])],
                [], [])],
            [], []);
        var instance = DomainEntityInstance.Create(entity,
            new Dictionary<string, object?> { ["Qty"] = 1L });

        var result = instance.InvokeAction("Add");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ErrorMessage).Contains("Missing argument");
        await Assert.That(result.ErrorMessage).Contains("amount");
    }

    [Test]
    public async Task Store_DuplicateUnique_FailsClosed() {
        var entity = new Entity("Item",
            [new Property("Sku", new DomainTypeReference("Text"), [new UniqueConstraint()])],
            [], [], []);
        var store = new DomainInstanceStore();
        store.Add(DomainEntityInstance.Create(entity, new Dictionary<string, object?> { ["Sku"] = "A" }));
        var dup = DomainEntityInstance.Create(entity, new Dictionary<string, object?> { ["Sku"] = "A" });
        var ex = Assert.Throws<InvalidOperationException>(() => store.Add(dup));
        await Assert.That(ex!.Message).Contains("Unique");
    }

    [Test]
    public async Task NotifyTransition_CreateDuringNotify_DoesNotThrow() {
        var poly = """
            domain T
            Parent: entity {
              Name: Text
              kids: many Child
              when kids Active {
                create Child { Name: "spawned" }
              }
            }
            Child: entity {
              Name: Text
              parent: Parent
              Draft: stage {
                Activate: action { transition to Active }
              }
              Active: stage { }
            }
            """;
        var domain = Apply(poly);
        var store = new DomainInstanceStore();
        var parent = DomainEntityInstance.Create(
            domain.Types.OfType<Entity>().Single(e => e.Name == "Parent"),
            new Dictionary<string, object?> { ["Name"] = "p" },
            domain);
        var child = DomainEntityInstance.Create(
            domain.Types.OfType<Entity>().Single(e => e.Name == "Child"),
            new Dictionary<string, object?> { ["Name"] = "c" },
            domain);
        store.Add(parent);
        store.Add(child);
        store.Link("kids", parent, child);
        var result = child.InvokeAction("Activate");
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Contract_StoredPropertyOfContractValueType_AnalysisError() {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser("""
                domain T
                Stripe: contract external stripe v1 {
                  ChargeRequest: value { Amount: Number }
                  Charge: outbound operation ChargeRequest
                }
                Order: entity {
                  Leak: ChargeRequest
                }
                """).Parse());
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Message.Contains("Stored state", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task ContractBinding_UnknownAction_AnalysisError() {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser("""
                domain T
                Item: entity { Name: Text }
                Stripe: contract external stripe v1 {
                  Charge: inbound operation Number
                }
                Bad: bind Stripe Charge to Missing amount
                """).Parse());
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Message.Contains("Missing", StringComparison.Ordinal)
            && d.Message.Contains("action", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Subscription_All_SpreadStages_Fires() {
        var poly = """
            domain T
            Board: entity {
              Flag: Text
              items: many Item
              when all items Ready, Done {
                assign Flag to "set"
              }
            }
            Item: entity {
              Draft: stage {
                Prep: action { transition to Ready }
                Finish: action { transition to Done }
              }
              Ready: stage { }
              Done: stage { }
            }
            """;
        var domain = Apply(poly);
        var store = new DomainInstanceStore();
        var board = DomainEntityInstance.Create(
            domain.Types.OfType<Entity>().Single(e => e.Name == "Board"),
            new Dictionary<string, object?> { ["Flag"] = "no" },
            domain);
        var itemE = domain.Types.OfType<Entity>().Single(e => e.Name == "Item");
        var a = DomainEntityInstance.Create(itemE, domain: domain);
        var b = DomainEntityInstance.Create(itemE, domain: domain);
        store.Add(board);
        store.Add(a);
        store.Add(b);
        store.Link("items", board, a);
        store.Link("items", board, b);
        a.InvokeAction("Prep");
        await Assert.That(board.GetProperty<string>("Flag")).IsEqualTo("no");
        b.InvokeAction("Finish");
        await Assert.That(board.GetProperty<string>("Flag")).IsEqualTo("set");
    }
}