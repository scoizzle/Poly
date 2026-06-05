using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.TreeWalking;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Syntax.Analysis;

namespace Poly.Tests.Interpretation;

public class TreeWalkingInterpreterRobustnessContractsTests {
    private sealed class ReplaceAddAnalyzer(int replacement) : INodeAnalyzer {
        public void Analyze(AnalysisContext context, Node node) {
            if (node is Add) {
                context.SetNodeReplacement(node, new Constant(replacement));
            }

            this.AnalyzeChildren(context, node);
        }
    }

    private sealed class WarningAnalyzer(string code) : INodeAnalyzer {
        public void Analyze(AnalysisContext context, Node node) {
            context.ReportWarning(node, $"warn:{code}", code);
            this.AnalyzeChildren(context, node);
        }
    }

    private sealed class OrderedWarningAnalyzer(string code) : INodeAnalyzer {
        public void Analyze(AnalysisContext context, Node node) {
            context.ReportWarning(node, code, code);
            this.AnalyzeChildren(context, node);
        }
    }

    [Test]
    public async Task ExceptionPath_AfterThrow_InterpreterCanBeReused() {
        var walker = new TreeWalkingInterpreter();

        await Assert.That(() => walker.Evaluate(new Divide(new Constant(10), new Constant(0))))
            .Throws<DivideByZeroException>();

        var result = walker.Evaluate(new Add(new Constant(2), new Constant(3)));
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(5);
    }

    [Test]
    public async Task ResumeMisuse_RepeatedResumeAfterCompletion_ThrowsNoSuspendedState() {
        var walker = new TreeWalkingInterpreter();
        var ast = new Block([
            new SuspendNode(new Constant(1), "pause"),
            new Constant(2)
        ]);

        var first = walker.Evaluate(ast);
        await Assert.That(first.Value).IsTypeOf<SuspendedExecution>();

        var resumed = walker.Resume();
        await Assert.That(resumed.HasValue).IsTrue();
        await Assert.That(resumed.Value).IsEqualTo(2);

        var ex = await Assert.That(() => walker.Resume()).Throws<InvalidOperationException>();
        await Assert.That(ex!.Message).Contains("No suspended state to resume");
    }

    [Test]
    public async Task ResumeMisuse_FailedRefinedAnalysis_DoesNotLoseSuspendedState() {
        var walker = new TreeWalkingInterpreter();
        var ast = new Block([
            new SuspendNode(new Constant(1), "pause"),
            new Constant(2)
        ]);

        var first = walker.Evaluate(ast);
        await Assert.That(first.HasValue).IsTrue();
        await Assert.That(first.Value).IsTypeOf<SuspendedExecution>();

        var badContext = new AnalysisContext(ClrTypeDefinitionRegistry.Shared);
        badContext.ReportError(ast, "forced analysis failure", "FORCED_ANALYSIS_ERROR");
        var badAnalysis = new AnalysisResult(badContext, AnalysisTelemetry.Empty);

        await Assert.That(() => walker.Resume(badAnalysis)).Throws<InvalidOperationException>();

        var resumed = walker.Resume();
        await Assert.That(resumed.HasValue).IsTrue();
        await Assert.That(resumed.Value).IsEqualTo(2);
    }

    [Test]
    public async Task BreakpointWithReplacement_BreaksAtOriginalNodeBeforeReplacement() {
        var add = new Add(new Constant(1), new Constant(2));
        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new ReplaceAddAnalyzer(99))
            .Build()
            .Analyze(add);

        var walker = new TreeWalkingInterpreter(analysis)
            .BreakOn(add);

        var first = walker.Evaluate(add);
        await Assert.That(first.HasValue).IsTrue();
        await Assert.That(first.Value).IsTypeOf<SuspendedExecution>();
        await Assert.That(((SuspendedExecution)first.Value!).AtNode).IsSameReferenceAs(add);

