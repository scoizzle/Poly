using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

public class ValueRepresentationTests {
    private static readonly Analyzer AnalyzerWithPass = new AnalyzerBuilder()
        .UseThisReferenceContext()
        .UseTypeAndMemberResolver()
        .UseVariableScopeValidator()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseConstantFolding()
        .UseControlFlowAnalysis()
        .UseDefiniteAssignmentAnalysis()
        .UseLambdaReturnTypeResolution()
        .UseValueRepresentationAnalysis()
        .Build();

    [Test]
    public async Task Block_PropagatesLastExpressionKind() {
        var node = new Block(new Constant(1), new Add(new Constant(2), new Constant(3)));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task IntConstant_IsStackScalar() {
        var node = new Constant(42);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task BoolConstant_IsBool() {
        var node = new Constant(true);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Bool);
    }

    [Test]
    public async Task StringConstant_IsHeapRef() {
        var node = new Constant("hello");
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task NullConstant_IsHeapRef() {
        var node = new Constant(null);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task Block_LastExpressionDeterminesKind() {
        var node = new Block(Wrap(1), Wrap(2));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Arithmetic_IsStackScalar() {
        var node = new Add(new Constant(1), new Constant(2));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Invoke_StoredLambdaReturningBool_IsBool() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(captured, new Constant(false)),
            new Assignment(fn, new Lambda([], captured)),
            new Assignment(captured, new Constant(true)),
            new Invoke(fn)
        ], [captured, fn]);
        var result = AnalyzerWithPass.Analyze(node);
        var invoke = (Invoke)node.Nodes[^1];
        var meta = result.GetMetadata<ValueRepresentationMetadata>(invoke);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Bool);
        var lambda = (Lambda)((Assignment)node.Nodes[1]).Value;
        var captures = result.GetMetadata<LambdaCaptureMetadata>(lambda);
        await Assert.That(captures).IsNotNull();
        await Assert.That(captures!.Bindings.Count).IsEqualTo(1);
        await Assert.That(captures.Bindings[0].Variable).IsSameReferenceAs(captured);
    }

    [Test]
    public async Task EqualityComparison_IsBool() {
        var node = new Equal(new Constant(1), new Constant(2));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Bool);
    }

