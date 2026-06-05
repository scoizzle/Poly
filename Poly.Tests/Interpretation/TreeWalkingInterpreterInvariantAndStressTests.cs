using System.Linq.Expressions;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.TreeWalking;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

public class TreeWalkingInterpreterInvariantAndStressTests {
    [Test]
    public async Task BooleanOperator_MatrixEvaluation_MatchesTruthTables() {
        var walker = new TreeWalkingInterpreter();

        var cases = new (bool Left, bool Right)[] {
            (false, false),
            (false, true),
            (true, false),
            (true, true)
        };

        foreach (var item in cases) {
            var andResult = walker.Evaluate(new And(new Constant(item.Left), new Constant(item.Right)));
            var orResult = walker.Evaluate(new Or(new Constant(item.Left), new Constant(item.Right)));

            await Assert.That(andResult.HasValue).IsTrue();
            await Assert.That(orResult.HasValue).IsTrue();
            await Assert.That((bool)andResult.Value!).IsEqualTo(item.Left && item.Right);
            await Assert.That((bool)orResult.Value!).IsEqualTo(item.Left || item.Right);
        }
    }

    [Test]
    public async Task ComparisonOperator_MatrixEvaluation_MatchesExpectedRelations() {
        var walker = new TreeWalkingInterpreter();
        var values = new[] { -2, -1, 0, 1, 2 };

        foreach (var left in values) {
            foreach (var right in values) {
                var eq = walker.Evaluate(new Equal(new Constant(left), new Constant(right)));
                var ne = walker.Evaluate(new NotEqual(new Constant(left), new Constant(right)));
                var lt = walker.Evaluate(new LessThan(new Constant(left), new Constant(right)));
                var le = walker.Evaluate(new LessThanOrEqual(new Constant(left), new Constant(right)));
                var gt = walker.Evaluate(new GreaterThan(new Constant(left), new Constant(right)));
                var ge = walker.Evaluate(new GreaterThanOrEqual(new Constant(left), new Constant(right)));

                await Assert.That((bool)eq.Value!).IsEqualTo(left == right);
                await Assert.That((bool)ne.Value!).IsEqualTo(left != right);
                await Assert.That((bool)lt.Value!).IsEqualTo(left < right);
                await Assert.That((bool)le.Value!).IsEqualTo(left <= right);
                await Assert.That((bool)gt.Value!).IsEqualTo(left > right);
                await Assert.That((bool)ge.Value!).IsEqualTo(left >= right);
            }
        }
    }

    [Test]
    public async Task ArithmeticTree_RandomizedAgainstLinqExpression_MatchesForManyCases() {
        var random = new Random(1337);
        var walker = new TreeWalkingInterpreter();

        for (int i = 0; i < 250; i++) {
            var ast = BuildRandomArithmeticTree(random, depth: 4);

            var treeWalkerResult = walker.Evaluate(ast);
            await Assert.That(treeWalkerResult.HasValue).IsTrue();

            var expression = ast.BuildExpression();
            var compiled = Expression.Lambda<Func<int>>(expression).Compile();
            var linqResult = compiled();

            await Assert.That((int)treeWalkerResult.Value!).IsEqualTo(linqResult);
        }
    }

    [Test]
    public async Task ArithmeticMetamorphic_AddAndMultiply_AreCommutative() {
        var random = new Random(4242);
        var walker = new TreeWalkingInterpreter();

        for (int i = 0; i < 250; i++) {
            var a = random.Next(-100, 101);
            var b = random.Next(-100, 101);

            var addLeft = walker.Evaluate(new Add(new Constant(a), new Constant(b)));
            var addRight = walker.Evaluate(new Add(new Constant(b), new Constant(a)));
            var mulLeft = walker.Evaluate(new Multiply(new Constant(a), new Constant(b)));
            var mulRight = walker.Evaluate(new Multiply(new Constant(b), new Constant(a)));

            await Assert.That(addLeft.HasValue).IsTrue();
            await Assert.That(addRight.HasValue).IsTrue();
            await Assert.That(mulLeft.HasValue).IsTrue();
            await Assert.That(mulRight.HasValue).IsTrue();

            await Assert.That(addLeft.Value).IsEqualTo(addRight.Value);
            await Assert.That(mulLeft.Value).IsEqualTo(mulRight.Value);
        }
    }

