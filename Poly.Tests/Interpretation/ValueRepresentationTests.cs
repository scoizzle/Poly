using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Vm;
using Poly.Introspection;

namespace Poly.Tests.Interpretation;

public class ValueRepresentationTests {
    private static readonly Analyzer Analyzer = new AnalyzerBuilder()
        .UseThisReferenceContext()
        .UseVariableScopeValidator()
        .UseTypeAndMemberResolver()
        .UseSideEffectAnalysis()
        .UseJumpTargetResolution()
        .UseConstantFolding()
        .UseControlFlowAnalysis()
        .UseDefiniteAssignmentAnalysis()
        .UseLambdaReturnTypeResolution()
        .Build();

    [Test]
    public async Task Block_PropagatesLastExpressionKind() {
        var node = new Block(new Constant(1), new Add(new Constant(2), new Constant(3)));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task IntConstant_IsStackScalar() {
        var node = new Constant(42);
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task BoolConstant_IsWordWithBoolClrType() {
        var node = new Constant(true);
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(clr).IsEqualTo(typeof(bool));
    }

    [Test]
    public async Task StringConstant_IsHeapRef() {
        var node = new Constant("hello");
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task NullConstant_IsHeapRef() {
        var node = new Constant(null);
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task Block_LastExpressionDeterminesKind() {
        var node = new Block(Wrap(1), Wrap(2));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Arithmetic_IsStackScalar() {
        var node = new Add(new Constant(1), new Constant(2));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Invoke_StoredLambdaReturningBool_IsBoolWord() {
        var captured = new Variable("captured");
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(captured, new Constant(false)),
            new Assignment(fn, new Lambda([], captured)),
            new Assignment(captured, new Constant(true)),
            new Invoke(fn)
        ], [captured, fn]);
        var result = Analyzer.Analyze(node);
        var invoke = (Invoke)node.Nodes[^1];
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, invoke);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(clr).IsEqualTo(typeof(bool));
        var lambda = (Lambda)((Assignment)node.Nodes[1]).Value;
        var captures = result.GetMetadata<LambdaCaptureMetadata>(lambda);
        await Assert.That(captures).IsNotNull();
        await Assert.That(captures!.Bindings.Count).IsEqualTo(1);
        await Assert.That(captures.Bindings[0].Variable).IsSameReferenceAs(captured);
    }

    [Test]
    public async Task EqualityComparison_IsBoolWord() {
        var node = new Equal(new Constant(1), new Constant(2));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(clr).IsEqualTo(typeof(bool));
    }

    [Test]
    public async Task BooleanAnd_IsBoolWord() {
        var node = new And(new Constant(true), new Constant(false));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(clr).IsEqualTo(typeof(bool));
    }

    [Test]
    public async Task IfStatement_IsVoid() {
        var cond = new Parameter("cond", TypeReference.To<bool>());
        var node = new IfStatement(cond, Wrap(1), Wrap(2));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Assignment_PropagatesRhsKind() {
        var x = new Variable("x");
        var node = new Assignment(x, new Constant(7L));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task VariableHoldingLambda_IsHeapRef() {
        var fn = new Variable("fn");
        var node = new Block([
            new Assignment(fn, new Lambda([], new Constant(true))),
            fn
        ], [fn]);
        var result = Analyzer.Analyze(node);
        var (varKind, varClr) = ValueRepresentationClassifier.Classify(result, fn);
        await Assert.That(varKind).IsEqualTo(ValueRepresentationKind.HeapRef);
        var invoke = new Invoke(fn);
        var invokeBlock = new Block([
            new Assignment(fn, new Lambda([], new Constant(true))),
            invoke
        ], [fn]);
        var invokeResult = Analyzer.Analyze(invokeBlock);
        var (invokeKind, invokeClr) = ValueRepresentationClassifier.Classify(invokeResult, invoke);
        await Assert.That(invokeKind).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(invokeClr).IsEqualTo(typeof(bool));
    }

    [Test]
    public async Task UsingStatement_IsVoid() {
        var node = new UsingStatement(new Constant("r"), new Constant(1L));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task TryCatchFinally_PropagatesTryKind() {
        var node = new TryCatchFinally(new Constant(3L), []);
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task SwitchStatement_PropagatesCaseBodyKind() {
        var node = new SwitchStatement(
            new Constant(1L),
            [new SwitchCase(new Constant(1L), new Constant("hit"))],
            new Constant("miss"));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(clr).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task DoWhileLoop_IsVoid() {
        var node = new DoWhileLoop(Wrap(1), new Constant(false));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task BreakContinueGoto_AreVoid() {
        var br = new BreakStatement();
        var cont = new ContinueStatement();
        var gt = new GotoStatement("x");
        await Assert.That(ValueRepresentationClassifier.KindOf(Analyzer.Analyze(br), br))
            .IsEqualTo(ValueRepresentationKind.Void);
        await Assert.That(ValueRepresentationClassifier.KindOf(Analyzer.Analyze(cont), cont))
            .IsEqualTo(ValueRepresentationKind.Void);
        await Assert.That(ValueRepresentationClassifier.KindOf(Analyzer.Analyze(gt), gt))
            .IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Lambda_Node_IsHeapRefFunctionTypeNotBodyType() {
        var lambda = new Lambda([], new Constant(true));
        var result = Analyzer.Analyze(lambda);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, lambda);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(clr).IsEqualTo(typeof(Func<bool>));
        await Assert.That(result.GetResolvedType(lambda)!.GetRuntimeType()).IsEqualTo(typeof(Func<bool>));
    }

    [Test]
    public async Task Lambda_WithParameter_IsFuncOfParamAndYield() {
        var x = new Parameter("x", TypeReference.To<long>());
        var lambda = new Lambda([x], new Add(x, new Constant(1L)));
        var result = Analyzer.Analyze(lambda);
        await Assert.That(result.GetResolvedType(lambda)!.GetRuntimeType())
            .IsEqualTo(typeof(Func<long, long>));
    }

    [Test]
    public async Task IfStatement_HasNoResolvedType() {
        var cond = new Parameter("cond", TypeReference.To<bool>());
        var node = new IfStatement(cond, Wrap(1), Wrap(2));
        var result = Analyzer.Analyze(node);
        await Assert.That(result.GetResolvedType(node)).IsNull();
    }

    [Test]
    public async Task UsingStatement_HasNoResolvedType() {
        var node = new UsingStatement(new Constant("r"), new Constant(1L));
        var result = Analyzer.Analyze(node);
        await Assert.That(result.GetResolvedType(node)).IsNull();
    }

    [Test]
    public async Task Block_EarlyReturn_HasReturnKindWhenLastIsVoid() {
        var node = new Block([
            new IfStatement(new Constant(true), new Return(new Constant(7L))),
            new Comment("after")
        ]);
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Invoke_LambdaBlock_ReturnThenComment_IsStackScalar() {
        var invoke = new Invoke(new Lambda([], new Block([
            new IfStatement(new Constant(true), new Return(new Constant(7L))),
            new Comment("x")
        ])));
        var result = Analyzer.Analyze(invoke);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, invoke);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Block_DominatingReturn_WinsOverDeadNonVoidTail() {
        var node = new Block([
            new IfStatement(new Constant(true), new Return(new Constant(7L))),
            new Constant("miss")
        ]);
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.GetValue<long>()).IsEqualTo(7L);
    }

    [Test]
    public async Task Block_IfFalseReturn_VoidFallthrough() {
        var node = new Block([
            new IfStatement(new Constant(false), new Return(new Constant(7L))),
            new Comment("fall")
        ]);
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.Void);
        using var exec = Interpreter.Execute(Interpreter.Compile(node));
        await Assert.That(exec.Result.IsVoid).IsTrue();
    }

    [Test]
    public async Task Block_LastNodeAssignment_IsRhsKind() {
        var x = new Variable("x");
        var assign = new Assignment(x, new Constant(7L));
        var node = new Block([assign], [x]);
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
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
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, invokeResult);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Invoke_Parameter_HasCalleeBodyKind() {
        var x = new Parameter("x", TypeReference.To<long>());
        var add1 = new Lambda([x], new Add(x, new Constant(1L)));
        var f = new Parameter("f");
        var invokeF = new Invoke(f, new Constant(41L));
        var apply = new Lambda([f], invokeF);
        var node = new Invoke(apply, add1);
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, invokeF);
        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        var (rootKind, rootClr) = ValueRepresentationClassifier.Classify(result, node);
        await Assert.That(rootKind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task WhileLoop_IsVoid() {
        var node = new WhileLoop(new Constant(true), Wrap(1));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Not_IsBoolWord() {
        var node = new Not(new Constant(true));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(clr).IsEqualTo(typeof(bool));
    }

    [Test]
    public async Task UnaryMinus_IsStackScalar() {
        var node = new UnaryMinus(new Constant(5));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task ThrowStatement_IsVoid() {
        var node = new ThrowStatement(new Constant("error"));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Return_WithoutValue_IsVoid() {
        var node = new Return();
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Return_WithValue_PropagatesOperandKind() {
        var node = new Return(new Constant(42));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task NewArray_IsHeapRef() {
        var node = new NewArray(
            TypeReference.To<int>(),
            new Constant(10));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task Coalesce_IntConstants_IsStackScalar() {
        var node = new Coalesce(new Constant(42), new Constant(99));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Conditional_IntBranches_IsStackScalar() {
        var node = new Conditional(new Constant(true), new Constant(1), new Constant(2));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
    }

    [Test]
    public async Task Add_ResolvedType_HasClrType() {
        var node = new Add(new Constant(1), new Constant(2));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(clr).IsNotNull();
    }

    [Test]
    public async Task ForEachLoop_IsVoid() {
        var variable = new Variable("item");
        var node = new ForEachLoop(variable, new Constant(new int[0]), Wrap(1));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.Void);
    }

    [Test]
    public async Task Conditional_HeapBranches_IsHeapRef() {
        var node = new Conditional(new Constant(true), new Constant("hello"), new Constant("world"));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    [Test]
    public async Task Coalesce_String_IsHeapRef() {
        var node = new Coalesce(new Constant("hello"), new Constant("default"));
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.HeapRef);
    }

    private static Node Wrap(int value) => new Constant(value);

    [Test]
    public async Task Member_IntProperty_IsStackScalar() {
        var node = new Member(new Constant("hello"), "Length");
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.StackScalar);
        await Assert.That(clr).IsNotNull();
    }

    [Test]
    public async Task Member_RefReturningProperty_IsHeapRef() {
        var node = new Member(new Constant("hello"), "ToUpper");
        var result = Analyzer.Analyze(node);
        var (kind, clr) = ValueRepresentationClassifier.Classify(result, node);

        await Assert.That(kind).IsEqualTo(ValueRepresentationKind.HeapRef);
        await Assert.That(clr).IsEqualTo(typeof(string));
    }
}