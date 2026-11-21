using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #607: a <c>create in Rel</c> inside <c>for x in Rel { … }</c> adds to the collection the
/// loop is enumerating. Both hosts throw ("Collection was modified") part-way through the
/// action, and the generated C# action throws instead of returning a <c>DomainResult</c>.
/// Analysis refuses the shape, positioned on the create-in and naming the collection.
/// </summary>
public class IteratedRelationshipMutationTests {
    private const string Head = """
        domain Edge
        Inv: entity {
          Qty: Number default(0)
          lines: many Ln
          extras: many Extra
          Open: stage {
            Grow: action {
        """;

    private const string Tail = """
            }
          }
        }
        Ln: entity { Qty: Number default(0) }
        Extra: entity { Qty: Number default(0) }
        """;

    private static Domain WithGrow(string body) => Apply(Head + body + "\n" + Tail);

    private static Domain Apply(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return result.Root;
    }

    private static List<Diagnostic> GrowErrors(Domain domain) =>
        DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                && d.Code == DomainModelDiagnosticCodes.CreateInIteratedRelationship)
            .ToList();

    [Test]
    public async Task CreateIn_IntoTheIteratedRelationship_IsPositionedError() {
        var errors = GrowErrors(WithGrow("for l in lines { create in lines { Qty: 1 } }"));

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Node is CreateEntityInRelationshipEffect).IsTrue();
        await Assert.That(errors[0].Message).Contains("'lines'");
        await Assert.That(errors[0].Message).Contains("'for'");
    }

    [Test]
    public async Task CreateIn_NestedInConditionalInForBody_IsError() {
        var errors = GrowErrors(WithGrow(
            "for l in lines { if (Qty > 0) { create in lines { Qty: 1 } } }"));

        await Assert.That(errors.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CreateIn_IntoOuterRelationshipFromNestedLoop_IsOneError() {
        var domain = WithNestedLoop(WithGrow("assign Qty to Qty + 1"));

        var errors = GrowErrors(domain);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("'lines'");
    }

    [Test]
    public async Task CreateIn_IntoOtherRelationshipFromNestedLoop_Passes() {
        var domain = WithNestedLoop(WithGrow("assign Qty to Qty + 1"), innerLoop: "lines", createdIn: "extras");

        await Assert.That(GrowErrors(domain).Count).IsEqualTo(0);
    }

    [Test]
    public async Task CreateEntityInstance_WithIteratedRelationshipName_IsError() {
        var domain = WithGrowEffects(WithGrow("assign Qty to Qty + 1"),
            new ForEachEffect("l", "lines", null, [
                new CreateEntityInstance(new DomainTypeReference("Ln"), [], "lines"),
            ]));

        var errors = GrowErrors(domain);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Node is CreateEntityInstance).IsTrue();
    }

    // The DSL refuses a `for` inside a `for` body, so the nested shape is planted through the model API.
    private static Domain WithNestedLoop(Domain domain, string innerLoop = "extras", string createdIn = "lines") =>
        WithGrowEffects(domain,
            new ForEachEffect("l", "lines", null, [
                new ForEachEffect("e", innerLoop, null, [
                    new CreateEntityInRelationshipEffect(createdIn, []),
                ]),
            ]));

    private static Domain WithGrowEffects(Domain domain, params Effect[] effects) =>
        domain with {
            Types = domain.Types.Select(type => type is Entity { Name: "Inv" } inv
                ? inv with {
                    Stages = inv.Stages.Select(stage => stage with {
                        Actions = stage.Actions.Select(action => action.Name == "Grow"
                            ? action with { Effects = effects }
                            : action).ToList()
                    }).ToList()
                }
                : type).ToList()
        };

    [Test]
    public async Task CreateIn_IntoDifferentRelationshipInForBody_Passes() {
        var errors = GrowErrors(WithGrow("for l in lines { create in extras { Qty: 1 } }"));

        await Assert.That(errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CreateIn_OutsideAnyForOverTheSameRelationship_Passes() {
        var errors = GrowErrors(WithGrow("create in lines { Qty: 1 }"));

        await Assert.That(errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CreateIn_IntoIteratedRelationship_AfterTheLoop_Passes() {
        var errors = GrowErrors(WithGrow("for l in lines { assign Qty to Qty + 1 } create in lines { Qty: 1 }"));

        await Assert.That(errors.Count).IsEqualTo(0);
    }
}