namespace Poly.DomainModeling.V2.Core;

public enum TypeExpressionKind {
    Primitive,
    PrimitiveNullable,
    TypeReference,
    TypeReferenceNullable,
    PrimitiveList,
    TypeReferenceList,
}

public static class TypeExpression {
    private static readonly HashSet<string> PrimitiveNames = new(StringComparer.Ordinal)
    {
        "string",
        "int",
        "long",
        "decimal",
        "bool",
        "date",
        "datetime",
        "guid",
    };

    private static readonly Regex TypeReferenceRegex = new(
        @"^[A-Za-z][A-Za-z0-9_]*\.[A-Za-z][A-Za-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string input, out TypeExpressionKind kind, out string? referencedTypeName)
    {
        kind = default;
        referencedTypeName = null;

        if (string.IsNullOrWhiteSpace(input)) {
            return false;
        }

        if (!string.Equals(input, input.Trim(), StringComparison.Ordinal)) {
            return false;
        }

        var isList = input.EndsWith("[]", StringComparison.Ordinal);
        var withoutList = isList ? input[..^2] : input;

        var isNullable = withoutList.EndsWith("?", StringComparison.Ordinal);
        var core = isNullable ? withoutList[..^1] : withoutList;

        if (string.IsNullOrEmpty(core)) {
            return false;
        }

        // v1 intentionally rejects nullable-list combinations to keep grammar narrow.
        if (isList && isNullable) {
            return false;
        }

        var isPrimitive = PrimitiveNames.Contains(core);
        var isTypeReference = TypeReferenceRegex.IsMatch(core);

        if (!isPrimitive && !isTypeReference) {
            return false;
        }

        if (isPrimitive) {
            if (isList) {
                kind = TypeExpressionKind.PrimitiveList;
                return true;
            }

            kind = isNullable ? TypeExpressionKind.PrimitiveNullable : TypeExpressionKind.Primitive;
            return true;
        }

        referencedTypeName = core;
        if (isList) {
            kind = TypeExpressionKind.TypeReferenceList;
            return true;
        }

        kind = isNullable ? TypeExpressionKind.TypeReferenceNullable : TypeExpressionKind.TypeReference;
        return true;
    }
}