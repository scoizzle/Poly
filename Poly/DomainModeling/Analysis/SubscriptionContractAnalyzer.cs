using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Validates that stage subscriptions have correct structure:
/// - The subscription's relationship name resolves to an existing relationship on the owning entity.
/// - Target stage names exist on the target entity type (resolved from the relationship).
/// - Basic structural checks (non-empty names, defined quantifier).
///
/// TODO (post-A′): Validate <c>this</c>/<c>event</c> expression bindings in subscription effects.
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