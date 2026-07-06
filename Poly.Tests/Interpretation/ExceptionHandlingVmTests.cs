using Poly.Interpretation;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Tests.Interpretation;

public class ExceptionHandlingVmTests {
    private static readonly Analyzer _analyzer = new AnalyzerBuilder()
        .UseTypeAndMemberResolver()
        .UseVariableScopeValidator()
        .UseSideEffectAnalysis()
        .UseThisReferenceContext()
        .UseJumpTargetResolution()
        .UseControlFlowAnalysis()
        .UseValueRepresentationAnalysis()
        .UseCallSiteCatalog()
        .UseConstantFolding()
        .UseDefiniteAssignmentAnalysis()
        .UseLambdaReturnTypeResolution()
        .UseExceptionRegionAnalysis()
        .UsePrimitiveExpansion()
        .Build();

    [Test]
    public async Task TryCatch_NormalCompletion_SkipsCatch() {
        var node = new TryCatchFinally(
            new Constant(99),
            [new CatchClause(null, null, new Constant(0))]);
        var analysis = _analyzer.Analyze(node);
        var result = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(result);
        await Assert.That(exec.RawValue).IsEqualTo(99L);
    }

    [Test]
    public async Task Throw_OutsideTry_Propagates() {
        var node = new ThrowStatement(new New(TypeReference.To<Exception>()));
        await Assert.That(() => { Interpreter.Execute(node); }).ThrowsExactly<Exception>();
    }
}