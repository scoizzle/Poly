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

        if (TryWhenHandler(name, args))
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
    /// The dictionary instance has no per-stage CLR method; this routes to
    /// <see cref="Notify(string, string?)"/> so the store can fill the
    /// registry and run the compiled Notify body.
    /// </summary>
    private bool TryNotifyStageSubscribers(string name, object?[] args) {
        const string prefix = "Notify";
        const string suffix = "Subscribers";
        if (name.Length <= prefix.Length + suffix.Length
            || !name.StartsWith(prefix, StringComparison.Ordinal)
            || !name.EndsWith(suffix, StringComparison.Ordinal))
            return false;
        var stage = name[prefix.Length..^suffix.Length];
        if (stage.Length == 0
            || !Entity.Stages.Any(s => string.Equals(s.Name, stage, StringComparison.Ordinal)))
            return false;
        string? previous = null;
        if (args.Length > 0)
            previous = args[0] as string ?? args[0]?.ToString();
        Notify(stage, previous);
        return true;
    }

    /// <summary>
    /// Compiled <c>Notify{Stage}Subscribers</c> calls <c>sub.When…</c>.
    /// That name is not an action; this arm runs the cached subscription
    /// body (<see cref="RuntimeAnalysisCache.TryGetSubscriptionBody"/>).
    /// </summary>
    private bool TryWhenHandler(string name, object?[] args) {
        if (name.Length <= 4
            || !name.StartsWith("When", StringComparison.Ordinal)
            || Domain is null)
            return false;
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        RuntimeAnalysisCache.GetOrLower(
            Domain, RuntimeAnalysisCache.Session(Domain), analysis);
        if (!RuntimeAnalysisCache.TryGetModuleMethod(Domain, Entity.Name, name, out var method)
            || method?.Body is null)
            return false;
        if (!TryMatchSubscriptionBody(method.Body, out var entry, out var targetStageName))
            return false;

        DomainEntityInstance peer = this;
        string? previousStageName = null;
        foreach (var arg in args) {
            if (arg is DomainEntityInstance instance)
                peer = instance;
            else if (arg is string text)
                previousStageName = text;
            else if (arg is not null)
                previousStageName = arg.ToString();
        }
        ExecuteSubscriptionEffects(
            entry.Effects, peer, entry.PeerBinding,
            planEntry: entry, targetStageName: targetStageName,
            previousStageName: previousStageName);
        return true;
    }

    private bool TryMatchSubscriptionBody(
        Node body,
        out SubscriptionDispatchPlanEntry entry,
        out string targetStageName) {
        entry = null!;
        targetStageName = null!;
        if (Domain is null)
            return false;
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
        if (MatchPlan(
                analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(Entity),
                body, out entry, out targetStageName))
            return true;
        foreach (var stage in Entity.Stages) {
            if (MatchPlan(
                    analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(stage),
                    body, out entry, out targetStageName))
                return true;
        }
        return false;
    }

    private bool MatchPlan(
        SubscriptionDispatchPlanMetadata? plan,
        Node body,
        out SubscriptionDispatchPlanEntry entry,
        out string targetStageName) {
        entry = null!;
        targetStageName = null!;
        if (plan is null || Domain is null)
            return false;
        foreach (var candidate in plan.ByRelationshipName.Values.SelectMany(e => e)) {
            foreach (var stageName in candidate.StageNames) {
                if (RuntimeAnalysisCache.TryGetSubscriptionBody(
                        Domain, candidate, stageName, out var cached)
                    && ReferenceEquals(cached, body)) {
                    entry = candidate;
                    targetStageName = stageName;
                    return true;
                }
            }
        }
        return false;
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