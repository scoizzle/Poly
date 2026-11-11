using System.Globalization;

using Poly.DomainModeling.Ontology;

using TokenKind = Poly.DomainModeling.Language.DslTokenKind;

namespace Poly.DomainModeling.Language;

// DomainResolver (Constraints) — constraint declarations on properties and value-type members.

public sealed partial class DomainResolver {
    // ── Constraint parser ─────────────────────────────────────

    private Constraint? ParseConstraint(string? typeName = null) {
        switch (Current.Kind) {
            case TokenKind.Optional:
                Advance();
                return new OptionalConstraint();

            case TokenKind.Unique:
                Advance();
                return new UniqueConstraint();

            case TokenKind.Equals:
                Advance();
                Expect(TokenKind.LParen);
                var dvExpr = ParseExpression();
                Expect(TokenKind.RParen);
                return new DefaultValueConstraint(dvExpr);

            case TokenKind.Equality:
                Advance();
                Expect(TokenKind.LParen);
                var eqExpr = ParseExpression();
                Expect(TokenKind.RParen);
                if (eqExpr is not Literal { Value: { } expected })
                    throw Error("equals() requires a literal value.");
                return new EqualityConstraint(expected);

            case TokenKind.Enum:
                throw Error("Inline enum(...) constraints are no longer supported. " +
                    "Use a top-level enum type declaration: Name: enum { Member1, Member2 }");

            case TokenKind.Range:
                Advance();
                Expect(TokenKind.LParen);
                object? min = null, max = null;
                // Signed/fractional bounds: range(-500, ) / range(0.01, 1.0) — the
                // natural overdraft/pricing surface was previously unparseable.
                if (Current.Kind == TokenKind.Number || Current.Kind == TokenKind.Minus) {
                    min = ParseNumberBound(typeName);
                }
                if (Current.Kind == TokenKind.Comma) {
                    Advance();
                }
                if (Current.Kind == TokenKind.Number || Current.Kind == TokenKind.Minus) {
                    max = ParseNumberBound(typeName);
                }
                Expect(TokenKind.RParen);
                return new RangeConstraint(min, max);

            case TokenKind.Length:
                Advance();
                Expect(TokenKind.LParen);
                // Open bounds mirror range: `length(3, )` is open upper (int.MaxValue),
                // `length(, 5)` is open lower (0), `length(3)` is exact. A `length(3, )`
                // that collapsed to `length(3, 3)` silently rejected 4-char values.
                int lenMin = 0;
                int lenMax = int.MaxValue;
                if (Current.Kind == TokenKind.Number) {
                    lenMin = int.Parse(Current.Text, CultureInfo.InvariantCulture);
                    Advance();
                }
                if (Current.Kind == TokenKind.Comma) {
                    Advance();
                    if (Current.Kind == TokenKind.Number) {
                        lenMax = int.Parse(Current.Text, CultureInfo.InvariantCulture);
                        Advance();
                    }
                }
                else {
                    lenMax = lenMin; // single-arg form "length(3)" — exact length
                }
                Expect(TokenKind.RParen);
                return new LengthConstraint(lenMin, lenMax);

            case TokenKind.Pattern:
                Advance();
                Expect(TokenKind.LParen);
                var pattern = Expect(TokenKind.StringLiteral).Text;
                Expect(TokenKind.RParen);
                return new PatternConstraint(pattern);

            default:
                return null;
        }
    }

    /// <summary>Parses a signed numeric bound for `range(min, max)` — e.g. `-500` or `0.01`.</summary>
    private object ParseNumberBound(string? typeName) {
        var negative = Current.Kind == TokenKind.Minus;
        if (negative) Advance();
        if (Current.Kind != TokenKind.Number)
            throw Error($"Expected a number in range bound, got '{Current.Text}'.");
        var text = Current.Text;
        if (IsIntegralRangeType(typeName) && !IsIntegralNumberText(text))
            throw Error($"Fractional bound '{text}' is not legal on {typeName}. Use Decimal.");
        if (typeName is "Decimal") {
            var dec = decimal.Parse(text, CultureInfo.InvariantCulture);
            Advance();
            return negative ? -dec : dec;
        }
        var value = double.Parse(text, CultureInfo.InvariantCulture);
        Advance();
        return negative ? -value : value;
    }

    private static bool IsIntegralRangeType(string? typeName) =>
        typeName is "Number" or "Int" or "Int64";

    private static bool IsIntegralNumberText(string text) {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return true;
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec)
            && dec == decimal.Truncate(dec);
    }

    private static bool IsConstraint(TokenKind kind) => kind switch {
        TokenKind.Optional or TokenKind.Unique or TokenKind.Range
            or TokenKind.Length or TokenKind.Pattern
            or TokenKind.Equals or TokenKind.Equality or TokenKind.Enum => true,
        _ => false,
    };
}