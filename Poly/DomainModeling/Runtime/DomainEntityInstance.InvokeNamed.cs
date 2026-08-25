using Poly.DomainModeling.Analysis;
using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Runtime;

public sealed partial record DomainEntityInstance {
    /// <summary>
    /// VM protocol for AST methods with no CLR MethodInfo: dispatch by name to
    /// <see cref="InvokeAction"/>. Args map to the action's parameter names in
    /// declaration order. Re-entrancy / <c>_invokeDepth</c> stays with InvokeAction.
    /// </summary>
    internal object? InvokeNamed(string name, object?[] args) {
        ArgumentException.ThrowIfNullOrEmpty(name);
        args ??= [];

        var action = ResolveActionForNamedInvoke(name);
        if (action is null)
            throw new InvalidOperationException(
                $"Action '{name}' not found on entity '{Entity.Name}'.");

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
            throw new InvalidOperationException(
                result.ErrorMessage
                ?? (result.FailedGuards.Count > 0
                    ? $"invoke '{name}' blocked by guards: {string.Join(", ", result.FailedGuards)}"
                    : $"invoke '{name}' failed."));
        }
        return result.ResultInstance;
    }

    private Action? ResolveActionForNamedInvoke(string name) {
        if (Domain is not null) {
            var analysis = RuntimeAnalysisCache.GetOrAnalyze(Domain);
            analysis.TryResolveAction(Domain, Entity, CurrentStage, name, out var action);
            return action;
        }
        return ResolveStandaloneAction(name);
    }
}
