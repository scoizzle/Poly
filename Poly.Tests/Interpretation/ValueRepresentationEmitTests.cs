using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Introspection;

using CSharpCallOracle = Poly.Tests.Introspection.CSharpCallOracle;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Representation is the effective node's resolved CLR type: a replacement carries
/// the real runtime action's type, and emit classifies that type. These tests run
/// the standard pipeline WITHOUT a representation pass, so a green test proves emit
/// derives the slot kind itself.
/// </summary>
public enum ByteMark : byte { None = 0, Low = 1, High = 200 }

public enum ShortMark : short { None = 0, Low = 1, High = -300 }

public enum IntMark : int { None = 0, Low = 1, High = -70000 }

public enum LongMark : long { None = 0, Low = 1, Big = 5_000_000_000 }

public enum ULongMark : ulong { None = 0, Low = 1, Big = ulong.MaxValue }

public class ValueRepresentationEmitTests {
    private static readonly Analyzer AnalyzerWithoutRepresentationPass = new AnalyzerBuilder()
        .UseTypeDefinitionNodeAnalyzer()
        .UseThisReferenceContext()
        .UseVariableScopeValidator()
        .UseTypeAndMemberResolver()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseConstantFolding()
        .UseControlFlowAnalysis()
        .UseExceptionRegionAnalysis()
        .UseLambdaReturnTypeResolution()
        .UseSyntaxTypeCompatibility()
        .UseCallSiteCatalog()
        .UseDefiniteAssignmentAnalysis()
        .Build();

    private static AnalysisResult Analyze(Node node) =>
        AnalyzerWithoutRepresentationPass.Analyze(node);

    private static Member Probe(string name) =>
        new(new ClrTypeReference(typeof(RepresentationProbe)), name);

    [Test]
    public async Task ScalarIntoObjectParameter_IsBoxedWithoutARepresentationPass() {
        // A non-constant scalar has no representation metadata here; emit must read
        // the parameter's resolved type and box the raw word, not deref it.
        var x = new Parameter("x", TypeReference.To<int>());
        var node = new Invoke(Probe(nameof(RepresentationProbe.SeenAsObject)), x);
        var result = Analyze(node);

        using var exec = Interpreter.Execute(
            Interpreter.Compile(node, result), s => s.SetArgs(5));

        await Assert.That(exec.GetValue<int>()).IsEqualTo(5);
    }

