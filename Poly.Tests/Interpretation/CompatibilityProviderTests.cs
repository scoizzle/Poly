using Poly.Analysis;
using Poly.Interpretation;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.DomainModeling.Analysis;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The lowered-tree compatibility analysis over operands the operator ledger's 22-operand grid omits: DateTimeOffset,
/// CLR enums, IntPtr and UIntPtr, and the nullable of every value operand. Each cell is read twice: the analysis
/// verdict, and whether the type provider defines the operator for the unwrapped operands. Cells are pinned per
/// family by count and by a checksum of their keys, so a verdict that moves inside a family is visible.
/// </summary>
internal static class ExtraOperandGrid {
    internal enum Shade { Red, Blue }

    internal enum Other { One, Two }

    internal enum Wide : ulong { None = 0, All = ulong.MaxValue }

    private static readonly Type[] Base = [.. OperatorOracle.Operands.Where(static o => o.Clr is not null).Select(static o => o.Clr!)];

    private static readonly Type[] Extras = [typeof(DateTimeOffset), typeof(Shade), typeof(Other), typeof(Wide), typeof(IntPtr), typeof(UIntPtr)];

    public static readonly Type[] Operands = [
        .. Base, .. Extras,
        .. Base.Concat(Extras).Where(static t => t.IsValueType).Select(static t => typeof(Nullable<>).MakeGenericType(t))
    ];

    public static readonly OperatorKind[] Kinds = [
        OperatorKind.Add, OperatorKind.Subtract, OperatorKind.Multiply, OperatorKind.Divide, OperatorKind.Modulo,
        OperatorKind.Equal, OperatorKind.NotEqual, OperatorKind.LessThan, OperatorKind.LessThanOrEqual,
        OperatorKind.GreaterThan, OperatorKind.GreaterThanOrEqual
    ];

    public sealed record GridCell(Type Left, OperatorKind Op, Type Right, string Verdict, bool Defined, string Family) {
        public string Key => $"{Name(Left)} {Op} {Name(Right)}";
        public string Bucket => $"{Family}|{Verdict}|{(Defined ? "defined" : "none")}";
    }

    public static string Name(Type type) => Nullable.GetUnderlyingType(type) is { } u ? $"{u.Name}?" : type.Name;

    private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    public static readonly Lazy<IReadOnlyList<GridCell>> Cells = new(() => {
        var cells = new List<GridCell>();
        foreach (var kind in Kinds)
            foreach (var left in Operands)
                foreach (var right in Operands) {
                    if (Base.Contains(left) && Base.Contains(right))
                        continue;
                    cells.Add(Evaluate(left, kind, right));
                }
        return cells;
    });

