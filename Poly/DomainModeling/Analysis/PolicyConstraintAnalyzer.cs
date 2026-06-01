using Poly.DomainModeling.Constraints;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

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

        var requiredByPolicy = new List<Property>();
        var referencedByPolicy = new List<Property>();
        var entityPropMap = BuildPropertyMap(entity);

        foreach (var policy in entity.Policies) {
            CollectRequiredFromExpression(policy.Expression, entity, entityPropMap, requiredByPolicy, referencedByPolicy);
        }

        foreach (var property in entity.Properties) {
            if (property.Constraints.Any(static c => c is RequiredConstraint)) {
                if (!requiredByPolicy.Contains(property)) {
                    requiredByPolicy.Add(property);
                }
            }
        }

        if (requiredByPolicy.Count > 0) {
            context.SetMetadata(entity, new RequiredPropertiesMetadata(requiredByPolicy));
        }
    }

    private static void AnalyzeStage(AnalysisContext context, Stage stage) {
        if (!context.TryBeginAnalyzerVisit<PolicyConstraintAnalyzer>(stage))
            return;

        var required = new List<Property>();

        foreach (var policy in stage.Policies) {
            CollectRequiredFromExpression(policy.Expression, null, null, required, null);
        }

        if (required.Count > 0) {
            context.SetMetadata(stage, new RequiredPropertiesMetadata(required));
        }
    }

    private static void CollectRequiredFromExpression(
        DomainExpression expr,
        Entity? entity,
        Dictionary<string, Property>? entityPropMap,
        List<Property> required,
        List<Property>? referenced) {

        switch (expr) {
            case Exists exists:
                CollectPropertyFromTarget(exists.Target, entity, entityPropMap, required);
                break;
            case NotExists:
                break;
            case And and:
                CollectRequiredFromExpression(and.Left, entity, entityPropMap, required, referenced);
                CollectRequiredFromExpression(and.Right, entity, entityPropMap, required, referenced);
                return;
            case Or or:
                CollectRequiredFromExpression(or.Left, entity, entityPropMap, required, referenced);
                CollectRequiredFromExpression(or.Right, entity, entityPropMap, required, referenced);
                return;
            case Not not:
                CollectRequiredFromExpression(not.Operand, entity, entityPropMap, required, referenced);
                return;
        }

        // Collect all PropertyAccess references from any expression for broader coverage
        CollectReferencedProperties(expr, entityPropMap, referenced);
    }

    private static void CollectPropertyFromTarget(
        DomainExpression target,
        Entity? entity,
        Dictionary<string, Property>? entityPropMap,
        List<Property> required) {

        switch (target) {
            case OwnedAccess owned:
                if (entityPropMap is not null && entityPropMap.TryGetValue(owned.OwnedName, out var ownedProp)) {
                    if (!required.Contains(ownedProp))
                        required.Add(ownedProp);
                }
                break;
            case PropertyAccess propAccess:
                if (entityPropMap is not null && entityPropMap.TryGetValue(propAccess.Name, out var prop)) {
                    if (!required.Contains(prop))
                        required.Add(prop);
                }
                break;
        }
    }

    private static void CollectReferencedProperties(
        DomainExpression expr,
        Dictionary<string, Property>? entityPropMap,
        List<Property>? referenced) {

        if (referenced is null || entityPropMap is null) return;

        switch (expr) {
            case PropertyAccess pa:
                if (entityPropMap.TryGetValue(pa.Name, out var prop) && !referenced.Contains(prop))
                    referenced.Add(prop);
                break;
            case OwnedAccess oa:
                if (entityPropMap.TryGetValue(oa.OwnedName, out var ownedProp) && !referenced.Contains(ownedProp))
                    referenced.Add(ownedProp);
                break;
        }

        foreach (var child in expr.Children.OfType<DomainExpression>()) {
            CollectReferencedProperties(child, entityPropMap, referenced);
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