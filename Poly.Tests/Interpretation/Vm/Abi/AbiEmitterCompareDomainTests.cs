using System.Reflection;
using System.Runtime.Loader;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Introspection;
using Poly.Tests.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// One comparison reads values, never slots (#330 R7 and the compare protocol).
/// The domain is C# numeric promotion plus the nullable lift. An absent operand
/// equals only an absent operand and orders as false. A same-type integral pair
/// compares as that type. A mixed integral pair promotes to <c>decimal</c>,
/// and an integral against <c>float</c> promotes to <c>float</c>. The constant
/// fold still promotes every integral pair to <c>decimal</c>; that answer
/// matches because <c>decimal</c> is exact. An enum slot is its underlying integer.
/// </summary>
public class AbiEmitterCompareDomainTests {
    private static long Run(Node body, params object?[] args) {
        using var exec = Interpreter.Execute(Interpreter.Compile(body), s => s.SetArgs(args));
        return exec.RawValue;
    }

    private static Parameter Param<T>(string name) => new(name, TypeReference.To<T>());

    private static Parameter Optional<T>(string name) =>
        new(name, new OptionalTypeReference(TypeReference.To<T>()));

    [Test]
    public async Task RequiredDecimal_EqualNull_Unseeded_IsFalse() {
        var amount = Param<decimal>("amount");
        await Assert.That(Run(new Equal(amount, new Constant(null)))).IsEqualTo(0L);
        await Assert.That(Run(new NotEqual(amount, new Constant(null)))).IsEqualTo(1L);
    }

