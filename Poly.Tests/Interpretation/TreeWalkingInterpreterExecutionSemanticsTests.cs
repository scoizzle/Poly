using System.Collections.Generic;

using Poly.Interpretation.TreeWalking;

namespace Poly.Tests.Interpretation;

public class TreeWalkingInterpreterExecutionSemanticsTests {
    private sealed class SuspendReturningDefaultCompiler : ITreeWalkerCompiler {
        public bool TryEvaluate(
            Node node,
            Func<Node, InterpreterState, InterpreterResult> evaluateChild,
            InterpreterState state,
            out InterpreterResult result) {
            state.Suspend("Compiler requested suspend", node);
            result = default;
            return true;
        }
    }

    [Test]
    public async Task CustomCompiler_ReturningDefaultResult_CanSuspendWithoutCrashing() {
        var walker = new TreeWalkingInterpreter()
            .RegisterCompiler(new SuspendReturningDefaultCompiler());

        var result = walker.Evaluate(new Constant(7));

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsTypeOf<SuspendedExecution>();

        var suspended = (SuspendedExecution)result.Value!;
        await Assert.That(suspended.Reason).IsEqualTo("Compiler requested suspend");
    }

    [Test]
    public async Task AndOperator_LeftFalse_ShortCircuitsRightOperand() {
        var ast = new And(
            new Constant(false),
            new SuspendNode(new Constant(true), "should-not-run"));

        var walker = new TreeWalkingInterpreter();
        var result = walker.Evaluate(ast);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((bool)result.Value!).IsFalse();
        await Assert.That(result.Value).IsNotTypeOf<SuspendedExecution>();
    }

    [Test]
    public async Task OrOperator_LeftTrue_ShortCircuitsRightOperand() {
        var ast = new Or(
            new Constant(true),
            new SuspendNode(new Constant(false), "should-not-run"));

        var walker = new TreeWalkingInterpreter();
        var result = walker.Evaluate(ast);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That((bool)result.Value!).IsTrue();
        await Assert.That(result.Value).IsNotTypeOf<SuspendedExecution>();
    }

    [Test]
    public async Task Conditional_WithNonBooleanCondition_ReturnsVoid() {
        var walker = new TreeWalkingInterpreter();
        var result = walker.Evaluate(new Conditional(new Constant(123), new Constant(1), new Constant(2)));

        await Assert.That(result.IsVoid).IsTrue();
    }

    [Test]
    public async Task NotOperator_WithNonBooleanOperand_ReturnsVoid() {
        var walker = new TreeWalkingInterpreter();
        var result = walker.Evaluate(new Not(new Constant("not-a-bool")));

        await Assert.That(result.IsVoid).IsTrue();
    }

    [Test]
    public async Task TypeCast_CurrentBehavior_ReturnsOperandValueWithoutConversion() {
        var walker = new TreeWalkingInterpreter();
        var ast = new TypeCast(new Constant("123"), TypeReference.To<int>());

        var result = walker.Evaluate(ast);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsTypeOf<string>();
        await Assert.That(result.Value).IsEqualTo("123");
    }

    [Test]
    public async Task TypeIs_CurrentBehavior_ChecksOnlyNullability() {
        var walker = new TreeWalkingInterpreter();

        var nonNullResult = walker.Evaluate(new TypeIs(new Constant("text"), TypeReference.To<int>()));
        var nullResult = walker.Evaluate(new TypeIs(new Constant(null), TypeReference.To<string>()));

        await Assert.That(nonNullResult.HasValue).IsTrue();
        await Assert.That((bool)nonNullResult.Value!).IsTrue();
        await Assert.That(nullResult.HasValue).IsTrue();
        await Assert.That((bool)nullResult.Value!).IsFalse();
    }

    [Test]
    public async Task WhileLoop_WithReturnSignal_PropagatesReturnValue() {
        var walker = new TreeWalkingInterpreter();
        var ast = new WhileLoop(new Constant(true), new Return(new Constant(42)));

        var result = walker.Evaluate(ast);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(42);
    }

    [Test]
    public async Task ForLoop_WithReturnSignal_PropagatesReturnValue() {
        var walker = new TreeWalkingInterpreter();
        var ast = new ForLoop(
            null,
            new Constant(true),
            null,
            new Return(new Constant(17)));

        var result = walker.Evaluate(ast);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(17);
    }

    [Test]
    public async Task ForLoop_WhenConditionFalse_DoesNotExecuteBody() {
        var x = new Variable("x");
        var ast = new Block([
            new Assignment(x, new Constant(0)),
            new ForLoop(
                null,
                new Constant(false),
                null,
                new Assignment(x, new Constant(99))),
            x
        ]);

        var walker = new TreeWalkingInterpreter();
        var result = walker.Evaluate(ast);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(0);
    }

