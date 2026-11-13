using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Language;

/// <summary>How a binary operator groups with itself: <c>a - b - c</c> is <c>(a - b) - c</c> for <see cref="Left"/>;
/// a <see cref="None"/> operator cannot chain, so <c>a &lt; b &lt; c</c> stops after <c>a &lt; b</c>.</summary>
public enum Assoc {
    Left,
    None,
}

/// <summary>
/// One infix operator: the token (and an optional second token, as in <c>is not</c>), its
/// <paramref name="Precedence"/> (higher binds tighter), its <paramref name="Assoc"/> and the IR it folds to.
/// </summary>
public sealed record BinaryRow(
    DslTokenKind Op,
    int Precedence,
    Assoc Assoc,
    Func<DomainExpression, DomainExpression, DomainExpression> Fold,
    DslTokenKind? Follow = null);

/// <summary>
/// One prefix operator. Its operand parses at <paramref name="OperandPrecedence"/> (the loosest level the operand
/// may contain), so <c>not a + b</c> negates the whole sum.
/// </summary>
public sealed record PrefixRow(
    DslTokenKind Op,
    int OperandPrecedence,
    Func<DomainExpression, DomainExpression> Fold);

/// <summary>
/// The expression driver's operator rows. A binary row sits at a precedence level; the levels (the distinct
/// precedences, loosest first) are the driver's chain frames, so adding an operator is one row here and
/// no grammar edit. A prefix row applies at the head of the level just below its operand precedence (where its
/// result closes the level, so <c>not a &gt; b</c> stops after <c>not a</c>) and at any operand position (where
/// its operand is the whole add-level chain, so <c>a + not b + c</c> negates <c>b + c</c>).
/// Rows are immutable once built; <see cref="With(BinaryRow)"/> returns a new table and a duplicate throws.
/// </summary>
public sealed class DslOperatorTable {
    private readonly Dictionary<(DslTokenKind Op, DslTokenKind? Follow), BinaryRow> _binary;
    private readonly Dictionary<DslTokenKind, PrefixRow> _prefix;
    private readonly HashSet<DslTokenKind> _hasFollow;

    private DslOperatorTable(IEnumerable<BinaryRow> binary, IEnumerable<PrefixRow> prefix) {
        _binary = [];
        _prefix = [];
        foreach (var row in binary) {
            if (!_binary.TryAdd((row.Op, row.Follow), row))
                throw new InvalidOperationException($"Duplicate binary operator row '{row.Op}'{(row.Follow is { } f ? $" '{f}'" : "")}.");
        }

        foreach (var row in prefix) {
            if (!_prefix.TryAdd(row.Op, row))
                throw new InvalidOperationException($"Duplicate prefix operator row '{row.Op}'.");
        }

        _hasFollow = [.. _binary.Keys.Where(k => k.Follow is not null).Select(k => k.Op)];
        var levels = _binary.Values
            .GroupBy(r => r.Precedence)
            .OrderBy(g => g.Key)
            .Select(g => {
                var assocs = g.Select(r => r.Assoc).Distinct().ToList();
                if (assocs.Count > 1)
                    throw new InvalidOperationException($"Precedence {g.Key} mixes associativities.");
                return new OperatorLevel(g.Key, assocs[0]);
            })
            .ToList();
        Levels = levels;
    }

    /// <summary>The distinct precedences, loosest first, each with its associativity.</summary>
    public IReadOnlyList<OperatorLevel> Levels { get; }

    public IReadOnlyCollection<BinaryRow> BinaryRows => _binary.Values;

    public IReadOnlyCollection<PrefixRow> PrefixRows => _prefix.Values;

