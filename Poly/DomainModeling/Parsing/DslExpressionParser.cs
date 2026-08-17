using System.Globalization;

using Poly.Grammar;

namespace Poly.DomainModeling.Parsing;

/// <summary>
/// Grammar-guided expression parser layered over the cursor (which wraps the
/// examine/consume reader + matcher).
/// </summary>
public sealed class DslExpressionParser {
    private readonly IDslParseCursor _c;
    private readonly ExpressionFormRegistry _forms;
    private readonly ExpressionFoldTable _folds;

    public DslExpressionParser(
        IDslParseCursor cursor,
        ExpressionFormRegistry? forms = null,
        ExpressionFoldTable? folds = null) {
        _c = cursor ?? throw new ArgumentNullException(nameof(cursor));
        _forms = forms ?? new ExpressionFormRegistry();
        _folds = folds ?? ExpressionFoldTable.Core();
    }

    public DomainExpression ParseExpression() => ParseOr();

    public DomainExpression ParseNestedExpression() => ParseExpression();

    private DomainExpression ParseOr() {
        var left = ParseAnd();
        while (_c.MatchRule("expr-or-op") is { } op) {
            Consume(op);
            var right = ParseAnd();
            left = DomainExpression.Or(left, right);
        }
        return left;
    }

    private DomainExpression ParseAnd() {
        var left = ParseNot();
        while (_c.MatchRule("expr-and-op") is { } op) {
            Consume(op);
            var right = ParseNot();
            left = DomainExpression.And(left, right);
        }
        return left;
    }

    private DomainExpression ParseNot() {
        if (_c.MatchRule("expr-not-op") is { } m) {
            Consume(m);
            var operand = ParseAdd();
            return DomainExpression.Not(operand);
        }
        return ParseComparison();
    }

    private DomainExpression ParseComparison() {
        var left = ParseAdd();
        if (_c.MatchRule("expr-compare-op") is { } opMatch) {
            var kind = opMatch.Tokens[0].Kind;
            Consume(opMatch);

            if (kind == DslTokenKind.Is && _c.MatchRule("expr-not-op") is { } notMatch) {
                Consume(notMatch);
                var rhs = ParseAdd();
                return DomainExpression.NotEqual(left, rhs);
            }

            var right = ParseAdd();
            return kind switch {
                DslTokenKind.Is => DomainExpression.Equal(left, right),
                DslTokenKind.Eq => DomainExpression.Equal(left, right),
                DslTokenKind.Neq => DomainExpression.NotEqual(left, right),
                DslTokenKind.Gt => DomainExpression.GreaterThan(left, right),
                DslTokenKind.Gte => DomainExpression.GreaterThanOrEqual(left, right),
                DslTokenKind.Lt => DomainExpression.LessThan(left, right),
                DslTokenKind.Lte => DomainExpression.LessThanOrEqual(left, right),
                _ => throw _c.Error($"Unknown comparison operator '{kind}'"),
            };
        }
        return left;
    }

    private DomainExpression ParseAdd() {
        var left = ParseMultiply();
        while (_c.MatchRule("expr-add-op") is { } op) {
            var isPlus = op.Tokens[0].Kind == DslTokenKind.Plus;
            Consume(op);
            var right = ParseMultiply();
            left = _forms.TryFoldBinary(left, right, isPlus) ?? (isPlus
                ? DomainExpression.Add(left, right)
                : DomainExpression.Subtract(left, right));
        }
        return left;
    }

    private DomainExpression ParseMultiply() {
        var left = ParsePrimary();
        while (_c.MatchRule("expr-mul-op") is { } op) {
            var isStar = op.Tokens[0].Kind == DslTokenKind.Star;
            Consume(op);
            var right = ParsePrimary();
            left = isStar
                ? DomainExpression.Multiply(left, right)
                : DomainExpression.Divide(left, right);
        }
        return left;
    }

