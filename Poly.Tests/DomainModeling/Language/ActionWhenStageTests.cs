using Poly.DomainModeling.Language;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// #492 (G-05): <c>X: action when S { }</c> used to be accepted and the gate silently dropped.
/// The guide lists the form as unsupported: the action's stage is its placement. It is refused at
/// parse, for a real stage name and for an unknown one alike.
/// </summary>
public sealed class ActionWhenStageTests {
    [Test]
    [Arguments("S")]
    [Arguments("Nope")]
    public async Task ActionWhenClause_IsRefusedWithThePlacementFix(string stage) {
        var ex = Assert.Throws<GrammarException>(() => new PolyDslParser($$"""
            domain T
            A: entity {
              S: stage { }
              X: action when {{stage}} { }
            }
            """).Parse());

        await Assert.That(ex!.Line).IsEqualTo(4);
        await Assert.That(ex.Column).IsEqualTo(13);
        await Assert.That(ex.Detail).Contains("'action when Stage' is not supported");
        await Assert.That(ex.Detail).Contains("inside the stage body");
    }
}