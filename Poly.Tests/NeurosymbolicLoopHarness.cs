using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Poly.Interpretation.Analysis;
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

public class NeurosymbolicLoopHarness {
    [Test]
    public async Task SimpleSuspendAndResume_DemonstratesBasicLoop() {
        var walker = new TreeWalker();

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
        var walker = new TreeWalker();

        var analyzerRan = false;
        var insightMessages = new List<string>();

        walker.RegisterLiveStateAnalyzer(new CallbackLiveStateAnalyzer((ctx, suspended) => {
            analyzerRan = true;
            ctx.ReportDiagnostic(
                suspended.AtNode ?? suspended.State.CurrentFrame.CurrentNode,
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
        var walker = new TreeWalker();
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
        var walker = new TreeWalker();
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
}