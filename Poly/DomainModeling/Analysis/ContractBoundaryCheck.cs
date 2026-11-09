using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Contract;

using And = Poly.DomainModeling.Ontology.And;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// #425 (epic #427, spike #413 decision 21): the boundary check at a resolved bind. The
/// caller's argument must sit inside the endpoint's public payload constraints and the
/// preconditions that reduce to a payload-property comparison. A proof is silence; a
/// contradiction is <c>DMCON014</c> and Lower refuses; anything the merge cannot decide, or
/// that is not readable from the argument, is <c>DMCON015</c> and Lower is not blocked (#442).
/// <para>
/// Scot's 2026-11-08 ruling: library bodies are not walked. Every contributed body is
/// <see cref="ContractBoundaryVerdict.Unknown"/> on the postcondition half, named once. The
/// caller half is the only thing this slice can decide.
/// </para>
/// </summary>
internal static class ContractBoundaryCheck {
    /// <summary>One obligation, its verdict, and whether it is readable from the argument.</summary>
    private readonly record struct Obligation(
        string Name,
        ContractBoundaryVerdict Verdict,
        bool Unreadable);

    public static ContractBoundaryMetadata Check(
        AnalysisContext context,
        ContractBinding binding,
        ImportedContract contract,
        Domain domain,
        ContractEndpoint endpoint,
        Property localParameter,
        bool hasBody) {
        var obligations = Obligations(contract, domain, endpoint, localParameter).ToList();
        var precondition = Verdict(obligations);
        var compatibility = Compatibility(obligations, precondition);
        // Scot 2026-11-08: a contributed body is not walked, so its postcondition half is
        // always unknown. A surface-only contribution has no body to prove either.
        var postcondition = ContractBoundaryVerdict.Unknown;

        Report(context, binding, endpoint, obligations);

        if (hasBody) {
            context.ReportWarning(
                binding,
                $"Contract binding '{binding.Name}' splices a contributed body for endpoint " +
                $"'{contract.Name}.{endpoint.Name}'. Its postconditions are not proved in this slice; " +
                "the half is unknown and Lower is not blocked.",
                DomainModelDiagnosticCodes.ContractBoundaryUnknown);
        }

        return new ContractBoundaryMetadata(precondition, postcondition, compatibility);
    }