    /// <summary>The product rows. <paramref name="forms"/> lets a library claim <c>+</c> and <c>-</c> (a date plus a duration).</summary>
    public static DslOperatorTable Core(ExpressionFormRegistry? forms = null) {
        DomainExpression Plus(DomainExpression l, DomainExpression r) =>
            forms?.TryFoldBinary(l, r, true) ?? DomainExpression.Add(l, r);
        DomainExpression Minus(DomainExpression l, DomainExpression r) =>
            forms?.TryFoldBinary(l, r, false) ?? DomainExpression.Subtract(l, r);

        const int or = 10, and = 20, compare = 40, add = 50, mul = 60;
        return new DslOperatorTable(
            [
                new(DslTokenKind.Or, or, Assoc.Left, DomainExpression.Or),
                new(DslTokenKind.And, and, Assoc.Left, DomainExpression.And),
                new(DslTokenKind.Is, compare, Assoc.None, DomainExpression.Equal),
                new(DslTokenKind.Is, compare, Assoc.None, DomainExpression.NotEqual, DslTokenKind.Not),
                new(DslTokenKind.Eq, compare, Assoc.None, DomainExpression.Equal),
                new(DslTokenKind.Neq, compare, Assoc.None, DomainExpression.NotEqual),
                new(DslTokenKind.Gt, compare, Assoc.None, DomainExpression.GreaterThan),
                new(DslTokenKind.Gte, compare, Assoc.None, DomainExpression.GreaterThanOrEqual),
                new(DslTokenKind.Lt, compare, Assoc.None, DomainExpression.LessThan),
                new(DslTokenKind.Lte, compare, Assoc.None, DomainExpression.LessThanOrEqual),
                new(DslTokenKind.Plus, add, Assoc.Left, Plus),
                new(DslTokenKind.Minus, add, Assoc.Left, Minus),
                new(DslTokenKind.Star, mul, Assoc.Left, DomainExpression.Multiply),
                new(DslTokenKind.Slash, mul, Assoc.Left, DomainExpression.Divide),
            ],
            [new PrefixRow(DslTokenKind.Not, add, DomainExpression.Not)]);
    }

    public DslOperatorTable With(BinaryRow row) => new([.. _binary.Values, row], _prefix.Values);

    public DslOperatorTable With(PrefixRow row) => new(_binary.Values, [.. _prefix.Values, row]);

    /// <summary>The binary row at <paramref name="level"/> that <paramref name="current"/> starts, and how many tokens it spans.</summary>
    public bool TryBinary(
        int level,
        DslToken current,
        Func<DslToken> next,
        out BinaryRow row,
        out int tokens) {
        if (_hasFollow.Contains(current.Kind)
            && _binary.TryGetValue((current.Kind, next().Kind), out row!)
            && row.Precedence == Levels[level].Precedence) {
            tokens = 2;
            return true;
        }

        if (_binary.TryGetValue((current.Kind, null), out row!) && row.Precedence == Levels[level].Precedence) {
            tokens = 1;
            return true;
        }

        row = null!;
        tokens = 0;
        return false;
    }

    public bool TryPrefix(DslTokenKind kind, out PrefixRow row) => _prefix.TryGetValue(kind, out row!);

    /// <summary>The first level whose precedence is at least <paramref name="precedence"/>; <see cref="Levels"/>.Count when none (the primary).</summary>
    public int LevelAtOrAbove(int precedence) {
        for (var i = 0; i < Levels.Count; i++) {
            if (Levels[i].Precedence >= precedence)
                return i;
        }

        return Levels.Count;
    }

    /// <summary>The level of the binary row for <paramref name="op"/>, or -1.</summary>
    public int LevelOf(DslTokenKind op) {
        foreach (var row in _binary.Values) {
            if (row.Op == op && row.Follow is null)
                return LevelAtOrAbove(row.Precedence);
        }

        return -1;
    }

    /// <summary>The level at whose head <paramref name="row"/> applies: the highest level below its operand precedence.</summary>
    public int HeadLevel(PrefixRow row) => LevelAtOrAbove(row.OperandPrecedence) - 1;
}

/// <summary>One precedence level of <see cref="DslOperatorTable.Levels"/>.</summary>
public sealed record OperatorLevel(int Precedence, Assoc Assoc);