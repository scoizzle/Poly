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
    private readonly IReadOnlyDictionary<DslTokenKind, Func<DomainExpression, DomainExpression, DomainExpression?>> _claims;

    private DslOperatorTable(
        IEnumerable<BinaryRow> binary,
        IEnumerable<PrefixRow> prefix,
        IReadOnlyDictionary<DslTokenKind, Func<DomainExpression, DomainExpression, DomainExpression?>> claims) {
        _claims = claims;
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

    private static readonly IReadOnlyDictionary<DslTokenKind, Func<DomainExpression, DomainExpression, DomainExpression?>> NoClaims =
        new Dictionary<DslTokenKind, Func<DomainExpression, DomainExpression, DomainExpression?>>();

    /// <summary>
    /// The product rows. <paramref name="forms"/> carries the library claims: a claim on an operator runs before the row's own
    /// node (a date plus a duration), so each plain row is its claim first and its plain node as the one fallback. A claim
    /// wraps the plain row of an operator, not its follow-word variants (a claim on <c>is</c> does not affect <c>is not</c>).
    /// The table snapshots the claims when it is built: a claim registered afterwards is ignored. Registration is the one
    /// door that validates the operator (<see cref="ExpressionFormRegistry.RegisterBinaryClaim"/> throws for one with no row).
    /// </summary>
    public static DslOperatorTable Core(ExpressionFormRegistry? forms = null) {
        var claims = forms?.BinaryClaims() ?? NoClaims;

        BinaryRow Row(DslTokenKind op, int precedence, Assoc assoc, Func<DomainExpression, DomainExpression, DomainExpression> plain, DslTokenKind? follow = null) =>
            follow is null && claims.TryGetValue(op, out var claim)
                ? new BinaryRow(op, precedence, assoc, (l, r) => claim(l, r) ?? plain(l, r))
                : new BinaryRow(op, precedence, assoc, plain, follow);

        const int or = 10, and = 20, compare = 40, add = 50, mul = 60;
        return new DslOperatorTable(
            [
                Row(DslTokenKind.Or, or, Assoc.Left, DomainExpression.Or),
                Row(DslTokenKind.And, and, Assoc.Left, DomainExpression.And),
                Row(DslTokenKind.Is, compare, Assoc.None, DomainExpression.Equal),
                Row(DslTokenKind.Is, compare, Assoc.None, DomainExpression.NotEqual, DslTokenKind.Not),
                Row(DslTokenKind.Eq, compare, Assoc.None, DomainExpression.Equal),
                Row(DslTokenKind.Neq, compare, Assoc.None, DomainExpression.NotEqual),
                Row(DslTokenKind.Gt, compare, Assoc.None, DomainExpression.GreaterThan),
                Row(DslTokenKind.Gte, compare, Assoc.None, DomainExpression.GreaterThanOrEqual),
                Row(DslTokenKind.Lt, compare, Assoc.None, DomainExpression.LessThan),
                Row(DslTokenKind.Lte, compare, Assoc.None, DomainExpression.LessThanOrEqual),
                Row(DslTokenKind.Plus, add, Assoc.Left, DomainExpression.Add),
                Row(DslTokenKind.Minus, add, Assoc.Left, DomainExpression.Subtract),
                Row(DslTokenKind.Star, mul, Assoc.Left, DomainExpression.Multiply),
                Row(DslTokenKind.Slash, mul, Assoc.Left, DomainExpression.Divide),
            ],
            [new PrefixRow(DslTokenKind.Not, add, DomainExpression.Not)],
            claims);
    }

    /// <summary>The library claim on the plain row of <paramref name="op"/> (not a follow-word variant) for this pair, or null when no claim takes it (the row then keeps its plain node). Throws when <paramref name="op"/> has no row.</summary>
    public DomainExpression? Claim(DslTokenKind op, DomainExpression left, DomainExpression right) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        RequireBinaryRow(op);
        return _claims.TryGetValue(op, out var claim) ? claim(left, right) : null;
    }

    /// <summary>The fold of the <paramref name="op"/> row: its library claim, else its plain node. Throws when <paramref name="op"/> has no row.</summary>
    public DomainExpression Fold(DslTokenKind op, DomainExpression left, DomainExpression right) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        RequireBinaryRow(op);
        return _binary[(op, null)].Fold(left, right);
    }

    /// <summary>Throws unless <paramref name="op"/> has a binary row of the core table; the registry asks this when a claim registers.</summary>
    internal static void RequireCoreBinaryRow(DslTokenKind op) => Core().RequireBinaryRow(op);

    private void RequireBinaryRow(DslTokenKind op) {
        if (!_binary.ContainsKey((op, null)))
            throw new InvalidOperationException($"No binary operator row for '{op}': a library claim needs an operator the table has.");
    }

    /// <summary>
    /// A copy with one more binary row. A row whose precedence falls between a prefix row's head level and its operand
    /// precedence would silently change how that prefix binds (<c>not a &gt; b</c> would become <c>(not a) &gt; b</c>), so it
    /// is refused: give the row a precedence outside that gap, or change the prefix row deliberately. Binary rows with a precedence between the <c>not</c> head level and its operand precedence (41 to 49 in the core table) are refused;
    /// <see cref="With(PrefixRow)"/> is not guarded.
    /// </summary>
    public DslOperatorTable With(BinaryRow row) {
        var next = new DslOperatorTable([.. _binary.Values, row], _prefix.Values, _claims);
        foreach (var prefix in _prefix.Values) {
            if (HeadPrecedence(prefix) != next.HeadPrecedence(prefix))
                throw new InvalidOperationException(
                    $"Binary row '{row.Op}' at precedence {row.Precedence} would change how prefix '{prefix.Op}' binds.");
        }

        return next;
    }

    private int HeadPrecedence(PrefixRow row) {
        var head = HeadLevel(row);
        return head < 0 ? int.MinValue : Levels[head].Precedence;
    }

    public DslOperatorTable With(PrefixRow row) => new(_binary.Values, [.. _prefix.Values, row], _claims);

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