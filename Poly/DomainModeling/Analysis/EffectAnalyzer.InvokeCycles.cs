// EffectAnalyzer partial: unconditional same-entity invoke cycle detection (#535).
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

internal sealed partial class EffectAnalyzer {
    /// <summary>
    /// DMEFF021: reject an unconditional invoke cycle on one entity (<c>Ping: invoke Pong</c> +
    /// <c>Pong: invoke Ping</c>, or a self-invoke). The runtime cuts an invoke chain at a depth
    /// guard, so every invocation of an action on the cycle fails; a cycle that passes a real
    /// guard may terminate, so it stays allowed — the same rule DMEFF013 applies to automatic
    /// stage transitions.
    /// </summary>
    private static void ValidateUnconditionalInvokeCycles(AnalysisContext context, Entity entity) {
        var cycle = FindCycle(UnconditionalInvokeGraph(entity));
        if (cycle is null)
            return;
        context.ReportError(
            entity,
            $"Entity '{entity.Name}' has an unconditional invoke cycle: {string.Join(" → ", cycle)}. " +
            "Every invocation of an action on it fails at run time (the invoke depth guard stops the chain); " +
            "add a guard (if) on at least one invoke, or remove one.",
            DomainModelDiagnosticCodes.UnconditionalInvokeCycle);
    }

    /// <summary>
    /// Action name → the actions it unconditionally invokes on the same instance. A name with
    /// several bodies (one entity-level action plus a copy per stage) keeps only the targets every
    /// copy invokes: an invoke runs exactly one branch, so one guarded copy makes the edge
    /// conditional.
    /// </summary>
    private static Dictionary<string, HashSet<string>> UnconditionalInvokeGraph(Entity entity) {
        var copies = new Dictionary<string, List<Action>>(StringComparer.Ordinal);
        void Add(Action action) {
            if (!copies.TryGetValue(action.Name, out var list))
                copies[action.Name] = list = [];
            list.Add(action);
        }

        foreach (var action in entity.Actions)
            Add(action);
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions)
                Add(action);

        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (name, bodies) in copies) {
            HashSet<string>? shared = null;
            foreach (var body in bodies) {
                var targets = UnconditionalInvokeTargets(body.Effects, guarded: false);
                if (shared is null)
                    shared = targets;
                else
                    shared.IntersectWith(targets);
            }
            if (shared is { Count: > 0 })
                adjacency[name] = shared;
        }
        return adjacency;
    }

    /// <summary>
    /// The same-entity actions reached without passing a real guard. A <c>for</c> body may run
    /// zero times, so nothing inside it is unconditional; a cross-entity invoke (<c>invoke</c> on a
    /// relationship, or the fan-out <c>for … invoke</c>) names another entity's action and is not
    /// an edge here.
    /// </summary>
    private static HashSet<string> UnconditionalInvokeTargets(IReadOnlyList<Effect> effects, bool guarded) {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in effects) {
            switch (effect) {
                case InvokeActionEffect { TargetRelationship: null } invoke when !guarded:
                    targets.Add(invoke.ActionName);
                    break;
                case ConditionalEffect conditional:
                    var alwaysTrue = IsProvablyAlwaysTrue(conditional.Condition);
                    targets.UnionWith(UnconditionalInvokeTargets(
                        conditional.ThenEffects, guarded || !alwaysTrue));
                    if (conditional.ElseEffects is { Count: > 0 })
                        targets.UnionWith(UnconditionalInvokeTargets(conditional.ElseEffects, guarded: true));
                    break;
                case CompositeEffect composite:
                    targets.UnionWith(UnconditionalInvokeTargets(composite.Effects, guarded));
                    break;
            }
        }
        return targets;
    }
}