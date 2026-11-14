using Poly.Analysis;
using Poly.Interpretation;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.DomainModeling.Analysis;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The lowered-tree compatibility analysis over operands the operator ledger's 22 base operands omit: DateTimeOffset,
/// CLR enums, IntPtr and UIntPtr (the ledger adds those two apart since T-18b), and the nullable of every value operand. Each cell is read twice: the analysis
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

    /// <summary>
    /// Count and key checksum per family, verdict and provider answer. T-18 moved these from master (pinned in its
    /// characterisation commit) only accepted to rejected, in four families: 408 nullable variants of the ledger
    /// grid's 212 newly rejected cells, 40 Decimal cells beside an enum or DateTimeOffset, 2,184 IntPtr or UIntPtr
    /// cells, and 144 comparisons of two different enum types. One family moved from rejected to accepted, on
    /// purpose (review F2): 499 cells with a <c>char?</c> beside a number, once the VM was shown to execute them as C#
    /// does (<c>NullableCharProofTests</c>). T-18b defines the native integers: the IntPtr family's 4,136 cells are 1,768
    /// accepted and defined (it was 24, IntPtr's declared members), 24 rejected and defined (Duration times or divided by
    /// a native integer: <c>VmExecutesDeclaredOperator</c>, T-21; it was 96), the 24 date-offset cells T-10 fences, and
    /// 2,320 rejected and undefined (it was 3,992).
    /// </summary>
    private static readonly Dictionary<string, (int Count, string Checksum)> ExpectedBuckets = new(StringComparer.Ordinal) {
        ["DateTimeOffset|accepts|defined"] = (20, "b83fed908612bc49"),
        ["DateTimeOffset|accepts|none"] = (20, "26111cdc23f61d36"),
        ["DateTimeOffset|rejects|defined"] = (5, "074c70669ca06014"),
        ["DateTimeOffset|rejects|none"] = (374, "fb18a2c89867bd04"),
        ["DecimalBesideEnumOrOffset|rejects|none"] = (80, "d023b484873e8dfa"),
        ["Enum|accepts|defined"] = (12, "befeb42408f3821d"),
        ["Enum|accepts|none"] = (288, "6877c6037cfaa217"),
        ["Enum|rejects|none"] = (5184, "7fad361dbea81895"),
        ["IntPtr|accepts|defined"] = (1768, "cfce18bd83719974"),
        ["IntPtr|accepts|none"] = (24, "6c0426819d4a7d42"),
        ["IntPtr|rejects|defined"] = (24, "36842c99eaa50dc8"),
        ["IntPtr|rejects|none"] = (2320, "4d1107bf57bceb0c"),
        ["Nullable|accepts|defined"] = (4448, "2de1f3caa17904d4"),
        ["Nullable|accepts|none"] = (78, "f788f25092bfd0cd"),
        ["Nullable|rejects|defined"] = (231, "cb4b463a4d1108f3"),
        ["Nullable|rejects|none"] = (7564, "49cbcd4ee4e1228f"),
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

    // ---- a nullable char beside a number (lock amendment 4): rejected on master, accepted since the VM is shown correct ----

    [Test]
    public async Task NullableChar_BesideANumber_IsAccepted_AndRuns() {
        await Assert.That(Run(new GreaterThan(Param<char?>("c"), Param<int>("n")), 'a', 96)).IsEqualTo(1L);
        using var sum = Interpreter.Execute(Interpreter.Compile(new Add(Param<char?>("c"), Param<int>("n"))), s => s.SetArgs('a', 1));
        await Assert.That(sum.GetValue<int>()).IsEqualTo(98);
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

    // ---- fail closed (T-18): each cell is accepted on master and rejected after, beside an accepting sibling ----

    private sealed record FailClosedCase(Func<(Node Node, Node Left)> Rejected, string Message, Func<Node> Accepted, object?[] Args, object Value);

    private static (Node, Node) Pair(Func<Node, Node, Node> build, Node left, Node right) => (build(left, right), left);

    private static readonly Dictionary<string, FailClosedCase> FailClosed = new(StringComparer.Ordinal) {
        ["DecimalPlusDouble"] = new(() => Pair(static (l, r) => new Add(l, r), Param<decimal>("d"), Param<double>("f")),
            "arithmetic operand is not numeric (got 'Decimal' and 'Double'): no Add operator for these types",
            static () => new Add(Param<decimal>("d"), Param<long>("n")), [1.5m, 2L], 3.5m),
        ["DecimalTimesDouble"] = new(() => Pair(static (l, r) => new Multiply(l, r), Param<decimal>("d"), Param<double>("f")),
            "arithmetic operand is not numeric (got 'Decimal' and 'Double'): no Multiply operator for these types",
            static () => new Multiply(Param<decimal>("d"), Param<long>("n")), [1.5m, 2L], 3.0m),
        ["DecimalEqualDouble"] = new(() => Pair(static (l, r) => new Equal(l, r), Param<decimal>("d"), Param<double>("f")),
            "comparison between incompatible types 'Decimal' and 'Double': no Equal operator for these types",
            static () => new Equal(Param<decimal>("d"), Param<long>("n")), [2m, 2L], true),
        ["DecimalLessThanDouble"] = new(() => Pair(static (l, r) => new LessThan(l, r), Param<decimal>("d"), Param<double>("f")),
            "comparison between incompatible types 'Decimal' and 'Double': no LessThan operator for these types",
            static () => new LessThan(Param<decimal>("d"), Param<long>("n")), [1.5m, 2L], true),
        ["DecimalPlusBoolean"] = new(() => Pair(static (l, r) => new Add(l, r), Param<decimal>("d"), Param<bool>("b")),
            "arithmetic operand is not numeric (got 'Decimal' and 'Boolean'): no Add operator for these types",
            static () => new Add(Param<decimal>("d"), Param<decimal>("e")), [1.5m, 2m], 3.5m),
        ["OptionalDecimalPlusDouble"] = new(() => Pair(static (l, r) => new Add(l, r), Param<decimal?>("d"), Param<double>("f")),
            "arithmetic operand is not numeric (got 'Nullable`1' and 'Double'): no Add operator for these types",
            static () => new Add(Param<decimal?>("d"), Param<long>("n")), [1.5m, 2L], 3.5m),
        ["ULongPlusLong"] = new(() => Pair(static (l, r) => new Add(l, r), Param<ulong>("u"), Param<long>("n")),
            "arithmetic operand is not numeric (got 'UInt64' and 'Int64'): no Add operator for these types",
            static () => new Add(Param<ulong>("u"), Param<uint>("w")), [5UL, 2u], 7UL),
        ["ULongEqualLong"] = new(() => Pair(static (l, r) => new Equal(l, r), Param<ulong>("u"), Param<long>("n")),
            "comparison between incompatible types 'UInt64' and 'Int64': no Equal operator for these types",
            static () => new Equal(Param<ulong>("u"), Param<uint>("w")), [5UL, 5u], true),
        ["ULongPlusNegativeConstant"] = new(() => Pair(static (l, r) => new Add(l, r), Param<ulong>("u"), new Constant(-1L)),
            "arithmetic operand is not numeric (got 'UInt64' and 'Int64'): no Add operator for these types",
            static () => new Add(Param<ulong>("u"), new Constant(1L)), [5UL], 6UL),
        ["TextEqualChar"] = new(() => Pair(static (l, r) => new Equal(l, r), Param<string>("s"), Param<char>("c")),
            "comparison between incompatible types 'String' and 'Char': no Equal operator for these types",
            static () => new Equal(Param<string>("s"), Param<string>("t")), ["a", "a"], true),
        ["DatePlusDecimal"] = new(() => Pair(static (l, r) => new Add(l, r), Param<DateOnly>("day"), Param<decimal>("d")),
            "arithmetic operand is not numeric (got 'DateOnly' and 'Decimal'): no Add operator for these types",
            static () => new Add(Param<DateOnly>("day"), Param<long>("n")), [new DateOnly(2024, 2, 28), 2L], new DateOnly(2024, 3, 1)),
        ["DifferentEnumsEqual"] = new(() => Pair(static (l, r) => new Equal(l, r), Param<ExtraOperandGrid.Shade>("a"), Param<ExtraOperandGrid.Other>("b")),
            "comparison between incompatible types 'Shade' and 'Other': no Equal operator for these types",
            static () => new Equal(Param<ExtraOperandGrid.Shade>("a"), Param<ExtraOperandGrid.Shade>("b")), [ExtraOperandGrid.Shade.Blue, ExtraOperandGrid.Shade.Blue], true),
    };

    public static IEnumerable<string> FailClosedNames() => FailClosed.Keys;

    [Test]
    [MethodDataSource(nameof(FailClosedNames))]
    public async Task UndefinedOperator_IsAnErrorOnTheLeftOperand_AndCompileIsRefused(string name) {
        var row = FailClosed[name];
        var (node, left) = row.Rejected();
        var errors = Interpreter.Analyze(node).Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        await Assert.That(errors.Length).IsEqualTo(1);
        await Assert.That(errors[0].Code).IsEqualTo(Poly.Interpretation.Analysis.Semantics.SyntaxTypeCompatibilityAnalyzer.DiagnosticCode);
        await Assert.That(errors[0].Message).IsEqualTo(row.Message);
        await Assert.That(ReferenceEquals(errors[0].Node, left)).IsTrue();
        await Assert.That(() => Interpreter.Compile(row.Rejected().Node))
            .Throws<InvalidOperationException>()
            .WithMessage("VM compile rejected: " + row.Message);
    }

    [Test]
    [MethodDataSource(nameof(FailClosedNames))]
    public async Task UndefinedOperator_AcceptingSibling_Runs(string name) {
        var row = FailClosed[name];
        using var exec = Interpreter.Execute(Interpreter.Compile(row.Accepted()), s => s.SetArgs(row.Args));
        // The untyped result of a UInt64 is its 64-bit word as Int64, so a ulong is read typed.
        object? actual = row.Value switch {
            bool => exec.RawValue != 0,
            ulong => exec.GetValue<ulong>(),
            _ => exec.Result.Value
        };
        await Assert.That(actual).IsEqualTo(row.Value);
    }

    // ---- member assignment keeps its category rule, AssignCategoryRule (T-27 removes it): pinned in both directions ----

    internal sealed class AssignHost {
        public ExtraOperandGrid.Shade Color { get; set; }
        public string Label { get; set; } = "";
        public long Count { get; set; }
    }

    private static Node AssignMember(string member, object? value) {
        var host = new Variable("host");
        return new Block([
            new Assignment(host, new New(TypeReference.To<AssignHost>())),
            new Assignment(new Member(host, member), new Constant(value))
        ], [host]);
    }

    private static string[] AssignErrors(string member, object? value) =>
        [.. Interpreter.Analyze(AssignMember(member, value)).Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Message)];

    [Test]
    public async Task MemberAssign_TextIntoEnum_AndEnumIntoText_AreAccepted() {
        await Assert.That(AssignErrors("Color", "Blue")).IsEmpty();
        await Assert.That(AssignErrors("Label", ExtraOperandGrid.Shade.Red)).IsEmpty();
    }

    /// <summary>
    /// Pinned as they are, and three of them are not what C# says (row T-27 replaces the category rule with the
    /// provider's assignability): <c>char</c> into a <c>long</c> member is rejected though C# converts it implicitly;
    /// <c>decimal</c> into a <c>long</c> member is accepted though C# needs an explicit cast; <c>char</c> into a
    /// <c>string</c> member is accepted though C# has no such conversion.
    /// </summary>
    [Test]
    public async Task MemberAssign_ByCategory_AsToday_T27InvertsThreeOfThem() {
        await Assert.That(AssignErrors("Count", 'a')).IsEquivalentTo(["cannot assign 'Char' to 'Count' (type 'Int64')"]);
        await Assert.That(AssignErrors("Count", 1.5m)).IsEmpty();
        await Assert.That(AssignErrors("Label", 'a')).IsEmpty();
        await Assert.That(AssignErrors("Count", true)).IsEquivalentTo(["cannot assign 'Boolean' to 'Count' (type 'Int64')"]);
    }

    // ---- And and Or over a bool? operand (T-20 re-review N1; T-21a) ----

    /// <summary>
    /// C# defines no <c>bool &amp;&amp; bool?</c> (CS0019), but <c>CheckBooleanOperands</c> judges by category, so the lowered
    /// analysis accepts it until row T-28 moves the Boolean operand checks onto the provider. The VM reads an optional
    /// Boolean operand as the owner's ruling reads an optional Boolean condition (#862): <c>X == true</c>, so absent is not
    /// true. Before T-21a the VM read the optional's presence, so a stored false answered true.
    /// </summary>
    [Test]
    public async Task And_TrueWithNullableBool_IsAccepted_UntilT28() {
        var node = new And(new Constant(true), new Parameter("f", TypeReference.To<bool?>()));
        await Assert.That(Interpreter.Analyze(node).Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Select(static d => d.Message).ToArray()).IsEmpty();
    }

    /// <summary>The operand arrives as a parameter, a CLR property read and a call result; each operator in both operand
    /// orders, and beside another <c>bool?</c>. The root is a bool, 0 or 1, and true only when the operand is stored true.
    /// Accepting <c>and</c>/<c>or</c> over a <c>bool?</c> at all is interim: row T-28 rejects the pair as C# does.</summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    [Arguments(null)]
    public async Task AndOr_OverNullableBool_ReadTheValue_AbsentIsNotTrue(bool? flag) {
        var sources = new (string Name, Func<Node> Operand, object?[] Args)[] {
            ("parameter", static () => new Parameter("f", TypeReference.To<bool?>()), [flag]),
            ("property", static () => new Member(new Parameter("b", TypeReference.To<NullableFlagBox>()), nameof(NullableFlagBox.Flag)), [new NullableFlagBox { Flag = flag }]),
            ("call", () => new Invoke(new Member(new ClrTypeReference(typeof(NullableFlagBox)), nameof(NullableFlagBox.Of)), new Constant(NullableFlagBox.Code(flag))), []),
        };
        var answers = new List<string>();
        foreach (var (name, operand, args) in sources) {
            foreach (var (shape, node) in new (string, Node)[] {
                ("true and X", new And(new Constant(true), operand())),
                ("X and true", new And(operand(), new Constant(true))),
                ("false or X", new Or(new Constant(false), operand())),
                ("X or false", new Or(operand(), new Constant(false))),
            }) {
                var program = Interpreter.Compile(node);
                using var exec = Interpreter.Execute(program, args);
                answers.Add($"{name} {shape}: {program.RootClrType?.Name} {exec.RawValue}");
            }
        }
        var expected = flag == true ? 1L : 0L;
        await Assert.That(answers).IsEquivalentTo(sources.SelectMany(s => new[] { "true and X", "X and true", "false or X", "X or false" }
            .Select(shape => $"{s.Name} {shape}: Boolean {expected}")).ToArray());

        var both = Interpreter.Compile(new And(new Parameter("f", TypeReference.To<bool?>()), new Parameter("g", TypeReference.To<bool?>())));
        await Assert.That(both.RootClrType).IsEqualTo(typeof(bool));
        using var pair = Interpreter.Execute(both, true, flag);
        await Assert.That(pair.RawValue).IsEqualTo(expected);
    }

    /// <summary>Short-circuit over a <c>bool?</c> right operand: <c>false and X</c> and <c>true or X</c> never run X.</summary>
    [Test]
    public async Task AndOr_OverNullableBool_DoNotRunTheRightOperand_WhenTheLeftDecides() {
        static Node Explode() => new Invoke(new Member(new ClrTypeReference(typeof(NullableFlagBox)), nameof(NullableFlagBox.Explode)));
        using (var and = Interpreter.Execute(Interpreter.Compile(new And(new Constant(false), Explode()))))
            await Assert.That(and.RawValue).IsEqualTo(0L);
        using (var or = Interpreter.Execute(Interpreter.Compile(new Or(new Constant(true), Explode()))))
            await Assert.That(or.RawValue).IsEqualTo(1L);
    }
}

public sealed class NullableFlagBox {
    public static bool? Explode() => throw new InvalidOperationException("the right operand ran");

    public bool? Flag { get; set; }

    public static int Code(bool? flag) => flag switch { true => 1, false => 2, null => 0 };

    public static bool? Of(int code) => code switch { 1 => true, 2 => false, _ => null };
}