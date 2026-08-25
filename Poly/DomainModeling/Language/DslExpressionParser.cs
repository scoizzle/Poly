using System.Globalization;

using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.DomainModeling.Language;

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

    public DomainExpression ParseExpression() {
        var match = _c.MatchRule("expr-live");
        if (match is null)
            throw _c.Error($"Expected expression, got '{_c.Current.Text}'");
        Consume(match);
        return FoldRule(match);
    }

    public DomainExpression ParseNestedExpression() => ParseExpression();

    private DomainExpression FoldAddOp(DomainExpression left, DslTokenKind op, DomainExpression right) {
        var isPlus = op == DslTokenKind.Plus;
        return _forms.TryFoldBinary(left, right, isPlus) ?? (isPlus
            ? DomainExpression.Add(left, right)
            : DomainExpression.Subtract(left, right));
    }

    private static string CaptureText(MatchResult<DslToken, DslTokenKind> match, string name) =>
        match.Captures.TryGetValue(name, out var tokens) && tokens.Count > 0
            ? tokens[0].Text
            : throw new InvalidOperationException($"Match '{match.PatternName}' is missing capture '{name}'.");

    private DomainExpression FoldRule(MatchResult<DslToken, DslTokenKind> match, bool inWhere = false) =>
        match.RuleName switch {
            "expr" or "expr-live" => FoldRule(match.Children[0], inWhere),
            "expr-or" or "expr-live-or" => FoldLeftAssoc(match, inWhere, DomainExpression.Or),
            "expr-and" or "expr-live-and" => FoldLeftAssoc(match, inWhere, DomainExpression.And),
            "expr-not" or "expr-live-not" => FoldNot(match, inWhere),
            "expr-compare" or "expr-live-compare" => FoldCompare(match, inWhere),
            "expr-add" or "expr-add-no-not" => FoldLeftAssoc(match, inWhere, FoldAddOp),
            "expr-mul" or "expr-mul-no-not" => FoldLeftAssoc(match, inWhere, FoldMulOp),
            "expr-primary" or "expr-primary-no-not" => FoldPrimary(match, inWhere),
            _ => FoldPrimary(match, inWhere),
        };

    private DomainExpression FoldLeftAssoc(
        MatchResult<DslToken, DslTokenKind> match,
        bool inWhere,
        Func<DomainExpression, DomainExpression, DomainExpression> combine) {
        var operands = match.Children;
        var acc = FoldRule(operands[0], inWhere);
        for (var i = 1; i < operands.Count; i++)
            acc = combine(acc, FoldRule(operands[i], inWhere));
        return acc;
    }

    private DomainExpression FoldLeftAssoc(
        MatchResult<DslToken, DslTokenKind> match,
        bool inWhere,
        Func<DomainExpression, DslTokenKind, DomainExpression, DomainExpression> combine) {
        var operands = match.Children;
        var acc = FoldRule(operands[0], inWhere);
        for (var i = 1; i < operands.Count; i++)
            acc = combine(acc, match.Operators[i - 1].Kind, FoldRule(operands[i], inWhere));
        return acc;
    }

    private static DomainExpression FoldMulOp(DomainExpression left, DslTokenKind op, DomainExpression right) =>
        op == DslTokenKind.Star
            ? DomainExpression.Multiply(left, right)
            : DomainExpression.Divide(left, right);

    private DomainExpression FoldNot(MatchResult<DslToken, DslTokenKind> match, bool inWhere) =>
        match.PatternName == "not"
            ? DomainExpression.Not(FoldRule(match.Children[0], inWhere))
            : FoldRule(match.Children[0], inWhere);

    private DomainExpression FoldCompare(MatchResult<DslToken, DslTokenKind> match, bool inWhere) {
        if (match.PatternName == "bare")
            return FoldRule(match.Children[0], inWhere);
        var left = FoldRule(match.Children[0], inWhere);
        var right = FoldRule(match.Children[1], inWhere);
        if (match.PatternName == "is-not")
            return DomainExpression.NotEqual(left, right);
        var op = match.Captures["compare-op"][0].Kind;
        return Compare(op, left, right);
    }

    private static DomainExpression Compare(DslTokenKind op, DomainExpression left, DomainExpression right) =>
        op switch {
            DslTokenKind.Is or DslTokenKind.Eq => DomainExpression.Equal(left, right),
            DslTokenKind.Neq => DomainExpression.NotEqual(left, right),
            DslTokenKind.Gt => DomainExpression.GreaterThan(left, right),
            DslTokenKind.Gte => DomainExpression.GreaterThanOrEqual(left, right),
            DslTokenKind.Lt => DomainExpression.LessThan(left, right),
            DslTokenKind.Lte => DomainExpression.LessThanOrEqual(left, right),
            _ => throw new InvalidOperationException($"Unknown comparison operator '{op}'"),
        };

    private DomainExpression FoldPrimary(MatchResult<DslToken, DslTokenKind> match, bool inWhere) {
        if (match.PatternName is not "group" and not "not"
            && _folds.TryFold(match.RuleName, match, out var folded))
            return folded;

        return match.PatternName switch {
            "group" => FoldGroup(match, inWhere),
            "not" => DomainExpression.Not(FoldRule(match.Children[0], inWhere)),
            "now" or "today" => DomainExpression.Property(match.Tokens[0].Text),
            "duration" => throw _c.Error(
                $"Duration '{string.Join(' ', match.Tokens.Select(t => t.Text))}' requires uses temporal."),
            "exists" => DomainExpression.Exists(DomainExpression.Property(CaptureText(match, "rel"))),
            "where-nav" => FoldWhereNav(match, inWhere),
            "quant-where" => FoldQuantWhere(match, inWhere),
            "count-bare" => DomainExpression.Count(CaptureText(match, "rel"), null),
            "path-is-not" => FoldPathCmp(match, notEqual: true, inWhere),
            "path-cmp" => FoldPathCmp(match, notEqual: false, inWhere),
            "path" => FoldPath(match),
            _ => throw new InvalidOperationException(
                $"Cannot fold primary '{match.RuleName}/{match.PatternName}'."),
        };
    }

    private DomainExpression FoldGroup(MatchResult<DslToken, DslTokenKind> match, bool inWhere) {
        if (match.Children.Count > 0)
            return FoldRule(match.Children[0], inWhere);
        if (match.Tokens.Count < 2)
            throw _c.Error("Expected expression inside parentheses");
        var inner = match.Tokens.Skip(1).Take(match.Tokens.Count - 2).ToList();
        if (inner.Count == 0)
            throw _c.Error("Expected expression inside parentheses");
        var last = inner[^1];
        var tokens = new List<DslToken>(inner) { new(DslTokenKind.EndOfFile, "", last.Line, last.Col) };
        var reader = new ListTokenReader<DslToken, DslTokenKind>(tokens, static k => k == DslTokenKind.EndOfFile);
        var nested = new DslCursor(reader, new Matcher<DslToken, DslTokenKind>(_c.Grammar, reader)) {
            InWhereBody = inWhere,
        };
        return new DslExpressionParser(nested, _forms, _folds).ParseExpression();
    }

    private DomainExpression FoldWhereNav(MatchResult<DslToken, DslTokenKind> match, bool inWhere = false) {
        if (inWhere)
            throw _c.Error("Nested 'where' is not allowed. Use parentheses for grouped conditions instead.");
        var body = FoldRule(match.Children[0], inWhere: true);
        return DomainExpression.RelationshipNav(CaptureText(match, "rel"), body);
    }

    private DomainExpression FoldQuantWhere(MatchResult<DslToken, DslTokenKind> match, bool inWhere = false) {
        if (inWhere)
            throw _c.Error("Nested 'where' is not allowed. Use parentheses for grouped conditions instead.");
        var q = CaptureText(match, "quant").ToLowerInvariant();
        var rel = CaptureText(match, "rel");
        var body = FoldRule(match.Children[0], inWhere: true);
        return q switch {
            "any" => DomainExpression.Any(rel, body),
            "all" => DomainExpression.All(rel, body),
            "none" => DomainExpression.None(rel, body),
            "count" => DomainExpression.Count(rel, body),
            _ => throw _c.Error($"Unknown quantifier '{q}'"),
        };
    }

    private DomainExpression FoldPath(MatchResult<DslToken, DslTokenKind> match) {
        var names = PathHopNames(match, includeTrailingPrimary: false);
        return WrapPath(names, DomainExpression.Property(names[^1]));
    }

    private DomainExpression FoldPathCmp(
        MatchResult<DslToken, DslTokenKind> match,
        bool notEqual,
        bool inWhere = false) {
        var names = PathHopNames(match, includeTrailingPrimary: false);
        var rhs = FoldPrimary(match.Children[^1], inWhere);
        DomainExpression inner = notEqual
            ? DomainExpression.NotEqual(DomainExpression.Property(names[^1]), rhs)
            : Compare(match.Captures["op"][0].Kind, DomainExpression.Property(names[^1]), rhs);
        return WrapPath(names, inner);
    }

    private static List<string> PathHopNames(
        MatchResult<DslToken, DslTokenKind> match,
        bool includeTrailingPrimary) {
        var names = new List<string> { CaptureText(match, "rel"), CaptureText(match, "prop") };
        var hops = includeTrailingPrimary
            ? match.Children
            : match.Children.Count > 0 && match.PatternName is "path-cmp" or "path-is-not"
                ? match.Children.Take(match.Children.Count - 1)
                : match.Children;
        foreach (var hop in hops)
            names.Add(CaptureText(hop, "hop"));
        return names;
    }

    private static DomainExpression WrapPath(List<string> names, DomainExpression inner) {
        for (var i = names.Count - 2; i >= 0; i--)
            inner = DomainExpression.RelationshipNav(names[i], inner);
        return inner;
    }

    private void Consume(MatchResult<DslToken, DslTokenKind> match) => _c.Consume(match);
}