    [Test]
    public async Task RuntimeMemberAccess_MissingMember_ReturnsVoid() {
        var target = new Variable("target");
        var ast = new Member(target, "Missing");

        var precomputed = new AnalyzerBuilder().Build().Analyze(ast);
        var walker = new TreeWalkingInterpreter(precomputed);

        var result = walker.Evaluate(ast, new Dictionary<string, object?> {
            ["target"] = new { Existing = 1 }
        });

        await Assert.That(result.IsVoid).IsTrue();
    }

    [Test]
    public async Task RuntimeIndexAccess_MissingDictionaryKey_ReturnsNullValue() {
        var target = new Variable("target");
        var ast = new IndexAccess(target, new Constant("missing"));

        var precomputed = new AnalyzerBuilder().Build().Analyze(ast);
        var walker = new TreeWalkingInterpreter(precomputed);

        var result = walker.Evaluate(ast, new Dictionary<string, object?> {
            ["target"] = new Dictionary<string, int> {
                ["present"] = 10
            }
        });

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsNull();
    }

    [Test]
    public async Task Assignment_WithUnsupportedDestination_ReturnsVoid() {
        var ast = new Assignment(new Constant(1), new Constant(2));
        var precomputed = new AnalyzerBuilder().Build().Analyze(ast);
        var walker = new TreeWalkingInterpreter(precomputed);

        var result = walker.Evaluate(ast);

        await Assert.That(result.IsVoid).IsTrue();
    }

    [Test]
    public async Task Add_WithNullOperand_ThrowsInvalidOperationException() {
        var walker = new TreeWalkingInterpreter();
        var ast = new Add(new Constant(null), new Constant(1));

        await Assert.That(() => walker.Evaluate(ast)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Divide_ByZero_ThrowsDivideByZeroException() {
        var walker = new TreeWalkingInterpreter();
        var ast = new Divide(new Constant(10), new Constant(0));

        await Assert.That(() => walker.Evaluate(ast)).Throws<DivideByZeroException>();
    }

    [Test]
    public async Task Assignment_IndexArgumentWithoutValue_ThrowsInvalidOperationException() {
        var target = new Parameter("target", TypeReference.To<List<int>>());
        var ast = new Assignment(
            new IndexAccess(target, new Variable("missing")),
            new Constant(5));

        var precomputed = new AnalyzerBuilder().Build().Analyze(ast);
        var walker = new TreeWalkingInterpreter(precomputed);

        var ex = await Assert.That(() => walker.Evaluate(ast, new Dictionary<string, object?> {
            ["target"] = new List<int> { 1, 2, 3 }
        })).Throws<InvalidOperationException>();

        await Assert.That(ex!.Message).Contains("Index argument did not produce a value");
    }

    [Test]
    public async Task RuntimeMemberAccess_NullOwner_ReturnsVoid() {
        var target = new Parameter("target", TypeReference.To<string>());
        var ast = new Member(target, "Length");
        var precomputed = new AnalyzerBuilder().Build().Analyze(ast);
        var walker = new TreeWalkingInterpreter(precomputed);

        var result = walker.Evaluate(ast, new Dictionary<string, object?> {
            ["target"] = null
        });

        await Assert.That(result.IsVoid).IsTrue();
    }

    [Test]
    public async Task RuntimeIndexAccess_ArrayAndList_ReturnExpectedValue() {
        var arrayTarget = new Variable("arr");
        var listTarget = new Variable("list");
        var arrayAst = new IndexAccess(arrayTarget, new Constant(2));
        var listAst = new IndexAccess(listTarget, new Constant(1));

        var arrayAnalysis = new AnalyzerBuilder().Build().Analyze(arrayAst);
        var listAnalysis = new AnalyzerBuilder().Build().Analyze(listAst);

        var arrayWalker = new TreeWalkingInterpreter(arrayAnalysis);
        var listWalker = new TreeWalkingInterpreter(listAnalysis);

        var arrayResult = arrayWalker.Evaluate(arrayAst, new Dictionary<string, object?> {
            ["arr"] = new[] { 5, 6, 7 }
        });

        var listResult = listWalker.Evaluate(listAst, new Dictionary<string, object?> {
            ["list"] = new List<int> { 11, 12, 13 }
        });

        await Assert.That(arrayResult.HasValue).IsTrue();
        await Assert.That(arrayResult.Value).IsEqualTo(7);
        await Assert.That(listResult.HasValue).IsTrue();
        await Assert.That(listResult.Value).IsEqualTo(12);
    }
}