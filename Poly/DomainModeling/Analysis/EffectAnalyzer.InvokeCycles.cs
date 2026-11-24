// EffectAnalyzer partial: recursive invoke cycle detection (#535, #1101).
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

internal sealed partial class EffectAnalyzer {
    /// <summary>
    /// DMEFF021: refuse every recursive invoke cycle in the domain (owner ruling 2026-11-23, #1101). A guard does not
    /// make recursion safe: the simulator stops a deep chain cleanly, but the printed C# has no invoke depth guard and
    /// overflows the process.
    /// <para>
    /// Every copy of an action is its own node: the entity-level copy and each stage's copy, plus one node per stage
    /// entry and exit. An invoke on the same instance reaches the copy the module dispatches for the instance's
    /// <i>current</i> stage at the moment of the invoke: the stage's copy when that stage declares the action, else the
    /// entity-level copy. So the walk carries the set of stages the instance may be in:
    /// </para>
    /// <list type="bullet">
    ///   <item>a stage action, stage entry or stage exit body starts in <c>{that stage}</c>;</item>
    ///   <item>an entity-level body starts in every stage, or "no stage" for a stageless entity;</item>
    ///   <item><c>transition to T</c> sets it to <c>{T}</c> for the effects after it;</item>
    ///   <item>a conditional joins its branches (with no else, joins with the incoming set);</item>
    ///   <item>a <c>for</c> body may run zero or more times, so its result joins its incoming set;</item>
    ///   <item>after a same-instance invoke it becomes every stage, because the callee may transition.</item>
    /// </list>
    /// <para>
    /// An invoke across a relationship, a binder or an entity parameter reaches every copy of the action on the target
    /// entity (the target instance's stage is unknown). Edges are every invoke (same instance, over a relationship, a
    /// binder or an entity parameter, and the <c>for … invoke</c> fan-out, whether guarded or not), a transition into
    /// the target stage's entry and out of the possible current stages' exits, and a create's initial entry. A cycle is
    /// reported when it passes through an action: a cycle of transitions alone is DMEFF013's rule, which allows a
    /// guarded one. Subscriptions are not edges: the printed notify dispatch has its own depth guard.
    /// </para>
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
                graph.Walk(InvokeGraph.ActionNode(entity.Name, null, action.Name), entity, null, action, action.Effects, none);
            foreach (var stage in entity.Stages) {
                foreach (var action in stage.Actions)
                    graph.Walk(
                        InvokeGraph.ActionNode(entity.Name, stage.Name, action.Name),
                        entity, stage.Name, action, action.Effects, none);
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

        /// <summary>Every action copy, in declaration order, with its entity.</summary>
        public List<(string Node, string Entity)> Actions { get; } = [];

        public static string ActionNode(string entity, string? stage, string action) =>
            $"{entity}{Separator}{stage ?? ""}{Separator}action{Separator}{action}";
        public static string EntryNode(string entity, string stage) => $"{entity}{Separator}{stage}{Separator}entry{Separator}";
        public static string ExitNode(string entity, string stage) => $"{entity}{Separator}{stage}{Separator}exit{Separator}";

        private static bool IsAction(string node) {
            var parts = node.Split(Separator);
            return parts.Length == 4 && string.Equals(parts[2], "action", StringComparison.Ordinal);
        }

        /// <summary>
        /// The edges of one body. <paramref name="stage"/> is the stage the body runs in (null for an entity-level
        /// action, which may run in any stage); <paramref name="binders"/> maps a <c>for</c> binder to its entity.
        /// </summary>
        public void Walk(
            string from, Entity entity, string? stage, Action? action,
            IReadOnlyList<Effect> effects, IReadOnlyDictionary<string, string> binders) {
            if (IsAction(from) && !Actions.Any(a => a.Node == from))
                Actions.Add((from, entity.Name));
            _ = WalkBody(from, entity, action, effects, binders, StartStages(entity, stage));
        }

        private static HashSet<string?> StartStages(Entity entity, string? stage) {
            if (stage is not null)
                return new HashSet<string?>(StringComparer.Ordinal) { stage };
            return AllStages(entity);
        }

        private static HashSet<string?> AllStages(Entity entity) =>
            entity.Stages.Count == 0
                ? new HashSet<string?>(StringComparer.Ordinal) { null }
                : new HashSet<string?>(entity.Stages.Select(s => (string?)s.Name), StringComparer.Ordinal);

        /// <summary>Walks a body from the possible current stages and returns the stages it may leave the instance in.</summary>
        private HashSet<string?> WalkBody(
            string from, Entity entity, Action? action,
            IReadOnlyList<Effect> effects, IReadOnlyDictionary<string, string> binders, HashSet<string?> current) {
            var stages = new HashSet<string?>(current, StringComparer.Ordinal);
            foreach (var effect in effects) {
                switch (effect) {
                    case InvokeActionEffect invoke:
                        if (invoke.TargetRelationship is null) {
                            // Same instance: dispatch against the stage it is in at this effect.
                            foreach (var stage in stages)
                                if (CopyFor(entity, stage, invoke.ActionName) is { } copy)
                                    Edge(from, copy);
                            // The callee may transition, so the next effect sees any stage.
                            stages = AllStages(entity);
                        }
                        else if (TargetOf(entity, action, binders, invoke.TargetRelationship) is { } target) {
                            // Another instance: its stage is unknown, so every copy of the name can run.
                            foreach (var copy in CopiesOn(target, invoke.ActionName))
                                Edge(from, copy);
                        }
                        break;
                    case ForEachInvokeEffect fan:
                        if (NavigationTarget(entity, fan.RelationshipName) is { } fanTarget)
                            foreach (var copy in CopiesOn(fanTarget, fan.ActionName))
                                Edge(from, copy);
                        break;
                    case ForEachEffect loop:
                        var inner = new Dictionary<string, string>(binders, StringComparer.Ordinal);
                        if (NavigationTarget(entity, loop.RelationshipName) is { } loopTarget)
                            inner[loop.BinderName] = loopTarget;
                        // Zero or more iterations: the result includes the incoming stages, and a later
                        // iteration may start from a stage an earlier one reached.
                        var combined = new HashSet<string?>(stages, StringComparer.Ordinal);
                        while (true) {
                            var after = WalkBody(
                                from, entity, action, loop.Body, inner, new HashSet<string?>(combined, StringComparer.Ordinal));
                            var grown = new HashSet<string?>(combined, StringComparer.Ordinal);
                            grown.UnionWith(after);
                            if (grown.SetEquals(combined))
                                break;
                            combined = grown;
                        }
                        stages = combined;
                        break;
                    case ConditionalEffect conditional:
                        var then = WalkBody(
                            from, entity, action, conditional.ThenEffects, binders,
                            new HashSet<string?>(stages, StringComparer.Ordinal));
                        var otherwise = conditional.ElseEffects is { Count: > 0 } elseEffects
                            ? WalkBody(
                                from, entity, action, elseEffects, binders,
                                new HashSet<string?>(stages, StringComparer.Ordinal))
                            : new HashSet<string?>(stages, StringComparer.Ordinal);
                        stages = new HashSet<string?>(then, StringComparer.Ordinal);
                        stages.UnionWith(otherwise);
                        break;
                    case CompositeEffect composite:
                        stages = WalkBody(from, entity, action, composite.Effects, binders, stages);
                        break;
                    case StageTransitionEffect transition:
                        foreach (var left in stages)
                            if (left is not null)
                                Edge(from, ExitNode(entity.Name, left));
                        Edge(from, EntryNode(entity.Name, transition.TargetStage.StageName));
                        stages = new HashSet<string?>(StringComparer.Ordinal) { transition.TargetStage.StageName };
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
            return stages;
        }

        /// <summary>The copy a same-instance invoke runs in <paramref name="stage"/>: that stage's, else entity-level.</summary>
        private string? CopyFor(Entity entity, string? stage, string actionName) {
            if (stage is not null && entity.Stages.Any(s =>
                    string.Equals(s.Name, stage, StringComparison.Ordinal)
                    && s.Actions.Any(a => string.Equals(a.Name, actionName, StringComparison.Ordinal))))
                return ActionNode(entity.Name, stage, actionName);
            return entity.Actions.Any(a => string.Equals(a.Name, actionName, StringComparison.Ordinal))
                ? ActionNode(entity.Name, null, actionName)
                : null;
        }

        /// <summary>Every copy of <paramref name="actionName"/> on the entity, entity-level and per stage.</summary>
        private IEnumerable<string> CopiesOn(string entityName, string actionName) {
            if (!entities.TryGetValue(entityName, out var entity))
                yield break;
            if (entity.Actions.Any(a => string.Equals(a.Name, actionName, StringComparison.Ordinal)))
                yield return ActionNode(entityName, null, actionName);
            foreach (var stage in entity.Stages)
                if (stage.Actions.Any(a => string.Equals(a.Name, actionName, StringComparison.Ordinal)))
                    yield return ActionNode(entityName, stage.Name, actionName);
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
        /// step reads <c>Shipped (entry)</c>. An action copy reads by name: the cycle is within one entity, and the
        /// stage it runs in is the copy's identity, not part of the path the author wrote.</summary>
        public string Describe(IReadOnlyList<string> cycle) {
            var oneEntity = cycle.Select(n => n.Split(Separator)[0]).Distinct(StringComparer.Ordinal).Count() == 1;
            return string.Join(" → ", cycle.Select(node => {
                var parts = node.Split(Separator);
                var name = string.Equals(parts[2], "action", StringComparison.Ordinal)
                    ? parts[3]
                    : $"{parts[1]} ({parts[2]})";
                return oneEntity ? name : $"{parts[0]}.{name}";
            }));
        }
    }
}