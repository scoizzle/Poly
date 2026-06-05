using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.TreeWalking;
using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Tests;

internal sealed class CallbackNodeAnalyzer(Action<AnalysisContext, Node> callback) : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        callback(context, node);
        this.AnalyzeChildren(context, node);
    }
}

internal sealed class CallbackLiveStateAnalyzer(Action<AnalysisContext, SuspendedExecution> callback) : ILiveStateAnalyzer {
    public void AnalyzeSuspendedState(AnalysisContext context, SuspendedExecution suspended) {
        callback(context, suspended);
    }
}

internal sealed class ReplaceAddWithConstantAnalyzer(object? value) : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (node is Add) {
            context.SetNodeReplacement(node, new Constant(value));
        }

        this.AnalyzeChildren(context, node);
    }
}

internal sealed class ReplaceNodeTypeWithConstantAnalyzer(Type targetNodeType, object? value) : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (targetNodeType.IsInstanceOfType(node)) {
            context.SetNodeReplacement(node, new Constant(value));
        }

        this.AnalyzeChildren(context, node);
    }
}

internal sealed class CountingNoOpCompiler(Node targetNode) : ITreeWalkerCompiler {
    public int Hits { get; private set; }

    public bool TryEvaluate(
        Node node,
        Func<Node, InterpreterState, InterpreterResult> evaluateChild,
        InterpreterState state,
        out InterpreterResult result) {
        if (ReferenceEquals(node, targetNode)) {
            Hits++;
        }

        result = default;
        return false;
    }
}

internal sealed class WarningAnalyzer(string message) : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        context.ReportWarning(node, message, "TEST_WARNING");
    }
}

internal sealed class MixedDiagnosticAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        context.ReportHint(node, "hint", "TEST_HINT");
        context.ReportInformation(node, "info", "TEST_INFO");
        context.ReportWarning(node, "warn", "TEST_WARN");
        context.ReportError(node, "error", "TEST_ERROR");
    }
}

public class NeurosymbolicLoopHarness {
    [Test]
    public async Task SimpleSuspendAndResume_DemonstratesBasicLoop() {
        var walker = new TreeWalkingInterpreter();

        var x = new Variable("x");
        var ast = new Block([
            new Assignment(x, new Constant(10)),
            new SuspendNode(new Constant(42), "Checkpoint"),
            x
        ]);

        var result = walker.Evaluate(ast);

        await Assert.That(result.HasValue).IsTrue();
        var suspended = result.Value as SuspendedExecution;
        await Assert.That(suspended).IsNotNull();
        await Assert.That(suspended!.Reason).IsEqualTo("Checkpoint");
        await Assert.That(suspended.AtNode).IsTypeOf<SuspendNode>();
        await Assert.That(suspended.State.Variables["x"]).IsEqualTo(10);
        await Assert.That(suspended.CallStackDepth).IsGreaterThan(0);

        var resumed = walker.Resume();

        await Assert.That(resumed.HasValue).IsTrue();
        await Assert.That(resumed.Value).IsEqualTo(10);
    }

    [Test]
    public async Task InsightAnalysisOnSuspend_ProducesDiagnostics() {
        var walker = new TreeWalkingInterpreter();

        var analyzerRan = false;
        var insightMessages = new List<string>();

        walker.RegisterLiveStateAnalyzer(new CallbackLiveStateAnalyzer((ctx, suspended) => {
            analyzerRan = true;
            ctx.ReportDiagnostic(
                suspended.AtNode ?? suspended.State.CurrentFrame.CurrentNode!,
                DiagnosticSeverity.Information,
                $"Inspected state: {suspended.Reason}, stack depth={suspended.CallStackDepth}",
                "LIVE_STATE_INSPECT");
            insightMessages.Add($"LiveStateAnalyzer ran at '{suspended.Reason}'");
        }));

        walker.RegisterInsightAnalyzer(new CallbackNodeAnalyzer((ctx, node) => {
            ctx.ReportHint(node, "AST-level insight on suspended node", "AST_INSIGHT");
        }));

        var ast = new Block([
            new SuspendNode(new Constant(99), "InsightPoint")
        ]);

        var result = walker.Evaluate(ast);

        await Assert.That(result.HasValue).IsTrue();
        var suspended = result.Value as SuspendedExecution;
        await Assert.That(suspended).IsNotNull();

        await Assert.That(analyzerRan).IsTrue();
        await Assert.That(insightMessages.Any(m => m.Contains("InsightPoint"))).IsTrue();
        await Assert.That(walker.LastInsightResult).IsNotNull();
        var diagnostics = walker.LastInsightResult!.Diagnostics;
        await Assert.That(diagnostics.Any(d => d.Code == "LIVE_STATE_INSPECT")).IsTrue();
        await Assert.That(diagnostics.Any(d => d.Code == "AST_INSIGHT")).IsTrue();
    }

