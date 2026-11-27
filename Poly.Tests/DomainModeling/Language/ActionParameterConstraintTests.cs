using Poly.DomainModeling.Language;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// #503: an action parameter carries no constraint (bounds are inferred from the effects).
/// The refusal must say that, not report that the parser expected a parameter name.
/// </summary>
public sealed class ActionParameterConstraintTests {
    [Test]
    public async Task ConstraintOnActionParameter_NamesTheRule() {
        var ex = Assert.Throws<GrammarException>(() => new PolyDslParser("""
            domain T
            Product: entity {
              Reserved: Number range(0, 100000)
              Reserve: action (qty: Number range(1, 100)) { assign Reserved to Reserved + qty }
            }
            """).Parse());

        await Assert.That(ex!.Line).IsEqualTo(4);
        await Assert.That(ex.Column).IsEqualTo(32);
        await Assert.That(ex.Detail).Contains("parameter 'qty' cannot carry a constraint");
        await Assert.That(ex.Detail).Contains("target property");
        await Assert.That(ex.Detail).DoesNotContain("Expected parameter name");
    }
}