    private static GridCell Evaluate(Type left, OperatorKind kind, Type right) {
        var node = Build(kind, new Parameter("a", new ClrTypeReference(left)), new Parameter("b", new ClrTypeReference(right)));
        string verdict;
        try {
            var analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
            verdict = analysis.Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error) ? "rejects" : "accepts";
        }
        catch (Exception ex) {
            verdict = $"throws {ex.GetType().Name}";
        }
        var defined = OperatorOperands.Clr(Unwrap(left)).GetOperator(kind, OperatorOperands.Clr(Unwrap(right))) is not null;
        return new GridCell(left, kind, right, verdict, defined, FamilyOf(left, kind, right));
    }

    /// <summary>The first that applies: IntPtr or UIntPtr; Decimal arithmetic beside an enum or DateTimeOffset; an enum; a nullable; DateTimeOffset.</summary>
    private static string FamilyOf(Type left, OperatorKind kind, Type right) {
        var (l, r) = (Unwrap(left), Unwrap(right));
        if (l == typeof(IntPtr) || l == typeof(UIntPtr) || r == typeof(IntPtr) || r == typeof(UIntPtr))
            return "IntPtr";
        var arithmetic = kind is OperatorKind.Add or OperatorKind.Subtract or OperatorKind.Multiply or OperatorKind.Divide or OperatorKind.Modulo;
        if (arithmetic && (left == typeof(decimal) || right == typeof(decimal))
            && (l.IsEnum || r.IsEnum || l == typeof(DateTimeOffset) || r == typeof(DateTimeOffset)))
            return "DecimalBesideEnumOrOffset";
        if (l.IsEnum || r.IsEnum)
            return "Enum";
        if (left != l || right != r)
            return "Nullable";
        return "DateTimeOffset";
    }

    public static Node Build(OperatorKind kind, Node left, Node right) => kind switch {
        OperatorKind.Add => new Add(left, right),
        OperatorKind.Subtract => new Subtract(left, right),
        OperatorKind.Multiply => new Multiply(left, right),
        OperatorKind.Divide => new Divide(left, right),
        OperatorKind.Modulo => new Modulo(left, right),
        OperatorKind.Equal => new Equal(left, right),
        OperatorKind.NotEqual => new NotEqual(left, right),
        OperatorKind.LessThan => new LessThan(left, right),
        OperatorKind.LessThanOrEqual => new LessThanOrEqual(left, right),
        OperatorKind.GreaterThan => new GreaterThan(left, right),
        OperatorKind.GreaterThanOrEqual => new GreaterThanOrEqual(left, right),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

public class CompatibilityProviderTests {
    private static long Run(Node body, params object?[] args) {
        using var exec = Interpreter.Execute(Interpreter.Compile(body), s => s.SetArgs(args));
        return exec.RawValue;
    }

    private static Parameter Param<T>(string name) => new(name, TypeReference.To<T>());

    /// <summary>Count and key checksum per family, verdict and provider answer.</summary>
    private static readonly Dictionary<string, (int Count, string Checksum)> ExpectedBuckets = new(StringComparer.Ordinal) {
        ["DateTimeOffset|accepts|defined"] = (20, "b83fed908612bc49"),
        ["DateTimeOffset|accepts|none"] = (20, "26111cdc23f61d36"),
        ["DateTimeOffset|rejects|defined"] = (5, "074c70669ca06014"),
        ["DateTimeOffset|rejects|none"] = (374, "fb18a2c89867bd04"),
        ["DecimalBesideEnumOrOffset|accepts|none"] = (40, "b8110cfcd0bace9b"),
        ["DecimalBesideEnumOrOffset|rejects|none"] = (40, "7b2e833445247d81"),
        ["Enum|accepts|defined"] = (12, "befeb42408f3821d"),
        ["Enum|accepts|none"] = (432, "878449745db41406"),
        ["Enum|rejects|none"] = (5040, "0cdfc19e1491fc5c"),
        ["IntPtr|accepts|defined"] = (112, "5048c22c593d0e0f"),
        ["IntPtr|accepts|none"] = (2120, "b2ebd23bef029736"),
        ["IntPtr|rejects|defined"] = (8, "33104c425d7570c2"),
        ["IntPtr|rejects|none"] = (1896, "ad5585575f2c05ec"),
        ["Nullable|accepts|defined"] = (3949, "faeba0813049adf0"),
        ["Nullable|accepts|none"] = (486, "42d466b5e189d6ea"),
        ["Nullable|rejects|defined"] = (730, "3c9ac5bd83614c7d"),
        ["Nullable|rejects|none"] = (7156, "fdefa4b1cb1a74fa"),
    };

    [Test]
    public async Task ExtraOperandGrid_VerdictsAndProviderAnswers_ArePinnedPerFamily() {
        var cells = ExtraOperandGrid.Cells.Value;
        await Assert.That(cells.Count).IsEqualTo((ExtraOperandGrid.Operands.Length * ExtraOperandGrid.Operands.Length - 19 * 19) * ExtraOperandGrid.Kinds.Length);
        var actual = cells.GroupBy(static c => c.Bucket, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => (Count: g.Count(), Checksum: LedgerCheck.Checksum(g.Select(static c => c.Key))), StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (bucket, found) in actual.OrderBy(static b => b.Key, StringComparer.Ordinal)) {
            if (!ExpectedBuckets.TryGetValue(bucket, out var expected))
                problems.Add($"unpinned bucket [\"{bucket}\"] = ({found.Count}, \"{found.Checksum}\")");
            else if (expected != found)
                problems.Add($"bucket [\"{bucket}\"]: expected ({expected.Count}, \"{expected.Checksum}\"), actual ({found.Count}, \"{found.Checksum}\")");
        }
        foreach (var bucket in ExpectedBuckets.Keys.Where(k => !actual.ContainsKey(k)))
            problems.Add($"bucket [\"{bucket}\"] expected but empty");
        await Assert.That(problems).IsEmpty();
    }

    // ---- accepted comparisons the arithmetic-only VM predicate must never touch (lock amendment 6's L1 scope) ----

    [Test]
    public async Task DeclaredAndPlatformComparisons_AreAccepted_AndRun() {
        var guid = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        var day = new DateTime(2026, 1, 1);
        await Assert.That(Run(new Equal(Param<string>("l"), Param<string>("r")), "same", "same")).IsEqualTo(1L);
        await Assert.That(Run(new Equal(Param<Guid>("l"), Param<Guid>("r")), guid, guid)).IsEqualTo(1L);
        await Assert.That(Run(new LessThan(Param<DateTime>("l"), Param<DateTime>("r")), day, day.AddDays(1))).IsEqualTo(1L);
        await Assert.That(Run(new Equal(Param<TimeSpan>("l"), Param<TimeSpan>("r")), TimeSpan.FromHours(1), TimeSpan.FromMinutes(60))).IsEqualTo(1L);
        await Assert.That(Run(new LessThan(Param<double>("l"), Param<double>("r")), 1.5d, 2.5d)).IsEqualTo(1L);
        await Assert.That(Run(new LessThan(Param<float>("l"), Param<float>("r")), 1.5f, 2.5f)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(Param<double>("l"), Param<float>("r")), 1.5d, 1.5f)).IsEqualTo(1L);
    }

    [Test]
    public async Task DeclaredComparisons_ProviderAnswersDeclaredOrPlatform() {
        await Assert.That(OperatorOperands.Clr(typeof(string)).GetOperator(OperatorKind.Equal, OperatorOperands.Clr(typeof(string))) is { PlatformApplied: false }).IsTrue();
        await Assert.That(OperatorOperands.Clr(typeof(Guid)).GetOperator(OperatorKind.Equal, OperatorOperands.Clr(typeof(Guid))) is { PlatformApplied: false }).IsTrue();
        await Assert.That(OperatorOperands.Clr(typeof(DateTime)).GetOperator(OperatorKind.LessThan, OperatorOperands.Clr(typeof(DateTime))) is { PlatformApplied: false }).IsTrue();
        await Assert.That(OperatorOperands.Clr(typeof(TimeSpan)).GetOperator(OperatorKind.Equal, OperatorOperands.Clr(typeof(TimeSpan))) is { PlatformApplied: false }).IsTrue();
        await Assert.That(OperatorOperands.Clr(typeof(double)).GetOperator(OperatorKind.LessThan, OperatorOperands.Clr(typeof(double))) is { PlatformApplied: true }).IsTrue();
        await Assert.That(OperatorOperands.Clr(typeof(float)).GetOperator(OperatorKind.LessThan, OperatorOperands.Clr(typeof(float))) is { PlatformApplied: true }).IsTrue();
    }

    // ---- a nullable char beside a number (lock amendment 4): rejected today, and it stays rejected ----

    [Test]
    public async Task NullableChar_BesideANumber_IsCompileRejected() {
        await Assert.That(() => Interpreter.Compile(new GreaterThan(Param<char?>("c"), Param<int>("n"))))
            .Throws<InvalidOperationException>()
            .WithMessage("VM compile rejected: comparison between incompatible types 'Nullable`1' and 'Int32'");
        await Assert.That(() => Interpreter.Compile(new Add(Param<char?>("c"), Param<int>("n"))))
            .Throws<InvalidOperationException>()
            .WithMessage("VM compile rejected: arithmetic operand is not numeric (got 'Nullable`1' and 'Int32')");
    }

    [Test]
    public async Task NullableChar_BesideAChar_AndCharBesideANumber_AreAccepted() {
        await Assert.That(Run(new Equal(Param<char?>("c"), Param<char>("d")), 'a', 'a')).IsEqualTo(1L);
        await Assert.That(Run(new GreaterThan(Param<char>("c"), Param<int>("n")), 'a', 96)).IsEqualTo(1L);
        await Assert.That(OperatorOperands.Clr(typeof(char)).GetOperator(OperatorKind.GreaterThan, OperatorOperands.Clr(typeof(int))) is not null).IsTrue();
    }

    // ---- a non-negative signed integral constant beside a ulong (owner ruling 6842272727, lock amendment 5) ----

    [Test]
    public async Task NonNegativeSignedConstant_BesideULong_IsAccepted_AndRuns() {
        await Assert.That(Run(new LessThanOrEqual(new Constant(1L), Param<ulong>("u")), 5UL)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(new Constant((short)0), Param<ulong>("u")), 0UL)).IsEqualTo(1L);
        await Assert.That(Run(new GreaterThan(Param<ulong?>("u"), new Constant((sbyte)3)), 4UL)).IsEqualTo(1L);
        using var sum = Interpreter.Execute(Interpreter.Compile(new Add(Param<ulong>("u"), new Constant(2))), s => s.SetArgs(ulong.MaxValue - 2));
        await Assert.That(sum.GetValue<ulong>()).IsEqualTo(ulong.MaxValue);
        using var constants = Interpreter.Execute(Interpreter.Compile(new Multiply(new Constant(3), new Constant(5UL))));
        await Assert.That(constants.GetValue<ulong>()).IsEqualTo(15UL);
    }

    [Test]
    public async Task SignedBesideULong_ProviderAnswersNone() {
        foreach (var signed in new[] { typeof(sbyte), typeof(short), typeof(int), typeof(long) }) {
            await Assert.That(OperatorOperands.Clr(typeof(ulong)).GetOperator(OperatorKind.GreaterThan, OperatorOperands.Clr(signed))).IsNull();
            await Assert.That(OperatorOperands.Clr(signed).GetOperator(OperatorKind.Add, OperatorOperands.Clr(typeof(ulong)))).IsNull();
        }
    }
}