    [Test]
    public async Task NeurosymbolicFeedback_SimulatesModelImprovementLoop() {
        var walker = new TreeWalkingInterpreter();
        var feedbackLog = new List<string>();

        walker.RegisterLiveStateAnalyzer(new CallbackLiveStateAnalyzer((ctx, suspended) => {
            foreach (var kv in suspended.State.Variables) {
                feedbackLog.Add($"Variable '{kv.Key}' = {kv.Value} at '{suspended.Reason}'");
            }
            feedbackLog.Add($"Call stack depth: {suspended.CallStackDepth}");
        }));

        var titleVar = new Variable("title");
        var yearVar = new Variable("year");
        var authorVar = new Variable("author");

        var assignBookAction = new Block([
            new Assignment(titleVar, new Constant("The Hobbit")),
            new Assignment(yearVar, new Constant(1937)),
            new Assignment(authorVar, new Constant("J.R.R. Tolkien")),
            new SuspendNode(new Constant("AssignBook prepared"), "PostAssign"),
            titleVar,
        ]);

        var result = walker.Evaluate(assignBookAction);

        await Assert.That(result.HasValue).IsTrue();
        var suspended = result.Value as SuspendedExecution;
        await Assert.That(suspended).IsNotNull();
        await Assert.That(suspended!.State.Variables["title"]).IsEqualTo("The Hobbit");
        await Assert.That(suspended.State.Variables["year"]).IsEqualTo(1937);
        await Assert.That(suspended.State.Variables["author"]).IsEqualTo("J.R.R. Tolkien");

        await Assert.That(feedbackLog.Any(x => x.Contains("title") && x.Contains("The Hobbit"))).IsTrue();
        await Assert.That(feedbackLog.Any(x => x.Contains("author") && x.Contains("Tolkien"))).IsTrue();

        var resumed = walker.Resume();
        await Assert.That(resumed.HasValue).IsTrue();
        await Assert.That(resumed.Value).IsEqualTo("The Hobbit");
    }

    [Test]
    public async Task ComprehensivePolicyEvaluation_MultiPointSuspension() {
        var walker = new TreeWalkingInterpreter();
        var suspensionPoints = new List<string>();

        walker.RegisterLiveStateAnalyzer(new CallbackLiveStateAnalyzer((ctx, suspended) => {
            suspensionPoints.Add(suspended.Reason);
        }));

        var countVar = new Variable("count");
        var thresholdVar = new Variable("threshold");
        var resultVar = new Variable("result");

        var policyEvalAst = new Block([
            new Assignment(countVar, new Constant(5)),
            new Assignment(thresholdVar, new Constant(3)),
            new SuspendNode(new Constant("Initialized"), "InitCheck"),
            new IfStatement(
                new LessThan(countVar, thresholdVar),
                new Assignment(resultVar, new Constant("below threshold")),
                new Assignment(resultVar, new Constant("at or above threshold"))
            ),
            new SuspendNode(new Constant("Evaluated"), "PolicyEval"),
        ]);

        var result1 = walker.Evaluate(policyEvalAst);
        await Assert.That(result1.HasValue).IsTrue();
        var s1 = result1.Value as SuspendedExecution;
        await Assert.That(s1).IsNotNull();
        await Assert.That(s1!.Reason).IsEqualTo("InitCheck");
        await Assert.That(s1.State.Variables["count"]).IsEqualTo(5);
        await Assert.That(s1.State.Variables["threshold"]).IsEqualTo(3);

        var result2 = walker.Resume();
        await Assert.That(result2.HasValue).IsTrue();
        var s2 = result2.Value as SuspendedExecution;
        await Assert.That(s2).IsNotNull();
        await Assert.That(s2!.Reason).IsEqualTo("PolicyEval");
        await Assert.That(s2.State.Variables["result"]).IsEqualTo("at or above threshold");

        var result3 = walker.Resume();
        await Assert.That(result3.IsVoid).IsTrue();

        await Assert.That(suspensionPoints.Count).IsEqualTo(2);
        await Assert.That(suspensionPoints[0]).IsEqualTo("InitCheck");
        await Assert.That(suspensionPoints[1]).IsEqualTo("PolicyEval");
    }

