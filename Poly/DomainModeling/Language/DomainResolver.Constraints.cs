using Poly.DomainModeling.Analysis;
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
                    lenMin = DslNumberText.ParseLengthBound(Current);
                    Advance();
                }
                if (Current.Kind == TokenKind.Comma) {
                    Advance();
                    if (Current.Kind == TokenKind.Number) {
                        lenMax = DslNumberText.ParseLengthBound(Current);
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
                var pattern = Expect(TokenKind.StringLiteral).Unescape();
                Expect(TokenKind.RParen);
                return new PatternConstraint(pattern);

            default:
                return null;
        }
    }

    /// <summary>Parses a signed numeric bound for `range(min, max)` — e.g. `-500` or `0.01`.</summary>
    /// <remarks>
    /// The declared type decides the bound's CLR type, never the text: a Number/Int/Int64 bound is a
    /// <see cref="long"/>, and a Decimal or any other non-Number-family bound is a <see cref="decimal"/>.
    /// Both bounds of one constraint therefore share a type. A bound outside its type's range is a
    /// positioned error (#515), never a silent widening.
    /// </remarks>
    private object ParseNumberBound(string? typeName) {
        var negative = Current.Kind == TokenKind.Minus;
        if (negative) Advance();
        if (Current.Kind != TokenKind.Number)
            throw Error($"Expected a number in range bound, got '{Shown(Current)}'.");
        var text = Current.Span.ToString();
        var signed = negative ? "-" + text : text;

        if (IsIntegralRangeType(typeName)) {
            if (!IsIntegralNumberText(text))
                throw Error($"Fractional bound '{text}' is not legal on {typeName}. Use Decimal.");
            if (!TryParseLongBound(text, negative, out var integral))
                throw Error($"Range bound '{signed}' is out of range for {typeName} (64-bit).");
            Advance();
            return integral;
        }

        if (ExactNumber.TryParseDecimalText(text) is not { } exact)
            throw Error($"Range bound '{signed}' is out of range for {typeName}. Use a smaller value.");
        Advance();
        var dec = exact.ToDecimal();
        return negative ? -dec : dec;
    }

    /// <summary>Exact long parse of an integral bound; a magnitude of 2^63 with a minus is long.MinValue.</summary>
    private static bool TryParseLongBound(string text, bool negative, out long value) =>
        DslNumberText.TryParseWholeInt64(text, negative, out value);

    private static bool IsIntegralRangeType(string? typeName) =>
        typeName is "Number" or "Int" or "Int64";

    private static bool IsIntegralNumberText(string text) {
        // Syntactic: digits, or digits with a dot whose fraction is all zeros. Length never matters
        // here — an out-of-range bound is reported by the bound parse, not as "fractional" (#515).
        var dot = text.IndexOf('.');
        if (dot < 0)
            return text.Length > 0 && text.All(char.IsAsciiDigit);
        var whole = text[..dot];
        var fraction = text[(dot + 1)..];
        return whole.Length > 0 && whole.All(char.IsAsciiDigit)
            && fraction.Length > 0 && fraction.All(c => c == '0');
    }

    private static bool IsConstraint(TokenKind kind) => kind switch {
        TokenKind.Optional or TokenKind.Unique or TokenKind.Range
            or TokenKind.Length or TokenKind.Pattern
            or TokenKind.Equals or TokenKind.Equality or TokenKind.Enum => true,
        _ => false,
    };
}