    /// <summary>The fold is a constant: the unseeded decimal handle is not dereferenced.</summary>
    [Test]
    public async Task RequiredDecimal_EqualNull_EmitsNoHeapDeref() {
        var amount = Param<decimal>("amount");
        var body = new Equal(amount, new Constant(null));
        var built = AbiEmitter.Build(body, Interpreter.Analyze(body), CompilationMode.NoDebug);
        var scan = VmTree.Read(built.Expression);
        await Assert.That(scan.HeapDerefs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task IntegerParameter_Left_DecimalParameter_Right_DifferentValues_IsFalse() {
        await Assert.That(Run(new Equal(Param<int>("count"), Param<decimal>("amount")), 4, 4.5m)).IsEqualTo(0L);
    }

    [Test]
    public async Task IntegerParameter_Left_DecimalParameter_Right_SameValue_IsTrue() {
        await Assert.That(Run(new Equal(Param<int>("count"), Param<decimal>("amount")), 4, 4.0m)).IsEqualTo(1L);
    }

    [Test]
    public async Task DecimalParameter_Left_IntegerParameter_Right_UsesValues() {
        await Assert.That(Run(new Equal(Param<decimal>("amount"), Param<int>("count")), 4.5m, 4)).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalInt_Left_OptionalDecimal_Right_DifferentValues_IsFalse() {
        await Assert.That(Run(new Equal(Optional<int>("count"), Optional<decimal>("amount")), 4, 4.5m)).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalInt_Left_OptionalDecimal_Right_SameValue_IsTrue() {
        await Assert.That(Run(new Equal(Optional<int>("count"), Optional<decimal>("amount")), 4, 4.0m)).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalInt_Left_OptionalDecimal_Right_BothAbsent_IsTrue() {
        await Assert.That(Run(new Equal(Optional<int>("count"), Optional<decimal>("amount")))).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalInt_Left_OptionalDecimal_Right_LeftAbsent_IsFalse() {
        await Assert.That(Run(new Equal(Optional<int>("count"), Optional<decimal>("amount")), null, 4.0m)).IsEqualTo(0L);
    }

    /// <summary>The platform defines no <c>decimal == double</c> (C# CS0019; issue 815), so the lowered analysis rejects it (T-18).</summary>
    [Test]
    public async Task DecimalParameter_Equal_DoubleParameter_IsCompileRejected() {
        await Assert.That(() => Interpreter.Compile(new Equal(Param<decimal>("amount"), Param<double>("width"))))
            .Throws<InvalidOperationException>()
            .WithMessage("VM compile rejected: comparison between incompatible types 'Decimal' and 'Double': no Equal operator for these types");
    }

    /// <summary>The platform defines no <c>decimal == float</c> (C# CS0019; issue 815), so the lowered analysis rejects it (T-18).</summary>
    [Test]
    public async Task DecimalParameter_Equal_FloatParameter_IsCompileRejected() {
        await Assert.That(() => Interpreter.Compile(new Equal(Param<decimal>("amount"), Param<float>("width"))))
            .Throws<InvalidOperationException>()
            .WithMessage("VM compile rejected: comparison between incompatible types 'Decimal' and 'Single': no Equal operator for these types");
    }

    /// <summary>Was a value test of the same pair with different values; the pair is rejected before any value is read (T-18).</summary>
    [Test]
    public async Task DecimalParameter_Equal_DoubleParameter_DifferentValues_IsCompileRejected() {
        await Assert.That(() => Interpreter.Compile(new Equal(Param<decimal>("amount"), Param<double>("width"))))
            .Throws<InvalidOperationException>()
            .WithMessage("VM compile rejected: comparison between incompatible types 'Decimal' and 'Double': no Equal operator for these types");
    }

    [Test]
    public async Task DecimalParameter_Equal_ULongParameter_SameValue_IsTrue() {
        await Assert.That(Run(new Equal(Param<decimal>("amount"), Param<ulong>("count")), 5m, 5UL)).IsEqualTo(1L);
    }

    [Test]
    public async Task DecimalParameter_Equal_ULongMax_UsesUnsignedValue() {
        var amount = (decimal)ulong.MaxValue;
        await Assert.That(Run(
            new Equal(Param<decimal>("amount"), Param<ulong>("count")), amount, ulong.MaxValue)).IsEqualTo(1L);
    }

    [Test]
    public async Task DecimalParameter_Equal_ULongMax_MatchesFoldedConstant() {
        await Assert.That(Run(
            new Equal(Param<decimal>("amount"), new Constant(ulong.MaxValue)),
            (decimal)ulong.MaxValue)).IsEqualTo(1L);
    }

    [Test]
    public async Task ULongParameter_GreaterThan_One_IsTrue() {
        await Assert.That(Run(new GreaterThan(Param<ulong>("count"), new Constant(1)), ulong.MaxValue)).IsEqualTo(1L);
    }

    /// <summary>
    /// -1 is not in range for <c>ulong</c>, so C# has no constant conversion and no operator for the pair (spec 10.2.11;
    /// owner ruling #679 6842272727): rejected, where a non-negative constant is accepted (the test above, T-18).
    /// </summary>
    [Test]
    public async Task ULongParameter_Equal_SignedMinusOne_IsCompileRejected() {
        await Assert.That(() => Interpreter.Compile(new Equal(new Constant(-1), Param<ulong>("count"))))
            .Throws<InvalidOperationException>()
            .WithMessage("VM compile rejected: comparison between incompatible types 'Int32' and 'UInt64': no Equal operator for these types");
    }

    [Test]
    public async Task OptionalULong_GreaterThan_One_IsTrue() {
        await Assert.That(Run(
            new GreaterThan(Optional<ulong>("count"), new Constant(1)), ulong.MaxValue)).IsEqualTo(1L);
    }

    /// <summary>C# promotes <c>uint</c> to <c>double</c>. The constant fold does too.</summary>
    [Test]
    public async Task UnsignedInt_Equal_Double_UsesNumericPromotion() {
        await Assert.That(Run(new Equal(Param<uint>("count"), Param<double>("width")), 2u, 2.0d)).IsEqualTo(1L);
    }

    [Test]
    public async Task UnsignedInt_Equal_DoubleLiteral_UsesNumericPromotion() {
        await Assert.That(Run(new Equal(Param<uint>("count"), new Constant(2.0d)), 2u)).IsEqualTo(1L);
    }

    [Test]
    public async Task ULongParameter_Equal_DoubleMax_UsesUnsignedValue() {
        var widened = (double)ulong.MaxValue;
        await Assert.That(Run(new Equal(Param<ulong>("count"), new Constant(widened)), ulong.MaxValue)).IsEqualTo(1L);
    }

    /// <summary>C# promotes the integral operand to <c>float</c>, which rounds
    /// 16777217 to 16777216. The fold does the same.</summary>
    [Test]
    public async Task UnsignedInt_Equal_Float_UsesFloatPromotion() {
        await Assert.That(Run(new Equal(Param<uint>("count"), new Constant(16777216f)), 16777217u)).IsEqualTo(1L);
    }

    [Test]
    public async Task Char_GreaterThan_ULongMax_IsFalse() {
        await Assert.That(Run(new GreaterThan(new Constant('a'), Param<ulong>("count")), ulong.MaxValue)).IsEqualTo(0L);
    }

    /// <summary>An API-built double constant beside a decimal is the fail-closed outcome issue 815 names: no operator, rejected (T-18).</summary>
    [Test]
    public async Task DecimalParameter_Equal_DoubleConstant_IsCompileRejected() {
        const decimal exact = 9007199254740993m;
        await Assert.That(() => Interpreter.Compile(new Equal(Param<decimal>("amount"), new Constant((double)exact))))
            .Throws<InvalidOperationException>()
            .WithMessage("VM compile rejected: comparison between incompatible types 'Decimal' and 'Double': no Equal operator for these types");
    }

    private enum Shade { Red, Blue }

    [Test]
    public async Task EnumParameter_Equal_SameMemberConstant_IsTrue() {
        await Assert.That(Run(new Equal(Param<Shade>("shade"), new Constant(Shade.Red)), Shade.Red)).IsEqualTo(1L);
    }

    [Test]
    public async Task EnumParameter_Equal_DifferentMemberConstant_IsFalse() {
        await Assert.That(Run(new Equal(Param<Shade>("shade"), new Constant(Shade.Blue)), Shade.Red)).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalEnum_Equal_Constant_PresentAndAbsent() {
        var shade = Optional<Shade>("shade");
        await Assert.That(Run(new Equal(shade, new Constant(Shade.Red)), Shade.Red)).IsEqualTo(1L);
        await Assert.That(Run(new Equal(shade, new Constant(Shade.Red)))).IsEqualTo(0L);
        await Assert.That(Run(new NotEqual(shade, new Constant(Shade.Red)))).IsEqualTo(1L);
    }

    /// <summary>Every same-enum comparison, for each underlying type, against C#'s own answer (T-25a).</summary>
    [Test]
    [Arguments(typeof(ComparisonEnums.SB))]
    [Arguments(typeof(ComparisonEnums.B))]
    [Arguments(typeof(ComparisonEnums.S))]
    [Arguments(typeof(ComparisonEnums.US))]
    [Arguments(typeof(ComparisonEnums.I))]
    [Arguments(typeof(ComparisonEnums.UI))]
    [Arguments(typeof(ComparisonEnums.L))]
    [Arguments(typeof(ComparisonEnums.UL))]
    public async Task SameEnumComparison_EveryShape_MatchesCSharp(Type enumType) {
        var mismatches = EnumComparisonOracle.Mismatches(enumType);
        await Assert.That(string.Join("\n", mismatches)).IsEqualTo("");
    }

    [Test]
    public async Task Bool_Ordering_IsCompileRejected() {
        var left = Param<bool>("left");
        var right = Param<bool>("right");
        await Assert.That(() => Interpreter.Compile(new GreaterThan(left, right)))
            .Throws<InvalidOperationException>()
            .WithMessage("VM compile rejected: ordering comparison of 'Boolean' is not supported.");
    }

    /// <summary>A constant pair must not fold away the rejection. C# has no
    /// <c>bool</c> ordering operator.</summary>
    [Test]
    public async Task BoolConstants_Ordering_IsCompileRejected() {
        await Assert.That(() => Interpreter.Compile(new GreaterThan(new Constant(true), new Constant(false))))
            .Throws<InvalidOperationException>()
            .WithMessage("VM compile rejected: ordering comparison of 'Boolean' is not supported.");
    }

    [Test]
    public async Task EnumMember_Equal_SameMemberConstant_IsTrue() {
        var day = new DateTime(2026, 10, 26);
        await Assert.That(Run(new Equal(
            new Member(new Constant(day), "DayOfWeek"),
            new Constant(day.DayOfWeek)))).IsEqualTo(1L);
    }

    [Test]
    public async Task EnumMember_Equal_EnumMember_IsTrue() {
        var day = new DateTime(2026, 10, 26);
        await Assert.That(Run(new Equal(
            new Member(new Constant(day), "DayOfWeek"),
            new Member(new Constant(day), "DayOfWeek")))).IsEqualTo(1L);
    }

    [Test]
    public async Task ULongEnum_Parameter_Equal_Constant_UsesUnderlyingBits() {
        await Assert.That(Run(
            new Equal(Param<Wide>("wide"), new Constant(Wide.All)), Wide.All)).IsEqualTo(1L);
    }

    private enum Wide : ulong { None = 0, All = ulong.MaxValue }

    [Test]
    public async Task DecimalParameter_LessThan_InlineFive_IsFalse() {
        await Assert.That(Run(new LessThan(Param<decimal>("amount"), new Constant(5)), 5.5m)).IsEqualTo(0L);
    }

    [Test]
    public async Task DecimalParameter_GreaterThan_InlineFive_IsTrue() {
        await Assert.That(Run(new GreaterThan(Param<decimal>("amount"), new Constant(5)), 5.5m)).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalDecimal_Ordering_Present_UsesValues() {
        await Assert.That(Run(
            new LessThan(Optional<decimal>("left"), Optional<decimal>("right")), 5.5m, 6m)).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalDecimal_Ordering_Absent_IsFalse() {
        await Assert.That(Run(new LessThan(Optional<decimal>("left"), Optional<decimal>("right")))).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalDateTime_Ordering_Present_UsesValues() {
        var day = new DateTime(2026, 1, 1);
        await Assert.That(Run(
            new LessThan(Optional<DateTime>("left"), Optional<DateTime>("right")),
            day, day.AddDays(1))).IsEqualTo(1L);
    }

    [Test]
    public async Task OptionalDateTime_Ordering_Absent_IsFalse() {
        await Assert.That(Run(
            new LessThan(Optional<DateTime>("left"), Optional<DateTime>("right")))).IsEqualTo(0L);
    }

    [Test]
    public async Task String_Equal_UsesValue() {
        var left = Param<string>("left");
        var right = Param<string>("right");
        await Assert.That(Run(new Equal(left, right), "same", "same")).IsEqualTo(1L);
        await Assert.That(Run(new Equal(left, right), "same", "other")).IsEqualTo(0L);
        await Assert.That(Run(new Equal(left, new Constant(null)))).IsEqualTo(1L);
        await Assert.That(Run(new Equal(left, new Constant(null)), "same")).IsEqualTo(0L);
    }

    [Test]
    public async Task OptionalInt_Equal_Long_UsesValue() {
        await Assert.That(Run(new Equal(Optional<int>("count"), new Constant(4L)), 4)).IsEqualTo(1L);
    }

    /// <summary>
    /// T-21d (#484): an optional temporal beside a required one of the same type is C#'s lifted comparison: an absent operand
    /// makes <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c> and <c>==</c> false and <c>!=</c> true. The expected value is
    /// computed here by C# lambdas (never a table); <c>Run</c> is the VM.
    /// </summary>
    private static readonly (string Name, Func<Node, Node, Node> Make)[] Operators = [
        ("<", (l, r) => new LessThan(l, r)), ("<=", (l, r) => new LessThanOrEqual(l, r)), (">", (l, r) => new GreaterThan(l, r)),
        (">=", (l, r) => new GreaterThanOrEqual(l, r)), ("==", (l, r) => new Equal(l, r)), ("!=", (l, r) => new NotEqual(l, r)),
    ];

    /// <summary>
    /// C#'s own operators over <c>T?</c> and <c>T</c>, in each operand order and for <c>T?</c> against <c>T?</c>: one lambda per
    /// operator, bound by the C# compiler (never a truth table, and the absent rule is C#'s). Order: <, <=, >, >=, ==, !=.
    /// </summary>
    internal sealed record Cmp<T>(Func<T?, T, bool>[] OptReq, Func<T, T?, bool>[] ReqOpt, Func<T?, T?, bool>[] OptOpt) where T : struct {
        private static readonly string[] Names = ["<", "<=", ">", ">=", "==", "!="];

        public bool Lifted(string op, T? left, T right) => OptReq[Array.IndexOf(Names, op)](left, right);

        public bool Lifted(string op, T left, T? right) => ReqOpt[Array.IndexOf(Names, op)](left, right);

        public bool LiftedBoth(string op, T? left, T? right) => OptOpt[Array.IndexOf(Names, op)](left, right);
    }

    internal static readonly Cmp<DateTime> DateTimeOps = new(
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b],
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b],
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b]);
    internal static readonly Cmp<DateOnly> DateOnlyOps = new(
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b],
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b],
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b]);
    internal static readonly Cmp<TimeOnly> TimeOnlyOps = new(
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b],
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b],
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b]);
    internal static readonly Cmp<TimeSpan> TimeSpanOps = new(
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b],
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b],
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b]);
    internal static readonly Cmp<DateTimeOffset> DateTimeOffsetOps = new(
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b],
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b],
        [(a, b) => a < b, (a, b) => a <= b, (a, b) => a > b, (a, b) => a >= b, (a, b) => a == b, (a, b) => a != b]);

    /// <summary>
    /// The optional value takes every position against the required one: less, EQUAL and greater (so <c>&lt;=</c>, <c>&gt;=</c>
    /// and <c>==</c> at equality, and <c>&gt;</c> and <c>&gt;=</c> above it, are run), and absent.
    /// </summary>
    private static async Task OptionalAgainstRequired<T>(Cmp<T> cmp, T less, T required, T greater, Func<Node> requiredNode, params object?[] extraArgs)
        where T : struct {
        foreach (var (op, make) in Operators)
            foreach (var optionalLeft in new[] { true, false })
                foreach (var value in new T?[] { less, required, greater, null }) {
                    Node body = optionalLeft ? make(Optional<T>("o"), requiredNode()) : make(requiredNode(), Optional<T>("o"));
                    object?[] args = optionalLeft ? [value, .. extraArgs] : [.. extraArgs, value];
                    var expected = optionalLeft ? cmp.Lifted(op, value, required) : cmp.Lifted(op, required, value);
                    await Assert.That(Run(body, args)).IsEqualTo(expected ? 1L : 0L)
                        .Because($"{typeof(T).Name}? {(optionalLeft ? "opt " + op + " req" : "req " + op + " opt")} value={(value?.ToString() ?? "absent")}");
                }
    }

    [Test]
    public async Task OptionalDateTime_AgainstRequired_IsCSharpsLiftedComparison() =>
        await OptionalAgainstRequired(DateTimeOps, new DateTime(2026, 1, 3), new DateTime(2026, 1, 5), new DateTime(2026, 1, 9), () => Param<DateTime>("r"), new DateTime(2026, 1, 5));

    [Test]
    public async Task OptionalDateOnly_AgainstRequired_IsCSharpsLiftedComparison() =>
        await OptionalAgainstRequired(DateOnlyOps, new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), () => Param<DateOnly>("r"), new DateOnly(2026, 1, 5));

    [Test]
    public async Task OptionalTimeOnly_AgainstRequired_IsCSharpsLiftedComparison() =>
        await OptionalAgainstRequired(TimeOnlyOps, new TimeOnly(8, 0), new TimeOnly(9, 0), new TimeOnly(10, 0), () => Param<TimeOnly>("r"), new TimeOnly(9, 0));

    [Test]
    public async Task OptionalTimeSpan_AgainstRequired_IsCSharpsLiftedComparison() =>
        await OptionalAgainstRequired(TimeSpanOps, TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2), () => Param<TimeSpan>("r"), TimeSpan.FromHours(1));

    [Test]
    public async Task OptionalDateTimeOffset_AgainstRequired_IsCSharpsLiftedComparison() {
        var required = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        await OptionalAgainstRequired(DateTimeOffsetOps, required.AddDays(-2), required, required.AddDays(4), () => Param<DateTimeOffset>("r"), required);
    }

    /// <summary>The required side as each shape the lowered tree can hold: a CLR member read, a call result, a constant (a static clock read cannot be equal, so it has less and greater only).</summary>
    [Test]
    public async Task OptionalDateTime_AgainstEveryRequiredShape_IsCSharpsLiftedComparison() {
        var day = new DateTime(2026, 1, 5, 13, 0, 0);
        await OptionalAgainstRequired(DateTimeOps, new DateTime(2026, 1, 3), day.Date, new DateTime(2026, 1, 9), () => new Member(Param<DateTime>("r"), "Date"), day);
        await OptionalAgainstRequired(DateTimeOps, new DateTime(2026, 1, 3), day.AddDays(1), new DateTime(2026, 1, 9), () => new Invoke(new Member(Param<DateTime>("r"), "AddDays"), new Constant(1.0)), day);
        await OptionalAgainstRequired(DateTimeOps, new DateTime(2026, 1, 3), new DateTime(2026, 1, 9), new DateTime(2026, 1, 12), () => new Constant(new DateTime(2026, 1, 9)));
        var now = DateTime.UtcNow;
        foreach (var value in new DateTime?[] { now.AddDays(-1), now.AddDays(1), null })
            await Assert.That(Run(new LessThan(Optional<DateTime>("o"), new Member(new NamedTypeReference("DateTime"), "UtcNow")), value))
                .IsEqualTo(DateTimeOps.Lifted("<", value, now) ? 1L : 0L).Because($"UtcNow value={value}");
    }

    [Test]
    public async Task OptionalTimeSpan_AgainstAConstant_IsCSharpsLiftedComparison() =>
        await OptionalAgainstRequired(TimeSpanOps, TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(3), () => new Constant(TimeSpan.FromHours(2)));

    /// <summary>Optional against optional: the present pair takes less, equal and greater, and every absent combination.</summary>
    private static async Task BothOptional<T>(Cmp<T> cmp, T a, T b) where T : struct {
        T?[][] pairs = [[a, b], [a, a], [b, a], [a, null], [null, b], [null, null]];
        foreach (var (op, make) in Operators)
            foreach (var pair in pairs) {
                var expected = cmp.LiftedBoth(op, pair[0], pair[1]);
                await Assert.That(Run(make(Optional<T>("l"), Optional<T>("r")), pair[0], pair[1])).IsEqualTo(expected ? 1L : 0L)
                    .Because($"{typeof(T).Name}? {op} {typeof(T).Name}? {pair[0]?.ToString() ?? "absent"}/{pair[1]?.ToString() ?? "absent"}");
            }
    }

    [Test]
    public async Task OptionalAgainstOptional_EveryTemporalFamily_IsCSharpsLiftedComparison() {
        await BothOptional(DateTimeOps, new DateTime(2026, 1, 3), new DateTime(2026, 1, 5));
        await BothOptional(DateOnlyOps, new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 5));
        await BothOptional(TimeOnlyOps, new TimeOnly(8, 0), new TimeOnly(9, 0));
        await BothOptional(TimeSpanOps, TimeSpan.FromMinutes(30), TimeSpan.FromHours(1));
        await BothOptional(DateTimeOffsetOps, new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));
    }

    private static string Refusal(Node body) {
        try {
            Interpreter.Compile(body);
        }
        catch (InvalidOperationException e) {
            return e.Message;
        }
        return "(accepted)";
    }

    /// <summary>
    /// A nullable temporal beside a different type is refused with a positioned message: the pair has no operator, and the
    /// two optional/DateTimeOffset pairs are the narrowed interim rule's (C# compiles them; the VM does not convert in a
    /// nullable compare yet, row T-21b).
    /// </summary>
    [Test]
    [Arguments("DateOnly?", "DateTime")]
    [Arguments("DateTime?", "DateOnly")]
    [Arguments("DateTime?", "TimeSpan")]
    [Arguments("TimeOnly?", "DateOnly")]
    [Arguments("TimeSpan?", "DateTime")]
    [Arguments("DateTime?", "DateTimeOffset")]
    [Arguments("DateTimeOffset?", "DateTime")]
    [Arguments("DateOnly?", "Int32")]
    [Arguments("DateOnly?", "Int64")]
    [Arguments("DateOnly?", "Decimal")]
    [Arguments("DateOnly?", "String")]
    [Arguments("DateOnly?", "Boolean")]
    [Arguments("DateOnly?", "Object")]
    public async Task OptionalTemporal_BesideADifferentType_IsRefusedWithAPositionedMessage(string optional, string required) {
        static Node Opt(string type) => type switch {
            "DateOnly?" => Optional<DateOnly>("o"),
            "DateTime?" => Optional<DateTime>("o"),
            "TimeOnly?" => Optional<TimeOnly>("o"),
            "TimeSpan?" => Optional<TimeSpan>("o"),
            _ => Optional<DateTimeOffset>("o"),
        };
        static Node Req(string type) => type switch {
            "DateTime" => Param<DateTime>("r"),
            "DateOnly" => Param<DateOnly>("r"),
            "TimeSpan" => Param<TimeSpan>("r"),
            "DateTimeOffset" => Param<DateTimeOffset>("r"),
            "Int32" => Param<int>("r"),
            "Int64" => Param<long>("r"),
            "Decimal" => Param<decimal>("r"),
            "String" => Param<string>("r"),
            "Boolean" => Param<bool>("r"),
            _ => Param<object>("r"),
        };
        foreach (var (op, make) in Operators.Where(static o => o.Name is "==" or "<"))
            foreach (var optionalLeft in new[] { true, false }) {
                var message = Refusal(optionalLeft ? make(Opt(optional), Req(required)) : make(Req(required), Opt(optional)));
                await Assert.That(message).StartsWith("VM compile rejected: comparison between incompatible types")
                    .Because($"{optional} {op} {required} optionalLeft={optionalLeft}");
            }
    }

    /// <summary>Optional-date arithmetic stays refused in the lowered analysis.</summary>
    [Test]
    public async Task OptionalTemporalArithmetic_StaysRefused() {
        await Assert.That(Refusal(new Add(Optional<DateTime>("o"), Param<TimeSpan>("r"))))
            .IsEqualTo("VM compile rejected: arithmetic operand is not numeric (got 'Nullable`1' and 'TimeSpan')");
        await Assert.That(Refusal(new Subtract(Optional<DateTime>("o"), Param<DateTime>("r"))))
            .IsEqualTo("VM compile rejected: arithmetic operand is not numeric (got 'Nullable`1' and 'DateTime')");
        await Assert.That(Refusal(new Add(Optional<DateOnly>("o"), Param<int>("r"))))
            .IsEqualTo("VM compile rejected: temporal offset requires a date type with AddDays (got 'Nullable`1')");
    }

    [Test]
    public async Task RequiredDecimal_Unseeded_Equality_Throws() {
        var program = Interpreter.Compile(new Equal(Param<decimal>("left"), Param<decimal>("right")));
        await Assert.That(() => Interpreter.Execute(program).Dispose())
            .Throws<InvalidOperationException>()
            .WithMessage("VM: required Decimal value is absent");
    }

    /// <summary>The same absent-handle failure covers every required heap value,
    /// not only decimal.</summary>
    [Test]
    public async Task RequiredGuid_Unseeded_Equality_Throws() {
        var program = Interpreter.Compile(new Equal(Param<Guid>("left"), Param<Guid>("right")));
        await Assert.That(() => Interpreter.Execute(program).Dispose())
            .Throws<InvalidOperationException>()
            .WithMessage("VM: required Guid value is absent");
    }

    [Test, Timeout(10_000)]
    public async Task MixedDecimal_Comparison_EvaluatesLeftThenRight(CancellationToken ct) {
        _ = ct;
        var types = StampType();
        var lot = new Variable("lot");
        var body = new Block([
            new Assignment(lot, new New(new TypeReference("Branch.StampLot"))),
            new Equal(
                new Invoke(new Member(lot, "Left"), []),
                new Invoke(new Member(lot, "Right"), [])),
            new Member(lot, "mark"),
        ], [lot]);
        using var exec = Interpreter.Execute(Interpreter.Compile(body, types));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    private static ITypeDefinitionProvider StampType() {
        var analyzer = new TypeDefinitionNodeAnalyzer();
        var context = AnalysisContext.CreateDefault();
        analyzer.Analyze(context, new TypeDefinitionNode(
            "StampLot", "Branch",
            Fields: [
                new FieldDefinitionNode("n", TypeReference.To<long>(), new Constant(0L)),
                new FieldDefinitionNode("mark", TypeReference.To<long>(), new Constant(0L)),
            ],
            Constructors: [new ConstructorDefinitionNode([], new Block([]))],
            Methods: [
                Stamp("Left", TypeReference.To<int>(), new Constant(0), 1L),
                Stamp("Right", TypeReference.To<decimal>(), new Constant(0m), 2L),
            ]));
        return context.TypeDefinitions;
    }

    private static MethodDefinitionNode Stamp(string name, TypeReference returns, Node value, long mark) =>
        new(
            name,
            returns,
            Parameters: [],
            Body: new Block([
                new Assignment(new Member(new ThisReference(), "mark"), new Constant(mark)),
                new Return(value),
            ]));
}

