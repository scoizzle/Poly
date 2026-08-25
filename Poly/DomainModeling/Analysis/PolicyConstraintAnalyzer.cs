using Poly.DomainModeling.Ontology;

using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Validate pack: policy expression reference integrity (property, owned, quantifier, cardinality).
/// Writes no analysis facts — <see cref="RequiredPropertiesMetadata"/> is published by
/// <see cref="RequiredPropertiesPass"/>.
/// </summary>
internal sealed class PolicyConstraintAnalyzer : INodeAnalyzer {
    public const string Id = "DomainPolicyConstraint";
    public string PassName => Id;
    // Lint-only: reads DomainTypeLookupMetadata; publishes no bags others read.
    public string[] Dependencies => [DomainCatalogPass.Id];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node))
            return;

        if (node is Entity entity) {
            AnalyzeEntity(context, entity);
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        var lookup = context.GetTypeLookup();
        var entityPropMap = BuildPropertyMap(entity);

        foreach (var policy in entity.Policies) {
            ValidatePolicyPropertyReferences(context, lookup, policy.Expression, entity, entityPropMap);
        }

        foreach (var stage in entity.Stages) {
            foreach (var policy in stage.Policies) {
                ValidatePolicyPropertyReferences(context, lookup, policy.Expression, entity, entityPropMap);
            }
        }

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
                    if (lookup is not null
                        && !IsRelationshipOnEntity(context, lookup.Domain, pa.Name, entity, out var bagAvailable)
                        && bagAvailable) {
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
                // relationship is invalid (use Collection quantifiers instead).
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
    /// Validates a collection quantified expression body property references against the
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
        var targetPropMap = BuildPropertyMap(targetEntity);
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

        // amu-w1-2: catalog/RLM name resolve (no domain.Relationships scan).
        var relLookup = ResolveRelationshipLookup(context, lookup.Domain);
        if (relLookup is null) return (null, null); // bag unavailable — skip
        var relationship = relLookup.TryGetRelationship(entity.Name, resolvedRelName, out var rel) ? rel : null;

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

        // amu-w1-2: catalog/RLM name resolve (no domain.Relationships scans).
        var relLookup = ResolveRelationshipLookup(context, lookup.Domain);
        if (relLookup is null) return; // bag unavailable — skip

        // Find the relationship from the source entity's perspective
        var relationship = relLookup.TryGetRelationship(entity.Name, rn.RelationshipName, out var fromSource)
            ? fromSource
            : null;

        // Q1'''''.3: Report error for unknown relationship name
        if (relationship is null) {
            // Check if relationship exists but from the target side (wrong direction)
            var reverseRel = relLookup.FindByNameAcrossSources(rn.RelationshipName).FirstOrDefault();
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
                "quantifier instead (e.g. 'any Rel where ...').",
                DomainModelDiagnosticCodes.RelationshipNavigationCardinality);
            return;
        }

        // Q1'''''.4 + P2: Validate body against target; recurse nested path-prefix hops.
        ValidateRelatedBodyProperties(context, lookup, rn, relationship);
    }

    /// <summary>
    /// Validates that the TargetProperty of a RelationshipNavigation references
    /// valid properties on the target entity type. Nested
    /// <see cref="RelationshipNavigation"/> hops are validated as path-prefix
    /// from the target entity (P2 multi-hop).
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

        // Nested hop: validate as path-prefix from the related entity (P2).
        if (rn.TargetProperty is RelationshipNavigation nested) {
            ValidateRelationshipCardinality(context, lookup, nested, targetEntity);
            return;
        }

        // Build property map for target entity
        var targetPropMap = BuildPropertyMap(targetEntity);

        // Walk the body expression tree and validate PropertyAccess against target entity
        ValidateRelatedPropertyAccess(context, rn.TargetProperty, targetEntity, targetPropMap);
    }

    /// <summary>
    /// Recursively validates PropertyAccess nodes in the body expression against
    /// the target entity's property map. Nested RelationshipNavigation is handled
    /// by <see cref="ValidateRelatedBodyProperties"/> (hop recursion).
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
                // Nested hop validated via ValidateRelatedBodyProperties.
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
    /// amu-w1-2: catalog/RLM name resolve (no <c>domain.Relationships.Any</c> scan).
    /// Returns false with <paramref name="bagAvailable"/> false when neither the catalog
    /// nor RLM bag is available — caller must skip the error, not false-positive.
    /// </summary>
    private static bool IsRelationshipOnEntity(
        AnalysisContext context, Domain domain, string name, Entity entity, out bool bagAvailable) {
        var relLookup = ResolveRelationshipLookup(context, domain);
        if (relLookup is null) {
            bagAvailable = false;
            return false;
        }
        bagAvailable = true;
        return relLookup.TryGetRelationship(entity.Name, name, out _);
    }

    /// <summary>
    /// Catalog relationship lookup.
    /// </summary>
    private static RelationshipLookupMetadata? ResolveRelationshipLookup(AnalysisContext context, Domain domain) =>
        context.GetRelationshipLookup(domain) ?? context.GetRelationshipLookup();

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

    private static Dictionary<string, Property> BuildPropertyMap(Entity entity) {
        var map = new Dictionary<string, Property>(StringComparer.Ordinal);

        foreach (var prop in entity.Properties) {
            map[prop.Name] = prop;
        }

        return map;
    }
}