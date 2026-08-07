using System.Globalization;

using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

using TokenKind = DslTokenKind;

/// <summary>
/// E1 expression parser for the product DSL.
/// Precedence is Pratt/RD layering (or/and/not/compare/add/mul/primary);
/// primary open forms are pack-registered via <see cref="ExpressionFormRegistry"/>.
/// Structure dispatch remains on <see cref="PolyDslParser"/> + Matcher.
/// </summary>
public sealed class DslExpressionParser {
    private readonly IDslParseCursor _c;
    private readonly ExpressionFormRegistry _forms;

    public DslExpressionParser(IDslParseCursor cursor, ExpressionFormRegistry? forms = null) {
        _c = cursor ?? throw new ArgumentNullException(nameof(cursor));
        _forms = forms ?? new ExpressionFormRegistry();
    }

    /// <summary>Entry point used by structure handlers (policy, assign, if, require bodies).</summary>
    public DomainExpression ParseExpression() => ParseOr();

    /// <summary>Public for pack forms that need a nested expression (e.g. parenthesized).</summary>
    public DomainExpression ParseNestedExpression() => ParseExpression();

    private DomainExpression ParseOr() {
        var left = ParseAnd();
        while (_c.Current.Kind == TokenKind.Or
               || (_c.Current.Kind == TokenKind.Identifier && _c.Current.Text == "or")) {
            _c.Advance();
            var right = ParseAnd();
            left = DomainExpression.Or(left, right);
        }
        return left;
    }

    private DomainExpression ParseAnd() {
        var left = ParseNot();
        while (_c.Current.Kind == TokenKind.And
               || (_c.Current.Kind == TokenKind.Identifier && _c.Current.Text == "and")) {
            _c.Advance();
            var right = ParseNot();
            left = DomainExpression.And(left, right);
        }
        return left;
    }

    private DomainExpression ParseNot() {
        if (_c.Current.Kind == TokenKind.Not) {
            _c.Advance();
            var operand = ParseAdd();
            return DomainExpression.Not(operand);
        }
        return ParseComparison();
    }

    private DomainExpression ParseComparison() {
        var left = ParseAdd();

        if (IsComparisonOp(_c.Current.Kind)) {
            var op = _c.Current.Kind;

            if (op == TokenKind.Is && _c.PeekIs(TokenKind.Not)) {
                _c.Advance();
                _c.Advance();
                var rhs = ParseAdd();
                return DomainExpression.NotEqual(left, rhs);
            }

            _c.Advance();

            if (op == TokenKind.Is) {
                var rhs = ParseAdd();
                return DomainExpression.Equal(left, rhs);
            }

            var right = ParseAdd();
            return op switch {
                TokenKind.Eq => DomainExpression.Equal(left, right),
                TokenKind.Neq => DomainExpression.NotEqual(left, right),
                TokenKind.Gt => DomainExpression.GreaterThan(left, right),
                TokenKind.Gte => DomainExpression.GreaterThanOrEqual(left, right),
                TokenKind.Lt => DomainExpression.LessThan(left, right),
                TokenKind.Lte => DomainExpression.LessThanOrEqual(left, right),
                _ => throw _c.Error($"Unknown comparison operator '{op}'"),
            };
        }

        return left;
    }

    private DomainExpression ParseAdd() {
        var left = ParseMultiply();
        while (_c.Current.Kind is TokenKind.Plus or TokenKind.Minus) {
            var op = _c.Current.Kind;
            _c.Advance();
            var right = ParseMultiply();
            left = op == TokenKind.Plus
                ? DomainExpression.Add(left, right)
                : DomainExpression.Subtract(left, right);
        }
        return left;
    }

    private DomainExpression ParseMultiply() {
        var left = ParsePrimary();
        while (_c.Current.Kind is TokenKind.Star or TokenKind.Slash) {
            var op = _c.Current.Kind;
            _c.Advance();
            var right = ParsePrimary();
            left = op == TokenKind.Star
                ? DomainExpression.Multiply(left, right)
                : DomainExpression.Divide(left, right);
        }
        return left;
    }

