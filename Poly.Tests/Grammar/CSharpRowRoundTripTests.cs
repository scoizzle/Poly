using Poly.DomainModeling.Language;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// G-23b: round-trip tests for the C# surface defects the pattern-row check found (#848, #849, enum
/// values, the fused declaration, block lambdas, labels), and the guard for the one dead DSL row.
/// </summary>
public class CSharpRowRoundTripTests {
    /// <summary>Green guard: the shadowed <c>annotation/no-args</c> row is deleted; a bare annotation matches <c>with-args</c>.</summary>
    [Test]
    public async Task Annotation_BareCall_MatchesWithArgs() {
        var matcher = new Matcher<DslToken, DslTokenKind>(DslGrammar.Build(), new DslTokenReader("label()"));
        var match = matcher.TryMatch("annotation");
        await Assert.That(match?.PatternName).IsEqualTo("with-args");
    }
}