    private static void Report(
        AnalysisContext context,
        ContractBinding binding,
        ContractEndpoint endpoint,
        IReadOnlyList<Obligation> obligations) {
        foreach (var obligation in obligations.Where(o => o.Verdict == ContractBoundaryVerdict.Violated)) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' parameter cannot satisfy endpoint " +
                $"'{endpoint.Name}' public constraint '{obligation.Name}': the caller's values are " +
                "disjoint from it. Widen the caller's constraint or narrow the endpoint's surface.",
                DomainModelDiagnosticCodes.ContractBoundaryViolation);
        }

        foreach (var obligation in obligations.Where(o => o.Verdict == ContractBoundaryVerdict.Unknown)) {
            var why = obligation.Unreadable
                ? "it is not decidable from the bound argument"
                : "the caller's constraints do not decide it";
            context.ReportWarning(
                binding,
                $"Contract binding '{binding.Name}' cannot prove endpoint '{endpoint.Name}' public " +
                $"constraint '{obligation.Name}': {why}. Lower is not blocked; tighten the caller's " +
                "constraint to prove it.",
                DomainModelDiagnosticCodes.ContractBoundaryUnknown);
        }
    }

    private static ContractBoundaryVerdict Verdict(IReadOnlyList<Obligation> obligations) {
        if (obligations.Any(o => o.Verdict == ContractBoundaryVerdict.Violated))
            return ContractBoundaryVerdict.Violated;
        if (obligations.Any(o => o.Verdict == ContractBoundaryVerdict.Unknown))
            return ContractBoundaryVerdict.Unknown;
        return ContractBoundaryVerdict.Proven;
    }

    private static ContractCompatibility Compatibility(
        IReadOnlyList<Obligation> obligations,
        ContractBoundaryVerdict verdict) {
        if (verdict == ContractBoundaryVerdict.Violated)
            return ContractCompatibility.Reverse;
        if (obligations.Any(o => o.Unreadable))
            return ContractCompatibility.Incomparable;
        if (verdict == ContractBoundaryVerdict.Unknown)
            return ContractCompatibility.Reverse;
        return ContractCompatibility.Satisfies;
    }

    // ── Obligations ─────────────────────────────────────────────

    private static IEnumerable<Obligation> Obligations(
        ImportedContract contract,
        Domain domain,
        ContractEndpoint endpoint,
        Property localParameter) {
        var payload = FindValueType(contract, domain, endpoint.PayloadType.TypeName);
        var local = FindValueType(contract, domain, localParameter.Type.TypeName);

        if (payload is not null && local is not null) {
            foreach (var obligation in TypeConstraints(payload, local))
                yield return obligation;

            foreach (var property in payload.Properties) {
                var localProperty = local.Properties.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, property.Name, StringComparison.Ordinal));
                if (localProperty is null) {
                    yield return new Obligation(
                        $"{property.Name} on payload '{payload.Name}'",
                        ContractBoundaryVerdict.Unknown,
                        Unreadable: true);
                    continue;
                }
                foreach (var constraint in property.Constraints)
                    yield return ConstraintObligation(property.Name, constraint, localProperty.Constraints);
            }
        }

        foreach (var policy in endpoint.Preconditions)
            yield return PreconditionObligation(policy, local);
    }

    private static IEnumerable<Obligation> TypeConstraints(ValueType payload, ValueType local) {
        foreach (var constraint in payload.Constraints)
            yield return ConstraintObligation(payload.Name, constraint, local.Constraints);
    }

    /// <summary>
    /// The caller must imply the payload's constraint. Identity proves it; an empty
    /// intersection violates it; a net that is not the caller's own constraint (the caller is
    /// wider) or a merge that cannot decide is unknown (#442).
    /// </summary>
    private static Obligation ConstraintObligation(
        string subject,
        Constraint obligation,
        IReadOnlyList<Constraint> caller) {
        var obligationText = Printer.PrintConstraintText(obligation);
        var name = $"{subject} {obligationText}";

        foreach (var candidate in caller.Where(c => c.GetType() == obligation.GetType())) {
            var candidateText = Printer.PrintConstraintText(candidate);
            if (string.Equals(candidateText, obligationText, StringComparison.Ordinal))
                return new Obligation(name, ContractBoundaryVerdict.Proven, Unreadable: false);

            var merge = candidate.MergeResult(obligation);
            if (merge.IsEmpty)
                return new Obligation(name, ContractBoundaryVerdict.Violated, Unreadable: false);
            if (merge.IsNet
                && string.Equals(
                    Printer.PrintConstraintText(merge.Net!), candidateText, StringComparison.Ordinal))
                return new Obligation(name, ContractBoundaryVerdict.Proven, Unreadable: false);

            return new Obligation(name, ContractBoundaryVerdict.Unknown, Unreadable: false);
        }

        // No caller constraint of this kind: the caller is wider, so the obligation is undecided.
        return new Obligation(name, ContractBoundaryVerdict.Unknown, Unreadable: false);
    }

    /// <summary>
    /// A precondition is readable only when it is a comparison of a payload property against a
    /// literal. A comparison against a symbol, a navigation, or a property the argument does not
    /// carry is unknown, never a violation: failing it would reject a legal call.
    /// </summary>
    private static Obligation PreconditionObligation(Policy policy, ValueType? local) {
        var bounds = new List<(string Property, ComparisonKind Kind, object Value)>();
        if (!TryReadBounds(policy.Expression, bounds))
            return new Obligation(policy.Name, ContractBoundaryVerdict.Unknown, Unreadable: true);

        var worst = ContractBoundaryVerdict.Proven;
        foreach (var (propertyName, kind, value) in bounds) {
            var localProperty = local?.Properties.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, propertyName, StringComparison.Ordinal));
            if (localProperty is null)
                return new Obligation(policy.Name, ContractBoundaryVerdict.Unknown, Unreadable: true);

            var verdict = ComparisonVerdict(localProperty.Constraints, kind, value);
            if (verdict == ContractBoundaryVerdict.Violated)
                return new Obligation(policy.Name, ContractBoundaryVerdict.Violated, Unreadable: false);
            if (verdict == ContractBoundaryVerdict.Unknown)
                worst = ContractBoundaryVerdict.Unknown;
        }
        return new Obligation(policy.Name, worst, Unreadable: false);
    }

    private static bool TryReadBounds(
        DomainExpression expression,
        List<(string Property, ComparisonKind Kind, object Value)> bounds) {
        switch (expression) {
            case And and:
                return TryReadBounds(and.Left, bounds) && TryReadBounds(and.Right, bounds);
            case Comparison { Left: PropertyAccess access, Right: Literal literal } comparison
                when literal.Value is not null
                     && comparison.Kind is not ComparisonKind.NotEqual:
                bounds.Add((access.Name, comparison.Kind, literal.Value));
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Numeric bounds only. The caller's range (or equality) must decide the comparison;
    /// anything else is unknown, so a strict bound is never widened into a proof.
    /// </summary>
    private static ContractBoundaryVerdict ComparisonVerdict(
        IReadOnlyList<Constraint> constraints,
        ComparisonKind kind,
        object value) {
        if (!TryToDouble(value, out var bound))
            return ContractBoundaryVerdict.Unknown;

        if (constraints.OfType<EqualityConstraint>().FirstOrDefault() is { } equality) {
            return TryToDouble(equality.ExpectedValue, out var equal)
                ? CompareInterval(equal, equal, kind, bound)
                : ContractBoundaryVerdict.Unknown;
        }

        if (constraints.OfType<RangeConstraint>().FirstOrDefault() is not { } range)
            return ContractBoundaryVerdict.Unknown;

        var minimum = range.Minimum is not null && TryToDouble(range.Minimum, out var low) ? low : (double?)null;
        var maximum = range.Maximum is not null && TryToDouble(range.Maximum, out var high) ? high : (double?)null;
        return CompareInterval(minimum, maximum, kind, bound);
    }

    private static ContractBoundaryVerdict CompareInterval(
        double? minimum,
        double? maximum,
        ComparisonKind kind,
        double bound) {
        return kind switch {
            ComparisonKind.GreaterThan => minimum > bound
                ? ContractBoundaryVerdict.Proven
                : maximum <= bound ? ContractBoundaryVerdict.Violated : ContractBoundaryVerdict.Unknown,
            ComparisonKind.GreaterThanOrEqual => minimum >= bound
                ? ContractBoundaryVerdict.Proven
                : maximum < bound ? ContractBoundaryVerdict.Violated : ContractBoundaryVerdict.Unknown,
            ComparisonKind.LessThan => maximum < bound
                ? ContractBoundaryVerdict.Proven
                : minimum >= bound ? ContractBoundaryVerdict.Violated : ContractBoundaryVerdict.Unknown,
            ComparisonKind.LessThanOrEqual => maximum <= bound
                ? ContractBoundaryVerdict.Proven
                : minimum > bound ? ContractBoundaryVerdict.Violated : ContractBoundaryVerdict.Unknown,
            ComparisonKind.Equal => minimum == bound && maximum == bound
                ? ContractBoundaryVerdict.Proven
                : bound < minimum || bound > maximum
                    ? ContractBoundaryVerdict.Violated
                    : ContractBoundaryVerdict.Unknown,
            _ => ContractBoundaryVerdict.Unknown,
        };
    }

    private static bool TryToDouble(object value, out double result) {
        switch (value) {
            case sbyte or byte or short or ushort or int or uint or long or ulong
                or float or double or decimal:
                result = Convert.ToDouble(value);
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static ValueType? FindValueType(ImportedContract contract, Domain domain, string name) =>
        contract.Types.FirstOrDefault(type => string.Equals(type.Name, name, StringComparison.Ordinal))
        ?? domain.Types.OfType<ValueType>()
            .FirstOrDefault(type => string.Equals(type.Name, name, StringComparison.Ordinal));

    private static readonly DomainDslPrinter Printer = new();
}