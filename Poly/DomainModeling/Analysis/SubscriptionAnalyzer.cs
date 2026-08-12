using Poly.Analysis;
using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Lint-only: unified subscription contract, causality, and replay diagnostics.
/// Writes no metadata others read. Depends on Semantic (type lookup) and Capability
/// (transition targets for causality filtering).
/// </summary>
internal sealed class SubscriptionAnalyzer : INodeAnalyzer {
    public const string Id = "DomainSubscriptionAnalyzer";
    public string PassName => Id;
    // DomainTypeLookupMetadata (Semantic) + ActionCapabilityMetadata (Capability)
    // for causality edges filtered to transitions that can produce cycles.
    public string[] Dependencies => [SemanticDomainAnalyzer.Id, CapabilityAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) return;

        if (node is Domain domain) {
            ValidateDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    /// <summary>
    /// Relationship lookup for domain-bound name resolve (amu-w1-3). Prefers the
    /// catalog relationship bag; falls back to the intermediate RLM bag published
    /// by <see cref="SemanticDomainAnalyzer"/>. Null when neither is available
    /// (stripped/failed trees) — callers skip validation rather than false-report.
    /// </summary>
    private static RelationshipLookupMetadata? ResolveRelationshipLookup(AnalysisContext context, Domain domain) =>
        context.GetRelationshipLookup(domain) ?? context.GetMetadata<RelationshipLookupMetadata>(default);

    private static void ValidateDomain(AnalysisContext context, Domain domain) {
        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) return;

        foreach (var entity in lookup.Entities) {
            // Entity-level when is always-active (entity-level dispatch); same contract + binding
            // validation as stage-scoped (including optional peer binder).
            foreach (var entitySub in entity.Subscriptions) {
                ValidateSubscription(context, entitySub, entity, domain, lookup);
            }

            foreach (var stage in entity.Stages) {
                // ── Check for duplicate subscription keys on this stage ────
                for (int i = 0; i < stage.Subscriptions.Count; i++) {
                    for (int j = i + 1; j < stage.Subscriptions.Count; j++) {
                        if (SemanticKeyMatch(stage.Subscriptions[i], stage.Subscriptions[j])) {
                            context.ReportWarning(
                                stage.Subscriptions[i],
                                $"Duplicate stage subscription key on stage '{stage.Name}' of entity '{entity.Name}': " +
                                $"{SubscriptionKey(stage.Subscriptions[i])}. " +
                                $"Remove-all semantics apply.",
                                DomainModelDiagnosticCodes.SubscriptionContractMismatch);
                        }
                    }
                }

                foreach (var subscription in stage.Subscriptions) {
                    ValidateSubscription(context, subscription, entity, domain, lookup);
                    // ── Replay-safety check (folded from SubscriptionReplaySafetyAnalyzer — D2.5) ──
                    var hasNonIdempotent = EffectHelpers.FlattenEffects(subscription.Effects).Any(static e =>
                        e is CreateEntityInstance or StageTransitionEffect);
                    if (hasNonIdempotent) {
                        context.ReportHint(
                            subscription,
                            $"Stage subscription (when {subscription.RelationshipName} -> {string.Join("/", subscription.StageNames)}) " +
                            $"has idempotency risk under replay because it performs create, transition, or link effects.",
                            DomainModelDiagnosticCodes.SubscriptionIdempotencyReplay);
                    }
                }
            }
        }

        // ── Causality cycle detection (restored full version — D2′.2) ──
        // Build edge graph where E₁ → E₂ exists only when E₂ has at least one action
        // that transitions to a stage E₁'s subscription watches (avoids false positives).
        // amu-w1-3: name resolve via catalog/RLM lookup (no per-subscription scan).
        var relLookup = ResolveRelationshipLookup(context, domain);
        var edges = new List<(string FromEntity, string ToEntity, string StageName)>();
        foreach (var entity in lookup.Entities) {
            foreach (var stage in entity.Stages) {
                foreach (var sub in stage.Subscriptions) {
                    if (relLookup is null
                        || !relLookup.TryGetRelationship(entity.Name, sub.RelationshipName, out var relationship))
                        continue;
                    var targetType = lookup.Types.GetValueOrDefault(relationship.Target.TypeName);
                    if (targetType is not Entity targetEntity) continue;
                    foreach (var sn in sub.StageNames) {
                        // Filter: only add edge if target entity has an action that
                        // transitions to the watched stage (capability-aware).
                        if (TargetHasTransitionTo(targetEntity, sn, context)) {
                            edges.Add((entity.Name, targetEntity.Name, sn));
                        }
                    }
                }
            }
        }

        // DFS cycle detection over the filtered edge graph
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (from, to, _) in edges) {
            if (!adjacency.TryGetValue(from, out var list))
                adjacency[from] = list = new();
            if (!list.Contains(to, StringComparer.Ordinal))
                list.Add(to);
        }