    [Test]
    public async Task RootScalar_AfterHeapAllocations_DecodesRawWord() {
        // Raw 2 with three live heap handles would be misread as handle 2 unless the
        // root kind comes from the resolved type.
        var a = new Variable("a");
        var b = new Variable("b");
        var c = new Variable("c");
        var p = new Parameter("p", TypeReference.To<long>());
        var node = new Block([
            new Assignment(a, new Constant("one")),
            new Assignment(b, new Constant("two")),
            new Assignment(c, new Constant("three")),
            new Add(p, new Constant(1L))
        ], [a, b, c]);
        var result = Analyze(node);

        using var exec = Interpreter.Execute(
            Interpreter.Compile(node, result), s => s.SetArgs(1L));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task DominatingReturn_BeatsDeadNonVoidTail() {
        var node = new Block([
            new IfStatement(new Constant(true), new Return(new Constant(7L))),
            new Constant("miss")
        ]);
        var result = Analyze(node);

        using var exec = Interpreter.Execute(Interpreter.Compile(node, result));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task NestedDominatingReturn_BeatsALaterDeadTail() {
        var node = new Block([
            new Block([new Return(new Constant(1L))]),
            new Return(new Constant("miss"))
        ]);
        var result = Analyze(node);

        using var exec = Interpreter.Execute(Interpreter.Compile(node, result));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task IfTrue_NestedConditionalReturn_YieldsTheUnconditionalReturn() {
        var flag = new Parameter("flag", TypeReference.To<bool>());
        var node = new Block([
            new IfStatement(new Constant(true), new Block([
                new IfStatement(flag, new Return(new Constant("s"))),
                new Return(new Constant(1))
            ]))
        ]);
        var result = Analyze(node);

        using var exec = Interpreter.Execute(
            Interpreter.Compile(node, result), s => s.SetArgs(false));

        await Assert.That(exec.GetValue<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task IfTrue_NakedNestedReturn_YieldsTheLiveReturn() {
        var node = new Block([
            new IfStatement(new Constant(true),
                new IfStatement(new Constant(true), new Return(new Constant(1L)))),
            new Return(new Constant("miss"))
        ]);
        var result = Analyze(node);

        using var exec = Interpreter.Execute(Interpreter.Compile(node, result));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task IfTrue_IfElseBothReturn_YieldsTheBranchValue() {
        var flag = new Parameter("flag", TypeReference.To<bool>());
        var node = new Block([
            new IfStatement(new Constant(true), new Block([
                new IfStatement(flag, new Return(new Constant(1L)), new Return(new Constant(2L)))
            ])),
            new Return(new Constant(99L))
        ]);
        var result = Analyze(node);

        using var exec = Interpreter.Execute(
            Interpreter.Compile(node, result), s => s.SetArgs(false));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(2L);
    }

    [Test]
    public async Task IfTrue_WithFallThroughIf_DoesNotStealTheLaterReturn() {
        var flag = new Parameter("flag", TypeReference.To<bool>());
        var a = new Variable("a");
        var node = new Block([
            new Assignment(a, new Constant("heap")),
            new IfStatement(new Constant(true), new Block([
                new IfStatement(flag, new Return(new Constant("s")))
            ])),
            new Return(new Constant(1L))
        ], [a]);
        var result = Analyze(node);

        using var exec = Interpreter.Execute(
            Interpreter.Compile(node, result), s => s.SetArgs(false));

        await Assert.That(exec.GetValue<long>()).IsEqualTo(1L);
    }

    [Test]
    public async Task IfElse_DifferentArmTypes_DoNotBecomeTheBlockType() {
        var flag = new Parameter("flag", TypeReference.To<bool>());
        var a = new Variable("a");
        var node = new Block([
            new Assignment(a, new Constant("heap")),
            new IfStatement(flag,
                new Return(new Constant(1L)), new Return(new Constant("no"))),
            new Return(new Constant("tail"))
        ], [a]);
        var result = Analyze(node);

        using var exec = Interpreter.Execute(
            Interpreter.Compile(node, result), s => s.SetArgs(false));

        await Assert.That(exec.GetValue<string>()).IsEqualTo("no");
    }

    [Test]
    public async Task EnumConstant_IntoEnumParameter_Unboxes() {
        var node = new Invoke(
            new Member(new ClrTypeReference(typeof(EnumProbe)), nameof(EnumProbe.Id)),
            new Constant(EnumProbe.Value.A));
        var result = Analyze(node);

        using var exec = Interpreter.Execute(Interpreter.Compile(node, result));

        await Assert.That(exec.GetValue<int>()).IsEqualTo(40);
    }

    [Test]
    public async Task NullableMember_PassedToMethod_IsTheValueNotTheHandle() {
        var arg = new Parameter("box", TypeReference.To<NullableProbe.Box>());
        var node = new Invoke(
            new Member(new ClrTypeReference(typeof(NullableProbe)), nameof(NullableProbe.Id)),
            new Member(arg, nameof(NullableProbe.Box.N)));
        var result = Analyze(node);

        using var exec = Interpreter.Execute(
            Interpreter.Compile(node, result), s => s.SetArgs(new NullableProbe.Box { N = 5 }));

        await Assert.That(exec.GetValue<int>()).IsEqualTo(5);
    }

    [Test]
    public async Task StatementTail_WithNoValuedReturn_IsVoid() {
        var node = new Block([new Constant(1), new Block([])]);
        var result = Analyze(node);

        using var exec = Interpreter.Execute(Interpreter.Compile(node, result));

        await Assert.That(exec.Result.IsVoid).IsTrue();
    }

    [Test]
    public async Task FoldedShift_CarriesTheOriginalIntegerType() {
        // x * 8 lowers to ShiftLeft(x, 3); the replacement must carry x's type so
        // emit classifies it as a scalar.
        var p = new Parameter("p", TypeReference.To<int>());
        var multiply = new Multiply(p, new Constant(8L));
        var node = new Block([
            new Assignment(new Variable("a"), new Constant("heap")),
            multiply
        ], [new Variable("a")]);
        var result = Analyze(node);
        var effective = result.GetNodeReplacement(multiply) ?? multiply;

        await Assert.That(ValueRepresentationClassifier.KindOf(result, effective))
            .IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task StringConcat_RootIsAString() {
        var node = new Add(new Constant(1), new Constant("a"));
        var result = Analyze(node);

        using var exec = Interpreter.Execute(Interpreter.Compile(node, result));

        await Assert.That(exec.GetValue<string>()).IsEqualTo("1a");
    }

    [Test]
    public async Task DateOffset_RootIsTheDate() {
        var date = new DateOnly(2026, 1, 1);
        var p = new Parameter("p", TypeReference.To<DateOnly>());
        var node = new Add(p, new Constant(1L));
        var result = Analyze(node);

        using var exec = Interpreter.Execute(
            Interpreter.Compile(node, result), s => s.SetArgs(date));

        await Assert.That(exec.GetValue<DateOnly>()).IsEqualTo(date.AddDays(1));
    }

    [Test]
    public async Task DecimalArithmetic_RootIsADecimal() {
        var p = new Parameter("d", TypeReference.To<decimal>());
        var node = new Add(p, new Constant(1m));
        var result = Analyze(node);

        using var exec = Interpreter.Execute(
            Interpreter.Compile(node, result), s => s.SetArgs(1.5m));

        await Assert.That(exec.GetValue<decimal>()).IsEqualTo(2.5m);
    }

    private static string Answer(Node node, params object?[] args) {
        try {
            using var exec = Interpreter.Execute(Interpreter.Compile(node), args);
            return exec.Result.Value is { } value ? $"{value.GetType().Name} {value}" : "null";
        }
        catch (InvalidOperationException ex) {
            return ex.Message;
        }
    }

    private static Parameter P<T>(string name) => new(name, TypeReference.To<T>());

    private static Invoke NullableCall(string method, bool present) =>
        new(new Member(new ClrTypeReference(typeof(NullableProbe)), method), new Constant(present));

    /// <summary>
    /// Unary minus and <c>~</c> on a value held behind a handle (issue 832): a struct wider than a word, and a nullable
    /// that arrives as a word (a call result). The type's own operator runs; the handle is never the operand. A struct
    /// with no such operator is refused by the analysis at the node (T-22a), where the VM used to negate the handle.
    /// </summary>
    [Test]
    public async Task UnaryOnAHandleHeldValue_IsTheTypesOperator_OrRefused() {
        var answers = new[] {
            Answer(new UnaryMinus(P<decimal>("d")), 0.1m),
            Answer(new UnaryMinus(P<decimal?>("d")), 0.1m),
            Answer(new UnaryMinus(P<decimal?>("d")), (object?)null),
            Answer(new UnaryMinus(P<TimeSpan>("t")), TimeSpan.FromHours(1)),
            Answer(new UnaryMinus(P<TimeSpan?>("t")), TimeSpan.FromHours(1)),
            Answer(new UnaryMinus(P<Int128>("i")), (Int128)5),
            Answer(new BitwiseNot(P<Int128>("i")), (Int128)5),
            Answer(new BitwiseNot(P<UInt128>("i")), (UInt128)5),
            Answer(new UnaryMinus(P<System.Numerics.BigInteger>("b")), new System.Numerics.BigInteger(5)),
            Answer(new UnaryMinus(NullableCall(nameof(NullableProbe.FiveOrNull), true))),
            Answer(new UnaryMinus(NullableCall(nameof(NullableProbe.FiveOrNull), false))),
            Answer(new BitwiseNot(NullableCall(nameof(NullableProbe.FiveOrNull), true))),
            Answer(new UnaryMinus(NullableCall(nameof(NullableProbe.FiveDecimalOrNull), true))),
            Answer(new UnaryMinus(P<Guid>("g")), Guid.Empty),
            Answer(new UnaryMinus(P<DateTimeOffset>("o")), DateTimeOffset.UnixEpoch),
            Answer(new BitwiseNot(P<Guid>("g")), Guid.Empty),
        };
        await Assert.That(answers).IsEquivalentTo(new[] {
            Shown(-0.1m),
            Shown(-(decimal?)0.1m),
            Shown(null),
            Shown(-TimeSpan.FromHours(1)),
            Shown(-(TimeSpan?)TimeSpan.FromHours(1)),
            Shown(-(Int128)5),
            Shown(~(Int128)5),
            Shown(~(UInt128)5),
            Shown(-new System.Numerics.BigInteger(5)),
            Shown(-NullableProbe.FiveOrNull(true)),
            Shown(-NullableProbe.FiveOrNull(false)),
            Shown(~NullableProbe.FiveOrNull(true)),
            Shown(-NullableProbe.FiveDecimalOrNull(true)),
            "VM compile rejected: operator UnaryMinus is not defined for 'Guid': no UnaryMinus operator for this type",
            "VM compile rejected: operator UnaryMinus is not defined for 'DateTimeOffset': no UnaryMinus operator for this type",
            "VM compile rejected: operator BitwiseNot is not defined for 'Guid': no BitwiseNot operator for this type",
        }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static string Shown(object? value) => value is null ? "null" : $"{value.GetType().Name} {value}";

    /// <summary>The edges of each operator are the platform's: <c>-TimeSpan.MinValue</c> overflows (the VM throws the
    /// same <see cref="OverflowException"/>), <c>-decimal.MinValue</c> is exact, <c>-Int128.MinValue</c> wraps, and
    /// <c>-0m</c> is the platform's negative zero.</summary>
    [Test]
    public async Task UnaryOnAHandleHeldValue_Edges_AreThePlatforms() {
        var timeMin = TimeSpan.MinValue;
        await Assert.That(() => _ = -timeMin).Throws<OverflowException>();
        await Assert.That(() => Answer(new UnaryMinus(P<TimeSpan>("t")), TimeSpan.MinValue)).Throws<OverflowException>();
        var decimalMin = decimal.MinValue;
        await Assert.That(Answer(new UnaryMinus(P<decimal>("d")), decimalMin)).IsEqualTo(Shown(-decimalMin));
        var int128Min = Int128.MinValue;
        await Assert.That(Answer(new UnaryMinus(P<Int128>("i")), int128Min)).IsEqualTo(Shown(-int128Min));
        var zero = 0m;
        await Assert.That(Answer(new UnaryMinus(P<decimal>("d")), zero)).IsEqualTo(Shown(-zero));
    }

    private static readonly Analyzer AnalyzerWithoutOperandChecks = new AnalyzerBuilder()
        .UseTypeDefinitionNodeAnalyzer()
        .UseThisReferenceContext()
        .UseVariableScopeValidator()
        .UseTypeAndMemberResolver()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseConstantFolding()
        .UseControlFlowAnalysis()
        .UseExceptionRegionAnalysis()
        .UseLambdaReturnTypeResolution()
        .UseCallSiteCatalog()
        .UseDefiniteAssignmentAnalysis()
        .Build();

    /// <summary>The emitter's own refusal of a unary operator a struct does not define. Analysis refuses the operator
    /// first (row T-22); a caller-built analysis without the operand checks reaches the emitter, which fails closed.</summary>
    [Test]
    public async Task UnaryOnAStructWithNoOperator_PastAnAnalysisWithoutOperandChecks_IsRefusedAtEmit() {
        var node = new UnaryMinus(P<Guid>("g"));
        await Assert.That(() => Interpreter.Compile(node, AnalyzerWithoutOperandChecks.Analyze(node)))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("no unary '-' operator for 'Guid'");
    }

    private static string StoreNull<T>(T[] host) {
        var node = new Block([
            new Assignment(new IndexAccess(P<T[]>("arr"), new Constant(0)), new Constant(null)),
            new Constant(0)
        ]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node), (object)host);
        return Shown(host[0]);
    }

    /// <summary><c>arr[0] = null</c> stores null into a host array of a reference or a nullable element (the base
    /// stored a boxed <c>0L</c> or threw), and a nullable element reads back as its value.</summary>
    [Test]
    public async Task NullLiteral_StoredIntoAnArrayElement_IsNull_AndANullableElementReadsBack() {
        await Assert.That(StoreNull(new string?[] { "x" })).IsEqualTo(Shown(null));
        await Assert.That(StoreNull(new object?[] { 7 })).IsEqualTo(Shown(null));
        await Assert.That(StoreNull(new long?[] { 5 })).IsEqualTo(Shown(null));
        await Assert.That(Answer(new IndexAccess(P<long?[]>("arr"), new Constant(0)), (object)new long?[] { 5 }))
            .IsEqualTo(Shown((long?)5));
    }

    /// <summary>
    /// The tree the domain lowering gives a date minus a decimal day count, <c>Start.AddDays((Int32)-Days)</c>, built
    /// through the API (the DSL refuses a decimal day count from T-10a on). The decimal is negated, not its handle.
    /// </summary>
    [Test]
    public async Task DateMinusDecimalDays_AddDaysOfTheNegatedCount_IsTheEarlierDate() {
        var node = new Invoke(new Member(P<DateOnly>("start"), nameof(DateOnly.AddDays)),
            new TypeCast(new UnaryMinus(P<decimal>("days")), TypeReference.To<int>()));
        await Assert.That(Answer(node, new DateOnly(2024, 2, 28), 2m)).IsEqualTo($"DateOnly {new DateOnly(2024, 2, 26)}");
    }

    private static string Constructed<T>(Node argument, params object?[] args) =>
        Answer(new Member(new New(new ClrTypeReference(typeof(T)), argument), nameof(CtorProbe.ObjectBox.Text)), args);

    /// <summary>A CLR constructor argument is the value its parameter's type reads from the word, as a call argument is:
    /// a scalar for an <c>object</c> parameter is the boxed value, an IEEE word is the double, a decimal or optional handle
    /// is the value it holds.</summary>
    [Test]
    public async Task ConstructorArgument_IsTheValueForTheParameterType() {
        var answers = new[] {
            Constructed<CtorProbe.ObjectBox>(new Constant(7)),
            Constructed<CtorProbe.ObjectBox>(new Constant("x")),
            Constructed<CtorProbe.DoubleBox>(new Constant(1.5)),
            Constructed<CtorProbe.DoubleBox>(P<double>("d"), 1.5),
            Constructed<CtorProbe.DecimalBox>(new Constant(1.5m)),
            Constructed<CtorProbe.NullableIntBox>(P<int?>("n"), 5),
            Constructed<CtorProbe.NullableIntBox>(P<int?>("n"), (object?)null),
            Constructed<CtorProbe.BoolBox>(new Constant(true)),
        };
        await Assert.That(answers).IsEquivalentTo(new[] {
            "String Int32:7", "String String:x", "String 1.5", "String 1.5", "String 1.5", "String 5", "String <null>", "String True",
        }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static Invoke ToText(Node value) =>
        new(new Member(new ClrTypeReference(typeof(Convert)), nameof(Convert.ToString)), value);

    private static Parameter ObjectParameter(string name) => new(name, new ClrTypeReference(typeof(object)));

    private static string HostArrayAfter(Node stored) {
        var host = new object?[] { null };
        var node = new Block([new Assignment(new IndexAccess(P<object[]>("arr"), new Constant(0)), stored), new Constant(0)]);
        using var exec = Interpreter.Execute(Interpreter.Compile(node), (object)host);
        return host[0] is { } value ? $"{value.GetType().Name}:{value}" : "null";
    }

    private static string NameDispatched(Type type, object argument) {
        var typeDef = new TypeDefinitionNode(Name: "BoundaryPerson", Methods: [
            new MethodDefinitionNode("Take", new TypeReference("void"),
                Parameters: [new Parameter("x", new ClrTypeReference(type))], Body: new Block([]))]);
        var context = AnalysisContext.CreateDefault();
        new TypeDefinitionNodeAnalyzer().Analyze(context, typeDef);
        var taker = new BoundaryProbe.Taker();
        var node = new Invoke(new Member(new Parameter("e", new TypeReference("BoundaryPerson")), "Take"),
            new Parameter("v", new ClrTypeReference(type)));
        using var exec = Interpreter.Execute(Interpreter.Compile(node, context.TypeDefinitions), taker, argument);
        return taker.Seen;
    }

    /// <summary>
    /// Every boundary where a value crosses between a frame word and a CLR object carries the value, by the static
    /// type on the word's side: an <c>object[]</c> element read and written, a <c>foreach</c> element with no declared
    /// type, a bridged delegate's <c>object</c> parameter, a forwarded host delegate's <c>object</c> result, and a
    /// name-dispatched call's argument. Each used to carry the word (a boxed long, the IEEE bits or a handle's number).
    /// </summary>
    [Test]
    public async Task BoundarySites_CarryTheValue_NotTheWord() {
        var element = new Variable("e");
        var text = new Variable("r");
        var forEach = new Block([
            new Assignment(text, new Constant("")),
            new ForEachLoop(element, P<List<object>>("list"), new Assignment(text, ToText(element))),
            text
        ], [text]);
        var array = new Variable("v");
        var vmArray = new Block([
            new Assignment(array, new NewArray(new ClrTypeReference(typeof(object)), new Constant(1))),
            new Assignment(new IndexAccess(array, new Constant(0)), new Constant(7)),
            ToText(new IndexAccess(array, new Constant(0)))
        ], [array]);
        var o = ObjectParameter("o");
        var forwarded = P<Func<object>>("g");
        var answers = new[] {
            Answer(ToText(new IndexAccess(P<object[]>("arr"), new Constant(0))), (object)new object[] { 7 }),
            Answer(vmArray),
            HostArrayAfter(new Constant(7)),
            HostArrayAfter(new Constant("s")),
            Answer(forEach, new List<object> { 7 }),
            Answer(new Invoke(new Member(new ClrTypeReference(typeof(BoundaryProbe)), nameof(BoundaryProbe.Apply)), new Lambda([o], ToText(o)))),
            Answer(new Invoke(new Lambda([forwarded], new Invoke(new Member(new ClrTypeReference(typeof(BoundaryProbe)), nameof(BoundaryProbe.CallIt)), forwarded)),
                P<Func<object>>("f")), (object)(Func<object>)(static () => 7)),
            NameDispatched(typeof(bool), true),
            NameDispatched(typeof(int), 7),
            NameDispatched(typeof(double), 1.5),
        };
        await Assert.That(answers).IsEquivalentTo(new[] {
            "String 7", "String 7", "Int32:7", "String:s", "String 7", "String 7", "String 7",
            "Boolean:True", "Int32:7", "Double:1.5",
        }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    // ---- T-21c: a value passed from one module method into another's parameter arrives as C# passes it ----

    private enum Shape { Parameter, NullLiteral, ZeroLiteral, Property, TimesTwo }

    private sealed record Row(Type Argument, Type Parameter, object? Value, object? Expected, Shape Shape = Shape.Parameter);

    private static string Show(Type type) => Nullable.GetUnderlyingType(type) is { } underlying ? underlying.Name + "?" : type.Name;

    private static string Named(Row row, string note) => $"{Show(row.Argument)} -> {Show(row.Parameter)}{note}";

    private static Row R(Type argument, Type parameter, object? value, object? expected, Shape shape = Shape.Parameter) =>
        new(argument, parameter, value, expected, shape);

    private static readonly DateOnly Day = new(2024, 2, 28);
    private static readonly DateTime Moment = new(2024, 2, 28, 1, 2, 3, DateTimeKind.Utc);
    private static readonly TimeOnly Clock = new(13, 14, 15);

    /// <summary>Rows that were red on the base: the argument is a frame word the callee read by the wrong type, or the
    /// call did not bind. The expected value is what C# passes, from the platform.</summary>
    private static readonly Dictionary<string, Row> RedRows = new[] {
        R(typeof(DateOnly), typeof(DateOnly?), Day, Day),
        R(typeof(DateTime), typeof(DateTime?), Moment, Moment),
        R(typeof(TimeOnly), typeof(TimeOnly?), Clock, Clock),
        R(typeof(ByteMark), typeof(ByteMark?), ByteMark.High, ByteMark.High),
        R(typeof(ShortMark), typeof(ShortMark?), ShortMark.High, ShortMark.High),
        R(typeof(IntMark), typeof(IntMark?), IntMark.High, IntMark.High),
        R(typeof(LongMark), typeof(LongMark?), LongMark.Big, LongMark.Big),
        R(typeof(ULongMark), typeof(ULongMark?), ULongMark.Big, ULongMark.Big),
        R(typeof(ulong), typeof(ulong?), ulong.MaxValue, ulong.MaxValue),
        R(typeof(double), typeof(double?), 1.5, 1.5),
        R(typeof(float), typeof(float?), 1.5f, 1.5f),
        R(typeof(DateOnly), typeof(object), Day, Day),
        R(typeof(DateTime), typeof(object), Moment, Moment),
        R(typeof(TimeOnly), typeof(object), Clock, Clock),
        R(typeof(bool), typeof(object), true, true),
        R(typeof(IntMark), typeof(object), IntMark.High, IntMark.High),
        R(typeof(int), typeof(object), 7, 7),
        R(typeof(double), typeof(object), 1.5, 1.5),
        R(typeof(DateOnly), typeof(IComparable), Day, Day),
        R(typeof(long), typeof(decimal?), 7L, 7m),
        R(typeof(long), typeof(decimal?), 7L, 14m, Shape.TimesTwo),
        R(typeof(long), typeof(decimal?), 7L, 7m, Shape.Property),
        R(typeof(int), typeof(long?), 7, 7L),
        R(typeof(int), typeof(decimal?), 7, 7m),
        R(typeof(int), typeof(double?), 7, 7.0),
        R(typeof(long?), typeof(decimal?), 7L, 7m),
        R(typeof(long?), typeof(decimal?), null, null),
        R(typeof(int?), typeof(long?), 7, 7L),
        R(typeof(long), typeof(double), 7L, 7.0),
        R(typeof(int), typeof(double), 7, 7.0),
        R(typeof(int), typeof(float), 7, 7f),
    }.ToDictionary(static row => Named(row, row.Shape == Shape.Parameter ? (row.Value is null ? " (null)" : "") : $" ({row.Shape})"));

    /// <summary>Rows that were green on the base (a handle value, an already-nullable argument, a word that
    /// <c>Convert.ChangeType</c> happens to convert, the null literal): pinned so the new argument rule keeps them.</summary>
    private static readonly Dictionary<string, Row> PinnedRows = new[] {
        R(typeof(TimeSpan), typeof(TimeSpan?), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)),
        R(typeof(Guid), typeof(Guid?), Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")),
        R(typeof(decimal), typeof(decimal?), 7m, 7m),
        R(typeof(DateTimeOffset), typeof(DateTimeOffset?), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
        R(typeof(DateTimeOffset), typeof(DateTimeOffset?), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, Shape.Property),
        R(typeof(long), typeof(long?), 7L, 7L),
        R(typeof(int), typeof(int?), 7, 7),
        R(typeof(bool), typeof(bool?), true, true),
        R(typeof(uint), typeof(uint?), uint.MaxValue, uint.MaxValue),
        R(typeof(char), typeof(char?), 'x', 'x'),
        R(typeof(long), typeof(decimal), 7L, 7m),
        R(typeof(int), typeof(long), 7, 7L),
        R(typeof(DateOnly?), typeof(DateOnly?), Day, Day),
        R(typeof(DateOnly?), typeof(DateOnly?), null, null),
        R(typeof(DateTimeOffset?), typeof(DateTimeOffset?), null, null),
        R(typeof(object), typeof(DateOnly?), null, null, Shape.NullLiteral),
        R(typeof(object), typeof(decimal?), null, null, Shape.NullLiteral),
        R(typeof(object), typeof(ByteMark?), null, null, Shape.NullLiteral),
        R(typeof(object), typeof(object), null, null, Shape.NullLiteral),
    }.ToDictionary(static row => Named(row, row.Shape == Shape.Parameter ? (row.Value is null ? " (null)" : "") : $" ({row.Shape})"));

    public static IEnumerable<string> RedRowNames() => RedRows.Keys;

    public static IEnumerable<string> PinnedRowNames() => PinnedRows.Keys;

    private static (AnalysisContext Context, Node Root) ModuleCallTree(Row row, Type? parameterOverride = null) {
        var argument = new Parameter("v", new ClrTypeReference(row.Argument));
        var taken = new Parameter("x", new ClrTypeReference(parameterOverride ?? row.Parameter));
        var asObject = new ClrTypeReference(typeof(object));
        var typeDef = new TypeDefinitionNode(Name: "ArgumentBoundaryBox",
            Constructors: [new ConstructorDefinitionNode([], new Block([]))],
            Properties: [new PropertyDefinitionNode("Held", new ClrTypeReference(row.Argument))],
            Methods: [
                new MethodDefinitionNode("Take", asObject, Parameters: [taken], Body: new Return(new TypeCast(taken, asObject))),
                new MethodDefinitionNode("Run", asObject, Parameters: [argument], Body: new Block([
                    new Assignment(new Member(new ThisReference(), "Held"), argument),
                    new Return(new Invoke(new Member(new ThisReference(), "Take"), row.Shape switch {
                        Shape.NullLiteral => new Constant(null),
                        Shape.ZeroLiteral => new Constant(0),
                        Shape.Property => new Member(new ThisReference(), "Held"),
                        Shape.TimesTwo => new Multiply(argument, new Constant(2L)),
                        _ => argument,
                    })),
                ])),
            ]);
        var context = AnalysisContext.CreateDefault();
        new TypeDefinitionNodeAnalyzer().Analyze(context, typeDef);
        var root = new Invoke(new Member(new New(new TypeReference("ArgumentBoundaryBox")), "Run"),
            new Parameter("v", new ClrTypeReference(row.Argument)));
        return (context, root);
    }

    private static object? ModuleCall(Row row) {
        var (context, root) = ModuleCallTree(row);
        using var exec = Interpreter.Execute(Interpreter.Compile(root, context.TypeDefinitions), s => s.SetArgs(new object?[] { row.Value }));
        return exec.Result.Value;
    }

    private static async Task AssertArrivesAsCSharpPasses(Row row) {
        var seen = ModuleCall(row);
        await Assert.That(seen).IsEqualTo(row.Expected);
        if (row.Expected is not null)
            await Assert.That(seen!.GetType()).IsEqualTo(row.Expected.GetType());
        // The oracle: C# binds this call. (The null literal and the arithmetic shape bind by the same parameter rule.)
        var csharp = row.Shape == Shape.NullLiteral
            ? CSharpCallOracle.BindsExpression("null", row.Parameter)
            : CSharpCallOracle.Binds(row.Shape == Shape.TimesTwo ? typeof(long) : row.Argument, row.Parameter);
        await Assert.That(csharp).IsTrue().Because($"C# binds {Named(row, "")}");
    }

    [Test]
    [MethodDataSource(nameof(RedRowNames))]
    public async Task ModuleArgument_Red_ArrivesAsCSharpPasses(string name) => await AssertArrivesAsCSharpPasses(RedRows[name]);

    [Test]
    [MethodDataSource(nameof(PinnedRowNames))]
    public async Task ModuleArgument_Pinned_StillArrivesAsCSharpPasses(string name) => await AssertArrivesAsCSharpPasses(PinnedRows[name]);

    /// <summary>The pairs C# refuses stay analysis errors: no member binds, so the call does not compile.</summary>
    [Test]
    [Arguments(typeof(double), typeof(decimal?))]
    [Arguments(typeof(long), typeof(int?))]
    [Arguments(typeof(ulong), typeof(long?))]
    [Arguments(typeof(decimal?), typeof(decimal))]
    [Arguments(typeof(int), typeof(DateOnly?))]
    [Arguments(typeof(double?), typeof(decimal?))]
    public async Task ModuleArgument_CSharpRefuses_StaysAnAnalysisError(Type argument, Type parameter) {
        await Assert.That(CSharpCallOracle.Binds(argument, parameter)).IsFalse();
        var (context, root) = ModuleCallTree(R(argument, parameter, null, null));
        await Assert.That(() => Interpreter.Compile(root, context.TypeDefinitions))
            .Throws<InvalidOperationException>().WithMessageContaining("no matching member 'Take'");
    }

    /// <summary>C# converts the constant 0 to any enum; Poly does not model constant-expression conversions, so a number
    /// into an enum parameter is refused (<c>LoneCandidate_NeedingARemovedConversion_HasNoOverload</c> pins the same).</summary>
    [Test]
    public async Task ModuleArgument_ConstantZeroIntoAnEnum_IsRefused_WhereCSharpAcceptsTheConstant() {
        await Assert.That(CSharpCallOracle.BindsExpression("0", typeof(IntMark))).IsTrue();
        await Assert.That(CSharpCallOracle.BindsExpression("0", typeof(IntMark?))).IsTrue();
        var (context, root) = ModuleCallTree(R(typeof(int), typeof(IntMark?), 0, null, Shape.ZeroLiteral));
        await Assert.That(() => Interpreter.Compile(root, context.TypeDefinitions)).Throws<InvalidOperationException>();
    }
}

public static class RepresentationProbe {
    public static int SeenAsObject(object value) => value is int i ? i : -1;
}

public static class EnumProbe {
    public enum Value { A = 40 }

    public static int Id(Value value) => (int)value;
}

public static class NullableProbe {
    public sealed class Box {
        public int? N { get; set; }
    }

    public static int Id(int? value) => value ?? -1;

    public static int? FiveOrNull(bool present) => present ? 5 : null;

    public static decimal? FiveDecimalOrNull(bool present) => present ? 5m : null;
}

public static class CtorProbe {
    public sealed class ObjectBox(object? value) {
        public string Text { get; } = value is null ? "<null>" : $"{value.GetType().Name}:{value}";
    }

    public sealed class DoubleBox(double value) {
        public string Text { get; } = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class DecimalBox(decimal value) {
        public string Text { get; } = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class NullableIntBox(int? value) {
        public string Text { get; } = value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>";
    }

    public sealed class BoolBox(bool value) {
        public string Text { get; } = value.ToString();
    }
}

public static class BoundaryProbe {
    public sealed class Taker {
        public string Seen { get; private set; } = "";

        public void Take(object? value) =>
            Seen = value is null ? "null" : $"{value.GetType().Name}:{Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}";
    }

    public static string Apply(Func<object, string> f) => f(7);

    public static string CallIt(Func<object> f) => Convert.ToString(f()) ?? "<null>";
}