    private DomainExpression ParsePrimary() {
        // E1 open forms (temporal Now / unit durations, etc.) before builtins.
        if (_forms.TryParsePrimary(_c, this, out var specialized))
            return specialized;

        // Optional grammar first-token check (documents product primary surface).
        _ = _c.MatchRule("expr-primary");

        switch (_c.Current.Kind) {
            case TokenKind.Number:
                var numText = _c.Current.Text;
                _c.Advance();
                if (long.TryParse(numText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
                    return DomainExpression.Literal(longVal);
                return DomainExpression.Literal(numText);

            case TokenKind.StringLiteral:
                var str = _c.Current.Text;
                _c.Advance();
                return DomainExpression.Literal(str);

            case TokenKind.True:
                _c.Advance();
                return DomainExpression.Literal(true);

            case TokenKind.False:
                _c.Advance();
                return DomainExpression.Literal(false);

            case TokenKind.Null:
                _c.Advance();
                return DomainExpression.Literal(null);

            case TokenKind.LParen:
                _c.Advance();
                var expr = ParseExpression();
                _c.Expect(TokenKind.RParen);
                return expr;

            case TokenKind.Identifier:
                var name = _c.Current.Text;
                _c.Advance();
                if (IsQuantifierKeyword(name) && _c.Current.Kind == TokenKind.Identifier) {
                    return ParseQuantifiedExpression(name);
                }
                if (_c.Current.Kind == TokenKind.Identifier) {
                    return ParseRelatedAccess(name);
                }
                return DomainExpression.Property(name);

            case TokenKind.Not:
                return ParseNot();

            default:
                throw _c.Error($"Expected expression, got '{_c.Current.Text}'");
        }
    }

    private DomainExpression ParseRelatedAccess(string relName) {
        var next = _c.Current.Text;

        if (string.Equals(next, "exists", StringComparison.OrdinalIgnoreCase)) {
            _c.Advance();
            return DomainExpression.Exists(DomainExpression.Property(relName));
        }

        if (string.Equals(next, "where", StringComparison.OrdinalIgnoreCase)) {
            if (_c.InWhereBody)
                throw _c.Error("Nested 'where' is not allowed. Use parentheses for grouped conditions instead.");
            _c.Advance();
            _c.InWhereBody = true;
            try {
                var body = ParseAnd();
                return DomainExpression.RelationshipNav(relName, body);
            }
            finally {
                _c.InWhereBody = false;
            }
        }

        _c.Advance();
        var propName = next;

        if (_c.Current.Kind == TokenKind.Identifier
            && !string.Equals(_c.Current.Text, "exists", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_c.Current.Text, "where", StringComparison.OrdinalIgnoreCase)
            && !IsComparisonOp(_c.Current.Kind)) {
            return DomainExpression.RelationshipNav(relName, ParseRelatedAccess(propName));
        }

        var propExpr = DomainExpression.Property(propName);

        if (IsComparisonOp(_c.Current.Kind)) {
            var op = _c.Current.Kind;

            if (op == TokenKind.Is && _c.PeekIs(TokenKind.Not)) {
                _c.Advance();
                _c.Advance();
                var rhs = ParsePrimary();
                return DomainExpression.RelationshipNav(relName, DomainExpression.NotEqual(propExpr, rhs));
            }

            _c.Advance();

            if (op == TokenKind.Is) {
                var rhs = ParsePrimary();
                return DomainExpression.RelationshipNav(relName, DomainExpression.Equal(propExpr, rhs));
            }

            var right = ParsePrimary();
            var comparison = op switch {
                TokenKind.Eq => DomainExpression.Equal(propExpr, right),
                TokenKind.Neq => DomainExpression.NotEqual(propExpr, right),
                TokenKind.Gt => DomainExpression.GreaterThan(propExpr, right),
                TokenKind.Gte => DomainExpression.GreaterThanOrEqual(propExpr, right),
                TokenKind.Lt => DomainExpression.LessThan(propExpr, right),
                TokenKind.Lte => DomainExpression.LessThanOrEqual(propExpr, right),
                _ => throw _c.Error($"Unknown comparison operator '{op}'"),
            };
            return DomainExpression.RelationshipNav(relName, comparison);
        }

        return DomainExpression.RelationshipNav(relName, propExpr);
    }

    private DomainExpression ParseQuantifiedExpression(string quantifier) {
        var relName = _c.ExpectIdentifier(TokenKind.Identifier, "relationship name");

        if (quantifier == "count" && !string.Equals(_c.Current.Text, "where", StringComparison.OrdinalIgnoreCase)) {
            return DomainExpression.Count(relName, null);
        }

        if (!string.Equals(_c.Current.Text, "where", StringComparison.OrdinalIgnoreCase))
            throw _c.Error($"Expected 'where' after '{quantifier} {relName}', got '{_c.Current.Text}'");
        _c.Advance();

        var body = ParseAnd();

        return quantifier switch {
            "any" => DomainExpression.Any(relName, body),
            "all" => DomainExpression.All(relName, body),
            "none" => DomainExpression.None(relName, body),
            "count" => DomainExpression.Count(relName, body),
            _ => throw _c.Error($"Unknown quantifier '{quantifier}'"),
        };
    }

    private static bool IsQuantifierKeyword(string text) =>
        string.Equals(text, "any", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text, "all", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text, "none", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text, "count", StringComparison.OrdinalIgnoreCase);

    private static bool IsComparisonOp(TokenKind kind) => kind switch {
        TokenKind.Is or TokenKind.Eq or TokenKind.Neq
            or TokenKind.Gt or TokenKind.Gte or TokenKind.Lt or TokenKind.Lte => true,
        _ => false,
    };
}