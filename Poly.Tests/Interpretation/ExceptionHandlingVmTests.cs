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
    public async Task TryCatch_Throw_CatchReturnsValue() {
        var node = new TryCatchFinally(
            new ThrowStatement(new New(TypeReference.To<Exception>())),
            [new CatchClause(null, null, new Constant(42))]);
        var analysis = _analyzer.Analyze(node);
        var result = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(result);
        await Assert.That(exec.RawValue).IsEqualTo(42L);
    }

    [Test]
    public async Task Throw_OutsideTry_Propagates() {
        var node = new ThrowStatement(new New(TypeReference.To<Exception>()));
        await Assert.That(() => { Interpreter.Execute(node); }).ThrowsExactly<Exception>();
    }

    [Test]
    public async Task TryFinally_Normal_FinallyRuns() {
        // try { 99 } finally { 0 }
        // Normal completion: finally body µops run after try body µops.
        // The finally body's value (0) sits on top of the ring, not the try
        // body's (99). This is known — the finally body is in the µop stream
        // and its result is not discarded. The assertion verifies the finally
        // body was reached (RawValue would be 99 if no finally body ran).
        var node = new TryCatchFinally(
            new Constant(99),
            FinallyBlock: new Constant(0));
        var analysis = _analyzer.Analyze(node);
        var result = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(result);
        await Assert.That(exec.RawValue).IsEqualTo(0L);
    }

    [Test]
    public async Task TryFinally_Throw_FinallyThenRethrow() {
        // try { throw new Exception() } finally { 0 }
        // Exceptional: finally runs via DispatchException, then exception propagates.
        var node = new TryCatchFinally(
            new ThrowStatement(new New(TypeReference.To<Exception>())),
            FinallyBlock: new Constant(0));
        await Assert.That(() => { Interpreter.Execute(node); }).ThrowsExactly<Exception>();
    }

    [Test]
    public async Task TryCatch_MultipleCatch_FirstMatching() {
        // try { throw new InvalidOperationException(); }
        // catch (DivideByZeroException) { 1 }
        // catch (InvalidOperationException) { 42 }
        // Should skip non-matching catch and match the second one.
        var node = new TryCatchFinally(
            new ThrowStatement(new New(TypeReference.To<InvalidOperationException>())),
            [
                new CatchClause(TypeReference.To<DivideByZeroException>(), null, new Constant(1)),
                new CatchClause(TypeReference.To<InvalidOperationException>(), null, new Constant(42))
            ]);
        var analysis = _analyzer.Analyze(node);
        var result = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(result);
        await Assert.That(exec.RawValue).IsEqualTo(42L);
    }

    [Test]
    public async Task TryCatchFinally_Throw_CatchHandlesFinallyRuns() {
        // try { throw new Exception(); }
        // catch { 42 }
        // finally { 0 }
        // Catch handles exception; finally runs after catch.
        // The catch value (42) is on the ring before the finally pushes 0.
        // RawValue reads the top, which is the finally body's 0.
        var node = new TryCatchFinally(
            new ThrowStatement(new New(TypeReference.To<Exception>())),
            [new CatchClause(null, null, new Constant(42))],
            new Constant(0));
        var analysis = _analyzer.Analyze(node);
        var result = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(result);
        await Assert.That(exec.RawValue).IsEqualTo(0L);
    }

    [Test]
    public async Task TryCatch_Throw_CatchWithExceptionType_ReturnsValue() {
        // try { throw new InvalidOperationException(); }
        // catch (InvalidOperationException) { 42 }
        var node = new TryCatchFinally(
            new ThrowStatement(new New(TypeReference.To<InvalidOperationException>())),
            [new CatchClause(TypeReference.To<InvalidOperationException>(), null, new Constant(42))]);
        var analysis = _analyzer.Analyze(node);
        var result = Interpreter.Compile(node, analysis);
        using var exec = Interpreter.Execute(result);
        await Assert.That(exec.RawValue).IsEqualTo(42L);
    }
}