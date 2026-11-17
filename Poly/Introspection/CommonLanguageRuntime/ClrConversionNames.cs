namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR's special names for conversions (ECMA-335 I.10.3): how C# and the CLR encode a user-defined conversion
/// as a static member.
/// </summary>
internal static class ClrConversionNames {
    /// <summary>The member name for <paramref name="kind"/>.</summary>
    public static string NameOf(ConversionOperatorKind kind) => kind switch {
        ConversionOperatorKind.Implicit => "op_Implicit",
        ConversionOperatorKind.Explicit => "op_Explicit",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Conversion kind has no name.")
    };

    private static readonly Dictionary<string, ConversionOperatorKind> KindsByName = Enum.GetValues<ConversionOperatorKind>()
        .ToDictionary(NameOf, StringComparer.Ordinal);

    /// <summary>The conversion a member named <paramref name="name"/> encodes, or null when the name is no conversion's.</summary>
    public static ConversionOperatorKind? KindOf(string name) =>
        KindsByName.TryGetValue(name, out var kind) ? kind : null;
}