    [Test]
    public async Task BooleanAnd_IsBool() {
        var node = new And(new Constant(true), new Constant(false));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Bool);
    }

    [Test]
    public async Task IfStatement_IsVoid() {
        var node = new IfStatement(new Constant(true), Wrap(1), Wrap(2));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Assignment_PropagatesRhsKind() {
        var x = new Variable("x");
        var node = new Assignment(x, new Constant(7L));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task VariableHoldingLambda_IsHeapRef() {
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(fn, new Lambda([], new Constant(true))),
            fn
        ], [fn]);
        var result = AnalyzerWithPass.Analyze(node);
        var varMeta = result.GetMetadata<ValueRepresentationMetadata>(fn);
        await Assert.That(varMeta).IsNotNull();
        await Assert.That(varMeta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
        var invoke = new Invoke(fn);
        var invokeBlock = new Block([
            new Assignment(fn, new Lambda([], new Constant(true))),
            invoke
        ], [fn]);
        var invokeResult = AnalyzerWithPass.Analyze(invokeBlock);
        var invokeMeta = invokeResult.GetMetadata<ValueRepresentationMetadata>(invoke);
        await Assert.That(invokeMeta).IsNotNull();
        await Assert.That(invokeMeta!.Kind).IsEqualTo(ValueRepresentationKind.Bool);
    }

    [Test]
    public async Task UsingStatement_IsVoid() {
        var node = new UsingStatement(new Constant("r"), new Constant(1L));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task TryCatchFinally_PropagatesTryKind() {
        var node = new TryCatchFinally(new Constant(3L), []);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task SwitchStatement_PropagatesCaseBodyKind() {
        var node = new SwitchStatement(
            new Constant(1L),
            [new SwitchCase(new Constant(1L), new Constant("hit"))],
            new Constant("miss"));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(meta.ClrType).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task DoWhileLoop_IsVoid() {
        var node = new DoWhileLoop(Wrap(1), new Constant(false));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task BreakContinueGoto_AreVoid() {
        var br = new BreakStatement();
        var cont = new ContinueStatement();
        var gt = new GotoStatement("x");
        await Assert.That(AnalyzerWithPass.Analyze(br).GetMetadata<ValueRepresentationMetadata>(br)!.Kind)
            .IsEqualTo(ValueRepresentationKind.Void);
        await Assert.That(AnalyzerWithPass.Analyze(cont).GetMetadata<ValueRepresentationMetadata>(cont)!.Kind)
            .IsEqualTo(ValueRepresentationKind.Void);
        await Assert.That(AnalyzerWithPass.Analyze(gt).GetMetadata<ValueRepresentationMetadata>(gt)!.Kind)
            .IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Lambda_Node_IsHeapRefFunctionTypeNotBodyType() {
        var lambda = new Lambda([], new Constant(true));
        var result = AnalyzerWithPass.Analyze(lambda);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(lambda);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(meta.ClrType).IsEqualTo(typeof(Func<bool>));
        await Assert.That(result.GetResolvedType(lambda)!.GetRuntimeType()).IsEqualTo(typeof(Func<bool>));
    }

    [Test]
    public async Task Lambda_WithParameter_IsFuncOfParamAndYield() {
        var x = new Parameter("x", TypeReference.To<long>());
        var lambda = new Lambda([x], new Add(x, new Constant(1L)));
        var result = AnalyzerWithPass.Analyze(lambda);
        await Assert.That(result.GetResolvedType(lambda)!.GetRuntimeType())
            .IsEqualTo(typeof(Func<long, long>));
    }

    [Test]
    public async Task IfStatement_HasNoResolvedType() {
        var node = new IfStatement(new Constant(true), Wrap(1), Wrap(2));
        var result = AnalyzerWithPass.Analyze(node);
        await Assert.That(result.GetResolvedType(node)).IsNull();
    }

    [Test]
    public async Task UsingStatement_HasNoResolvedType() {
        var node = new UsingStatement(new Constant("r"), new Constant(1L));
        var result = AnalyzerWithPass.Analyze(node);
        await Assert.That(result.GetResolvedType(node)).IsNull();
    }

    [Test]
    public async Task Block_EarlyReturn_HasReturnKindWhenLastIsVoid() {
        var node = new Block([
            new IfStatement(new Constant(true), new Return(new Constant(7L))),
            new Comment("after")
        ]);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Invoke_LambdaBlock_ReturnThenComment_IsStackScalar() {
        var invoke = new Invoke(new Lambda([], new Block([
            new IfStatement(new Constant(true), new Return(new Constant(7L))),
            new Comment("x")
        ])));
        var result = AnalyzerWithPass.Analyze(invoke);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(invoke);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Block_DominatingReturn_WinsOverDeadNonVoidTail() {
        var node = new Block([
            new IfStatement(new Constant(true), new Return(new Constant(7L))),
            new Constant("miss")
        ]);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task Block_IfFalseReturn_VoidFallthrough() {
        var node = new Block([
            new IfStatement(new Constant(false), new Return(new Constant(7L))),
            new Comment("fall")
        ]);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.IsVoid).IsTrue();
    }

    [Test]
    public async Task Block_LastNodeAssignment_IsRhsKind() {
        var x = new Variable("x");
        var assign = new Assignment(x, new Constant(7L));
        var node = new Block([assign], [x]);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Invoke_ReturnedClosure_HasInnerBodyKind() {
        var captured = new Variable("captured");
        var inner = new Variable("inner");
        var outer = new Variable("outer");
        var resultFn = new Variable("resultFn");
        var invokeResult = new Invoke(resultFn);
        var node = new Block([
            new Assignment(captured, new Constant(1L)),
            new Assignment(outer, new Lambda([], new Block([
                new Assignment(inner, new Lambda([], captured)),
                inner
            ], [inner]))),
            new Assignment(resultFn, new Invoke(outer)),
            invokeResult
        ], [captured, outer, resultFn]);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(invokeResult);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Invoke_Parameter_HasCalleeBodyKind() {
        var x = new Parameter("x", TypeReference.To<long>());
        var add1 = new Lambda([x], new Add(x, new Constant(1L)));
        var f = new Parameter("f");
        var invokeF = new Invoke(f, new Constant(41L));
        var apply = new Lambda([f], invokeF);
        var node = new Invoke(apply, add1);
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(invokeF);
        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        var rootMeta = result.GetMetadata<ValueRepresentationMetadata>(node);
        await Assert.That(rootMeta).IsNotNull();
        await Assert.That(rootMeta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task WhileLoop_IsVoid() {
        var node = new WhileLoop(new Constant(true), Wrap(1));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Not_IsBool() {
        var node = new Not(new Constant(true));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Bool);
    }

    [Test]
    public async Task UnaryMinus_IsStackScalar() {
        var node = new UnaryMinus(new Constant(5));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task ThrowStatement_IsVoid() {
        var node = new ThrowStatement(new Constant("error"));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Return_WithoutValue_IsVoid() {
        var node = new Return();
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Return_WithValue_PropagatesOperandKind() {
        var node = new Return(new Constant(42));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task NewArray_IsHeapRef() {
        var node = new NewArray(
            TypeReference.To<int>(),
            new Constant(10));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task Coalesce_IntConstants_IsStackScalar() {
        var node = new Coalesce(new Constant(42), new Constant(99));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Conditional_IntBranches_IsStackScalar() {
        var node = new Conditional(new Constant(true), new Constant(1), new Constant(2));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Add_ResolvedType_HasClrType() {
        var node = new Add(new Constant(1), new Constant(2));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(meta.ClrType).IsNotNull();
    }

    [Test]
    public async Task ForEachLoop_IsVoid() {
        var variable = new Variable("item");
        var node = new ForEachLoop(variable, new Constant(new int[0]), Wrap(1));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Conditional_HeapBranches_IsHeapRef() {
        var node = new Conditional(new Constant(true), new Constant("hello"), new Constant("world"));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task Coalesce_String_IsHeapRef() {
        var node = new Coalesce(new Constant("hello"), new Constant("default"));
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    private static Node Wrap(int value) => new Constant(value);

    [Test]
    public async Task Member_IntProperty_IsStackScalar() {
        var node = new Member(new Constant("hello"), "Length");
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(meta.ClrType).IsNotNull();
    }

    [Test]
    public async Task Member_RefReturningProperty_IsHeapRef() {
        var node = new Member(new Constant("hello"), "ToUpper");
        var result = AnalyzerWithPass.Analyze(node);
        var meta = result.GetMetadata<ValueRepresentationMetadata>(node);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Kind).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(meta.ClrType).IsEqualTo(typeof(string));
    }
}