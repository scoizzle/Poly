using System.Globalization;

using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.DomainModeling.Packs.Temporal;

/// <summary>
/// Duration primary: <c>N Days</c> / <c>N Months</c> (singular <c>Day</c>/<c>Month</c>
/// also accepted). Units are exact PascalCase — lowercase is not a duration.
/// </summary>
public sealed class DurationForm : IExpressionPrimaryForm {
    public bool TryParse(IDslParseCursor cursor, DslExpressionParser expressions, out DomainExpression expression) {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(expressions);
        expression = null!;
        if (cursor.Current.Kind != DslTokenKind.Number
            || cursor.Peek(1).Kind != DslTokenKind.Identifier)
            return false;

        if (!TryGetUnit(cursor.Peek(1).Text, out var unit))
            return false;

        if (!long.TryParse(cursor.Current.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            return false;

        cursor.Advance();
        cursor.Advance();
        expression = new Duration(amount, unit);
        return true;
    }

    /// <summary>Accepted unit spellings (singular and plural), exact PascalCase.</summary>
    private static bool TryGetUnit(string text, out DurationUnit unit) {
        switch (text) {
            case "Day":
            case "Days":
                unit = DurationUnit.Days;
                return true;
            case "Month":
            case "Months":
                unit = DurationUnit.Months;
                return true;
            default:
                unit = default;
                return false;
        }
    }
}