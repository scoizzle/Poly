using Poly.DomainModeling;
using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Standalone DSL expression fragment entry: parses one product-DSL expression
/// (e.g. <c>Age &gt;= 18</c>) without a domain document. Fail-closed: empty input
/// throws; tokens after the expression throw.
/// </summary>
public static class DslExpressionFragment {
    /// <summary>
    /// Parses <paramref name="expression"/> as a DSL expression fragment. Throws
    /// <see cref="FormatException"/> (via GrammarError) on empty input, invalid syntax,
    /// or trailing tokens. <paramref name="inputs"/> supplies session concept folds;
    /// when null the empty registry is used.
    /// </summary>
    public static DomainExpression ParseExpressionFragment(
        string expression,
        DomainParserInputs? inputs = null) {
        if (string.IsNullOrWhiteSpace(expression))
            throw GrammarError.Error("Expression must not be empty");

        var session = DomainSession.FromInputs(inputs);
        var reader = new DslTokenReader(expression);
        var matcher = session.Language.Matcher(reader);
        var cursor = new DslCursor(reader, matcher);
        var parser = new DslExpressionParser(cursor, session.ParserInputs.ExpressionForms, session.Folds);
        var result = parser.ParseExpression();

        if (cursor.Current.Kind != DslTokenKind.EndOfFile)
            throw GrammarError.Error(
                $"Trailing tokens after expression: '{cursor.Current.Text}'",
                cursor.Current.Line,
                cursor.Current.Col);

        return result;
    }
}