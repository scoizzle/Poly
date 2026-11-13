namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// Interim rule (T-10 removes it): <c>Date + number</c> and <c>Date - number</c> add days. The type provider defines no
/// operator for the pair, so the compatibility analyzer rewrites the node to <c>AddDays</c> (a date of the left type) and
/// the resolver stamps the node with the same left type, which lets a parent read the stamp the rewrite will produce:
/// <c>(d + 1) + 2</c> is rewritten and <c>(d + 1) &gt; 5</c> is rejected, as before the provider typed arithmetic. A
/// non-nullable left beside a non-nullable decimal is not an offset: the provider judges that pair (no operator).
/// </summary>
internal static class DateOffsetRule {
    /// <summary>True when <paramref name="kind"/> is Add or Subtract, <paramref name="left"/> is a date type and <paramref name="right"/> a number.</summary>
    public static bool Applies(OperatorKind kind, Type left, Type right) =>
        kind is OperatorKind.Add or OperatorKind.Subtract
        && SyntaxTypeCompatibilityAnalyzer.IsDateCategory(left) && SyntaxTypeCompatibilityAnalyzer.IsNumberCategory(right)
        && !(Nullable.GetUnderlyingType(left) is null && right == typeof(decimal));
}
