using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling;

public class DomainModelingSurfaceCompletionTests {
    private static Domain Apply(string poly) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(poly).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary ?? "evolution failed");
        return result.Root!;
    }

    [Test]
    public async Task Parse_DecimalLiteral_IsNumber() {
        var rejected = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser("""
                domain T
                Item: entity {
                  Total: Number
                  Discounted: policy { Total * 0.9 > 0 }
                }
                """).Parse());
        var messages = rejected.Analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Message)
            .ToArray();
        await Assert.That(messages.Any(m => m.Contains("Decimal") && m.Contains(" (line "))).IsTrue();

        var domain = Apply("""
            domain T
            Item: entity {
              Total: Decimal
              Discounted: policy { Total * 0.9 > 0 }
            }
            """);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error)).IsFalse();
        var expr = domain.Types.OfType<Entity>().Single().Policies.Single().Expression;
        var mul = (Poly.DomainModeling.Ontology.Multiply)((Poly.DomainModeling.Ontology.Comparison)expr).Left;
        await Assert.That(((Literal)mul.Right).Value).IsEqualTo(0.9m);
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
            .ApplyAndAnalyze(new PolyDslParser("""
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
            .ApplyAndAnalyze(new PolyDslParser("""
                domain T
                uses temporal
                Item: entity {
                  Qty: Number
                  Go: action {
                    assign Qty to Now
                  }
                }
                """).Parse());
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Message.Contains("cannot assign", StringComparison.Ordinal)
            && d.Message.Contains("Number", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task InvokeAction_UnknownArgKey_FailsClosed() {
        var exec = ModuleExec.For(Apply("""
            domain T
            Item: entity {
              Age: Number
              Go: action { assign Age to 1 }
            }
            """));
        var instance = exec.Create("Item", new Dictionary<string, object?> { ["Age"] = 15L }).Instance!;
        var result = exec.Invoke(instance, "Go", new Dictionary<string, object?> { ["Age"] = 40L });
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Unknown argument");
        await Assert.That(instance.Bag["Age"]).IsEqualTo(15L);
    }

    [Test]
    public async Task InvokeAction_MissingDeclaredParam_FailsClosed() {
        var exec = ModuleExec.For(Apply("""
            domain T
            Item: entity {
              Qty: Number
              Add: action (amount: Number) { assign Qty to amount }
            }
            """));
        var instance = exec.Create("Item", new Dictionary<string, object?> { ["Qty"] = 1L }).Instance!;

        var result = exec.Invoke(instance, "Add");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error).Contains("Missing argument");
        await Assert.That(result.Error).Contains("amount");
    }

    [Test]
    public async Task Store_DuplicateUnique_FailsClosed() {
        var exec = ModuleExec.For(Apply("""
            domain T
            uses persistence
            Item: entity { Sku: Text unique }
            """));
        exec.Create("Item", new Dictionary<string, object?> { ["Sku"] = "A" });
        var dup = exec.Create("Item", new Dictionary<string, object?> { ["Sku"] = "A" });
        await Assert.That(dup.Succeeded).IsFalse();
        await Assert.That(dup.Error).Contains("Unique");
    }

    [Test]
    public async Task NotifyTransition_CreateDuringNotify_DoesNotThrow() {
        var poly = """
            domain T
            Parent: entity {
              Name: Text
              kids: many Child via parent
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
        var exec = ModuleExec.For(domain);
        var parent = exec.Create((domain.Types.OfType<Entity>().Single(e => e.Name == "Parent")).Name, new Dictionary<string, object?> { ["Name"] = "p" }).Instance!;
        var child = exec.Create((domain.Types.OfType<Entity>().Single(e => e.Name == "Child")).Name, new Dictionary<string, object?> { ["Name"] = "c" }).Instance!;
        exec.Link("kids", parent, child);
        var result = exec.Invoke(child, "Activate");
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Contract_StoredPropertyOfContractValueType_AnalysisError() {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser("""
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
            .ApplyAndAnalyze(new PolyDslParser("""
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
              board: Board via items
              Draft: stage {
                Prep: action { transition to Ready }
                Finish: action { transition to Done }
              }
              Ready: stage { }
              Done: stage { }
            }
            """;
        var domain = Apply(poly);
        var exec = ModuleExec.For(domain);
        var board = exec.Create((domain.Types.OfType<Entity>().Single(e => e.Name == "Board")).Name, new Dictionary<string, object?> { ["Flag"] = "no" }).Instance!;
        var itemE = domain.Types.OfType<Entity>().Single(e => e.Name == "Item");
        var a = exec.Create((itemE).Name, new Dictionary<string, object?>()).Instance!;
        var b = exec.Create((itemE).Name, new Dictionary<string, object?>()).Instance!;
        exec.Link("items", board, a);
        exec.Link("items", board, b);
        exec.Invoke(a, "Prep");
        await Assert.That((string)board.Bag["Flag"]!).IsEqualTo("no");
        exec.Invoke(b, "Finish");
        await Assert.That((string)board.Bag["Flag"]!).IsEqualTo("set");
    }
}