/// <summary>One enum per underlying type, five members in ascending numeric order (V0..V4): the unsigned ones
/// straddle the signed boundary of their width, the signed ones hold the minimum, -1, 0, 1 and the maximum.</summary>
public static class ComparisonEnums {
    public enum SB : sbyte { V0 = sbyte.MinValue, V1 = -1, V2 = 0, V3 = 1, V4 = sbyte.MaxValue }
    public enum B : byte { V0 = 0, V1 = 1, V2 = byte.MaxValue / 2, V3 = byte.MaxValue / 2 + 1, V4 = byte.MaxValue }
    public enum S : short { V0 = short.MinValue, V1 = -1, V2 = 0, V3 = 1, V4 = short.MaxValue }
    public enum US : ushort { V0 = 0, V1 = 1, V2 = ushort.MaxValue / 2, V3 = ushort.MaxValue / 2 + 1, V4 = ushort.MaxValue }
    public enum I : int { V0 = int.MinValue, V1 = -1, V2 = 0, V3 = 1, V4 = int.MaxValue }
    public enum UI : uint { V0 = 0, V1 = 1, V2 = uint.MaxValue / 2, V3 = uint.MaxValue / 2 + 1, V4 = uint.MaxValue }
    public enum L : long { V0 = long.MinValue, V1 = -1, V2 = 0, V3 = 1, V4 = long.MaxValue }
    public enum UL : ulong { V0 = 0, V1 = 1, V2 = ulong.MaxValue / 2, V3 = ulong.MaxValue / 2 + 1, V4 = ulong.MaxValue }
}

