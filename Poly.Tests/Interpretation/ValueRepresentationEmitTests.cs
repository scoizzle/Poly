using Poly.Interpretation;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
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
        var node = new Block([new Constant(1), new Comment("tail")]);
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
}

public static class RepresentationProbe {
    public static int SeenAsObject(object value) => value is int i ? i : -1;
}

public static class NullableProbe {
    public sealed class Box {
        public int? N { get; set; }
    }

    public static int Id(int? value) => value ?? -1;
}