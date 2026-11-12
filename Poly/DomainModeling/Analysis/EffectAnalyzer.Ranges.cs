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
        ValidateWrittenValue(
            context, domain, assign, assign.Value,
            $"assign {targetProp.Name} to {WritePrinter.PrintExpressionText(assign.Value)}",
            targetProp, values);
    }

    /// <summary>
    /// The create-initializer arm of the same channel (S4): one verdict, one finding and one fact for
    /// the initializer. The expression is inferred in the CREATOR's scope (the entity whose action
    /// creates) with the creating action's parameters in scope, exactly as master's
    /// <c>InferNumericRange</c> call did — never in the created entity's scope, where a same-named
    /// property would answer with the wrong constraints (A1).
    /// </summary>
    private static void ValidateInitializerValue(
        AnalysisContext context,
        Domain domain,
        PropertyBinding initializer,
        Entity creatorEntity,
        Action? action,
        Entity targetEntity,
        Property targetProp) {
        if (initializer.Expression is Literal literal) {
            ValidateLiteralAgainstConstraints(context, initializer, literal.Value, targetProp, domain);
            return;
        }

        var value = EffectInvariantAnalyzer.InferValue(initializer.Expression, creatorEntity, action);
        ValidateWrittenValue(
            context, domain, initializer, initializer.Expression,
            $"create {targetEntity.Name} {{ {targetProp.Name}: {WritePrinter.PrintExpressionText(initializer.Expression)} }}",
            targetProp, [value]);
    }

    /// <summary>The shared decision: every write-relevant constraint against every value, worst wins.</summary>
    private static void ValidateWrittenValue(
        AnalysisContext context,
        Domain domain,
        Node errorNode,
        DomainExpression valueExpression,
        string writeText,
        Property targetProp,
        IReadOnlyList<AbstractValue?> values) {
        WriteFinding? worst = null;
        foreach (var constraint in targetProp.Constraints) {
            if (!ConstraintValidation.IsWriteRelevant(constraint)) continue;
            foreach (var value in values) {
                var finding = new WriteFinding(DecideWrite(constraint, value, valueExpression), constraint, value);
                // A strictly worse verdict replaces the current one; on an equal rank the FIRST
                // decided value wins, consistent with WriteVerdict.Worst returning `this` on a tie.
                // (Ranks order the Unverified reasons too, so the outcome does not depend on the
                // stage order — review H4.)
                if (worst is null || finding.Verdict.Worst(worst.Value.Verdict) != worst.Value.Verdict)
                    worst = finding;
            }
        }
        if (worst is not { } decided) return;

        ReportWrite(context, errorNode, writeText, targetProp, decided);
        WriteRecorder.Record(
            context, domain, errorNode, targetProp.Name, ConstraintValidation.Describe(decided.Constraint), decided.Verdict);
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
        if (ConstraintKinds.Of(constraint) is null)
            return WriteVerdict.Unverified(
                UnverifiedReason.NonFlowCheckedKind, DomainModelDiagnosticCodes.ConstraintUndecided);
        if (value is null)
            return WriteVerdict.Unverified(UnverifiedReason.UnknownBound, DomainModelDiagnosticCodes.ConstraintUndecided);
        switch (ConstraintValidation.Decide(constraint, value)) {
            case ConstraintVerdict.Satisfied:
                return WriteVerdict.Satisfied;
            case ConstraintVerdict.Violated:
                return WriteVerdict.Violated;
            default:
                return constraint is RangeConstraint && value.NumericRange is not null
                    ? WriteVerdict.Unverified(
                        UnverifiedReason.PossibleViolation, DomainModelDiagnosticCodes.EffectConstraintViolation)
                    : WriteVerdict.Unverified(OpaqueReason(assigned), DomainModelDiagnosticCodes.ConstraintUndecided);
        }
    }

    /// <summary>
    /// Why an expression with no usable range could not be bounded: an operator the interval flow does
    /// not model anywhere in the tree (division, a call, a match, ...) is named first; otherwise the
    /// modelled node simply lost its bound because an operand has none.
    /// </summary>
    private static UnverifiedReason OpaqueReason(DomainExpression assigned) =>
        HasUnmodelledOperator(assigned) ? UnverifiedReason.UnsupportedOperator : UnverifiedReason.UnknownBound;

    /// <summary>True when any node of the expression is an operator the interval flow does not model (pre-order).
    /// The one purity gate: the shared evaluator's X − X cancellation and the opaque-reason text use it (B7).</summary>
    internal static bool HasUnmodelledOperator(DomainExpression expression) => expression switch {
        Add add => HasUnmodelledOperator(add.Left) || HasUnmodelledOperator(add.Right),
        Subtract sub => HasUnmodelledOperator(sub.Left) || HasUnmodelledOperator(sub.Right),
        Multiply mul => HasUnmodelledOperator(mul.Left) || HasUnmodelledOperator(mul.Right),
        PropertyAccess or ParameterAccess or RelationshipNavigation or Literal => false,
        _ => true,
    };

    private static void ReportWrite(
        AnalysisContext context, Node errorNode, string writeText, Property targetProp, WriteFinding finding) {
        var constraint = finding.Constraint;
        var described = ConstraintValidation.Describe(constraint);
        var numeric = finding.Value?.NumericRange;
        switch (finding.Verdict.Outcome) {
            case WriteOutcome.Violated:
                context.ReportError(
                    errorNode,
                    numeric is { } nr && constraint is RangeConstraint
                        ? $"Assigned expression value range [{FormatBound(nr.Min, lower: true)}, {FormatBound(nr.Max, lower: false)}] is entirely outside " +
                          $"constraint {described} on property '{targetProp.Name}'."
                        : $"Constraint {described} on property '{targetProp.Name}' has a bound that cannot be ordered against a number, so no assigned value can satisfy it.",
                    finding.Verdict.Code!);
                break;
            case WriteOutcome.Unverified when finding.Verdict.Reason == UnverifiedReason.PossibleViolation && numeric is { } range:
                context.ReportWarning(
                    errorNode,
                    $"Assigned expression value range [{FormatBound(range.Min, lower: true)}, {FormatBound(range.Max, lower: false)}] can fall outside " +
                    $"constraint {described} on property '{targetProp.Name}'.",
                    finding.Verdict.Code!);
                break;
            case WriteOutcome.Unverified:
                context.ReportWarning(
                    errorNode,
                    $"Cannot verify '{writeText}' against constraint " +
                    $"{described} on property '{targetProp.Name}': {UnverifiedReasonText.Of(finding.Verdict.Reason)}.",
                    finding.Verdict.Code!);
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
}