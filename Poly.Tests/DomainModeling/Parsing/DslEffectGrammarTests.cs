using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Parsing;

namespace Poly.Tests.DomainModeling.Parsing;

// ─── Effect statement grammar through PolyDslParser (gpure-5 parity) ───
// Proves the structure parser dispatches effect heads: head patterns +
// create/create-in + fail-closed negatives, same IR types.
public sealed class DslEffectGrammarTests {
    private static List<DomainChange> ParseEffects(string effectsText) {
        // Wrap in an action body so ParseEffect runs against the parser.
        var poly = $$"""
            domain D
            E: entity {
              Go: action {
                {{effectsText}}
              }
            }
            """;
        return new PolyDslParser(poly).Parse();
    }

    private static async Task AssertFails(string effectsText) {
        await Assert.That(() => ParseEffects(effectsText)).Throws<FormatException>();
    }

    [Test]
    public async Task EffectGrammar_HeadPatterns_Dispatched() {
        var changes = ParseEffects("""
            assign Status to "x"
            transition to Done
            invoke Approve()
            if (Active) { transition to Done }
            """);
        var effects = changes.OfType<AddEffectToActionChange>().Select(c => c.Effect).ToList();
        await Assert.That(effects.Count()).IsEqualTo(4);
        await Assert.That(effects[0]).IsTypeOf<AssignEffect>();
        await Assert.That(effects[1]).IsTypeOf<StageTransitionEffect>();
        await Assert.That(effects[2]).IsTypeOf<InvokeActionEffect>();
        await Assert.That(effects[3]).IsTypeOf<ConditionalEffect>();
    }

    [Test]
    public async Task EffectGrammar_CreateAndCreateIn_Dispatched() {
        var changes = ParseEffects("""
            create Order { Total: 10 }
            create in orders { Total: 5 }
            """);
        var effects = changes.OfType<AddEffectToActionChange>().Select(c => c.Effect).ToList();
        await Assert.That(effects.Count()).IsEqualTo(2);
        await Assert.That(effects[0]).IsTypeOf<CreateEntityInstance>();
        await Assert.That(effects[1]).IsTypeOf<CreateEntityInRelationshipEffect>();
    }

    [Test]
    public async Task EffectGrammar_WhenInBody_Rejected() {
        // F7: no 'when' pattern exists under "effect" — stays rejected.
        await AssertFails("when Rel Active { transition to Done }");
    }

    [Test]
    public async Task EffectGrammar_FailClosed_Negatives() {
        // F6: fail loud — no vacuous success.
        await AssertFails("assign Status to");          // missing expr
        await AssertFails("if (Active) {");             // unterminated then block
        await AssertFails("invoke any Approve");        // any requires RelName.Action
        await AssertFails("create");                    // missing entity name
        await AssertFails("transition to");             // missing stage name
    }
}