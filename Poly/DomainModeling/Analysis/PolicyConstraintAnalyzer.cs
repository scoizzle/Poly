using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Analyzes policies (expressed as DomainExpression) to compute required properties
/// for entities and stages. This is an early V3 implementation focused on the
/// most common patterns (Exists/NotExists on owned value types).
/// 
/// Full expression analysis and richer constraint extraction will be added as
/// the DomainExpression system and lowering mature.
/// </summary>
internal sealed class PolicyConstraintAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node))
            return;

        switch (node) {
            case Entity entity:
                AnalyzeEntity(context, entity);
                break;
            case Stage stage:
                AnalyzeStage(context, stage);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        if (!context.TryBeginAnalyzerVisit<PolicyConstraintAnalyzer>(entity))
            return;

        var required = new List<Property>();

        // Collect from entity-level policies
        foreach (var policy in entity.Policies) {
            CollectExistsRequirements(policy.Expression, entity, required);
        }

        // Note: In the current V3 model, Property does not carry its own Policies collection.
        // When that is added, we can extend the collection logic here.

        if (required.Count > 0) {
            context.SetMetadata(entity, new RequiredPropertiesMetadata(required));
        }
    }

    private static void AnalyzeStage(AnalysisContext context, Stage stage) {
        if (!context.TryBeginAnalyzerVisit<PolicyConstraintAnalyzer>(stage))
            return;

        var required = new List<Property>();
        var visited = new HashSet<NodeId>();

        // Note: In V3 the Stage.Parent is a StageReference, not a direct Stage.
        // Full parent resolution requires the SemanticDomainAnalyzer's lookup metadata.
        // For this early version we only look at the stage itself.
        foreach (var policy in stage.Policies) {
            CollectExistsRequirements(policy.Expression, null, required);
        }

        if (required.Count > 0) {
            context.SetMetadata(stage, new RequiredPropertiesMetadata(required));
        }
    }

    private static void CollectExistsRequirements(DomainExpression expr, Entity? entity, List<Property> required) {
        if (expr is Exists exists && exists.Target is OwnedAccess owned) {
            if (entity is not null) {
                var prop = entity.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, owned.OwnedName, StringComparison.Ordinal));

                if (prop is not null && !required.Contains(prop))
                    required.Add(prop);
            }
        }

        foreach (var child in expr.Children.OfType<DomainExpression>()) {
            CollectExistsRequirements(child, entity, required);
        }
    }
}

/// <summary>
/// Attached to Entity and Stage nodes by PolicyConstraintAnalyzer.
/// Lists properties that policies appear to treat as required (based on Exists expressions, etc.).
/// </summary>
public sealed record RequiredPropertiesMetadata(IReadOnlyList<Property> RequiredProperties) : IAnalysisMetadata;