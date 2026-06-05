using System.Linq.Expressions;

using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Interpretation.TreeWalking;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

public class TreeWalkingInterpreterFuzzAndCrossEngineTests {
    [Test]
    public async Task GrammarDrivenFuzz_Seeded_IntExpressions_MatchTreeWalkingAndLinq() {
        var seeds = new[] { 7, 42, 1337, 20260603 };
        const int samplesPerSeed = 60;

        foreach (var seed in seeds) {
            var randomA = new Random(seed);
            var randomB = new Random(seed);

            for (int i = 0; i < samplesPerSeed; i++) {
                var astA = GenerateIntExpression(randomA, depth: 5);
                var astB = GenerateIntExpression(randomB, depth: 5);

                await Assert.That(astA.ToString()).IsEqualTo(astB.ToString());

                var walker = new TreeWalkingInterpreter();
                var tw = walker.Evaluate(astA);
                await Assert.That(tw.HasValue).IsTrue();

                var expression = astA.BuildExpression();
                var compiled = Expression.Lambda<Func<int>>(expression).Compile();
                var linq = compiled();

                await Assert.That((int)tw.Value!).IsEqualTo(linq);
            }
        }
    }

    [Test]
    public async Task SuspendResumeReanalyze_Soak_1200Cycles_CompletesDeterministically() {
        const int cycles = 1200;
        const int reanalyzeEvery = 25;

        var acc = new Variable("acc");
        var nodes = new List<Node> {
            new Assignment(acc, new Constant(0))
        };

        for (int i = 1; i <= cycles; i++) {
            nodes.Add(new SuspendNode(new Constant(i), $"cycle-{i}"));
            nodes.Add(new Assignment(acc, new Add(acc, new Constant(i))));
        }

        nodes.Add(acc);
        var ast = new Block(nodes);

        var analyzer = new AnalyzerBuilder()
            .UseTypeResolver()
            .UseMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseConstantFolding()
            .Build();

        var walker = new TreeWalkingInterpreter();
        var result = walker.Evaluate(ast);

        var suspendCount = 0;
        while (result.HasValue && result.Value is SuspendedExecution) {
            suspendCount++;

            if (suspendCount % reanalyzeEvery == 0) {
                var refined = analyzer.Analyze(ast);
                result = walker.Resume(refined);
            }
            else {
                result = walker.Resume();
            }
        }

        await Assert.That(suspendCount).IsEqualTo(cycles);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(cycles * (cycles + 1) / 2);
    }

    [Test]
    public async Task CrossEngineInvariant_RandomPrograms_ConsistentWithGeneratedCSharpStructure() {
        var random = new Random(9001);
        var csharpGenerator = new CSharpGenerator();
        var walker = new TreeWalkingInterpreter();

        for (int i = 0; i < 160; i++) {
            var ast = GenerateIntExpression(random, depth: 5);

            var tw = walker.Evaluate(ast);
            await Assert.That(tw.HasValue).IsTrue();

            var expression = ast.BuildExpression();
            var linq = Expression.Lambda<Func<int>>(expression).Compile()();
            await Assert.That((int)tw.Value!).IsEqualTo(linq);

            var generated = csharpGenerator.Generate(ast);
            await Assert.That(string.IsNullOrWhiteSpace(generated)).IsFalse();
            await Assert.That(generated.Contains(';')).IsTrue();

            ValidateGeneratedCSharpTokens(ast, generated);
        }
    }

    private static Node GenerateIntExpression(Random random, int depth) {
        if (depth <= 0) {
            return new Constant(random.Next(-40, 41));
        }

        return random.Next(0, 6) switch {
            0 => new Add(GenerateIntExpression(random, depth - 1), GenerateIntExpression(random, depth - 1)),
            1 => new Subtract(GenerateIntExpression(random, depth - 1), GenerateIntExpression(random, depth - 1)),
            2 => new Multiply(GenerateIntExpression(random, depth - 1), GenerateIntExpression(random, depth - 1)),
            3 => new UnaryMinus(GenerateIntExpression(random, depth - 1)),
            4 => new Conditional(
                GenerateBoolExpression(random, depth - 1),
                GenerateIntExpression(random, depth - 1),
                GenerateIntExpression(random, depth - 1)),
            _ => new Block([
                GenerateIntExpression(random, depth - 1),
                GenerateIntExpression(random, depth - 1)
            ])
        };
    }

    private static Node GenerateBoolExpression(Random random, int depth) {
        if (depth <= 0) {
            return new Constant(random.Next(0, 2) == 1);
        }

        return random.Next(0, 7) switch {
            0 => new LessThan(GenerateIntExpression(random, depth - 1), GenerateIntExpression(random, depth - 1)),
            1 => new GreaterThan(GenerateIntExpression(random, depth - 1), GenerateIntExpression(random, depth - 1)),
            2 => new Equal(GenerateIntExpression(random, depth - 1), GenerateIntExpression(random, depth - 1)),
            3 => new NotEqual(GenerateIntExpression(random, depth - 1), GenerateIntExpression(random, depth - 1)),
            4 => new And(GenerateBoolExpression(random, depth - 1), GenerateBoolExpression(random, depth - 1)),
            5 => new Or(GenerateBoolExpression(random, depth - 1), GenerateBoolExpression(random, depth - 1)),
            _ => new Not(GenerateBoolExpression(random, depth - 1))
        };
    }

    private static void ValidateGeneratedCSharpTokens(Node node, string generated) {
        var stack = new Stack<Node>();
        stack.Push(node);

        while (stack.Count > 0) {
            var current = stack.Pop();

            switch (current) {
                case Add:
                    AssertToken(generated, "+");
                    break;
                case Subtract:
                    AssertToken(generated, "-");
                    break;
                case Multiply:
                    AssertToken(generated, "*");
                    break;
                case LessThan:
                    AssertToken(generated, "<");
                    break;
                case GreaterThan:
                    AssertToken(generated, ">");
                    break;
                case Equal:
                    AssertToken(generated, "==");
                    break;
                case NotEqual:
                    AssertToken(generated, "!=");
                    break;
                case And:
                    AssertToken(generated, "&&");
                    break;
                case Or:
                    AssertToken(generated, "||");
                    break;
                case Not:
                    AssertToken(generated, "!");
                    break;
                case Conditional:
                    AssertToken(generated, "?");
                    AssertToken(generated, ":");
                    break;
            }

            foreach (var child in current.Children) {
                if (child is not null) {
                    stack.Push(child);
                }
            }
        }
    }

    private static void AssertToken(string generated, string token) {
        if (!generated.Contains(token, StringComparison.Ordinal)) {
            throw new InvalidOperationException($"Expected generated C# to contain token '{token}', but it did not. Output: {generated}");
        }
    }
}