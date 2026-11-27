using Poly.DomainModeling.Language;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// #871: an unknown policy named in an action's <c>require</c> is reported at the <c>require</c>
/// token, where the author wrote it, not at the token after the entity's closing brace. The stage
/// twin already reports at its own <c>require</c>; the two now agree.
/// </summary>
public class RequirePolicyPositionTests {
    private static GrammarException Refusal(string text) =>
        Assert.Throws<GrammarException>(() => new PolyDslParser(text).Parse())!;

    [Test]
    public async Task UnknownActionRequirePolicy_IsReportedAtTheRequire() {
        var ex = Refusal("""
            domain A
            X: entity {
              Go: action require Missing { }
            }
            """);

        await Assert.That(ex.Line).IsEqualTo(3);
        await Assert.That(ex.Column).IsEqualTo(14);
        await Assert.That(ex.Detail).Contains("Action 'Go' requires policy 'Missing'");
    }

    [Test]
    public async Task UnknownNegatedActionRequirePolicy_IsReportedAtTheRequire() {
        var ex = Refusal("""
            domain A
            X: entity {
              Go: action require not Missing { }
            }
            """);

        await Assert.That(ex.Line).IsEqualTo(3);
        await Assert.That(ex.Column).IsEqualTo(14);
        await Assert.That(ex.Detail).Contains("Action 'Go' requires policy 'Missing'");
    }

    [Test]
    public async Task UnknownStageRequirePolicy_IsReportedAtTheRequire() {
        var ex = Refusal("""
            domain A
            X: entity {
              S: stage require Missing { }
            }
            """);

        await Assert.That(ex.Line).IsEqualTo(3);
        await Assert.That(ex.Column).IsEqualTo(12);
        await Assert.That(ex.Detail).Contains("Stage 'S' requires policy 'Missing'");
    }
}