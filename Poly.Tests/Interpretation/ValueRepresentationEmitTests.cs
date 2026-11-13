using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Interpretation.Vm.Abi;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Representation is the effective node's resolved CLR type: a replacement carries
/// the real runtime action's type, and emit classifies that type. These tests run
/// the standard pipeline WITHOUT a representation pass, so a green test proves emit
/// derives the slot kind itself.
/// </summary>
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
    /// with no such operator is refused at compile, naming the type, where the VM used to negate the handle.
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
            "Decimal -0.1",
            "Decimal -0.1",
            "null",
            $"TimeSpan {-TimeSpan.FromHours(1)}",
            $"TimeSpan {-TimeSpan.FromHours(1)}",
            "Int128 -5",
            "Int128 -6",
            $"UInt128 {~(UInt128)5}",
            "BigInteger -5",
            "Int32 -5",
            "null",
            "Int32 -6",
            "Decimal -5",
            "VM compile rejected: no unary '-' operator for 'Guid'.",
            "VM compile rejected: no unary '-' operator for 'DateTimeOffset'.",
            "VM compile rejected: no unary '~' operator for 'Guid'.",
        }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
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