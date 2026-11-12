// EffectAnalyzer partial: the unverified-write channel and derived value range checks.
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

internal sealed partial class EffectAnalyzer {
    /// <summary>The DSL text of an assigned expression, for the findings that name the write.</summary>
    private static readonly DomainDslPrinter WritePrinter = new();

    /// <summary>One constraint decided against one stage context's value.</summary>
    private readonly record struct WriteFinding(WriteVerdict Verdict, Constraint Constraint, AbstractValue? Value);

    /// <summary>
    /// The unverified-write channel (Q4) for an assign whose value is not a literal or a bare parameter.
    /// The value flows from the effect's postcondition in each stage context; every write-relevant
    /// constraint of the target is decided against it. The write gets exactly ONE verdict, the worst
    /// across constraints and stage contexts (Violated, then Unverified, then Satisfied), so one
    /// finding is reported and one fact recorded, and the write is verified only if every context is
    /// Satisfied. A context with no postcondition is Unverified (unknown bound), never skipped.
    /// <list type="bullet">
    /// <item>Satisfied: silent, counted in <see cref="WriteVerificationFacts.Verified"/>.</item>
    /// <item>Violated: DMEFF008 error (entirely outside).</item>
    /// <item>Undecided with a known value range: DMEFF008 warning ("can fall outside").</item>
    /// <item>Undecided with no range: DMCS003 warning naming the write, the property and the constraint.</item>
    /// </list>
    /// </summary>
    private static void ValidateAssignedValue(
        AnalysisContext context,
        Domain domain,
        AssignEffect assign,
        Entity entity,
        Action? action,
        Property targetProp) {
        var values = AssignedValues(context, assign, entity, action);
        WriteFinding? worst = null;
        foreach (var constraint in targetProp.Constraints) {
            if (!ConstraintValidation.IsWriteRelevant(constraint)) continue;
            foreach (var value in values) {
                var finding = new WriteFinding(DecideWrite(constraint, value, assign.Value), constraint, value);
                if (worst is null || finding.Verdict.Worst(worst.Value.Verdict) != worst.Value.Verdict)
                    worst = finding;
            }
        }
        if (worst is not { } decided) return;

        ReportWrite(context, assign, targetProp, decided);
        WriteRecorder.Record(
            context, domain, assign, targetProp.Name, ConstraintValidation.Describe(decided.Constraint), decided.Verdict);
    }

    /// <summary>
    /// The assigned value in each stage context (null where the postcondition is missing). An effect
    /// outside an action body (entry, exit, a when handler) has no postcondition: its value is inferred
    /// in place against the declared ranges, as a create initializer's is.
    /// </summary>
    private static IReadOnlyList<AbstractValue?> AssignedValues(
        AnalysisContext context, AssignEffect assign, Entity entity, Action? action) {
        if (action is null) {
            var (lo, hi) = EffectInvariantAnalyzer.InferNumericRange(assign.Value, entity, null, null);
            return [lo is null && hi is null ? AbstractValue.Unknown : AbstractValue.From([new RangeConstraint(lo, hi)])];
        }

        var meta = context.GetMetadata<ActionInvariantMetadata>(action);
        if (meta is null || meta.StageContexts.Count == 0) return [null];
        return meta.StageContexts
            .Select(ctx => ctx.Postconditions.FirstOrDefault(p => ReferenceEquals(p.Effect, assign))?.Value)
            .ToList();
    }

    private static WriteVerdict DecideWrite(Constraint constraint, AbstractValue? value, DomainExpression assigned) {
        if (ConstraintKinds.Of(constraint) is null) return WriteVerdict.Unverified(UnverifiedReason.NonFlowCheckedKind);
        if (value is null) return WriteVerdict.Unverified(UnverifiedReason.UnknownBound);
        switch (ConstraintValidation.Decide(constraint, value)) {
            case ConstraintVerdict.Satisfied:
                return WriteVerdict.Satisfied;
            case ConstraintVerdict.Violated:
                return WriteVerdict.Violated;
            default:
                return constraint is RangeConstraint && value.NumericRange is not null
                    ? WriteVerdict.Unverified(UnverifiedReason.PossibleViolation)
                    : WriteVerdict.Unverified(OpaqueReason(assigned));
        }
    }

    /// <summary>
    /// Why an expression with no usable range could not be bounded: the operators the interval flow
    /// models (+, -, *, a property, a parameter, a related property) lose the bound when an operand has
    /// none; anything else (division, a call, a match, ...) is not modelled at all.
    /// </summary>
    private static UnverifiedReason OpaqueReason(DomainExpression assigned) => assigned switch {
        Add or Subtract or Multiply or PropertyAccess or ParameterAccess or RelationshipNavigation => UnverifiedReason.UnknownBound,
        _ => UnverifiedReason.UnsupportedOperator,
    };