        foreach (var (from, _) in adjacency) {
            var cycle = DfsDetectCycle(from, adjacency, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
            if (cycle is not null) {
                var key = string.Join("→", cycle);
                if (reported.Add(key)) {
                    context.ReportWarning(domain,
                        $"Stage-subscription cycle detected: {string.Join(" → ", cycle)}. " +
                        "This may produce infinite loops at runtime if both sides transition to the subscribed stages.",
                        DomainModelDiagnosticCodes.SubscriptionCausalityCycle);
                }
            }
        }
    }

    /// <summary>Returns true if <paramref name="entity"/> has an action that transitions to <paramref name="stageName"/>.</summary>
    private static bool TargetHasTransitionTo(Entity entity, string stageName, AnalysisContext context) {
        foreach (var action in entity.Actions) {
            var cap = context.GetMetadata<ActionCapabilityMetadata>(action);
            if (cap is not null) {
                if (cap.View.TransitionTargets.Any(s => string.Equals(s.Name, stageName, StringComparison.Ordinal)))
                    return true;
            }
            else {
                // Fallback: scan effects directly
                foreach (var effect in action.Effects)
                    if (EffectWalksToStage(effect, stageName)) return true;
            }
        }
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions) {
                var cap = context.GetMetadata<ActionCapabilityMetadata>(action);
                if (cap is not null) {
                    if (cap.View.TransitionTargets.Any(s => string.Equals(s.Name, stageName, StringComparison.Ordinal)))
                        return true;
                }
                else {
                    foreach (var effect in action.Effects)
                        if (EffectWalksToStage(effect, stageName)) return true;
                }
            }
        return false;
    }

    private static bool EffectWalksToStage(Effect effect, string stageName) {
        if (effect is StageTransitionEffect ste && string.Equals(ste.TargetStage.StageName, stageName, StringComparison.Ordinal))
            return true;
        if (effect is CompositeEffect ce)
            return ce.Effects.Any(e => EffectWalksToStage(e, stageName));
        if (effect is ConditionalEffect cond)
            return cond.ThenEffects.Any(e => EffectWalksToStage(e, stageName))
                || (cond.ElseEffects?.Any(e => EffectWalksToStage(e, stageName)) ?? false);
        return false;
    }

    /// <summary>DFS cycle detection. Returns the cycle path if found, null otherwise.</summary>
    private static List<string>? DfsDetectCycle(string start, Dictionary<string, List<string>> adj,
        HashSet<string> visiting, HashSet<string> visited) {
        if (visiting.Contains(start))
            return [start]; // back edge found
        if (visited.Contains(start))
            return null;
        visiting.Add(start);
        if (adj.TryGetValue(start, out var neighbors)) {
            foreach (var next in neighbors) {
                var sub = DfsDetectCycle(next, adj, visiting, visited);
                if (sub is not null) {
                    if (sub.Count > 0 && sub[0] != start)
                        sub.Insert(0, start);
                    visiting.Remove(start);
                    visited.Add(start);
                    return sub;
                }
            }
        }
        visiting.Remove(start);
        visited.Add(start);
        return null;
    }

    private static void ValidateSubscription(
        AnalysisContext context,
        StageSubscription subscription,
        Entity entity,
        Domain domain,
        DomainTypeLookupMetadata lookup) {

        // ── Empty/blank checks ──────────────────────────────────
        if (string.IsNullOrWhiteSpace(subscription.RelationshipName)) {
            context.ReportError(
                subscription,
                "Stage subscription has an empty or missing relationship name.",
                DomainModelDiagnosticCodes.SubscriptionContractMismatch);
            return;
        }

        if (subscription.StageNames.Count == 0) {
            context.ReportError(
                subscription,
                "Stage subscription must target at least one stage name.",
                DomainModelDiagnosticCodes.SubscriptionContractMismatch);
            return;
        }

        foreach (var sn in subscription.StageNames) {
            if (string.IsNullOrWhiteSpace(sn)) {
                context.ReportError(
                    subscription,
                    "Stage subscription contains an empty stage name.",
                    DomainModelDiagnosticCodes.SubscriptionContractMismatch);
            }
        }

        if (!Enum.IsDefined(subscription.Quantifier)) {
            context.ReportError(
                subscription,
                $"Stage subscription has an undefined quantifier '{subscription.Quantifier}'.",
                DomainModelDiagnosticCodes.SubscriptionContractMismatch);
        }

        // ── Relationship resolution ──────────────────────────────
        // amu-w1-3: catalog/RLM name resolve (no domain.Relationships scan).
        var relLookup = ResolveRelationshipLookup(context, domain);
        if (relLookup is null) return; // bag unavailable — skip (no false positive)
        var relationship = relLookup.TryGetRelationship(entity.Name, subscription.RelationshipName, out var rel)
            ? rel
            : null;

        if (relationship is null) {
            context.ReportError(
                subscription,
                $"Stage subscription references relationship '{subscription.RelationshipName}' which is not found " +
                $"on entity '{entity.Name}'.",
                DomainModelDiagnosticCodes.SubscriptionContractMismatch);
            return;
        }

        // ── Target entity resolution ─────────────────────────────
        if (!lookup.Types.TryGetValue(relationship.Target.TypeName, out var targetType) || targetType is not Entity targetEntity) {
            context.ReportError(
                subscription,
                $"Stage subscription targets entity '{relationship.Target.TypeName}' via relationship " +
                $"'{subscription.RelationshipName}', but that entity does not exist in the domain.",
                DomainModelDiagnosticCodes.SubscriptionContractMismatch);
            return;
        }

        // ── Target stage existence ───────────────────────────────
        foreach (var stageName in subscription.StageNames) {
            if (!targetEntity.Stages.Any(s => string.Equals(s.Name, stageName, StringComparison.Ordinal))) {
                context.ReportError(
                    subscription,
                    $"Stage subscription references stage '{stageName}' on entity '{targetEntity.Name}' " +
                    $"via relationship '{subscription.RelationshipName}', but that stage does not exist on the target entity. " +
                    $"Available stages: {string.Join(", ", targetEntity.Stages.Select(s => s.Name))}",
                    DomainModelDiagnosticCodes.SubscriptionContractMismatch);
            }
        }

        // ── Quantifier vs cardinality (fail-closed: reject, don't warn) ──
        bool isSingularFromSource = relationship.Cardinality is RelationshipCardinality.OneToOne
                                                         or RelationshipCardinality.ManyToOne;
        if (subscription.Quantifier != StageSubscriptionQuantifier.Each && isSingularFromSource) {
            context.ReportError(
                subscription,
                $"Stage subscription uses quantifier '{subscription.Quantifier}' on one-to-one relationship " +
                $"'{subscription.RelationshipName}'. '{subscription.Quantifier}' is meaningless on singular " +
                "relationships — omit it (Each) or change relationship cardinality.",
                DomainModelDiagnosticCodes.SubscriptionContractMismatch);
        }

        // ── Validate that subscription effect expressions reference known properties ──
        var subscriberRelNames = new HashSet<string>(
            domain.Relationships
                .Where(r => string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal))
                .Select(r => r.Name),
            StringComparer.Ordinal);
        ValidateSubscriptionEffectBindings(context, subscription, entity, targetEntity, subscriberRelNames);
    }

    /// <summary>
    /// Validates subscription effect bindings: subscriber props, optional peer binder
    /// (<c>as name</c> → scalar <c>name Prop</c>), reject legacy event, unbound peer-like
    /// roots, peer l-values, and nested peer path-prefix (F1–F4, F7).
    /// </summary>
    private static void ValidateSubscriptionEffectBindings(
        AnalysisContext context,
        StageSubscription subscription,
        Entity subscriberEntity,
        Entity targetEntity,
        HashSet<string> subscriberRelNames) {
        if (subscription.Effects.Count == 0) return;

        var subscriberProps = new HashSet<string>(
            subscriberEntity.Properties.Select(p => p.Name), StringComparer.Ordinal);
        var targetProps = new HashSet<string>(
            targetEntity.Properties.Select(p => p.Name), StringComparer.Ordinal);
        var peerBinding = subscription.PeerBinding;

        foreach (var effect in subscription.Effects) {
            var flags = new SubBindingFlags();
            CollectPropertyAccesses(effect, peerBinding, subscriberRelNames, flags, isAssignTarget: false);

            if (flags.UsesLegacyEvent) {
                context.ReportError(
                    subscription,
                    "Subscription effects must not use 'event' / 'event.Prop'. " +
                    "Declare a peer binder: when Rel Stage as name { … name Prop … }.",
                    DomainModelDiagnosticCodes.SubscriptionEffectBinding);
            }

            if (flags.UnboundPeerRoot is { Length: > 0 }) {
                var binderHint = peerBinding is { Length: > 0 }
                    ? $"It is not peer binder '{peerBinding}' and not a subscriber relationship. " +
                      "Use the binder name for peer fields, or a real relationship for subscriber navigation."
                    : "No peer binder was declared. Use 'when Rel Stage as name { … name Prop … }' " +
                      "to read the transitioned peer, or a real relationship name for subscriber navigation.";
                context.ReportError(
                    subscription,
                    $"Subscription effect path-prefix root '{flags.UnboundPeerRoot}' is invalid. {binderHint}",
                    DomainModelDiagnosticCodes.SubscriptionEffectBinding);
            }

            if (flags.PeerAsAssignTarget) {
                context.ReportError(
                    subscription,
                    "Peer binder path-prefix cannot be an assign target. " +
                    "Use peer fields only on the right-hand side (values, conditions, initializers).",
                    DomainModelDiagnosticCodes.SubscriptionEffectBinding);
            }

            if (flags.NestedPeerPath) {
                context.ReportError(
                    subscription,
                    "Nested path-prefix under the peer binder is not supported. " +
                    "Only scalar peer properties are allowed (e.g. 'order Code').",
                    DomainModelDiagnosticCodes.SubscriptionEffectBinding);
            }

            foreach (var propName in flags.SubscriberRefs) {
                if (!subscriberProps.Contains(propName)) {
                    context.ReportWarning(
                        subscription,
                        $"Subscription effect references property '{propName}' on subscriber entity " +
                        $"'{subscriberEntity.Name}', but that property does not exist. " +
                        $"Available: {string.Join(", ", subscriberProps)}.",
                        DomainModelDiagnosticCodes.SubscriptionEffectBinding);
                }
            }

            if (peerBinding is { Length: > 0 }) {
                foreach (var propName in flags.PeerRefs) {
                    if (!targetProps.Contains(propName)) {
                        context.ReportWarning(
                            subscription,
                            $"Subscription effect references peer property '{propName}' via binder " +
                            $"'{peerBinding}' on target entity '{targetEntity.Name}', but that property " +
                            $"does not exist. Available: {string.Join(", ", targetProps)}.",
                            DomainModelDiagnosticCodes.SubscriptionEffectBinding);
                    }
                }
            }
        }
    }

    private sealed class SubBindingFlags {
        public HashSet<string> SubscriberRefs { get; } = new(StringComparer.Ordinal);
        public HashSet<string> PeerRefs { get; } = new(StringComparer.Ordinal);
        public bool UsesLegacyEvent;
        public string? UnboundPeerRoot;
        public bool PeerAsAssignTarget;
        public bool NestedPeerPath;
    }

    private static void CollectPropertyAccesses(
        Effect effect,
        string? peerBinding,
        HashSet<string> subscriberRelNames,
        SubBindingFlags flags,
        bool isAssignTarget) {
        switch (effect) {
            case AssignEffect ae:
                CollectFromExpression(ae.Target, peerBinding, subscriberRelNames, flags, isAssignTarget: true);
                CollectFromExpression(ae.Value, peerBinding, subscriberRelNames, flags, isAssignTarget: false);
                break;
            case StageTransitionEffect:
                break;
            case CreateEntityInstance cei:
                foreach (var init in cei.Initializers)
                    CollectFromExpression(init.Expression, peerBinding, subscriberRelNames, flags, isAssignTarget: false);
                break;
            case CreateEntityInRelationshipEffect cir:
                foreach (var init in cir.Initializers)
                    CollectFromExpression(init.Expression, peerBinding, subscriberRelNames, flags, isAssignTarget: false);
                break;
            case InvokeActionEffect iae:
                foreach (var binding in iae.ParameterBindings)
                    CollectFromExpression(binding.Expression, peerBinding, subscriberRelNames, flags, isAssignTarget: false);
                break;
            case ConditionalEffect ce:
                CollectFromExpression(ce.Condition, peerBinding, subscriberRelNames, flags, isAssignTarget: false);
                foreach (var e in ce.ThenEffects)
                    CollectPropertyAccesses(e, peerBinding, subscriberRelNames, flags, isAssignTarget: false);
                if (ce.ElseEffects is not null)
                    foreach (var e in ce.ElseEffects)
                        CollectPropertyAccesses(e, peerBinding, subscriberRelNames, flags, isAssignTarget: false);
                break;
            case CompositeEffect ce:
                foreach (var e in ce.Effects)
                    CollectPropertyAccesses(e, peerBinding, subscriberRelNames, flags, isAssignTarget: false);
                break;
        }
    }

    private static void CollectFromExpression(
        DomainExpression expr,
        string? peerBinding,
        HashSet<string> subscriberRelNames,
        SubBindingFlags flags,
        bool isAssignTarget) {
        switch (expr) {
            case PropertyAccess pa:
                if (pa.Name.StartsWith(SubscriptionEventAccess.Prefix, StringComparison.Ordinal)
                    || string.Equals(pa.Name, SubscriptionEventAccess.RelationshipName, StringComparison.Ordinal)) {
                    flags.UsesLegacyEvent = true;
                }
                else {
                    flags.SubscriberRefs.Add(pa.Name);
                }
                break;
            case RelationshipNavigation rn:
                if (string.Equals(rn.RelationshipName, SubscriptionEventAccess.RelationshipName, StringComparison.Ordinal)) {
                    flags.UsesLegacyEvent = true;
                    return;
                }

                if (peerBinding is { Length: > 0 }
                    && string.Equals(rn.RelationshipName, peerBinding, StringComparison.Ordinal)) {
                    if (isAssignTarget)
                        flags.PeerAsAssignTarget = true;
                    if (rn.TargetProperty is RelationshipNavigation)
                        flags.NestedPeerPath = true;
                    else if (rn.TargetProperty is PropertyAccess peerPa)
                        flags.PeerRefs.Add(peerPa.Name);
                    else {
                        // Comparisons under peer root still need leaf props; nested rel is rejected above.
                        CollectPeerScalars(rn.TargetProperty, flags);
                        if (HasNestedRelationship(rn.TargetProperty))
                            flags.NestedPeerPath = true;
                    }
                    return;
                }

                if (!subscriberRelNames.Contains(rn.RelationshipName)) {
                    // Not a subscriber relationship and not the peer binder → unbound peer-like root (F1).
                    flags.UnboundPeerRoot ??= rn.RelationshipName;
                    return;
                }

                // Real subscriber relationship path-prefix — walk inner for bare props.
                CollectFromExpression(rn.TargetProperty, peerBinding, subscriberRelNames, flags, isAssignTarget);
                return;
        }

        foreach (var child in expr.Children.OfType<DomainExpression>())
            CollectFromExpression(child, peerBinding, subscriberRelNames, flags, isAssignTarget);
    }

    private static void CollectPeerScalars(DomainExpression expr, SubBindingFlags flags) {
        switch (expr) {
            case PropertyAccess pa:
                flags.PeerRefs.Add(pa.Name);
                break;
            default:
                foreach (var child in expr.Children.OfType<DomainExpression>())
                    CollectPeerScalars(child, flags);
                break;
        }
    }

    private static bool HasNestedRelationship(DomainExpression expr) =>
        expr is RelationshipNavigation
        || expr.Children.OfType<DomainExpression>().Any(HasNestedRelationship);

    private static bool SemanticKeyMatch(StageSubscription a, StageSubscription b) {
        if (!string.Equals(a.RelationshipName, b.RelationshipName, StringComparison.Ordinal))
            return false;
        if (a.Quantifier != b.Quantifier)
            return false;
        if (!string.Equals(a.PeerBinding, b.PeerBinding, StringComparison.Ordinal))
            return false;
        if (a.StageNames.Count != b.StageNames.Count)
            return false;
        for (int i = 0; i < a.StageNames.Count; i++) {
            if (!string.Equals(a.StageNames[i], b.StageNames[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static string SubscriptionKey(StageSubscription sub) =>
        $"{sub.RelationshipName} -> {string.Join("/", sub.StageNames)} ({sub.Quantifier}" +
        (sub.PeerBinding is { Length: > 0 } ? $", as {sub.PeerBinding}" : "") + ")";
}