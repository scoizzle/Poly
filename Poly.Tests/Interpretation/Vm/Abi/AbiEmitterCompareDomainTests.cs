using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Introspection;

namespace Poly.Tests.Interpretation.Vm.Abi;

/// <summary>
/// One comparison reads values, never slots (#330 R7 and the compare protocol).
/// The domain is C# numeric promotion plus the nullable lift, and the constant
/// fold uses that same promotion. An absent operand equals only an absent
/// operand and orders as false. An integral pair promotes to <c>decimal</c>,
/// and an integral against <c>float</c> promotes to <c>float</c>. An enum slot
/// is its underlying integer.
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

    [Test]
    public async Task DecimalParameter_Equal_DoubleParameter_UsesValue() {
        await Assert.That(Run(new Equal(Param<decimal>("amount"), Param<double>("width")), 1.5m, 1.5d)).IsEqualTo(1L);
    }

    [Test]
    public async Task DecimalParameter_Equal_FloatParameter_UsesValue() {
        await Assert.That(Run(new Equal(Param<decimal>("amount"), Param<float>("width")), 1.5m, 1.5f)).IsEqualTo(1L);
    }

    [Test]
    public async Task DecimalParameter_Equal_DoubleParameter_DifferentValues_IsFalse() {
        await Assert.That(Run(new Equal(Param<decimal>("amount"), Param<double>("width")), 1.5m, 1.9d)).IsEqualTo(0L);
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

    [Test]
    public async Task ULongParameter_Equal_SignedMinusOne_IsFalse() {
        await Assert.That(Run(new Equal(new Constant(-1), Param<ulong>("count")), ulong.MaxValue)).IsEqualTo(0L);
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

    [Test]
    public async Task DecimalParameter_Equal_DoubleBeyondPrecision_MatchesFold() {
        const decimal exact = 9007199254740993m;
        await Assert.That(Run(new Equal(Param<decimal>("amount"), new Constant((double)exact)), exact)).IsEqualTo(1L);
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

    [Test]
    public async Task DateTimeOptional_Equal_RequiredDateTime_IsRejected() {
        await Assert.That(() => Interpreter.Compile(
                new Equal(Optional<DateTime>("when"), Param<DateTime>("required"))))
            .Throws<InvalidOperationException>()
            .WithMessage("VM compile rejected: comparison between incompatible types 'Nullable`1' and 'DateTime'");
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