    private static void ReportWrite(AnalysisContext context, AssignEffect assign, Property targetProp, WriteFinding finding) {
        var constraint = finding.Constraint;
        var described = ConstraintValidation.Describe(constraint);
        var numeric = finding.Value?.NumericRange;
        switch (finding.Verdict.Outcome) {
            case WriteOutcome.Violated:
                context.ReportError(
                    assign,
                    numeric is { } nr && constraint is RangeConstraint
                        ? $"Assigned expression value range [{FormatBound(nr.Min, lower: true)}, {FormatBound(nr.Max, lower: false)}] is entirely outside " +
                          $"constraint {described} on property '{targetProp.Name}'."
                        : $"Constraint {described} on property '{targetProp.Name}' has a bound that cannot be ordered against a number, so no assigned value can satisfy it.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
                break;
            case WriteOutcome.Unverified when finding.Verdict.Reason == UnverifiedReason.PossibleViolation && numeric is { } range:
                context.ReportWarning(
                    assign,
                    $"Assigned expression value range [{FormatBound(range.Min, lower: true)}, {FormatBound(range.Max, lower: false)}] can fall outside " +
                    $"constraint {described} on property '{targetProp.Name}'.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
                break;
            case WriteOutcome.Unverified:
                context.ReportWarning(
                    assign,
                    $"Cannot verify 'assign {targetProp.Name} to {WritePrinter.PrintExpressionText(assign.Value)}' against constraint " +
                    $"{described} on property '{targetProp.Name}': {UnverifiedReasonText(finding.Verdict.Reason)}.",
                    DomainModelDiagnosticCodes.ConstraintUndecided);
                break;
        }
    }

    private static string UnverifiedReasonText(UnverifiedReason? reason) => reason switch {
        UnverifiedReason.UnsupportedOperator => "the expression uses an operator analysis does not model",
        UnverifiedReason.NonFlowCheckedKind => "the constraint kind is not checked against computed values",
        UnverifiedReason.RecursionCut => "the value comes from a recursive invoke whose effect was cut",
        _ => "the assigned value has no bound analysis can use",
    };

    /// <summary>
    /// The create-initializer path (no action, no postconditions): infer the value range against
    /// declared ranges and check it against the target's RangeConstraint. A range entirely outside is a
    /// definite violation (error); one that can extend outside is a possible violation (warning). A value
    /// with no range is still skipped here: S4 moves initializers onto the channel above.
    /// </summary>
    private static void ValidateDerivedValueRange(
        AnalysisContext context,
        Node errorNode,
        DomainExpression value,
        Entity entity,
        Action? action,
        Property targetProp) {
        var range = targetProp.Constraints.OfType<RangeConstraint>().FirstOrDefault();
        if (range is null) return;

        var (lo, hi) = EffectInvariantAnalyzer.InferNumericRange(value, entity, action, null);
        CheckDerivedRange(context, errorNode, AbstractValue.From([new RangeConstraint(lo, hi)]), range, targetProp);
    }

    private static void CheckDerivedRange(
        AnalysisContext context,
        Node errorNode,
        AbstractValue value,
        RangeConstraint range,
        Property targetProp) {
        // No numeric range at all is "Unknown", not the open interval (-∞, +∞): the
        // create-initializer callers reach here through InferNumericRange for every unranged
        // arithmetic initializer. Treating it as open would warn each one against its target.
        // (Assign effects no longer reach here: ValidateAssignedValue records the write as Unverified.
        // S4 does the same for initializers.)
        if (value.NumericRange is not { } numeric) return;

        // The null-endpoint semantics live in WriteVerification.DecideRange: a null target side
        // is satisfied, a null value side against a closed target is Undecided, never Violated.
        switch (WriteVerification.DecideRange(range, value)) {
            case ConstraintVerdict.Violated:
                context.ReportError(
                    errorNode,
                    $"Assigned expression value range [{FormatBound(numeric.Min, lower: true)}, {FormatBound(numeric.Max, lower: false)}] is entirely outside " +
                    $"constraint {ConstraintValidation.Describe(range)} on property '{targetProp.Name}'.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
                break;
            case ConstraintVerdict.Undecided:
                context.ReportWarning(
                    errorNode,
                    $"Assigned expression value range [{FormatBound(numeric.Min, lower: true)}, {FormatBound(numeric.Max, lower: false)}] can fall outside " +
                    $"constraint {ConstraintValidation.Describe(range)} on property '{targetProp.Name}'.",
                    DomainModelDiagnosticCodes.EffectConstraintViolation);
                break;
            case ConstraintVerdict.Satisfied:
            default:
                break;
        }
    }

    private static bool IsRangeSubsumed(RangeConstraint inner, RangeConstraint outer) {
        var innerMin = inner.Minimum is not null ? NumericBound.ToDoubleOrNull(inner.Minimum) : double.NegativeInfinity;
        var innerMax = inner.Maximum is not null ? NumericBound.ToDoubleOrNull(inner.Maximum) : double.PositiveInfinity;
        var outerMin = outer.Minimum is not null ? NumericBound.ToDoubleOrNull(outer.Minimum) : double.NegativeInfinity;
        var outerMax = outer.Maximum is not null ? NumericBound.ToDoubleOrNull(outer.Maximum) : double.PositiveInfinity;

        if (innerMin is null || innerMax is null || outerMin is null || outerMax is null) return false;

        return innerMin.Value >= outerMin.Value && innerMax.Value <= outerMax.Value;
    }

    private static bool HasUnorderableBound(RangeConstraint range) =>
        (range.Minimum is not null && NumericBound.ToDoubleOrNull(range.Minimum) is null)
        || (range.Maximum is not null && NumericBound.ToDoubleOrNull(range.Maximum) is null);
}