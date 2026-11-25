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
    ///   <item><c>transition to T</c> sets it to <c>{T}</c> for the effects after it, or to every stage when T's
    ///   entry, flattened, can transition or invoke on the same instance: lowering inlines that entry right after the
    ///   stage assignment, so it may move the instance again before the next effect runs;</item>
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
    /// guarded one. A subscription handler is not an edge: the printed notify dispatch has its own depth guard, so a
    /// cycle driven only by subscriptions is not DMEFF021. It does count when widening the stage set after a
    /// transition into a watched stage: lowering runs those handlers before the body's next effect, and a handler
    /// may move this instance (see <see cref="SubscribersMayChangeStage"/>). A create-in widens the same way: no
    /// transition occurs, but lowering still notifies the creator's subscriptions watching the new child's initial
    /// stage, and that handler runs on this instance.
    /// </para>
    /// <para>
    /// #1116: an invoke over a relationship, a binder or an entity parameter, or a create whose initial entry invokes
    /// back, may land on <i>this same instance</i> and move its stage. After such an effect the walk widens the stage
    /// set to every stage when the target's type-level reach (over the action/entry call graph, ignoring stages)
    /// includes a transition on this entity type; a reach that never touches this type cannot move it. The decision is
    /// made at the type level: a same-type relationship may alias this instance, so it is treated as may-alias.
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
        private readonly Dictionary<string, bool> _subscriptionMovesStage = new(StringComparer.Ordinal);

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

        /// <summary>True when <paramref name="stageName"/>'s entry, flattened, can transition or invoke on the same
        /// instance, so lowering's inlined entry may leave the instance in some other stage.</summary>
        private bool EntryMayChangeStage(Entity entity, string stageName) {
            var stage = entity.Stages.FirstOrDefault(s => string.Equals(s.Name, stageName, StringComparison.Ordinal));
            return stage is not null && EffectsMayChangeStage(entity, stage.OnEntryEffects);
        }

        /// <summary>
        /// Whether the flattened effects can move an instance of <paramref name="self"/> to another stage. A
        /// same-instance transition or invoke already counts (#1101). An invoke across a relationship, a binder or an
        /// entity parameter, and a create's initial entry, count only when their type-level reach (#1116) reaches
        /// stage-changing code on <paramref name="self"/>: the invoked instance may be this one, but a reach that never
        /// touches this entity type cannot move it.
        /// </summary>
        private bool EffectsMayChangeStage(Entity self, IReadOnlyList<Effect> effects) =>
            EffectsMayChangeStage(self, effects, NoBinders);

        /// <summary><paramref name="binders"/> maps a <c>for</c> binder to its entity, as in <see cref="ReachEffects"/>:
        /// without it an invoke on a loop binder resolves to nothing and the body looks stage-preserving.</summary>
        private bool EffectsMayChangeStage(
            Entity self, IReadOnlyList<Effect> effects, IReadOnlyDictionary<string, string> binders) {
            foreach (var effect in effects) {
                switch (effect) {
                    case StageTransitionEffect:
                    case InvokeActionEffect { TargetRelationship: null }:
                        return true;
                    case InvokeActionEffect invoke:
                        if (TargetOf(self, null, binders, invoke.TargetRelationship!) is { } target
                            && ReachTransitions(self, target, invoke.ActionName))
                            return true;
                        break;
                    case ForEachInvokeEffect fan:
                        if (NavigationTarget(self, fan.RelationshipName) is { } fanTarget
                            && ReachTransitions(self, fanTarget, fan.ActionName))
                            return true;
                        break;
                    case CreateEntityInRelationshipEffect createIn:
                        if (NavigationTarget(self, createIn.RelationshipName) is { } createdName
                            && entities.TryGetValue(createdName, out var createdEntity)) {
                            if (ReachInitialEntry(self, createdName))
                                return true;
                            // The notify for the child's initial stage runs the creator's handler, which runs on
                            // this instance and may move it: the same site as the create-in cases below.
                            if (createdEntity.InitialStage() is { } initial
                                && SubscribersMayChangeStage(self, createdEntity, initial.Name))
                                return true;
                        }
                        break;
                    case CreateEntityInstance create:
                        if (ReachInitialEntry(self, create.Type.TypeName))
                            return true;
                        break;
                    case ConditionalEffect conditional:
                        if (EffectsMayChangeStage(self, conditional.ThenEffects, binders)
                            || (conditional.ElseEffects is { } elseEffects
                                && EffectsMayChangeStage(self, elseEffects, binders)))
                            return true;
                        break;
                    case CompositeEffect composite:
                        if (EffectsMayChangeStage(self, composite.Effects, binders))
                            return true;
                        break;
                    case ForEachEffect loop:
                        var inner = new Dictionary<string, string>(binders, StringComparer.Ordinal);
                        if (NavigationTarget(self, loop.RelationshipName) is { } loopTarget)
                            inner[loop.BinderName] = loopTarget;
                        if (EffectsMayChangeStage(self, loop.Body, inner))
                            return true;
                        break;
                }
            }
            return false;
        }

        private static readonly IReadOnlyDictionary<string, string> NoBinders =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// #1116: the type-level reach of an invoke, ignoring stages. True when any action copy, stage entry or
        /// create entry reachable from <paramref name="entityName"/>'s copies of <paramref name="actionName"/> can
        /// transition an instance of <paramref name="self"/>. The starting entity can be a different type; the walk
        /// follows every invoke back, so a reach that lands on <paramref name="self"/>'s type counts even when the
        /// invoked instance was reached through another relationship. Same-type relationships may alias this
        /// instance (the analysis cannot prove them distinct), so this is deliberately conservative.
        /// </summary>
        private bool ReachTransitions(Entity self, string entityName, string actionName) =>
            ReachActionCopies(self, entityName, actionName, new HashSet<string>(StringComparer.Ordinal));

        private bool ReachActionCopies(Entity self, string entityName, string actionName, HashSet<string> visited) {
            if (!entities.TryGetValue(entityName, out var entity))
                return false;
            if (entity.Actions.FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal)) is { } onEntity
                && ReachEffects(self, entity, onEntity, onEntity.Effects, NoBinders, visited,
                    $"{entityName}{Separator}action{Separator}{actionName}"))
                return true;
            foreach (var stage in entity.Stages)
                if (stage.Actions.FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal)) is { } onStage
                    && ReachEffects(self, entity, onStage, onStage.Effects, NoBinders, visited,
                        $"{entityName}{Separator}{stage.Name}{Separator}action{Separator}{actionName}"))
                    return true;
            return false;
        }

        private bool ReachInitialEntry(Entity self, string entityName) =>
            ReachInitialEntry(self, entityName, new HashSet<string>(StringComparer.Ordinal));

        private bool ReachInitialEntry(Entity self, string entityName, HashSet<string> visited) =>
            entities.TryGetValue(entityName, out var created) && created.InitialStage() is { } initial
                && ReachEntryOf(self, entityName, initial.Name, visited);

        /// <summary>A stage entry reached by a transition: lowering inlines it right after the stage assignment, so
        /// the reach continues through it.</summary>
        private bool ReachEntryOf(Entity self, string entityName, string stageName, HashSet<string> visited) {
            if (!entities.TryGetValue(entityName, out var entity)
                || entity.Stages.FirstOrDefault(s => string.Equals(s.Name, stageName, StringComparison.Ordinal)) is not { } stage)
                return false;
            return ReachEffects(self, entity, null, stage.OnEntryEffects, NoBinders, visited,
                $"{entityName}{Separator}{stageName}{Separator}entry{Separator}");
        }

        /// <summary>A stage exit reached by a transition out of the possible current stages. Type-level reach does
        /// not know the source stage, so every exit of the entity is followed.</summary>
        private bool ReachExitOf(Entity self, string entityName, string stageName, HashSet<string> visited) {
            if (!entities.TryGetValue(entityName, out var entity)
                || entity.Stages.FirstOrDefault(s => string.Equals(s.Name, stageName, StringComparison.Ordinal)) is not { } stage)
                return false;
            return ReachEffects(self, entity, null, stage.OnExitEffects, NoBinders, visited,
                $"{entityName}{Separator}{stageName}{Separator}exit{Separator}");
        }

        private bool ReachEffects(
            Entity self, Entity entity, Action? action, IReadOnlyList<Effect> effects,
            IReadOnlyDictionary<string, string> binders, HashSet<string> visited, string key) {
            if (effects.Count == 0 || !visited.Add(key))
                return false;
            return ReachBody(self, entity, action, effects, binders, visited);
        }

        /// <summary>
        /// One body's effects. Only the action, entry and exit keys dedupe: those are the points where the reach can
        /// revisit itself. A nested body (a conditional branch, a <c>for</c> body or a composite) takes no key, because
        /// a key made from its parent would collide for siblings — two conditionals in one body, or two loops over
        /// different relationships with the same binder name — and the second sibling would never be walked.
        /// </summary>
        private bool ReachBody(
            Entity self, Entity entity, Action? action, IReadOnlyList<Effect> effects,
            IReadOnlyDictionary<string, string> binders, HashSet<string> visited) {
            foreach (var effect in effects) {
                switch (effect) {
                    case StageTransitionEffect transition:
                        // The body runs on an instance of `entity`. A transition on this entity type moves it; on
                        // another type it still runs that stage's inlined entry, the possible source exits and the
                        // notify dispatch for the target stage, so the reach continues through them (#1116). That
                        // dispatch runs the handlers of every subscriber watching the stage, and one of them may
                        // reach a transition on this entity type.
                        if (string.Equals(entity.Name, self.Name, StringComparison.Ordinal))
                            return true;
                        if (ReachEntryOf(self, entity.Name, transition.TargetStage.StageName, visited))
                            return true;
                        foreach (var source in entity.Stages)
                            if (ReachExitOf(self, entity.Name, source.Name, visited))
                                return true;
                        if (SubscribersReachStageChange(self, entity, transition.TargetStage.StageName, visited))
                            return true;
                        break;
                    case InvokeActionEffect invoke:
                        if (invoke.TargetRelationship is null) {
                            if (ReachActionCopies(self, entity.Name, invoke.ActionName, visited))
                                return true;
                        }
                        else if (TargetOf(entity, action, binders, invoke.TargetRelationship) is { } target
                                 && ReachActionCopies(self, target, invoke.ActionName, visited))
                            return true;
                        break;
                    case ForEachInvokeEffect fan:
                        if (NavigationTarget(entity, fan.RelationshipName) is { } fanTarget
                            && ReachActionCopies(self, fanTarget, fan.ActionName, visited))
                            return true;
                        break;
                    case ForEachEffect loop:
                        var inner = new Dictionary<string, string>(binders, StringComparer.Ordinal);
                        if (NavigationTarget(entity, loop.RelationshipName) is { } loopTarget)
                            inner[loop.BinderName] = loopTarget;
                        if (ReachBody(self, entity, action, loop.Body, inner, visited))
                            return true;
                        break;
                    case ConditionalEffect conditional:
                        if (ReachBody(self, entity, action, conditional.ThenEffects, binders, visited))
                            return true;
                        if (conditional.ElseEffects is { } elseEffects
                            && ReachBody(self, entity, action, elseEffects, binders, visited))
                            return true;
                        break;
                    case CompositeEffect composite:
                        if (ReachBody(self, entity, action, composite.Effects, binders, visited))
                            return true;
                        break;
                    case CreateEntityInRelationshipEffect createIn:
                        // A create-in lands the child in its initial stage without a transition, and lowering
                        // still dispatches that stage to the creator's subscriptions over this relationship: the
                        // handler runs on this instance and may move it, before the body's next effect.
                        if (NavigationTarget(entity, createIn.RelationshipName) is { } createdName
                            && entities.TryGetValue(createdName, out var createdEntity)) {
                            if (ReachInitialEntry(self, createdName, visited))
                                return true;
                            if (createdEntity.InitialStage() is { } initial
                                && SubscribersReachStageChange(self, createdEntity, initial.Name, visited))
                                return true;
                        }
                        break;
                    case CreateEntityInstance create:
                        if (ReachInitialEntry(self, create.Type.TypeName, visited))
                            return true;
                        break;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether <paramref name="stageName"/> is watched by a <c>when</c> handler on some entity whose relationship
        /// targets <paramref name="entity"/>, and that handler's body can move an instance of <paramref name="entity"/>.
        /// Lowering emits the notify dispatch right after the stage assignment and before the next effect of the
        /// transition's body, so such a handler runs there and may move this instance again. This widens the stage set
        /// only: a subscription is not an invoke-graph edge, so a cycle with no invoke re-entry stays out of DMEFF021.
        /// </summary>
        private bool SubscribersMayChangeStage(Entity entity, string stageName) =>
            SubscribersMayChangeStage(entity, entity, stageName);

        /// <summary>The memoized form for a publisher other than <paramref name="self"/>'s own type (a create-in's
        /// new child), whose subscriptions run on <paramref name="self"/> as well: the key carries both types,
        /// because the same publisher and stage may or may not move a given instance's entity type.</summary>
        private bool SubscribersMayChangeStage(Entity self, Entity publisher, string stageName) {
            var cacheKey = $"{self.Name}{Separator}{publisher.Name}{Separator}{stageName}";
            if (_subscriptionMovesStage.TryGetValue(cacheKey, out var cached))
                return cached;
            var moves = SubscribersReachStageChange(
                self, publisher, stageName, new HashSet<string>(StringComparer.Ordinal));
            _subscriptionMovesStage[cacheKey] = moves;
            return moves;
        }

        /// <summary>
        /// Whether a <c>when</c> handler watching <paramref name="publisher"/>'s <paramref name="stageName"/> can
        /// reach a stage change on <paramref name="self"/>. The handler runs on the subscriber, so its own
        /// transitions are the subscriber's; the reach follows its invokes and its peer transitions back and
        /// returns true only when it reaches a transition on this entity type. Handlers are keyed like actions so
        /// a subscription chain that comes back around terminates.
        /// </summary>
        private bool SubscribersReachStageChange(
            Entity self, Entity publisher, string stageName, HashSet<string> visited) {
            foreach (var subscriber in entities.Values) {
                var ordinal = 0;
                foreach (var subscription in SubscriptionsOf(subscriber)) {
                    ordinal++;
                    if (!subscription.StageNames.Any(n => string.Equals(n, stageName, StringComparison.Ordinal))
                        || NavigationTarget(subscriber, subscription.RelationshipName) is not { } target
                        || !string.Equals(target, publisher.Name, StringComparison.Ordinal))
                        continue;
                    if (ReachEffects(self, subscriber, null, subscription.Effects, NoBinders, visited,
                            $"{subscriber.Name}{Separator}when{Separator}{ordinal}{Separator}{stageName}")) {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Every <c>when</c> handler on the entity, entity-level and per stage. A stage-scoped handler is
        /// included: the subscriber's stage is unknown here, so it may be in the stage that gates the handler.</summary>
        private static IEnumerable<StageSubscription> SubscriptionsOf(Entity entity) {
            foreach (var subscription in entity.Subscriptions)
                yield return subscription;
            foreach (var stage in entity.Stages)
                foreach (var subscription in stage.Subscriptions)
                    yield return subscription;
        }

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
                            // #1116: the invoked instance may be this one; a reach through it that transitions this
                            // entity type leaves the instance in any stage before the next effect runs.
                            if (ReachTransitions(entity, target, invoke.ActionName))
                                stages = AllStages(entity);
                        }
                        break;
                    case ForEachInvokeEffect fan:
                        if (NavigationTarget(entity, fan.RelationshipName) is { } fanTarget) {
                            foreach (var copy in CopiesOn(fanTarget, fan.ActionName))
                                Edge(from, copy);
                            if (ReachTransitions(entity, fanTarget, fan.ActionName))
                                stages = AllStages(entity);
                        }
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
                        // Lowering emits the notify dispatch for the target stage before the next effect, so a
                        // watching handler that can move this instance leaves it in any stage too.
                        stages = EntryMayChangeStage(entity, transition.TargetStage.StageName)
                            || SubscribersMayChangeStage(entity, transition.TargetStage.StageName)
                            ? AllStages(entity)
                            : new HashSet<string?>(StringComparer.Ordinal) { transition.TargetStage.StageName };
                        break;
                    case CreateEntityInRelationshipEffect createIn:
                        if (NavigationTarget(entity, createIn.RelationshipName) is { } createdName) {
                            InitialEntry(from, createdName);
                            // The create-in lands the child in its initial stage without a transition, and
                            // lowering still dispatches that stage to the creator's subscriptions over this
                            // relationship, so a handler that moves this instance leaves it in any stage too.
                            if (ReachInitialEntry(entity, createdName)
                                || (entities.TryGetValue(createdName, out var createdEntity)
                                    && createdEntity.InitialStage() is { } initial
                                    && SubscribersMayChangeStage(entity, createdEntity, initial.Name)))
                                stages = AllStages(entity);
                        }
                        break;
                    case CreateEntityInstance create:
                        InitialEntry(from, create.Type.TypeName);
                        if (ReachInitialEntry(entity, create.Type.TypeName))
                            stages = AllStages(entity);
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