    [Test]
    public async Task AnalysisMetadata_RewritesExecutionBeforeEvaluation() {
        var ast = new Add(new Constant(1), new Constant(2));

        var baselineAnalysis = new AnalyzerBuilder()
            .Build()
            .Analyze(ast);

        var baselineWalker = new TreeWalkingInterpreter(baselineAnalysis);
        var baselineResult = baselineWalker.Evaluate(ast);

        await Assert.That(baselineResult.HasValue).IsTrue();
        await Assert.That(baselineResult.Value).IsEqualTo(3);

        var evolvedAnalysis = new AnalyzerBuilder()
            .AddAnalyzer(new ReplaceAddWithConstantAnalyzer(99))
            .Build()
            .Analyze(ast);

        var evolvedWalker = new TreeWalkingInterpreter(evolvedAnalysis);
        var evolvedResult = evolvedWalker.Evaluate(ast);

        await Assert.That(evolvedResult.HasValue).IsTrue();
        await Assert.That(evolvedResult.Value).IsEqualTo(99);
        await Assert.That(ast.LeftHandValue).IsTypeOf<Constant>();
        await Assert.That(((Constant)ast.LeftHandValue).Value).IsEqualTo(1);
        await Assert.That(ast.RightHandValue).IsTypeOf<Constant>();
        await Assert.That(((Constant)ast.RightHandValue).Value).IsEqualTo(2);
    }

    [Test]
    public async Task AnalysisMetadata_ReanalyzesSharedSubtreeAfterStructuralEdit() {
        var sharedLeft = new Constant(1);
        var baselineAst = new Add(sharedLeft, new Constant(2));

        var baselineAnalysis = new AnalyzerBuilder()
            .AddAnalyzer(new ReplaceNodeTypeWithConstantAnalyzer(typeof(Add), 3))
            .Build()
            .Analyze(baselineAst);

        var baselineWalker = new TreeWalkingInterpreter(baselineAnalysis);
        var baselineResult = baselineWalker.Evaluate(baselineAst);

        await Assert.That(baselineResult.HasValue).IsTrue();
        await Assert.That(baselineResult.Value).IsEqualTo(3);

        var evolvedAst = new Multiply(sharedLeft, new Constant(4));

        var evolvedAnalysis = new AnalyzerBuilder()
            .AddAnalyzer(new ReplaceNodeTypeWithConstantAnalyzer(typeof(Multiply), 7))
            .Build()
            .Analyze(evolvedAst, baselineAnalysis, [baselineAst]);

        var evolvedWalker = new TreeWalkingInterpreter(evolvedAnalysis);
        var evolvedResult = evolvedWalker.Evaluate(evolvedAst);

        await Assert.That(evolvedResult.HasValue).IsTrue();
        await Assert.That(evolvedResult.Value).IsEqualTo(7);
        await Assert.That(ReferenceEquals(baselineAst.LeftHandValue, evolvedAst.LeftHandValue)).IsTrue();
        await Assert.That(((Constant)evolvedAst.LeftHandValue).Value).IsEqualTo(1);
        await Assert.That(((Constant)baselineAst.RightHandValue).Value).IsEqualTo(2);
        await Assert.That(((Constant)evolvedAst.RightHandValue).Value).IsEqualTo(4);
    }

    [Test]
    public async Task SuspendedExecution_CanResumeWithRefinedAnalysisSnapshot() {
        var ast = new Block([
            new SuspendNode(new Constant("checkpoint"), "RefineHere"),
            new Add(new Constant(1), new Constant(2))
        ]);

        var walker = new TreeWalkingInterpreter();

        var firstResult = walker.Evaluate(ast);
        await Assert.That(firstResult.HasValue).IsTrue();
        var suspended = firstResult.Value as SuspendedExecution;
        await Assert.That(suspended).IsNotNull();
        await Assert.That(suspended!.Reason).IsEqualTo("RefineHere");

        var refinedAnalysis = new AnalyzerBuilder()
            .AddAnalyzer(new ReplaceAddWithConstantAnalyzer(77))
            .Build()
            .Analyze(ast);

        var resumed = walker.Resume(refinedAnalysis);
        await Assert.That(resumed.HasValue).IsTrue();
        await Assert.That(resumed.Value).IsEqualTo(77);
    }

