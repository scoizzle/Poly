using System.Globalization;

namespace Poly.DomainModeling.Language;

/// <summary>Which stored type a DSL digit text maps to (see <see cref="DslNumberText.Classify"/>).</summary>
internal enum NumberLiteralKind {
    Integer,
    Fractional,
    IntegerOverflow,
    FractionalOverflow,
}

internal static class DslNumberText {
    public static string Format(object? value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? throw new ArgumentNullException(nameof(value));

    public static NumberLiteralKind Classify(ReadOnlySpan<char> digitText) => NumberLiteralKind.Integer;
}