using System;
using System.Linq;

using Poly.Interpretation.TreeWalking;
using Poly.Syntax;
using Poly.Syntax.Analysis;

namespace Poly.Interpretation.Analysis;

public sealed class ExecutionInsightAnalyzer : ILiveStateAnalyzer {
    public void AnalyzeSuspendedState(AnalysisContext context, SuspendedExecution suspended) {
        AnalyzeCallStackDepth(context, suspended);
        AnalyzeEvaluationStackTypes(context, suspended);
        AnalyzeCreateOperations(context, suspended);
    }

    private static void AnalyzeCallStackDepth(AnalysisContext context, SuspendedExecution suspended) {
        if (suspended.CallStackDepth <= 3)
            return;

        var target = suspended.AtNode ?? suspended.State.CurrentFrame.CurrentNode;
        context.ReportHint(
            target,
            $"Call stack depth is {suspended.CallStackDepth}. Consider refactoring into smaller methods to improve maintainability.",
            "DEEP_CALL_STACK");
    }

    private static void AnalyzeEvaluationStackTypes(AnalysisContext context, SuspendedExecution suspended) {
        var span = suspended.State.ValueStack.AsSpan();
        if (span.Length < 2)
            return;

        Type? commonType = null;
        var hasMixed = false;

        for (var i = 0; i < span.Length; i++) {
            var value = span[i];
            if (value is null)
                continue;

            var valueType = value.GetType();
            if (commonType is null) {
                commonType = valueType;
            }
            else if (valueType != commonType) {
                hasMixed = true;
                break;
            }
        }

        if (!hasMixed)
            return;

        var target = suspended.AtNode ?? suspended.State.CurrentFrame.CurrentNode;
        if (target is null) {
            context.ReportError(NodeExtensions.Null,
                "Evaluation stack contains mixed types, but no current node is available for diagnostic targeting.",
                "MIXED_EVALUATION_STACK_NO_TARGET");
            return;
        }

        context.ReportWarning(
            target,
            $"Evaluation stack contains values of mixed types ({string.Join(", ", GetDistinctTypeNames(span))}), which may indicate a semantic coherence issue.",
            "MIXED_EVALUATION_STACK");
    }

    private static void AnalyzeCreateOperations(AnalysisContext context, SuspendedExecution suspended) {
        var target = suspended.AtNode ?? suspended.State.CurrentFrame.CurrentNode;

        foreach (var frame in suspended.State.CallStack.Frames) {
            var nodeStr = frame.CurrentNode.ToString();
            if (nodeStr is not null && nodeStr.Contains("Create", StringComparison.OrdinalIgnoreCase)) {
                context.ReportHint(
                    target,
                    "Execution suspended at a node related to a Create operation. Ensure idempotency guarantees are in place.",
                    "CREATE_OPERATION_FLAG");
                return;
            }
        }
    }

    private static HashSet<string> GetDistinctTypeNames(Span<object?> values) {
        HashSet<string> typeNames = [];

        for (var i = 0; i < values.Length; i++) {
            typeNames.Add(values[i]?.GetType().Name ?? "null");
        }

        return typeNames;
    }
}