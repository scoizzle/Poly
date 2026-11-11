// EffectAnalyzer partial: assign and write validation.
using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

internal sealed partial class EffectAnalyzer {
    private static bool IsExclusivelyOwned(AnalysisContext context, Entity entity, Domain domain) {
        var entityName = entity.Name;
        bool isSource = false;
        bool isOwnedTarget = false;

        foreach (var rel in context.GetAllRelationships(domain)) {
            if (string.Equals(rel.Source.TypeName, entityName, StringComparison.Ordinal)) {
                isSource = true;
            }
            if (string.Equals(rel.Target.TypeName, entityName, StringComparison.Ordinal) && rel.SourceOwnsTarget) {
                isOwnedTarget = true;
            }
        }

        // Exclusively owned: exists only as a target of owned relationships, never as a source
        return isOwnedTarget && !isSource;
    }

    internal static void ReportCrossEntityMutation(
        AnalysisContext context, Node node, Entity owner, string relationshipName) {
        context.ReportError(
            node,
            $"Assign targets state reached through relationship '{relationshipName}', which belongs to another entity. " +
            $"Only '{owner.Name}' and its own actions, entry, exit and when blocks may assign its state; " +
            $"cross-entity access is read-only. Invoke an action on the other entity instead " +
            $"(e.g. 'invoke {relationshipName}.SomeAction').",
            DomainModelDiagnosticCodes.EffectCrossEntityMutation);
    }

    private static void ReportPeerBinderMutation(AnalysisContext context, Node node, string peerBinding) =>
        context.ReportError(
            node,
            $"Assign target starts with peer binder '{peerBinding}', the other entity in this when handler. " +
            "Peer fields are read-only here: use them only on the right-hand side (values, conditions, initializers). " +
            "Change the peer in its own actions or when blocks.",
            DomainModelDiagnosticCodes.EffectCrossEntityMutation);

