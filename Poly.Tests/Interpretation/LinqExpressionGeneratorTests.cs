using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;
using Poly.Tests.TestHelpers;

using Expr = System.Linq.Expressions.Expression;
using Exprs = System.Linq.Expressions;

namespace Poly.Tests.Interpretation;

public class LinqExpressionGeneratorTests {
    [Test]
    public async Task Compile_ReusedGenerator_DoesNotLeakParametersBetweenRootCompilations() {
        var x = new Parameter("x", TypeReference.To<int>());
        var y = new Parameter("y", TypeReference.To<int>());
        var root = new Block(x, y);

        var analysis = new AnalyzerBuilder().UseAllAnalyzers().Build().Analyze(root, setup: ctx => {
            ctx.SetResolvedType(x, ctx.TypeDefinitions.GetTypeDefinition(typeof(int))!);
            ctx.SetResolvedType(y, ctx.TypeDefinitions.GetTypeDefinition(typeof(int))!);
        });

        var generator = new LinqExpressionGenerator(analysis);

        var firstCompilation = generator.Compile(x);
        var firstParameters = firstCompilation.Parameters.Select(parameter => parameter.Name!).ToArray();

        var secondCompilation = generator.Compile(y);
        var secondParameters = secondCompilation.Parameters.Select(parameter => parameter.Name!).ToArray();

        await Assert.That(firstParameters).IsEquivalentTo(["x"]);
        await Assert.That(secondParameters).IsEquivalentTo(["y"]);
    }

    [Test]
    public async Task Compile_ReusedGenerator_DoesNotLeakTypedReturnLabelsBetweenRootCompilations() {
        var firstBlock = new Block(Return.True);
        var secondBlock = new Block(new Return(new Constant(42)));
        var root = new Block(firstBlock, secondBlock);
        var analysis = root.AnalyzeNode();
        var generator = new LinqExpressionGenerator(analysis);

        var firstExpression = generator.Compile(firstBlock).Expression;
        var firstResult = Expr.Lambda<Func<bool>>(firstExpression).Compile()();

        var secondExpression = generator.Compile(secondBlock).Expression;
        var secondResult = Expr.Lambda<Func<int>>(secondExpression).Compile()();

        await Assert.That(firstResult).IsTrue();
        await Assert.That(secondResult).IsEqualTo(42);
    }

    [Test]
    public async Task Compile_NodeWithNestedLambda_ExportsOnlyOuterParameters() {
        var outer = new Parameter("outer", TypeReference.To<int>());
        var inner = new Parameter("inner", TypeReference.To<int>());
        var node = new Invoke(
            new Lambda([inner], new Add(inner, outer)),
            new Constant(5));

        var analysis = new AnalyzerBuilder().UseAllAnalyzers().Build().Analyze(node, setup: ctx => {
            var intType = ctx.TypeDefinitions.GetTypeDefinition(typeof(int))!;
            ctx.SetResolvedType(outer, intType);
            ctx.SetResolvedType(inner, intType);
        });

        var generator = new LinqExpressionGenerator(analysis);
        var compilation = generator.Compile(node);
        var exportedParameters = compilation.Parameters.Select(parameter => parameter.Name!).ToArray();

        await Assert.That(exportedParameters).IsEquivalentTo(["outer"]);
    }

    [Test]
    public async Task Compile_NodeWithConstantFolding_UsesFoldedConstantReplacement() {
        var node = new Add(new Multiply(new Constant(6), new Constant(7)), new Constant(1));

        var analysis = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .Build()
            .Analyze(node);

        var generator = new LinqExpressionGenerator(analysis);
        var compilation = generator.Compile(node);

        await Assert.That(compilation.Parameters).IsEmpty();
        await Assert.That(compilation.Expression).IsTypeOf<Exprs.ConstantExpression>();
        await Assert.That(((Exprs.ConstantExpression)compilation.Expression).Value).IsEqualTo(43);
    }

    [Test]
    public async Task Compile_InvokeLambdaWithConstantArgumentsAndConstantFolding_UsesFoldedConstantReplacement() {
        var parameter = new Parameter("x", TypeReference.To<int>());
        var node = new Invoke(new Lambda([parameter], new Add(parameter, new Constant(10))), new Constant(5));

        var analysis = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .Build()
            .Analyze(node);

        var generator = new LinqExpressionGenerator(analysis);
        var compilation = generator.Compile(node);

        await Assert.That(compilation.Parameters).IsEmpty();
        await Assert.That(compilation.Expression).IsTypeOf<Exprs.ConstantExpression>();
        await Assert.That(((Exprs.ConstantExpression)compilation.Expression).Value).IsEqualTo(15);
    }

    [Test]
    public async Task Compile_AddWithZeroRightAndConstantFolding_UsesSimplifiedOperandReplacement() {
        var parameter = new Parameter("x", TypeReference.To<int>());
        var node = new Add(parameter, new Constant(0));

        var analysis = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .Build()
            .Analyze(node);

        var generator = new LinqExpressionGenerator(analysis);
        var compilation = generator.Compile(node);

        await Assert.That(compilation.Parameters.Select(p => p.Name!)).IsEquivalentTo(["x"]);
        await Assert.That(compilation.Expression).IsTypeOf<Exprs.ParameterExpression>();
    }

    [Test]
    public async Task Compile_AndWithConstantTrueLeftAndConstantFolding_UsesSimplifiedOperandReplacement() {
        var parameter = new Parameter("flag", TypeReference.To<bool>());
        var node = new And(new Constant(true), parameter);

        var analysis = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .Build()
            .Analyze(node);

        var generator = new LinqExpressionGenerator(analysis);
        var compilation = generator.Compile(node);

        await Assert.That(compilation.Parameters.Select(p => p.Name!)).IsEquivalentTo(["flag"]);
        await Assert.That(compilation.Expression).IsTypeOf<Exprs.ParameterExpression>();
    }

}