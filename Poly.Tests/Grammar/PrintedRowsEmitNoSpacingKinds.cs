using Poly.DomainModeling.Language;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// Characterisation pin (G-27 P3): no DSL row printed through the table today emits a kind that a
/// <see cref="DslTokenWriter"/> spacing rule keys on. The rules (G-27) look at Comma, Arrow, LParen,
/// LBrace, RBrace and the <c>action</c> and <c>if</c> keywords; while no printed row writes one of them,
/// a rule cannot move a printed byte. The printed rows are <see cref="PatternRowFloors.Dsl"/> (G-23's list),
/// and every one must be found, so the walk cannot pass by finding nothing.
/// </summary>
public sealed class PrintedRowsEmitNoSpacingKinds {
    private static readonly DslTokenKind[] SpacingKinds = [
        DslTokenKind.Comma, DslTokenKind.Arrow, DslTokenKind.LParen, DslTokenKind.LBrace,
        DslTokenKind.RBrace, DslTokenKind.Action, DslTokenKind.If,
    ];

    private static IEnumerable<DslTokenKind> FixedKinds(IEnumerable<IPatternElement<DslToken, DslTokenKind>> elements) {
        foreach (var element in elements) {
            switch (element) {
                case MatchKind<DslToken, DslTokenKind> k:
                    yield return k.Kind;
                    break;
                case Balanced<DslToken, DslTokenKind> b:
                    yield return b.Open;
                    yield return b.Close;
                    break;
                case Optional<DslToken, DslTokenKind> o:
                    foreach (var inner in FixedKinds([o.Inner]))
                        yield return inner;
                    break;
            }
        }
    }

    [Test]
    public async Task PrintedDslRows_ContainNoSpacingRuleKind() {
        var grammar = DslGrammar.Core;
        var found = new List<string>();
        var offending = new List<string>();
        foreach (var (rule, pattern) in PatternRowFloors.Dsl) {
            if (!grammar.TryGetPattern(rule, pattern, out var row))
                continue;
            found.Add($"{rule}/{pattern}");
            foreach (var kind in FixedKinds(row.Elements).Where(SpacingKinds.Contains))
                offending.Add($"{rule}/{pattern}: {kind}");
        }

        await Assert.That(offending).IsEmpty();

        await Assert.That(found).IsEquivalentTo([
            "document/header", "uses/id", "uses/pinned",
            "expr-primary/ident", "expr-primary/number", "expr-primary/string",
            "expr-primary/true", "expr-primary/false", "expr-primary/null",
            "date-operation/add", "date-operation/sub",
        ]);
    }
}