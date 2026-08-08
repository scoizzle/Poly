using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

using Token = Poly.Grammar.Token<DslTokenKind>;
using TokenKind = DslTokenKind;

/// <summary>
/// Standalone DSL expression fragment entry point (mcp-minify-1).
/// Parses one product-DSL expression (e.g. <c>Age &gt;= 18</c>) without a domain
/// document, for unified <c>add(kind: policy)</c> and expression oracles.
/// Fail-closed: empty input throws; tokens after the expression throw.
/// </summary>
public static class DslExpressionFragment {
    /// <summary>
    /// Parses <paramref name="expression"/> as a DSL expression fragment.
    /// Throws <see cref="GrammarException"/> (a <see cref="FormatException"/>) on
    /// null/whitespace input, invalid syntax, or trailing tokens.
    /// <paramref name="inputs"/> supplies E1 open expression forms; when null the
    /// default (empty) form registry is used.
    /// </summary>
    public static DomainExpression ParseExpressionFragment(
        string expression,
        DomainParserInputs? inputs = null) {
        if (string.IsNullOrWhiteSpace(expression))
            throw new GrammarException("Expression must not be empty", 0, 0);

        var tokenReader = new DslTokenReader(expression);
        var grammar = DslGrammar.Build(g => inputs?.ExpressionForms.ContributeGrammarPatterns(g));
        var matcher = new Matcher<DslTokenKind>(grammar, tokenReader);
        var cursor = new FragmentCursor(tokenReader, matcher);
        var parser = new DslExpressionParser(cursor, inputs?.ExpressionForms);
        var result = parser.ParseExpression();

        if (cursor.Current.Kind != TokenKind.EndOfFile)
            throw new GrammarException(
                $"Trailing tokens after expression: '{cursor.Current.Text}'",
                cursor.Current.Line,
                cursor.Current.Col);

        return result;
    }

    /// <summary>
    /// Head-token cursor for fragment parsing — the same dual-cursor + Matcher
    /// pattern as <see cref="PolyDslParser"/>, shared via
    /// <see cref="DslParseCursorBase"/> (Unread the head, TryMatch, re-read the
    /// head). Expression precedence is <see cref="DslExpressionParser"/>'s,
    /// never reimplemented here.
    /// </summary>
    private sealed class FragmentCursor : DslParseCursorBase {
        public FragmentCursor(DslTokenReader reader, Matcher<DslTokenKind> matcher)
            : base(reader, matcher) {
        }
    }
}