/// <summary>
/// Roslyn is the authority for a same-enum comparison: one compilation of one static method per cell (an enum beside an
/// enum, a nullable enum, a named member or the null literal, either order), the VM's answer on every operand value
/// compared with the method's. Nothing is recorded: the C# compiler computes the expected value on each run.
/// </summary>
internal static class EnumComparisonOracle {
    private static readonly OperatorKind[] Kinds = [
        OperatorKind.Equal, OperatorKind.NotEqual, OperatorKind.LessThan,
        OperatorKind.LessThanOrEqual, OperatorKind.GreaterThan, OperatorKind.GreaterThanOrEqual
    ];

    private static readonly string[] Shapes = [
        "E|E", "E|E?", "E?|E", "E?|E?",
        "E|V0", "E|V2", "E|V4", "V0|E", "V2|E", "V4|E",
        "E?|V0", "E?|V2", "E?|V4", "V0|E?", "V2|E?", "V4|E?",
        "E?|null", "null|E?"
    ];

    private static readonly Lazy<Dictionary<(Type, string, OperatorKind), MethodInfo?>> Compiled = new(Compile);

    private static string Fqn(Type type) => "global::" + type.FullName!.Replace('+', '.');

    private static bool IsParameter(string side) => side is "E" or "E?";

    private static IEnumerable<(Type Enum, string Shape, OperatorKind Kind)> Cells(IEnumerable<Type> enums) =>
        from type in enums
        from shape in Shapes
        from kind in Kinds
            // The VM refuses an ordering against the null literal for every type, with a positioned error (not enum-specific).
        where kind is OperatorKind.Equal or OperatorKind.NotEqual || !shape.Contains("null")
        select (type, shape, kind);