        var resumed = walker.Resume();
        await Assert.That(resumed.HasValue).IsTrue();
        await Assert.That(resumed.Value).IsEqualTo(99);
    }

    [Test]
    public async Task BreakpointOrder_ParentThenChild_IsDeterministic() {
        var x = new Parameter("x");
        var child = new Add(x, new Constant(3));
        var root = new Add(child, new Constant(4));

        var walker = new TreeWalkingInterpreter()
            .BreakOn(root)
            .BreakOn(child);

        var first = walker.Evaluate(root, new Dictionary<string, object?> {
            ["x"] = 2
        });
        var s1 = (SuspendedExecution)first.Value!;
        await Assert.That(s1.AtNode).IsSameReferenceAs(root);

        walker.ClearBreakpoint(root);

        var second = walker.Resume();
        var s2 = (SuspendedExecution)second.Value!;
        await Assert.That(s2.AtNode).IsSameReferenceAs(child);

        var final = walker.Resume();
        await Assert.That(final.HasValue).IsTrue();
        await Assert.That(final.Value).IsEqualTo(9);
    }

    [Test]
    public async Task BreakpointDuplicateRegistration_IsIdempotent() {
        var node = new Add(new Constant(2), new Constant(5));
        var walker = new TreeWalkingInterpreter()
            .BreakOn(node)
            .BreakOn(node)
            .BreakOn(node.Id)
            .BreakOn(node.Id);

        var first = walker.Evaluate(node);
        await Assert.That(first.HasValue).IsTrue();
        await Assert.That(first.Value).IsTypeOf<SuspendedExecution>();
        await Assert.That(((SuspendedExecution)first.Value!).AtNode).IsSameReferenceAs(node);

        var resumed = walker.Resume();
        await Assert.That(resumed.HasValue).IsTrue();
        await Assert.That(resumed.Value).IsEqualTo(7);
    }

    [Test]
    public async Task MetadataConflict_NodeReplacement_LastAnalyzerWins() {
        var ast = new Add(new Constant(1), new Constant(2));

        var analysisA = new AnalyzerBuilder()
            .AddAnalyzer(new ReplaceAddAnalyzer(10))
            .AddAnalyzer(new ReplaceAddAnalyzer(20))
            .Build()
            .Analyze(ast);

        var analysisB = new AnalyzerBuilder()
            .AddAnalyzer(new ReplaceAddAnalyzer(20))
            .AddAnalyzer(new ReplaceAddAnalyzer(10))
            .Build()
            .Analyze(ast);

        var resultA = new TreeWalkingInterpreter(analysisA).Evaluate(ast);
        var resultB = new TreeWalkingInterpreter(analysisB).Evaluate(ast);

        await Assert.That(resultA.Value).IsEqualTo(20);
        await Assert.That(resultB.Value).IsEqualTo(10);
    }

    [Test]
    public async Task DeepRecursion_Depth500LeftChain_Completes() {
        Node ast = new Constant(1);
        for (int i = 0; i < 500; i++) {
            ast = new Add(ast, new Constant(1));
        }

        var result = new TreeWalkingInterpreter().Evaluate(ast);
        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(501);
    }

    [Test]
    public async Task Determinism_PreExecutionDiagnosticsOrder_IsStableAcrossRuns() {
        var ast = new Add(new Constant(1), new Constant(2));

        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(new OrderedWarningAnalyzer("A"))
            .AddAnalyzer(new OrderedWarningAnalyzer("B"))
            .Build();

        var run1 = analyzer.Analyze(ast).Diagnostics.Select(d => d.Code).ToArray();
        var run2 = analyzer.Analyze(ast).Diagnostics.Select(d => d.Code).ToArray();

        await Assert.That(run1.Length).IsEqualTo(run2.Length);
        for (int i = 0; i < run1.Length; i++) {
            await Assert.That(run1[i]).IsEqualTo(run2[i]);
        }
    }

    [Test]
    public async Task DisposeWhileSuspended_CurrentBehavior_ResumeStillCompletes() {
        var walker = new TreeWalkingInterpreter();
        var ast = new Block([
            new SuspendNode(new Constant(1), "pause"),
            new Constant(3)
        ]);

        var first = walker.Evaluate(ast);
        await Assert.That(first.Value).IsTypeOf<SuspendedExecution>();

        walker.Dispose();

        var resumed = walker.Resume();
        await Assert.That(resumed.HasValue).IsTrue();
        await Assert.That(resumed.Value).IsEqualTo(3);

        await Assert.That(() => walker.Evaluate(new Constant(1))).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task DisposeLifecycle_AfterEvaluationError_MultipleDisposeIsSafe() {
        var walker = new TreeWalkingInterpreter();

        await Assert.That(() => walker.Evaluate(new Divide(new Constant(1), new Constant(0))))
            .Throws<DivideByZeroException>();

        walker.Dispose();
        walker.Dispose();
        walker.Dispose();

        await Assert.That(() => walker.Evaluate(new Constant(1))).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task NullabilityBoundaries_NullNodeArgumentsThrow() {
        var walker = new TreeWalkingInterpreter();

        await Assert.That(() => walker.BreakOn((Node)null!)).Throws<ArgumentNullException>();
        await Assert.That(() => walker.ClearBreakpoint((Node)null!)).Throws<ArgumentNullException>();
        await Assert.That(() => walker.Evaluate((Node)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AnalysisModeParity_NoWarnings_RuntimeValueIsSameAcrossModes() {
        var ast = new Add(new Constant(5), new Constant(7));

        var balanced = new TreeWalkingInterpreter(
            null,
            null,
            InterpretationAnalysisSettings.ForMode(InterpretationAnalysisMode.Balanced));

        var strict = new TreeWalkingInterpreter(
            null,
            null,
            InterpretationAnalysisSettings.ForMode(InterpretationAnalysisMode.Strict));

        var explain = new TreeWalkingInterpreter(
            null,
            null,
            InterpretationAnalysisSettings.ForMode(InterpretationAnalysisMode.Explain));

        var r1 = balanced.Evaluate(ast);
        var r2 = strict.Evaluate(ast);
        var r3 = explain.Evaluate(ast);

        await Assert.That(r1.Value).IsEqualTo(12);
        await Assert.That(r2.Value).IsEqualTo(12);
        await Assert.That(r3.Value).IsEqualTo(12);
    }

    [Test]
    public async Task AnalysisModeParity_WarningGatingDiffersByMode() {
        var ast = new Add(new Constant(1), new Constant(2));
        var warningAnalysis = new AnalyzerBuilder()
            .AddAnalyzer(new WarningAnalyzer("WARN_TEST"))
            .Build()
            .Analyze(ast, AnalysisSettings.Default.With(new AnalysisDiagnosticConfiguration {
                TreatWarningsAsErrors = false
            }));

        var balanced = new TreeWalkingInterpreter(
            warningAnalysis,
            null,
            InterpretationAnalysisSettings.ForMode(InterpretationAnalysisMode.Balanced));
        var explain = new TreeWalkingInterpreter(
            warningAnalysis,
            null,
            InterpretationAnalysisSettings.ForMode(InterpretationAnalysisMode.Explain));
        var strict = new TreeWalkingInterpreter(
            warningAnalysis,
            null,
            InterpretationAnalysisSettings.ForMode(InterpretationAnalysisMode.Strict));

        await Assert.That(balanced.Evaluate(ast).Value).IsEqualTo(3);
        await Assert.That(explain.Evaluate(ast).Value).IsEqualTo(3);

        await Assert.That(() => strict.Evaluate(ast)).Throws<InvalidOperationException>();
    }
}