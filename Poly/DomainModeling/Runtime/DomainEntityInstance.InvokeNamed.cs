using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Runtime;

public sealed partial record DomainEntityInstance {
    /// <summary>
    /// VM protocol for AST methods with no CLR MethodInfo: dispatch by name to
    /// <see cref="InvokeAction"/>. Returns <see cref="DomainResult"/> — Success
    /// carries <see cref="ActionInvocationResult.ResultInstance"/> as
    /// <see cref="DomainResult.Value"/> so the <c>-&gt; Entity</c> instance is
    /// not dropped; Failure is an object so
    /// <c>if (!result.IsSuccess) return caller.Failure(error)</c> is live. Missing or
    /// wrong-stage actions return Failure (Kitchen nested invoke).
    /// Outer <see cref="InvokeAction"/> still owns the public
    /// <see cref="ActionInvocationResult"/>. Re-entrancy / <c>_invokeDepth</c>
    /// stays with InvokeAction.
    /// </summary>
    internal object? InvokeNamed(string name, object?[] args) {
        ArgumentException.ThrowIfNullOrEmpty(name);
        args ??= [];

        if (name is "Create" or "CreateIn" or "ProbeCreate")
            return RuntimeCreateFactory(name, args);

        if (TryNotifyStageSubscribers(name, args))
            return null;

        var action = ResolveActionForNamedInvoke(name);
        if (action is null) {
            var policy = ResolvePolicyForNamedInvoke(name);
            if (policy is not null) {
                if (args.Length != 0)
                    throw new InvalidOperationException(
                        $"Policy '{name}' does not take arguments.");
                return EvaluatePolicy(policy);
            }
            AnalysisResult? analysis = Domain is not null
                ? RuntimeAnalysisCache.GetOrAnalyze(Domain)
                : null;
            var unresolved = ReportUnresolvedAction(name, analysis);
            return DomainResult.Failure(
                unresolved.ErrorMessage ?? $"Action '{name}' not found on entity '{Entity.Name}'.");
        }

        IReadOnlyDictionary<string, object?>? mapped = null;
        if (action.Parameters.Count > 0) {
            if (args.Length != action.Parameters.Count)
                throw new InvalidOperationException(
                    $"Action '{name}' expects {action.Parameters.Count} argument(s), got {args.Length}.");
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int i = 0; i < action.Parameters.Count; i++)
                dict[action.Parameters[i].Name] = args[i];
            mapped = dict;
        }

        var result = InvokeAction(name, mapped);
        if (!result.Succeeded) {
            return DomainResult.Failure(
                result.ErrorMessage
                ?? (result.FailedGuards.Count > 0
                    ? $"invoke '{name}' blocked by guards: {string.Join(", ", result.FailedGuards)}"
                    : $"invoke '{name}' failed."));
        }
        return DomainResult.Success(result.ResultInstance);
    }

    /// <summary>
    /// Printed stage transitions call <c>Notify{Stage}Subscribers(previousStage)</c>.
    /// The dictionary instance has no per-stage CLR method; this is the same
    /// store fan-out as <see cref="Notify(string, string?)"/>.
    /// </summary>
    private bool TryNotifyStageSubscribers(string name, object?[] args) {
        const string prefix = "Notify";
        const string suffix = "Subscribers";
        if (name.Length <= prefix.Length + suffix.Length
            || !name.StartsWith(prefix, StringComparison.Ordinal)
            || !name.EndsWith(suffix, StringComparison.Ordinal))
            return false;
        var stage = name[prefix.Length..^suffix.Length];
        if (stage.Length == 0)
            return false;
        string? previous = null;
        if (args.Length > 0)
            previous = args[0] as string ?? args[0]?.ToString();
        Notify(stage, previous);
        return true;
    }

    private Action? ResolveActionForNamedInvoke(string name) {
        if (Domain is not null) {
            var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
            analysis.TryResolveAction(Domain, Entity, CurrentStage, name, out var action);
            return action;
        }
        return ResolveStandaloneAction(name);
    }

    private Policy? ResolvePolicyForNamedInvoke(string name) {
        foreach (var policy in EnumerateTypeDefPolicies(Entity)) {
            if (string.Equals(policy.Name, name, StringComparison.Ordinal))
                return policy;
        }
        return null;
    }
}