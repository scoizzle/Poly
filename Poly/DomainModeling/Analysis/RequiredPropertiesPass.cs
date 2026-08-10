using Poly.Analysis;
using Poly.DomainModeling.Constraints;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Fact emitter: publishes <see cref="RequiredPropertiesMetadata"/> on entities
/// (and stages when collectable) from policy Exists targets and RequiredConstraint.
/// Diagnostics for policy expressions live in <see cref="PolicyConstraintAnalyzer"/>.
/// </summary>
internal sealed class RequiredPropertiesPass : INodeAnalyzer {
    public const string Id = "DomainRequiredProperties";
    public string PassName => Id;
    public string[] Dependencies => [SemanticDomainAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node))
            return;

        switch (node) {
            case Entity entity:
                PublishEntity(context, entity);
                break;
            case Stage stage:
                PublishStage(context, stage);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void PublishEntity(AnalysisContext context, Entity entity) {
        var requiredByPolicy = new List<Property>();
        var entityPropMap = BuildPropertyMap(entity);

        foreach (var policy in entity.Policies) {
            CollectRequiredFromExpression(policy.Expression, entityPropMap, requiredByPolicy);
        }

        foreach (var property in entity.Properties) {
            if (property.Constraints.Any(static c => c is RequiredConstraint)
                && !requiredByPolicy.Contains(property)) {
                requiredByPolicy.Add(property);
            }
        }

        if (requiredByPolicy.Count > 0) {
            context.SetMetadata(entity, new RequiredPropertiesMetadata(requiredByPolicy));
        }

        // Stage-scoped required metadata — computed here (the entity visit has the
        // property map). The standalone Stage visit below cannot resolve property
        // names without it, so this is the only path that actually publishes.
        foreach (var stage in entity.Stages) {
            var stageRequired = new List<Property>();
            foreach (var policy in stage.Policies) {
                CollectRequiredFromExpression(policy.Expression, entityPropMap, stageRequired);
            }
            if (stageRequired.Count > 0) {
                context.SetMetadata(stage, new RequiredPropertiesMetadata(stageRequired));
            }
        }
    }

    private static void PublishStage(AnalysisContext context, Stage stage) {
        // Historical standalone walk — without the owning entity's property map,
        // Exists targets cannot resolve to Property objects, so nothing is
        // collected here. Real stage metadata is published from PublishEntity.
    }

    private static void CollectRequiredFromExpression(
        DomainExpression expr,
        Dictionary<string, Property>? entityPropMap,
        List<Property> required) {

        switch (expr) {
            case Exists exists:
                CollectPropertyFromTarget(exists.Target, entityPropMap, required);
                break;
            case NotExists:
                break;
            case And and:
                CollectRequiredFromExpression(and.Left, entityPropMap, required);
                CollectRequiredFromExpression(and.Right, entityPropMap, required);
                return;
            case Or or:
                CollectRequiredFromExpression(or.Left, entityPropMap, required);
                CollectRequiredFromExpression(or.Right, entityPropMap, required);
                return;
            case Not not:
                CollectRequiredFromExpression(not.Operand, entityPropMap, required);
                return;
        }
    }

    private static void CollectPropertyFromTarget(
        DomainExpression target,
        Dictionary<string, Property>? entityPropMap,
        List<Property> required) {

        if (entityPropMap is null)
            return;

        switch (target) {
            case OwnedAccess owned:
                if (entityPropMap.TryGetValue(owned.OwnedName, out var ownedProp)
                    && !required.Contains(ownedProp)) {
                    required.Add(ownedProp);
                }
                break;
            case PropertyAccess propAccess:
                if (entityPropMap.TryGetValue(propAccess.Name, out var prop)
                    && !required.Contains(prop)) {
                    required.Add(prop);
                }
                break;
        }
    }

    private static Dictionary<string, Property> BuildPropertyMap(Entity entity) {
        var map = new Dictionary<string, Property>(StringComparer.Ordinal);
        foreach (var prop in entity.Properties) {
            map[prop.Name] = prop;
        }
        return map;
    }
}

public sealed record RequiredPropertiesMetadata(IReadOnlyList<Property> RequiredProperties) : IAnalysisMetadata;