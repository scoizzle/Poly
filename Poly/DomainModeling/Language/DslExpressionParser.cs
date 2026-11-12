using System.Globalization;

using Poly.DomainModeling.Libraries.Temporal;
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

    public DomainExpression ParseExpression() => ParseExpressionAt(0);

    /// <summary>
    /// <paramref name="level"/> counts enclosing nesting levels: 0 at the top, one more per
    /// paren group, <c>not</c>, comparison operand or <c>where</c> body.
    /// </summary>
    internal DomainExpression ParseExpressionAt(int level) {
        var match = _c.MatchRule("expr-live");
        if (match is null)
            throw _c.Error($"Expected expression, got '{_c.Current.Text}'");
        Consume(match);
        var folded = FoldRule(match, inWhere: false, level);
        RejectUnknownDurationUnit(match);
        return folded;
    }

    /// <summary>
    /// OQ-4 ruled the nesting limit at <see cref="DslLimits.MaxExpressionDepth"/>. Over the
    /// limit is a positioned error on the token that crossed it, never an unbounded fold.
    /// </summary>
    private void GuardLevel(int level, DslToken at) {
        if (level <= DslLimits.MaxExpressionDepth)
            return;
        throw new GrammarException(
            DslLimits.DepthDetail, at.Line, at.Col, at.Offset, at.Length);
    }

    /// <summary>
    /// A duration is <c>Number</c> + a known unit (<c>DslGrammar.IsDurationUnitToken</c>), so a
    /// number followed on the same line by a word that is not a unit fails closed here with the
    /// legal spellings. Two shapes are exempt: <c>Identifier :</c> (property initializers and
    /// invoke bindings) and a same-line <c>function</c> declaration.
    /// </summary>
    private void RejectUnknownDurationUnit(MatchResult<DslToken, DslTokenKind> match) {
        var last = match.Tokens[^1];
        if (last.Kind != DslTokenKind.Number) return;
        if (_c.Current.Kind != DslTokenKind.Identifier) return;
        if (_c.Current.Line != last.Line) return;
        if (_c.PeekIs(DslTokenKind.Colon)) return;
        if (DslGrammar.IsFunctionDeclarationWord(_c.Current)) return;
        if (DslGrammar.IsDurationUnitToken(_c.Current)) return;
        throw _c.Error(
            $"Unknown duration unit '{_c.Current.Text}'. Legal units: {DurationUnits.LegalUnits}.");
    }

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

    private DomainExpression FoldRule(MatchResult<DslToken, DslTokenKind> match, bool inWhere, int level) =>
        match.RuleName switch {
            "expr" or "expr-live" => FoldRule(match.Children[0], inWhere, level),
            "expr-or" or "expr-live-or" => FoldLeftAssoc(match, inWhere, level, DomainExpression.Or),
            "expr-and" or "expr-live-and" => FoldLeftAssoc(match, inWhere, level, DomainExpression.And),
            "expr-not" or "expr-live-not" => FoldNot(match, inWhere, level),
            "expr-compare" or "expr-live-compare" => FoldCompare(match, inWhere, level),
            "expr-add" or "expr-add-no-not" => FoldLeftAssoc(match, inWhere, level, FoldAddOp),
            "expr-mul" or "expr-mul-no-not" => FoldLeftAssoc(match, inWhere, level, FoldMulOp),
            "expr-primary" or "expr-primary-no-not" => FoldPrimary(match, inWhere, level),
            _ => FoldPrimary(match, inWhere, level),
        };

    private DomainExpression FoldLeftAssoc(
        MatchResult<DslToken, DslTokenKind> match,
        bool inWhere,
        int level,
        Func<DomainExpression, DomainExpression, DomainExpression> combine) {
        var operands = match.Children;
        GuardChain(level, operands.Count, match);
        var acc = FoldRule(operands[0], inWhere, level + operands.Count - 1);
        for (var i = 1; i < operands.Count; i++)
            acc = combine(acc, FoldRule(operands[i], inWhere, level + operands.Count - i));
        return acc;
    }

    private DomainExpression FoldLeftAssoc(
        MatchResult<DslToken, DslTokenKind> match,
        bool inWhere,
        int level,
        Func<DomainExpression, DslTokenKind, DomainExpression, DomainExpression> combine) {
        var operands = match.Children;
        GuardChain(level, operands.Count, match);
        var acc = FoldRule(operands[0], inWhere, level + operands.Count - 1);
        for (var i = 1; i < operands.Count; i++)
            acc = combine(acc, match.Operators[i - 1].Kind,
                FoldRule(operands[i], inWhere, level + operands.Count - i));
        return acc;
    }

    /// <summary>
    /// A chain of N operands at <paramref name="level"/> reaches level + N - 1 at its deepest
    /// operand, because the fold is left-associative. Rejecting the whole chain up front keeps
    /// the deepest operand out of the fold, and the error names the operator that closes it.
    /// </summary>
    private void GuardChain(int level, int operands, MatchResult<DslToken, DslTokenKind> match) {
        // A one-operand chain adds no level: every rule family wraps a single expression in one.
        if (operands <= 1 || level + operands - 1 <= DslLimits.MaxExpressionDepth)
            return;
        var at = match.Operators.Count > 0 ? match.Operators[^1] : match.Tokens[^1];
        throw new GrammarException(DslLimits.DepthDetail, at.Line, at.Col, at.Offset, at.Length);
    }

    private static DomainExpression FoldMulOp(DomainExpression left, DslTokenKind op, DomainExpression right) =>
        op == DslTokenKind.Star
            ? DomainExpression.Multiply(left, right)
            : DomainExpression.Divide(left, right);

    private DomainExpression FoldNot(MatchResult<DslToken, DslTokenKind> match, bool inWhere, int level) {
        if (match.PatternName != "not")
            return FoldRule(match.Children[0], inWhere, level);
        GuardLevel(level + 1, match.Tokens[0]);
        return DomainExpression.Not(FoldRule(match.Children[0], inWhere, level + 1));
    }

    private DomainExpression FoldCompare(MatchResult<DslToken, DslTokenKind> match, bool inWhere, int level) {
        if (match.PatternName == "bare")
            return FoldRule(match.Children[0], inWhere, level);
        GuardLevel(level + 1, match.Tokens[0]);
        var left = FoldRule(match.Children[0], inWhere, level + 1);
        var right = FoldRule(match.Children[1], inWhere, level + 1);
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

    private DomainExpression FoldPrimary(MatchResult<DslToken, DslTokenKind> match, bool inWhere, int level) {
        if (match.PatternName is not "group" and not "not") {
            // Session folds override pattern payload (library Now vs core ident-shaped clock).
            if (_folds.TryFold(match.RuleName, match, out var folded))
                return RewriteIdent(folded);
            if (match.TryFoldExpression(out var bound))
                return RewriteIdent(bound);
        }

        return match.PatternName switch {
            "group" => FoldGroup(match, inWhere, level),
            "not" => NotAt(match, inWhere, level),
            "duration" => FoldUnresolvedDuration(match),
            "match" => FoldMatch(match, level),
            "call" => FoldCall(match, level),
            "exists" => DomainExpression.Exists(DomainExpression.Property(CaptureText(match, "rel"))),
            "where-nav" => FoldWhereNav(match, inWhere, level),
            "quant-where" => FoldQuantWhere(match, inWhere, level),
            "count-bare" => DomainExpression.Count(CaptureText(match, "rel"), null),
            "sum-prop" => DomainExpression.Sum(CaptureText(match, "rel"), CaptureText(match, "prop")),
            "path-is-not" => FoldPathCmp(match, notEqual: true, inWhere, level),
            "path-cmp" => FoldPathCmp(match, notEqual: false, inWhere, level),
            "path" => FoldPath(match),
            _ => throw new InvalidOperationException(
                $"Cannot fold primary '{match.RuleName}/{match.PatternName}'."),
        };
    }

    /// <summary>
    /// <c>match { cond -&gt; value, ..., else -&gt; value }</c>: the balanced braces are split at top-level
    /// commas; each arm is split at its first top-level <c>-&gt;</c>. Exactly one <c>else</c> arm is
    /// required and it must be last. Every malformation is a positioned error.
    /// </summary>
    private DomainExpression NotAt(MatchResult<DslToken, DslTokenKind> match, bool inWhere, int level) {
        GuardLevel(level + 1, match.Tokens[0]);
        return DomainExpression.Not(FoldRule(match.Children[0], inWhere, level + 1));
    }

    private DomainExpression FoldMatch(MatchResult<DslToken, DslTokenKind> match, int level) {
        GuardLevel(level + 1, match.Tokens[0]);
        var keyword = match.Tokens[0];
        var closing = match.Tokens[^1];
        var inner = match.Tokens.Skip(2).Take(match.Tokens.Count - 3).ToList();
        static string At(DslToken t) => $" (line {t.Line}, col {t.Col})";

        var segments = new List<List<DslToken>>();
        var current = new List<DslToken>();
        var depth = 0;
        foreach (var token in inner) {
            if (token.Kind is DslTokenKind.LParen or DslTokenKind.LBrace or DslTokenKind.LBracket) depth++;
            else if (token.Kind is DslTokenKind.RParen or DslTokenKind.RBrace or DslTokenKind.RBracket) depth--;
            if (depth == 0 && token.Kind == DslTokenKind.Comma) {
                if (current.Count == 0)
                    throw _c.Error($"Expected an arm in 'match' before '{token.Text}'{At(token)}");
                segments.Add(current);
                current = [];
            }
            else current.Add(token);
        }
        if (current.Count > 0) segments.Add(current);
        else if (segments.Count > 0)
            throw _c.Error($"Expected an arm in 'match' after the trailing ','{At(closing)}");

        if (segments.Count == 0)
            throw _c.Error($"'match' needs at least an 'else' arm (else -> value){At(keyword)}");

        var elseSegments = segments.Where(sg => sg[0].Kind == DslTokenKind.Else).ToList();
        if (elseSegments.Count > 1)
            throw _c.Error($"'match' has more than one 'else' arm{At(elseSegments[1][0])}");

        var arms = new List<MatchArm>();
        DomainExpression? elseValue = null;
        for (var i = 0; i < segments.Count; i++) {
            var segment = segments[i];
            var first = segment[0];
            var arrow = -1;
            depth = 0;
            for (var j = 0; j < segment.Count; j++) {
                var k = segment[j].Kind;
                if (k is DslTokenKind.LParen or DslTokenKind.LBrace or DslTokenKind.LBracket) depth++;
                else if (k is DslTokenKind.RParen or DslTokenKind.RBrace or DslTokenKind.RBracket) depth--;
                else if (depth == 0 && k == DslTokenKind.Arrow) { arrow = j; break; }
            }
            if (arrow < 0)
                throw _c.Error($"Expected '->' in 'match' arm starting at '{first.Text}'{At(first)}");
            var condition = segment.Take(arrow).ToList();
            var value = segment.Skip(arrow + 1).ToList();
            var isElse = condition is [{ Kind: DslTokenKind.Else }];
            if (condition.Count == 0)
                throw _c.Error($"Expected a condition before '->' in 'match'{At(segment[arrow])}");
            if (value.Count == 0)
                throw _c.Error($"Expected a value after '->' in 'match'{At(segment[arrow])}");
            if (isElse) {
                if (elseValue is not null)
                    throw _c.Error($"'match' has more than one 'else' arm{At(first)}");
                if (i != segments.Count - 1)
                    throw _c.Error($"'else' must be the last arm of a 'match'{At(first)}");
                elseValue = ParseArm(value, "match value", level);
            }
            else {
                if (condition.Any(t => t.Kind == DslTokenKind.Else))
                    throw _c.Error($"'else' cannot appear inside a 'match' condition{At(first)}");
                arms.Add(new MatchArm(
                    ParseArm(condition, "match condition", level),
                    ParseArm(value, "match value", level)));
            }
        }

        if (elseValue is null)
            throw _c.Error($"'match' requires a final 'else' arm (else -> value){At(keyword)}");
        return DomainExpression.Match(arms, elseValue, keyword.Line, keyword.Col);
    }

    private DomainExpression ParseArm(List<DslToken> tokens, string what, int level) {
        var last = tokens[^1];
        var withEof = new List<DslToken>(tokens) { new(DslTokenKind.EndOfFile, "", last.Line, last.Col) };
        var reader = new ListTokenReader<DslToken, DslTokenKind>(withEof, static k => k == DslTokenKind.EndOfFile);
        var nested = new DslCursor(reader, new Matcher<DslToken, DslTokenKind>(_c.Grammar, reader));
        var parsed = new DslExpressionParser(nested, _forms, _folds).ParseExpressionAt(level + 1);
        if (nested.Current.Kind != DslTokenKind.EndOfFile)
            throw nested.Error($"Unexpected '{nested.Current.Text}' in {what}");
        return parsed;
    }

    /// <summary>
    /// <c>Name(arg, …)</c>: the balanced parentheses are split at top-level commas and each
    /// segment is parsed as a full expression. Empty segments (a leading, doubled, or
    /// trailing comma) are a positioned error.
    /// </summary>
    private DomainExpression FoldCall(MatchResult<DslToken, DslTokenKind> match, int level) {
        // A zero-argument call has no arm to charge, so it pays its own level here.
        GuardLevel(level + 1, match.Tokens[0]);
        var nameToken = match.Tokens[0];
        var name = nameToken.Text;
        var inner = match.Tokens.Skip(2).Take(match.Tokens.Count - 3).ToList();
        var arguments = new List<DomainExpression>();
        if (inner.Count > 0) {
            var depth = 0;
            var segment = new List<DslToken>();
            void Flush(DslToken at) {
                if (segment.Count == 0)
                    throw _c.Error($"Expected an argument in call to '{name}' before '{at.Text}' (line {at.Line}, col {at.Col})");
                arguments.Add(ParseArgument(segment, level + 1));
                segment = [];
            }
            foreach (var token in inner) {
                if (token.Kind == DslTokenKind.LParen) depth++;
                else if (token.Kind == DslTokenKind.RParen) depth--;
                if (depth == 0 && token.Kind == DslTokenKind.Comma) Flush(token);
                else segment.Add(token);
            }
            Flush(match.Tokens[^1]);
        }
        return DomainExpression.Call(name, arguments, nameToken.Line, nameToken.Col);
    }

    private DomainExpression ParseArgument(List<DslToken> tokens, int level) {
        var last = tokens[^1];
        var withEof = new List<DslToken>(tokens) { new(DslTokenKind.EndOfFile, "", last.Line, last.Col) };
        var reader = new ListTokenReader<DslToken, DslTokenKind>(withEof, static k => k == DslTokenKind.EndOfFile);
        var nested = new DslCursor(reader, new Matcher<DslToken, DslTokenKind>(_c.Grammar, reader));
        var argument = new DslExpressionParser(nested, _forms, _folds).ParseExpressionAt(level);
        if (nested.Current.Kind != DslTokenKind.EndOfFile)
            throw nested.Error($"Unexpected '{nested.Current.Text}' in call argument");
        return argument;
    }

    private DomainExpression FoldUnresolvedDuration(MatchResult<DslToken, DslTokenKind> match) {
        var amountText = CaptureText(match, "amount");
        if (!long.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            throw _c.Error($"Duration amount '{amountText}' is not an integer.");
        return new UnresolvedDuration(amount, CaptureText(match, "unit"));
    }

    private DomainExpression RewriteIdent(DomainExpression expression) =>
        expression is PropertyAccess pa && _forms.TryRewriteIdent(pa.Name, out var claimed)
            ? claimed
            : expression;

    private DomainExpression FoldGroup(MatchResult<DslToken, DslTokenKind> match, bool inWhere, int level) {
        GuardLevel(level + 1, match.Tokens[0]);
        if (match.Children.Count > 0)
            return FoldRule(match.Children[0], inWhere, level + 1);
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
        var parsed = new DslExpressionParser(nested, _forms, _folds).ParseExpressionAt(level + 1);
        if (nested.Current.Kind != DslTokenKind.EndOfFile)
            throw nested.Error($"Unexpected '{nested.Current.Text}' in group");
        return parsed;
    }

    private DomainExpression FoldWhereNav(MatchResult<DslToken, DslTokenKind> match, bool inWhere, int level) {
        if (inWhere)
            throw _c.Error("Nested 'where' is not allowed. Use parentheses for grouped conditions instead.");
        GuardLevel(level + 1, match.Tokens[0]);
        var body = FoldRule(match.Children[0], inWhere: true, level + 1);
        return DomainExpression.RelationshipNav(CaptureText(match, "rel"), body);
    }

    private DomainExpression FoldQuantWhere(MatchResult<DslToken, DslTokenKind> match, bool inWhere, int level) {
        if (inWhere)
            throw _c.Error("Nested 'where' is not allowed. Use parentheses for grouped conditions instead.");
        var q = CaptureText(match, "quant").ToLowerInvariant();
        var rel = CaptureText(match, "rel");
        GuardLevel(level + 1, match.Tokens[0]);
        var body = FoldRule(match.Children[0], inWhere: true, level + 1);
        return q switch {
            "any" => DomainExpression.Any(rel, body),
            "all" => DomainExpression.All(rel, body),
            "none" => DomainExpression.None(rel, body),
            "count" => DomainExpression.Count(rel, body),
            "sum" => throw _c.Error("sum does not take where; write sum Rel Prop"),
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
        bool inWhere,
        int level) {
        var names = PathHopNames(match, includeTrailingPrimary: false);
        GuardLevel(level + 1, match.Tokens[0]);
        var rhs = FoldPrimary(match.Children[^1], inWhere, level + 1);
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