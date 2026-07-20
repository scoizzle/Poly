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
        //   - AnyExpr/AllExpr/NoneExpr/CountExpr: Q3′ collection quantifiers,
        //     validated against target entity separately
        switch (expr) {
            case PropertyAccess pa:
                if (!entityPropMap.ContainsKey(pa.Name)) {
                    // Q1''''''.1: Before reporting error, check if the name matches a
                    // relationship (N1 nav). `Rel exists` produces Exists(PropertyAccess(relName))
                    // where relName is a relationship, not an entity property.
                    if (lookup is not null && !IsRelationshipOnEntity(lookup, pa.Name, entity)) {
                        context.ReportError(
                            expr,
                            $"Policy references property '{pa.Name}' which does not exist on entity '{entity.Name}'.",
                            DomainModelDiagnosticCodes.SemanticReferenceResolution);
                    }
                }
                return;
            case OwnedAccess oa:
                // Validate OwnedName against known ValueTypes (if lookup is available).
                // Do NOT recurse into children — inner expressions reference
                // properties on the owned type, not this entity.
                ValidateOwnedAccessName(context, lookup, oa, entity);
                return;
            case RelationshipNavigation rn:
                // Validate that the relationship is not a 'many' cardinality
                // from the source entity's perspective. Path-prefix on a 'many'
                // relationship is invalid (use Q3′ quantifiers instead).
                ValidateRelationshipCardinality(context, lookup, rn, entity);
                return;
            case ParameterAccess:
                // Parameter references are not entity properties — skip.
                return;
            case AnyExpr a:
                ValidateQuantifierExpression(context, lookup, a.RelationshipName, a.Body, entity);
                return;
            case AllExpr a:
                ValidateQuantifierExpression(context, lookup, a.RelationshipName, a.Body, entity);
                return;
            case NoneExpr n:
                ValidateQuantifierExpression(context, lookup, n.RelationshipName, n.Body, entity);
                return;
            case CountExpr c when c.Body is not null:
                ValidateQuantifierExpression(context, lookup, c.RelationshipName, c.Body, entity);
                return;
            case CountExpr:
                // Bare count Rel (no body) — just validate relationship existence.
                ValidateQuantifierRelationship(context, lookup, expr, entity);
                return;
        }

        // Recurse into children for composite expressions (And, Or, Not, Comparison, etc.)
        foreach (var child in expr.Children.OfType<DomainExpression>()) {
            ValidatePolicyPropertyReferences(context, lookup, child, entity, entityPropMap);
        }
    }

    /// <summary>
    /// Validates a Q3′ quantified expression body property references against the
    /// target entity. Rejects unknown relationship, non-collection cardinality,
    /// reverse-side, self-rel, and unknown body properties.
    /// </summary>
    private static void ValidateQuantifierExpression(
        AnalysisContext context,
        DomainTypeLookupMetadata? lookup,
        string relationshipName,
        DomainExpression body,
        Entity entity) {
        var (targetEntity, _) = ValidateQuantifierRelationship(context, lookup, body, entity, relationshipName);
        if (targetEntity is null) return;

        // Build property map for target entity and validate body references
        var targetPropMap = BuildPropertyMap(targetEntity, lookup);
        ValidateRelatedPropertyAccess(context, body, targetEntity, targetPropMap);
    }

    /// <summary>
    /// Validates that a quantifier relationship exists, is OneToMany from the source,
    /// source-side only, not self-rel. Returns the target entity or null on error.
    /// </summary>
    private static (Entity? TargetEntity, Relationship? Relationship) ValidateQuantifierRelationship(
        AnalysisContext context,
        DomainTypeLookupMetadata? lookup,
        DomainExpression expr,
        Entity entity,
        string? relationshipName = null) {
        if (lookup is null) return (null, null);
        var domain = lookup.Domain;

        // Resolve relationship
        var resolvedRelName = relationshipName
            ?? (expr is AnyExpr a ? a.RelationshipName
                : expr is AllExpr a2 ? a2.RelationshipName
                : expr is NoneExpr n ? n.RelationshipName
                : expr is CountExpr c ? c.RelationshipName
                : null);
        if (resolvedRelName is null) return (null, null);

        var relationship = domain.Relationships.FirstOrDefault(r =>
            string.Equals(r.Name, resolvedRelName, StringComparison.Ordinal));

        if (relationship is null) {
            context.ReportError(expr,
                $"Quantifier references relationship '{resolvedRelName}' which does not exist " +
                $"on entity '{entity.Name}'.",
                DomainModelDiagnosticCodes.SemanticReferenceResolution);
            return (null, null);
        }

        // Source-side only
        if (!string.Equals(relationship.Source.TypeName, entity.Name, StringComparison.Ordinal)) {
            context.ReportError(expr,
                $"Quantifier relationship '{resolvedRelName}' may only be used from source entity " +
                $"'{relationship.Source.TypeName}' (caller is '{entity.Name}').",
                DomainModelDiagnosticCodes.SemanticReferenceResolution);
            return (null, null);
        }

        // Must be OneToMany for quantifiers (path-prefix uses OneToOne, covered by DMREL001)
        if (relationship.Cardinality is not RelationshipCardinality.OneToMany) {
            context.ReportError(expr,
                $"Quantifier '{resolvedRelName}' requires a OneToMany relationship from '{entity.Name}', " +
                $"but the cardinality is {relationship.Cardinality}. Use path-prefix for OneToOne reads.",
                DomainModelDiagnosticCodes.SemanticReferenceResolution);
            return (null, null);
        }

        // No self-relationship
        if (string.Equals(relationship.Source.TypeName, relationship.Target.TypeName, StringComparison.Ordinal)) {
            context.ReportError(expr,
                $"Quantifier on self-relationship '{resolvedRelName}' is not supported yet.",
                DomainModelDiagnosticCodes.SemanticReferenceResolution);
            return (null, null);
        }

        // Resolve target entity
        if (!lookup.Types.TryGetValue(relationship.Target.TypeName, out var targetType)
            || targetType is not Entity targetEntity) {
            context.ReportError(expr,
                $"Target entity type '{relationship.Target.TypeName}' for quantifier '{resolvedRelName}' not found.",
                DomainModelDiagnosticCodes.SemanticReferenceResolution);
            return (null, null);
        }

        return (targetEntity, relationship);
    }

    /// <summary>
    /// Validates that a RelationshipNavigation in a policy expression does not
    /// reference a 'many' cardinality relationship, that the relationship name
    /// is known to the entity, and that body property references are valid
    /// against the target entity type.
    /// </summary>
    private static void ValidateRelationshipCardinality(
        AnalysisContext context,
        DomainTypeLookupMetadata? lookup,
        RelationshipNavigation rn,
        Entity entity) {
        if (lookup is null) return;

        var domain = lookup.Domain;

        // Find the relationship from the source entity's perspective
        var relationship = domain.Relationships.FirstOrDefault(r =>
            string.Equals(r.Name, rn.RelationshipName, StringComparison.Ordinal) &&
            string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal));

        // Q1'''''.3: Report error for unknown relationship name
        if (relationship is null) {
            // Check if relationship exists but from the target side (wrong direction)
            var reverseRel = domain.Relationships.FirstOrDefault(r =>
                string.Equals(r.Name, rn.RelationshipName, StringComparison.Ordinal));
            if (reverseRel is not null) {
                context.ReportError(rn,
                    $"Relationship '{rn.RelationshipName}' exists but the source is " +
                    $"'{reverseRel.Source.TypeName}', not '{entity.Name}'. " +
                    "Path-prefix expressions must be on the source entity of the relationship.",
                    DomainModelDiagnosticCodes.SemanticReferenceResolution);
            }
            else {
                context.ReportError(rn,
                    $"Relationship name '{rn.RelationshipName}' is not defined on entity " +
                    $"'{entity.Name}'. Declare a navigation property (e.g. '{rn.RelationshipName}: TargetEntity') " +
                    "on this entity first.",
                    DomainModelDiagnosticCodes.SemanticReferenceResolution);
            }
            return;
        }

        // Check for 'many' cardinality from the source side
        if (relationship.Cardinality is RelationshipCardinality.OneToMany
            or RelationshipCardinality.ManyToMany) {
            context.ReportError(
                rn,
                $"Path-prefix expression '{rn.RelationshipName}' references relationship with " +
                $"cardinality {relationship.Cardinality} from entity '{entity.Name}'. " +
                "Bare path-prefix on a 'many' relationship is invalid. Use a collection " +
                "quantifier instead (e.g. 'any Rel where ...' — Q3′).",
                DomainModelDiagnosticCodes.RelationshipNavigationCardinality);
            return;
        }

        // Q1'''''.4: Validate body property references against the target entity
        ValidateRelatedBodyProperties(context, lookup, rn, relationship);
    }

    /// <summary>
    /// Validates that the TargetProperty of a RelationshipNavigation references
    /// valid properties on the target entity type.
    /// </summary>
    private static void ValidateRelatedBodyProperties(
        AnalysisContext context,
        DomainTypeLookupMetadata lookup,
        RelationshipNavigation rn,
        Relationship relationship) {
        // Resolve target entity
        if (!lookup.Types.TryGetValue(relationship.Target.TypeName, out var targetType)
            || targetType is not Entity targetEntity)
            return;

        // Build property map for target entity
        var targetPropMap = BuildPropertyMap(targetEntity, lookup);

        // Walk the body expression tree and validate PropertyAccess against target entity
        ValidateRelatedPropertyAccess(context, rn.TargetProperty, targetEntity, targetPropMap);
    }

    /// <summary>
    /// Recursively validates PropertyAccess nodes in the body expression against
    /// the target entity's property map. Skips nested RelationshipNavigation
    /// (those would be validated separately).
    /// </summary>
    private static void ValidateRelatedPropertyAccess(
        AnalysisContext context,
        DomainExpression expr,
        Entity targetEntity,
        Dictionary<string, Property> targetPropMap) {
        switch (expr) {
            case PropertyAccess pa:
                if (!targetPropMap.ContainsKey(pa.Name)) {
                    context.ReportError(expr,
                        $"Path-prefix body references property '{pa.Name}' which does not exist " +
                        $"on target entity '{targetEntity.Name}'.",
                        DomainModelDiagnosticCodes.SemanticReferenceResolution);
                }
                return;
            case RelationshipNavigation:
                // Nested navigation — skip (validated separately)
                return;
            case ParameterAccess:
                return;
        }

        // Recurse into children
        foreach (var child in expr.Children.OfType<DomainExpression>()) {
            ValidateRelatedPropertyAccess(context, child, targetEntity, targetPropMap);
        }
    }

    /// <summary>
    /// Returns true if <paramref name="name"/> is a relationship (N1 nav) on <paramref name="entity"/>,
    /// meaning the entity is the source of a relationship with that name.
    /// </summary>
    private static bool IsRelationshipOnEntity(DomainTypeLookupMetadata lookup, string name, Entity entity) {
        return lookup.Domain.Relationships.Any(r =>
            string.Equals(r.Name, name, StringComparison.Ordinal) &&
            string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal));
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

        foreach (var prop in entity.Properties) {
            map[prop.Name] = prop;
        }

        return map;
    }
}

public sealed record RequiredPropertiesMetadata(IReadOnlyList<Property> RequiredProperties) : IAnalysisMetadata;