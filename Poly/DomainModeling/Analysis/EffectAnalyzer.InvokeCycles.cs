// EffectAnalyzer partial: recursive invoke cycle detection (#535, #1101).
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

internal sealed partial class EffectAnalyzer {
    /// <summary>
    /// DMEFF021: refuse every recursive invoke cycle in the domain (owner ruling 2026-11-23, #1101). A guard does not
    /// make recursion safe: the simulator stops a deep chain cleanly, but the printed C# has no invoke depth guard and
    /// overflows the process. The graph has one node per action name on an entity (every copy of the name, entity-level
    /// and per stage, contributes its edges) and one per stage entry and exit; its edges are every invoke (same entity,
    /// over a relationship, a binder or an entity parameter, and the <c>for … invoke</c> fan-out, whether guarded or
    /// not), a transition into a stage's entry and out of the current stage's exit, and a create's initial entry. A
    /// cycle is reported when it passes through an action: a cycle of transitions alone is DMEFF013's rule, which
    /// allows a guarded one. Subscriptions are not edges: the printed notify dispatch has its own depth guard.
    /// </summary>
    private static void ValidateInvokeCycles(AnalysisContext context, Domain domain) {
        // A duplicated entity name is the structural pass's error; the graph takes the first of each name.
        var entities = new Dictionary<string, Entity>(StringComparer.Ordinal);
        foreach (var candidate in domain.Types.OfType<Entity>())
            entities.TryAdd(candidate.Name, candidate);
        var graph = new InvokeGraph(entities);
        var none = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in domain.Types.OfType<Entity>()) {
            foreach (var action in entity.Actions)
                graph.Walk(InvokeGraph.ActionNode(entity.Name, action.Name), entity, null, action, action.Effects, none);
            foreach (var stage in entity.Stages) {
                foreach (var action in stage.Actions)
                    graph.Walk(InvokeGraph.ActionNode(entity.Name, action.Name), entity, stage.Name, action, action.Effects, none);
                graph.Walk(InvokeGraph.EntryNode(entity.Name, stage.Name), entity, stage.Name, null, stage.OnEntryEffects, none);
                graph.Walk(InvokeGraph.ExitNode(entity.Name, stage.Name), entity, stage.Name, null, stage.OnExitEffects, none);
            }
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (start, owner) in graph.Actions) {
            if (reported.Contains(start) || graph.PathBack(start) is not { } cycle)
                continue;
            reported.UnionWith(cycle);
            context.ReportError(
                entities[owner],
                $"Entity '{owner}' has a recursive invoke cycle: {graph.Describe(cycle)}. A recursive invoke is not " +
                "supported, guarded or not: the printed C# has no invoke depth guard, so a recursion that runs deep " +
                "overflows the process. Remove one invoke on the cycle.",
                DomainModelDiagnosticCodes.InvokeCycle);
        }
    }

    private sealed class InvokeGraph(IReadOnlyDictionary<string, Entity> entities) {
        private const char Separator = '\u0001';
        private readonly Dictionary<string, HashSet<string>> _edges = new(StringComparer.Ordinal);

        /// <summary>Every action node, in declaration order, with its entity.</summary>
        public List<(string Node, string Entity)> Actions { get; } = [];

        public static string ActionNode(string entity, string action) => $"{entity}{Separator}{action}";
        public static string EntryNode(string entity, string stage) => $"{entity}{Separator}{stage}{Separator}entry";
        public static string ExitNode(string entity, string stage) => $"{entity}{Separator}{stage}{Separator}exit";

        private static bool IsAction(string node) => node.Count(c => c == Separator) == 1;

        /// <summary>
        /// The edges of one body. <paramref name="stage"/> is the stage the body runs in (null for an entity-level
        /// action, which may run in any stage); <paramref name="binders"/> maps a <c>for</c> binder to its entity.
        /// </summary>
        public void Walk(
            string from, Entity entity, string? stage, Action? action,
            IReadOnlyList<Effect> effects, IReadOnlyDictionary<string, string> binders) {
            if (IsAction(from) && !Actions.Any(a => a.Node == from))
                Actions.Add((from, entity.Name));
            foreach (var effect in effects) {
                switch (effect) {
                    case InvokeActionEffect invoke:
                        var target = invoke.TargetRelationship is null
                            ? entity.Name
                            : TargetOf(entity, action, binders, invoke.TargetRelationship);
                        if (target is not null)
                            Edge(from, ActionNode(target, invoke.ActionName));
                        break;
                    case ForEachInvokeEffect fan:
                        if (NavigationTarget(entity, fan.RelationshipName) is { } fanTarget)
                            Edge(from, ActionNode(fanTarget, fan.ActionName));
                        break;
                    case ForEachEffect loop:
                        var inner = new Dictionary<string, string>(binders, StringComparer.Ordinal);
                        if (NavigationTarget(entity, loop.RelationshipName) is { } loopTarget)
                            inner[loop.BinderName] = loopTarget;
                        Walk(from, entity, stage, action, loop.Body, inner);
                        break;
                    case ConditionalEffect conditional:
                        Walk(from, entity, stage, action, conditional.ThenEffects, binders);
                        if (conditional.ElseEffects is { Count: > 0 } otherwise)
                            Walk(from, entity, stage, action, otherwise, binders);
                        break;
                    case CompositeEffect composite:
                        Walk(from, entity, stage, action, composite.Effects, binders);
                        break;
                    case StageTransitionEffect transition:
                        Edge(from, EntryNode(entity.Name, transition.TargetStage.StageName));
                        foreach (var left in stage is null ? entity.Stages.Select(s => s.Name) : [stage])
                            Edge(from, ExitNode(entity.Name, left));
                        break;
                    case CreateEntityInRelationshipEffect createIn:
                        if (NavigationTarget(entity, createIn.RelationshipName) is { } created)
                            InitialEntry(from, created);
                        break;
                    case CreateEntityInstance create:
                        InitialEntry(from, create.Type.TypeName);
                        break;
                }
            }
        }

        private void InitialEntry(string from, string entityName) {
            if (entities.TryGetValue(entityName, out var created) && created.InitialStage() is { } initial)
                Edge(from, EntryNode(entityName, initial.Name));
        }

        private string? TargetOf(Entity entity, Action? action, IReadOnlyDictionary<string, string> binders, string name) {
            if (binders.TryGetValue(name, out var bound))
                return bound;
            if (NavigationTarget(entity, name) is { } navigated)
                return navigated;
            var parameter = action?.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
            return parameter is not null && entities.ContainsKey(parameter.Type.TypeName) ? parameter.Type.TypeName : null;
        }

        private static string? NavigationTarget(Entity entity, string relationship) =>
            entity.Navigations.FirstOrDefault(n => string.Equals(n.Name, relationship, StringComparison.Ordinal))?.Target.TypeName;

        private void Edge(string from, string to) {
            if (!_edges.TryGetValue(from, out var targets))
                _edges[from] = targets = new HashSet<string>(StringComparer.Ordinal);
            targets.Add(to);
        }

        /// <summary>The shortest path from <paramref name="start"/> back to itself, start included at both ends.</summary>
        public List<string>? PathBack(string start) {
            var previous = new Dictionary<string, string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (var next in _edges.GetValueOrDefault(start) ?? []) {
                if (next == start)
                    return [start, start];
                if (previous.TryAdd(next, start))
                    queue.Enqueue(next);
            }
            while (queue.Count > 0) {
                var node = queue.Dequeue();
                foreach (var next in _edges.GetValueOrDefault(node) ?? []) {
                    if (next == start) {
                        var path = new List<string> { start, node };
                        for (var at = node; previous[at] != start; at = previous[at])
                            path.Insert(1, previous[at]);
                        path.Add(start);
                        return path;
                    }
                    if (previous.TryAdd(next, node))
                        queue.Enqueue(next);
                }
            }
            return null;
        }

        /// <summary><c>Ping → Pong → Ping</c> on one entity; <c>A.Go → B.Ping → A.Go</c> across entities; a stage
        /// step reads <c>Shipped (entry)</c>.</summary>
        public string Describe(IReadOnlyList<string> cycle) {
            var oneEntity = cycle.Select(n => n.Split(Separator)[0]).Distinct(StringComparer.Ordinal).Count() == 1;
            return string.Join(" → ", cycle.Select(node => {
                var parts = node.Split(Separator);
                var name = parts.Length == 3 ? $"{parts[1]} ({parts[2]})" : parts[1];
                return oneEntity ? name : $"{parts[0]}.{name}";
            }));
        }
    }
}