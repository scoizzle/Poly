using TokenKind = Poly.DomainModeling.Language.DslTokenKind;

namespace Poly.DomainModeling.Language;

// DomainResolver (Types) — type-reference resolution (primitive vs named types).

public sealed partial class DomainResolver {
    private string ParseTypeName() {
        if (IsPrimitiveType(Current.Kind)) {
            var typeName = Current.Kind switch {
                TokenKind.Text => "Text",
                TokenKind.NumberType => "Number",
                TokenKind.BooleanType => "Boolean",
                _ => throw Error($"Unknown type '{Current.Kind}'"),
            };
            Advance();
            return typeName;
        }
        if (Current.Kind == TokenKind.Identifier) {
            return ExpectIdentifier(TokenKind.Identifier, "type name");
        }
        throw Error($"Expected a type name, got '{Shown(Current)}'");
    }

    private static bool IsPrimitiveType(TokenKind kind) => kind switch {
        TokenKind.Text or TokenKind.NumberType or TokenKind.BooleanType => true,
        _ => false,
    };

    private bool IsKnownPrimitiveName(string name) =>
        _primitiveTypeNames.Contains(name);
}