    private DomainExpression ParsePrimary() {
        if (_c.MatchRule("expr-primary") is { } matched
            && _folds.TryFold("expr-primary", matched, out var folded)) {
            Consume(matched);
            return folded;
        }

        switch (_c.Current.Kind) {
            case DslTokenKind.Number:
                var numText = _c.Current.Text;
                _c.Advance();
                if (long.TryParse(numText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
                    return DomainExpression.Literal(longVal);
                if (double.TryParse(numText, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal))
                    return DomainExpression.Literal(doubleVal);
                return DomainExpression.Literal(numText);

            case DslTokenKind.StringLiteral:
                var str = _c.Current.Text;
                _c.Advance();
                return DomainExpression.Literal(str);

            case DslTokenKind.True:
                _c.Advance();
                return DomainExpression.Literal(true);

            case DslTokenKind.False:
                _c.Advance();
                return DomainExpression.Literal(false);

            case DslTokenKind.Null:
                _c.Advance();
                return DomainExpression.Literal(null);

            case DslTokenKind.LParen:
                _c.Advance();
                var expr = ParseExpression();
                _c.Expect(DslTokenKind.RParen);
                return expr;

            case DslTokenKind.Identifier:
                var name = _c.Current.Text;
                _c.Advance();
                if (IsQuantifierKeyword(name) && _c.Current.Kind == DslTokenKind.Identifier) {
                    return ParseQuantifiedExpression(name);
                }
                // In an initializer value, a second identifier followed by a colon is the
                // NEXT initializer's property name, not a path-prefix continuation:
                // `create in fs { Name: newName Content: b }` — `newName` stands alone.
                if (_c.Current.Kind == DslTokenKind.Identifier
                    && !(_c.InPropertyInitializerValue && _c.Peek(1).Kind == DslTokenKind.Colon)) {
                    return ParseRelatedAccess(name);
                }
                return DomainExpression.Property(name);

            case DslTokenKind.Not:
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

        if (_c.Current.Kind == DslTokenKind.Identifier
            && !string.Equals(_c.Current.Text, "exists", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_c.Current.Text, "where", StringComparison.OrdinalIgnoreCase)
            && !IsComparisonOp(_c.Current.Kind)) {
            return DomainExpression.RelationshipNav(relName, ParseRelatedAccess(propName));
        }

        var propExpr = DomainExpression.Property(propName);

        if (IsComparisonOp(_c.Current.Kind)) {
            var op = _c.Current.Kind;

            if (op == DslTokenKind.Is && _c.PeekIs(DslTokenKind.Not)) {
                _c.Advance();
                _c.Advance();
                var rhs = ParsePrimary();
                return DomainExpression.RelationshipNav(relName, DomainExpression.NotEqual(propExpr, rhs));
            }

            _c.Advance();

            if (op == DslTokenKind.Is) {
                var rhs = ParsePrimary();
                return DomainExpression.RelationshipNav(relName, DomainExpression.Equal(propExpr, rhs));
            }

            var right = ParsePrimary();
            var comparison = op switch {
                DslTokenKind.Eq => DomainExpression.Equal(propExpr, right),
                DslTokenKind.Neq => DomainExpression.NotEqual(propExpr, right),
                DslTokenKind.Gt => DomainExpression.GreaterThan(propExpr, right),
                DslTokenKind.Gte => DomainExpression.GreaterThanOrEqual(propExpr, right),
                DslTokenKind.Lt => DomainExpression.LessThan(propExpr, right),
                DslTokenKind.Lte => DomainExpression.LessThanOrEqual(propExpr, right),
                _ => throw _c.Error($"Unknown comparison operator '{op}'"),
            };
            return DomainExpression.RelationshipNav(relName, comparison);
        }

        return DomainExpression.RelationshipNav(relName, propExpr);
    }

    private DomainExpression ParseQuantifiedExpression(string quantifier) {
        var relName = _c.ExpectIdentifier(DslTokenKind.Identifier, "relationship name");

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

    private void Consume(MatchResult<DslToken, DslTokenKind> match) => _c.Consume(match);

    private static bool IsQuantifierKeyword(string text) =>
        string.Equals(text, "any", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text, "all", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text, "none", StringComparison.OrdinalIgnoreCase)
        || string.Equals(text, "count", StringComparison.OrdinalIgnoreCase);

    private static bool IsComparisonOp(DslTokenKind kind) => DslGrammar.IsCompareOpKind(kind);
}