using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Validates that stage subscriptions have correct structure:
/// - The subscription's relationship name resolves to an existing relationship on the owning entity.
/// - Target stage names exist on the target entity type (resolved from the relationship).
/// - Basic structural checks (non-empty names, defined quantifier).
/// - Expression bindings in subscription effects reference valid <c>this.*</c> and <c>event.*</c> properties.
/// </summary>
internal sealed class SubscriptionContractAnalyzer : INodeAnalyzer {
    public const string Id = "DomainSubscriptionContractAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) return;

        if (node is Domain domain) {
            ValidateDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomain(AnalysisContext context, Domain domain) {
        if (!context.TryBeginAnalyzerVisit<SubscriptionContractAnalyzer>(domain)) return;

        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup is null) return;

        foreach (var entity in lookup.Entities) {
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
                }
            }
        }
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
        var relationship = domain.Relationships.FirstOrDefault(r =>
            string.Equals(r.Name, subscription.RelationshipName, StringComparison.Ordinal) &&
            string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal));

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

        // ── Quantifier vs cardinality ────────────────────────────
        bool isSingularFromSource = relationship.Cardinality is RelationshipCardinality.OneToOne
                                                         or RelationshipCardinality.ManyToOne;
        if (subscription.Quantifier != StageSubscriptionQuantifier.Each && isSingularFromSource) {
            context.ReportWarning(
                subscription,
                $"Stage subscription uses quantifier '{subscription.Quantifier}' on one-to-one relationship " +
                $"'{subscription.RelationshipName}'. '{subscription.Quantifier}' behaves identically to 'Each' " +
                "on singular relationships. Use 'Each' or change relationship cardinality.",
                DomainModelDiagnosticCodes.SubscriptionContractMismatch);
        }

        // ── Validate that subscription effect expressions reference known properties ──
        ValidateSubscriptionEffectBindings(context, subscription, entity, targetEntity);
    }

    /// <summary>
    /// Validates that property accesses in subscription effect expressions resolve to
    /// valid properties on the subscriber entity (<c>this.*</c>) or the event (target)
    /// entity (<c>event.*</c>). This catches typos and stale property references that
    /// would silently fail at runtime.
    /// </summary>
    private static void ValidateSubscriptionEffectBindings(
        AnalysisContext context,
        StageSubscription subscription,
        Entity subscriberEntity,
        Entity targetEntity) {
        if (subscription.Effects.Count == 0) return;

        var subscriberProps = new HashSet<string>(
            subscriberEntity.Properties.Select(p => p.Name), StringComparer.Ordinal);
        var targetProps = new HashSet<string>(
            targetEntity.Properties.Select(p => p.Name), StringComparer.Ordinal);

        foreach (var effect in subscription.Effects) {
            var subscriberRefs = new HashSet<string>(StringComparer.Ordinal);
            var eventRefs = new HashSet<string>(StringComparer.Ordinal);
            CollectPropertyAccesses(effect, subscriberRefs, eventRefs);

            // Validate this.* property accesses
            foreach (var propName in subscriberRefs) {
                if (!subscriberProps.Contains(propName)) {
                    context.ReportWarning(
                        subscription,
                        $"Subscription effect references property '{propName}' on subscriber entity " +
                        $"'{subscriberEntity.Name}', but that property does not exist. " +
                        $"Available: {string.Join(", ", subscriberProps)}.",
                        DomainModelDiagnosticCodes.SubscriptionEffectBinding);
                }
            }

            // Validate event.* property accesses
            foreach (var propName in eventRefs) {
                if (!targetProps.Contains(propName)) {
                    context.ReportWarning(
                        subscription,
                        $"Subscription effect references event property '{propName}' on target entity " +
                        $"'{targetEntity.Name}', but that property does not exist. " +
                        $"Available: {string.Join(", ", targetProps)}.",
                        DomainModelDiagnosticCodes.SubscriptionEffectBinding);
                }
            }
        }
    }

    /// <summary>
    /// Walks all expressions in <paramref name="effect"/> and collects:
    /// - <paramref name="subscriberRefs"/>: bare <see cref="PropertyAccess"/> names (this.* context)
    /// - <paramref name="eventRefs"/>: <see cref="PropertyAccess"/> names prefixed with "event."
    /// </summary>
    private static void CollectPropertyAccesses(
        Effect effect,
        HashSet<string> subscriberRefs,
        HashSet<string> eventRefs) {
        switch (effect) {
            case AssignEffect ae:
                CollectFromExpression(ae.Target, subscriberRefs, eventRefs);
                CollectFromExpression(ae.Value, subscriberRefs, eventRefs);
                break;
            case StageTransitionEffect:
            case DeleteEntityInstance:
                break;
            case CreateEntityInstance cei:
                foreach (var init in cei.Initializers)
                    CollectFromExpression(init.Expression, subscriberRefs, eventRefs);
                break;
            case InvokeActionEffect iae:
                // Bindings are subscriber-scoped; filter is target-scoped (EffectAnalyzer).
                foreach (var binding in iae.ParameterBindings)
                    CollectFromExpression(binding.Expression, subscriberRefs, eventRefs);
                break;
            case ConditionalEffect ce:
                CollectFromExpression(ce.Condition, subscriberRefs, eventRefs);
                foreach (var e in ce.ThenEffects) CollectPropertyAccesses(e, subscriberRefs, eventRefs);
                if (ce.ElseEffects is not null)
                    foreach (var e in ce.ElseEffects) CollectPropertyAccesses(e, subscriberRefs, eventRefs);
                break;
            case CompositeEffect ce:
                foreach (var e in ce.Effects) CollectPropertyAccesses(e, subscriberRefs, eventRefs);
                break;
            case LinkRelationshipEffect:
            case UnlinkRelationshipEffect:
            case TransitionRelationshipEffect:
                break;
        }
    }

    private static void CollectFromExpression(
        DomainExpression expr,
        HashSet<string> subscriberRefs,
        HashSet<string> eventRefs) {
        switch (expr) {
            case PropertyAccess pa:
                // "event.PropertyName" convention: bare property starting with Prefix
                // references the event (transitioned) instance, not the subscriber.
                if (pa.Name.StartsWith(SubscriptionEventAccess.Prefix, StringComparison.Ordinal)) {
                    eventRefs.Add(pa.Name[SubscriptionEventAccess.Prefix.Length..]);
                }
                else {
                    // Bare property access — references subscriber's property (this.*)
                    subscriberRefs.Add(pa.Name);
                }
                break;
            case RelationshipNavigation rn:
                // Lowered from "event PropertyName": RelationshipName = "event",
                // TargetProperty = PropertyAccess("PropertyName").
                if (string.Equals(rn.RelationshipName, SubscriptionEventAccess.RelationshipName, StringComparison.Ordinal)
                    && rn.TargetProperty is PropertyAccess eventPa) {
                    eventRefs.Add(eventPa.Name);
                }
                // Recurse into target property (may have further access patterns)
                CollectFromExpression(rn.TargetProperty, subscriberRefs, eventRefs);
                return; // children already handled
        }

        // Recurse into children
        foreach (var child in expr.Children.OfType<DomainExpression>())
            CollectFromExpression(child, subscriberRefs, eventRefs);
    }

    private static bool SemanticKeyMatch(StageSubscription a, StageSubscription b) {
        if (!string.Equals(a.RelationshipName, b.RelationshipName, StringComparison.Ordinal))
            return false;
        if (a.Quantifier != b.Quantifier)
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
        $"{sub.RelationshipName} -> {string.Join("/", sub.StageNames)} ({sub.Quantifier})";
}