    private static void ValidateAssign(
        AnalysisContext context, AssignEffect ae, Action? action, Entity entity, string? peerBinding) {
        // Only the owning entity's named behavior mutates its state. Entry, exit and
        // when blocks count as named behavior. Any other entity changes through
        // `invoke`; create-in, link and unlink are separate effects owned by the relationship.
        // In a when handler the path can instead start with the peer binder, which cannot be invoked.
        if (ae.Target is RelationshipNavigation nav) {
            if (string.Equals(nav.RelationshipName, peerBinding, StringComparison.Ordinal))
                ReportPeerBinderMutation(context, ae, nav.RelationshipName);
            else
                ReportCrossEntityMutation(context, ae, entity, nav.RelationshipName);
            return;
        }

        // The parser writes only a bare property here. Any other shape comes from a model
        // built through the API (an owned field, a parameter, a computed value) and cannot be
        // printed back as the same DSL, so it is an error rather than skipped.
        if (ae.Target is not PropertyAccess propAccess) {
            context.ReportError(
                ae,
                $"Assign target is not a property of entity '{entity.Name}'. " +
                "An assign writes one of the entity's own properties by name: 'assign <Property> to <value>'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        var targetProp = entity.Properties
            .FirstOrDefault(p => string.Equals(p.Name, propAccess.Name, StringComparison.Ordinal));

        if (targetProp is null) {
            context.ReportError(
                ae,
                $"Assign effect targets property '{propAccess.Name}' which does not exist on entity '{entity.Name}'.",
                DomainModelDiagnosticCodes.EffectBinding);
            return;
        }

        if (targetProp.Constraints.Count == 0) return;

        // ── Validate literal assignments against property constraints ──────
        if (ae.Value is Literal lit) {
            ValidateLiteralAgainstConstraints(context, ae, lit.Value, targetProp);
            return;
        }

        // ── Propagate the target's range onto derived (arithmetic) values ────
        if (ae.Value is Add or Subtract or Multiply) {
            ValidateDerivedValueRange(context, ae, ae.Value, entity, action, targetProp);
        }

        // ── Validate parameter-to-property constraint compatibility ─────────
        if (ae.Value is ParameterAccess pa && action is not null) {
            var sourceParam = action.Parameters
                .FirstOrDefault(p => string.Equals(p.Name, pa.Name, StringComparison.Ordinal));
            if (sourceParam is not null) {
                ValidateParameterConstraintCompatibility(context, ae, sourceParam, targetProp);

                // Also check DownstreamConstraintsMetadata if ConstraintPropagationAnalyzer has run
                var downstreamMeta = context.GetMetadata<DownstreamConstraintsMetadata>(sourceParam);
                if (downstreamMeta is not null) {
                    ValidateDownstreamConstraints(context, ae, downstreamMeta.Constraints, targetProp, sourceParam);
                }
            }
        }
    }

    private static void ValidateLiteralAgainstConstraints(
        AnalysisContext context, Node errorNode, object? literalValue, Property targetProp) {
        foreach (var constraint in targetProp.Constraints) {
            var verdict = ConstraintValidation.Evaluate(constraint, literalValue);
            if (verdict == ConstraintVerdict.Undecided) {
                ConstraintValidation.ReportUndecided(
                    context, errorNode, constraint, $"on property '{targetProp.Name}'");
                continue;
            }
            if (verdict == ConstraintVerdict.Violated) {
                var display = FormatValueForMessage(literalValue);
                context.ReportError(
                    errorNode,
                    $"Assigned value '{display}' violates constraint {ConstraintValidation.Describe(constraint)} " +
                    $"on property '{targetProp.Name}'.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
            }
        }
    }

    private static void ValidateParameterConstraintCompatibility(
        AnalysisContext context, Node errorNode, Property sourceParam, Property targetProp) {
        // Check that the parameter's constraints don't allow values the property prohibits.
        // For each property constraint, check if any value allowed by the parameter would violate it.
        // We use a simple subsumption check: the parameter's range/length must be within the property's.
        foreach (var propConstraint in targetProp.Constraints) {
            // Find the matching constraint on the parameter (same type)
            var paramConstraint = sourceParam.Constraints
                .FirstOrDefault(c => c.GetType() == propConstraint.GetType());
            if (paramConstraint is null) {
                // Parameter has no constraint of this type — parameter could carry any value,
                // including ones that violate the property constraint.
                context.ReportWarning(
                    errorNode,
                    $"Parameter '{sourceParam.Name}' has no {ConstraintValidation.Describe(propConstraint)} constraint, " +
                    $"but flows to property '{targetProp.Name}' which requires " +
                    $"{ConstraintValidation.Describe(propConstraint)}. The parameter may carry values " +
                    $"that violate the property constraint.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
                continue;
            }

            // Check parameter constraint does not exceed property constraint bounds
            if (!IsConstraintSubsumed(paramConstraint, propConstraint)) {
                context.ReportWarning(
                    errorNode,
                    $"Parameter '{sourceParam.Name}' has {ConstraintValidation.Describe(paramConstraint)} " +
                    $"which allows values outside property '{targetProp.Name}' constraint " +
                    $"{ConstraintValidation.Describe(propConstraint)}.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
            }
        }
    }

    private static void ValidateDownstreamConstraints(
        AnalysisContext context, Node errorNode,
        IReadOnlyList<Constraint> downstreamConstraints, Property targetProp, Property sourceParam) {
        foreach (var dc in downstreamConstraints) {
            // Check if downstream constraint is compatible with target property constraints
            var matchingPropConstraint = targetProp.Constraints
                .FirstOrDefault(c => c.GetType() == dc.GetType());
            if (matchingPropConstraint is null) continue;

            if (!IsConstraintSubsumed(dc, matchingPropConstraint)) {
                context.ReportWarning(
                    errorNode,
                    $"Effect-chain constraint {ConstraintValidation.Describe(dc)} from parameter " +
                    $"'{sourceParam.Name}' exceeds property '{targetProp.Name}' constraint " +
                    $"{ConstraintValidation.Describe(matchingPropConstraint)}.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
            }
        }
    }

    private static string FormatValueForMessage(object? value) => value switch {
        null => "<null>",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? "?"
    };
}