    [Test]
    public async Task BreakpointApi_SuspendsBeforeNodeExecution_ByReferenceAndId() {
        var sharedAdd = new Add(new Constant(2), new Constant(3));
        var ast = new Block([
            new Assignment(new Variable("prefix"), new Constant("ready")),
            new Add(new Constant(1), new Constant(2)),
            sharedAdd
        ]);

        var referenceWalker = new TreeWalkingInterpreter()
            .BreakOn(sharedAdd);

        var firstResult = referenceWalker.Evaluate(ast);
        await Assert.That(firstResult.HasValue).IsTrue();
        var firstSuspended = firstResult.Value as SuspendedExecution;
        await Assert.That(firstSuspended).IsNotNull();
        await Assert.That(firstSuspended!.AtNode).IsSameReferenceAs(sharedAdd);
        await Assert.That(firstSuspended.Reason).Contains("Breakpoint hit");

        var firstResumed = referenceWalker.Resume();
        await Assert.That(firstResumed.HasValue).IsTrue();
        await Assert.That(firstResumed.Value).IsEqualTo(5);

        var idWalker = new TreeWalkingInterpreter()
            .BreakOn(sharedAdd.Id);

        var secondResult = idWalker.Evaluate(ast);
        await Assert.That(secondResult.HasValue).IsTrue();
        var secondSuspended = secondResult.Value as SuspendedExecution;
        await Assert.That(secondSuspended).IsNotNull();
        await Assert.That(secondSuspended!.AtNode).IsSameReferenceAs(sharedAdd);

        var secondResumed = idWalker.Resume();
        await Assert.That(secondResumed.HasValue).IsTrue();
        await Assert.That(secondResumed.Value).IsEqualTo(5);
    }

    [Test]
    public async Task SideEffectAnalysis_SkipsPureIntermediateBlockNode() {
        var skippedCandidate = new Add(new Constant(1), new Constant(2));
        var ast = new Block([
            skippedCandidate,
            new Add(new Constant(10), new Constant(5))
        ]);

        var countingCompiler = new CountingNoOpCompiler(skippedCandidate);
        var walker = new TreeWalkingInterpreter()
            .RegisterCompiler(countingCompiler);

        var result = walker.Evaluate(ast);

        await Assert.That(result.HasValue).IsTrue();
        await Assert.That(result.Value).IsEqualTo(15);
        await Assert.That(countingCompiler.Hits).IsEqualTo(0);
    }

    [Test]
    public async Task InterpretationAnalysisSettings_StrictMode_TreatsWarningsAsBlocking() {
        var ast = new Add(new Constant(1), new Constant(2));

        var warningAnalysis = new AnalyzerBuilder()
            .AddAnalyzer(new WarningAnalyzer("warning for strict mode"))
            .Build()
            .Analyze(ast);

        var strictSettings = InterpretationAnalysisSettings.ForMode(InterpretationAnalysisMode.Strict);
        var walker = new TreeWalkingInterpreter(warningAnalysis, null, strictSettings);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.FromResult(walker.Evaluate(ast)));
        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Analysis failed before interpretation could start");
    }

    [Test]
    public async Task AnalysisDiagnosticConfiguration_CanFilterVerbosityAndPromoteWarnings() {
        var ast = new Constant(1);

        var settings = AnalysisSettings.Default
            .With(new AnalysisDiagnosticConfiguration {
                TreatWarningsAsErrors = true,
                Verbosity = AnalysisDiagnosticVerbosity.WarningAndAbove
            });

        var analysis = new AnalyzerBuilder()
            .AddAnalyzer(new MixedDiagnosticAnalyzer())
            .Build()
            .Analyze(ast, settings);

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "TEST_HINT")).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "TEST_INFO")).IsFalse();
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "TEST_WARN")).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d => d.Code == "TEST_WARN" && d.Severity == DiagnosticSeverity.Error)).IsTrue();
        await Assert.That(analysis.HasErrors).IsTrue();
    }
}