    private static string Operator(OperatorKind kind) => kind switch {
        OperatorKind.Equal => "==",
        OperatorKind.NotEqual => "!=",
        OperatorKind.LessThan => "<",
        OperatorKind.LessThanOrEqual => "<=",
        OperatorKind.GreaterThan => ">",
        OperatorKind.GreaterThanOrEqual => ">=",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static Dictionary<(Type, string, OperatorKind), MethodInfo?> Compile() {
        var enums = typeof(ComparisonEnums).GetNestedTypes().Where(static t => t.IsEnum).ToArray();
        var cells = Cells(enums).ToArray();
        var source = new StringBuilder("public static class Cells {\n");
        for (var i = 0; i < cells.Length; i++) {
            var (type, shape, kind) = cells[i];
            var sides = shape.Split('|');
            var parameters = new List<string>();
            var text = new string[2];
            for (var s = 0; s < 2; s++) {
                if (IsParameter(sides[s])) {
                    var name = "p" + s;
                    parameters.Add($"{Fqn(type)}{(sides[s] == "E?" ? "?" : "")} {name}");
                    text[s] = name;
                }
                else
                    text[s] = sides[s] == "null" ? "null" : $"{Fqn(type)}.{sides[s]}";
            }
            source.Append($"public static bool M{i}({string.Join(", ", parameters)}) => {text[0]} {Operator(kind)} {text[1]};\n");
        }
        source.Append("}\n");
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Where(static p => Path.GetFileName(p) is "System.Private.CoreLib.dll" or "System.Runtime.dll")
            .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(ComparisonEnums).Assembly.Location))
            .ToArray();
        var tree = CSharpSyntaxTree.ParseText(source.ToString(), new CSharpParseOptions(LanguageVersion.CSharp11));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var compilation = CSharpCompilation.Create("EnumComparisonOracle", [tree], references, options);
        using var pe = new MemoryStream();
        var emit = compilation.Emit(pe);
        if (!emit.Success)
            throw new InvalidOperationException("enum oracle: " + string.Join("\n",
                emit.Diagnostics.Where(static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Take(5)));
        pe.Position = 0;
        var holder = new AssemblyLoadContext("EnumComparisonOracle", isCollectible: true).LoadFromStream(pe).GetType("Cells")!;
        var answers = new Dictionary<(Type, string, OperatorKind), MethodInfo?>();
        for (var i = 0; i < cells.Length; i++)
            answers[cells[i]] = holder.GetMethod($"M{i}");
        return answers;
    }