    [Test]
    public async Task ArithmeticMetamorphic_AddAndMultiply_AreAssociativeForSafeRange() {
        var random = new Random(777);
        var walker = new TreeWalkingInterpreter();

        for (int i = 0; i < 150; i++) {
            var a = random.Next(-20, 21);
            var b = random.Next(-20, 21);
            var c = random.Next(-20, 21);

            var addGroupedLeft = walker.Evaluate(new Add(new Add(new Constant(a), new Constant(b)), new Constant(c)));
            var addGroupedRight = walker.Evaluate(new Add(new Constant(a), new Add(new Constant(b), new Constant(c))));

            var mulGroupedLeft = walker.Evaluate(new Multiply(new Multiply(new Constant(a), new Constant(b)), new Constant(c)));
            var mulGroupedRight = walker.Evaluate(new Multiply(new Constant(a), new Multiply(new Constant(b), new Constant(c))));

            await Assert.That(addGroupedLeft.Value).IsEqualTo(addGroupedRight.Value);
            await Assert.That(mulGroupedLeft.Value).IsEqualTo(mulGroupedRight.Value);
        }
    }

    [Test]
    public async Task SuspendResumeWithRefinedAnalysis_ManyCheckpoints_CompletesDeterministically() {
        var sum = new Variable("sum");
        var nodes = new List<Node> {
            new Assignment(sum, new Constant(0))
        };

        for (int i = 1; i <= 40; i++) {
            nodes.Add(new SuspendNode(new Constant(i), $"checkpoint-{i}"));
            nodes.Add(new Assignment(sum, new Add(sum, new Constant(i))));
        }

        nodes.Add(sum);
        var ast = new Block(nodes);
        var walker = new TreeWalkingInterpreter();

        var result = walker.Evaluate(ast);
        var suspendCount = 0;

        while (result.HasValue && result.Value is SuspendedExecution) {
            suspendCount++;

            var refined = new AnalyzerBuilder()
                .UseTypeResolver()
                .UseMemberResolver()
                .UseVariableScopeValidator()
                .UseSideEffectAnalysis()
                .UseConstantFolding()
                .Build()
                .Analyze(ast);

            result = walker.Resume(refined);
        }

        await Assert.That(suspendCount).IsEqualTo(40);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(820);
    }

    [Test]
    public async Task EvaluateRepeatedly_SameAst_ProducesIdenticalResult() {
        var ast = new Block([
            new Add(new Constant(1), new Constant(2)),
            new Multiply(new Constant(3), new Constant(4)),
            new Add(new Constant(10), new Constant(5))
        ]);

        var walker = new TreeWalkingInterpreter();
        var expected = walker.Evaluate(ast);

        await Assert.That(expected.HasValue).IsTrue();

        for (int i = 0; i < 100; i++) {
            var result = walker.Evaluate(ast);
            await Assert.That(result.HasValue).IsTrue();
            await Assert.That(result.Value).IsEqualTo(expected.Value);
        }
    }

    [Test]
    public async Task BreakpointStress_ManyBreakpoints_HitsEachAndCompletes() {
        var accumulator = new Variable("acc");
        var watchNodes = new List<Assignment>();
        var body = new List<Node> {
            new Assignment(accumulator, new Constant(0))
        };

        for (int i = 0; i < 30; i++) {
            var assignment = new Assignment(
                accumulator,
                new Add(accumulator, new Constant(i + 1)));

            watchNodes.Add(assignment);
            body.Add(assignment);
        }

        body.Add(accumulator);
        var ast = new Block(body);

        var walker = new TreeWalkingInterpreter();
        foreach (var n in watchNodes) {
            walker.BreakOn(n);
        }

        var seen = 0;
        var result = walker.Evaluate(ast);
        while (result.HasValue && result.Value is SuspendedExecution suspended) {
            await Assert.That(suspended.AtNode).IsTypeOf<Assignment>();
            seen++;
            result = walker.Resume();
        }

        await Assert.That(seen).IsEqualTo(30);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(465);
    }

    private static Node BuildRandomArithmeticTree(Random random, int depth) {
        if (depth <= 0) {
            return new Constant(random.Next(-50, 51));
        }

        var left = BuildRandomArithmeticTree(random, depth - 1);
        var right = BuildRandomArithmeticTree(random, depth - 1);

        return random.Next(0, 3) switch {
            0 => new Add(left, right),
            1 => new Subtract(left, right),
            _ => new Multiply(left, right)
        };
    }
}