using Poly.DomainModeling.Constraints;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class PolicyConstraintAnalyzer : INodeAnalyzer {
    public const string Id = "DomainPolicyConstraint";
    public string PassName => Id;
    public string[] Dependencies => [];
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
        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        var entityPropMap = BuildPropertyMap(entity, lookup);

        // ── Entity-level policies ─────────────────────────────
        foreach (var policy in entity.Policies) {
            ValidatePolicyPropertyReferences(context, lookup, policy.Expression, entity, entityPropMap);
            CollectRequiredFromExpression(policy.Expression, entity, entityPropMap, requiredByPolicy, referencedByPolicy);
        }

        // ── Stage-level policies ──────────────────────────────
        foreach (var stage in entity.Stages) {
            foreach (var policy in stage.Policies) {
                ValidatePolicyPropertyReferences(context, lookup, policy.Expression, entity, entityPropMap);
            }
        }

        // ── Action-level policies (entity-level and stage-level actions) ──
        foreach (var action in entity.Actions) {
            foreach (var policy in action.Policies) {
                ValidatePolicyPropertyReferences(context, lookup, policy.Expression, entity, entityPropMap);
            }
        }
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions) {
                foreach (var policy in action.Policies) {
                    ValidatePolicyPropertyReferences(context, lookup, policy.Expression, entity, entityPropMap);
                }
            }
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

    private static void ValidatePolicyPropertyReferences(
        AnalysisContext context,
        DomainTypeLookupMetadata? lookup,
        DomainExpression expr,
        Entity entity,
        Dictionary<string, Property> entityPropMap) {

        // Walk the expression tree and report errors for any PropertyAccess
        // that references a property not found on the entity, and for any
        // OwnedAccess whose name doesn't match a known ValueType.
        // Other expression types are skipped:
        //   - RelationshipNavigation: references properties on a related entity
        //   - ParameterAccess: external parameters, not entity properties
        switch (expr) {
            case PropertyAccess pa:
                if (!entityPropMap.ContainsKey(pa.Name)) {
                    context.ReportError(
                        expr,
                        $"Policy references property '{pa.Name}' which does not exist on entity '{entity.Name}'.",
                        DomainModelDiagnosticCodes.SemanticReferenceResolution);
                }
                return;
            case OwnedAccess oa:
                // Validate OwnedName against known ValueTypes (if lookup is available).
                // Do NOT recurse into children — inner expressions reference
                // properties on the owned type, not this entity.
                ValidateOwnedAccessName(context, lookup, oa, entity);
                return;
            case RelationshipNavigation:
                // Do NOT recurse — TargetProperty references properties on
                // the related entity, not on this entity.
                return;
            case ParameterAccess:
                // Parameter references are not entity properties — skip.
                return;
        }

        // Recurse into children for composite expressions (And, Or, Not, Comparison, etc.)
        foreach (var child in expr.Children.OfType<DomainExpression>()) {
            ValidatePolicyPropertyReferences(context, lookup, child, entity, entityPropMap);
        }
    }

    private static void ValidateOwnedAccessName(
        AnalysisContext context,
        DomainTypeLookupMetadata? lookup,
        OwnedAccess owned,
        Entity entity) {

        if (lookup is null) return;

        // OwnedAccess references a ValueType by name. If no such type exists,
        // the policy is referencing a value type that hasn't been defined.
        if (!lookup.Types.TryGetValue(owned.OwnedName, out var resolved) || resolved is not ValueType) {
            // Only report an error if the name isn't already known as an entity property
            // (backward compatibility — entity properties may shadow value-type names).
            if (!entity.Properties.Any(p => string.Equals(p.Name, owned.OwnedName, StringComparison.Ordinal))) {
                context.ReportError(
                    owned,
                    $"Policy references value type '{owned.OwnedName}' which does not exist in the domain. " +
                    $"Define a value type with that name, or check for typos.",
                    DomainModelDiagnosticCodes.SemanticReferenceResolution);
            }
        }
    }

    private static Dictionary<string, Property> BuildPropertyMap(Entity entity, DomainTypeLookupMetadata? lookup = null) {
        var map = new Dictionary<string, Property>(StringComparer.Ordinal);

        // Add parent-entity properties first so child can override (PCA.7)
        if (lookup is not null && entity.ParentEntityName is not null) {
            AddParentProperties(entity, lookup, map, new HashSet<string>(StringComparer.Ordinal));
        }

        foreach (var prop in entity.Properties) {
            map[prop.Name] = prop;
        }

        return map;
    }

    private static void AddParentProperties(
        Entity entity,
        DomainTypeLookupMetadata lookup,
        Dictionary<string, Property> map,
        HashSet<string> visited) {

        if (entity.ParentEntityName is null) return;
        if (!visited.Add(entity.Name)) return; // cycle guard

        if (lookup.Types.TryGetValue(entity.ParentEntityName, out var parentType) && parentType is Entity parent) {
            // Walk grandparent first so immediate parent overrides grandparent
            AddParentProperties(parent, lookup, map, visited);

            foreach (var prop in parent.Properties) {
                if (!map.ContainsKey(prop.Name)) {
                    map[prop.Name] = prop;
                }
            }
        }
    }
}

public sealed record RequiredPropertiesMetadata(IReadOnlyList<Property> RequiredProperties) : IAnalysisMetadata;