    private static Node Side(Type type, string side, string name) => side switch {
        "E" => new Parameter(name, new ClrTypeReference(type)),
        "E?" => new Parameter(name, new OptionalTypeReference(new ClrTypeReference(type))),
        "null" => new Constant(null),
        _ => new Constant(Enum.Parse(type, side))
    };

    private static IEnumerable<object?> Values(Type type, string side) =>
        side == "E?" ? Enum.GetValues(type).Cast<object?>().Append(null) : Enum.GetValues(type).Cast<object?>();

    private static string Show(object? value) => value is null ? "null" : value.ToString()!;

    public static List<string> Mismatches(Type type) {
        var mismatches = new List<string>();
        var answers = Compiled.Value;
        foreach (var (_, shape, kind) in Cells([type])) {
            var sides = shape.Split('|');
            var oracle = answers[(type, shape, kind)];
            if (oracle is null) {
                mismatches.Add($"{type.Name} {shape} {kind}: C# rejects the shape");
                continue;
            }
            var node = NativeIntegerGrid.Build(kind, Side(type, sides[0], "p0"), Side(type, sides[1], "p1"));
            VmProgram program;
            try {
                program = Interpreter.Compile(node);
            }
            catch (Exception ex) {
                mismatches.Add($"{type.Name} {shape} {kind}: VM compile {ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
                continue;
            }
            var leftValues = IsParameter(sides[0]) ? Values(type, sides[0]).ToArray() : [null];
            var rightValues = IsParameter(sides[1]) ? Values(type, sides[1]).ToArray() : [null];
            foreach (var left in leftValues) {
                foreach (var right in rightValues) {
                    var arguments = new List<object?>();
                    if (IsParameter(sides[0]))
                        arguments.Add(left);
                    if (IsParameter(sides[1]))
                        arguments.Add(right);
                    var expected = (bool)oracle.Invoke(null, [.. arguments])!;
                    string actual;
                    try {
                        using var exec = Interpreter.Execute(program, [.. arguments]);
                        actual = (exec.RawValue != 0).ToString();
                    }
                    catch (Exception ex) {
                        actual = $"throws {ex.GetType().Name}";
                    }
                    if (actual != expected.ToString())
                        mismatches.Add($"{type.Name} {shape} {kind} [{Show(left)}, {Show(right)}]: VM {actual}, C# {expected}");
                }
            }
        }
        return mismatches;
    }
}