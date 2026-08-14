using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.DomainModeling.Packs.Temporal;

/// <summary>
/// Clock primaries: exact PascalCase <c>Now</c> and <c>Today</c>. Lowercase
/// spellings stay <see cref="PropertyAccess"/> (fail closed — one injected spelling).
/// </summary>
public sealed class NowForm : IExpressionPrimaryForm {
    public bool TryParse(IDslParseCursor cursor, DslExpressionParser expressions, out DomainExpression expression) {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(expressions);
        expression = null!;
        if (cursor.Current.Kind != DslTokenKind.Identifier)
            return false;

        if (string.Equals(cursor.Current.Text, "Now", StringComparison.Ordinal)) {
            cursor.Advance();
            expression = new Now();
            return true;
        }
        if (string.Equals(cursor.Current.Text, "Today", StringComparison.Ordinal)) {
            cursor.Advance();
            expression = new Today();
            return true;
        }
        return false;
    }
}