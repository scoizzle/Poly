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
/// preconditions that name the bound argument. A proof is silence; a contradiction is
/// <c>DMCON014</c> and Lower refuses; anything the merge cannot decide is <c>DMCON015</c> and
/// Lower is not blocked (#442).
/// <para>
/// Two rules keep this from claiming more than it knows. A precondition is written against the
/// <b>entity</b> the exported action came from, so a bare <see cref="PropertyAccess"/> is entity
/// state and is never decided from the argument; only a <see cref="ParameterAccess"/> naming the
/// bound parameter of a primitive payload is argument-scoped. And <c>Empty</c> is only a
/// disjointness proof for <c>range</c>, <c>length</c> and <c>equals</c>: any other constraint kind
/// the payload relation cannot satisfy leaves <c>null</c>, so the bind stays on the generic
/// incompatibility path exactly as <c>origin/master</c> refused it.
/// </para>
/// <para>
/// Scot's 2026-11-08 ruling: library bodies are not walked. Every contributed body is
/// <see cref="ContractBoundaryVerdict.Unknown"/> on the postcondition half, named once.
/// </para>
/// </summary>
internal static class ContractBoundaryCheck {
    /// <summary>One obligation, its verdict, and whether it is readable from the argument.</summary>
    private sealed record Obligation(
        string Name,
        ContractBoundaryVerdict Verdict,
        bool Unreadable);

    /// <summary>
    /// The boundary note, or <c>null</c> when the payload relation still refuses the caller and
    /// the bind must stay <c>Incompatible</c>.
    /// </summary>
    public static ContractBoundaryMetadata? Check(
        AnalysisContext context,
        ContractBinding binding,
        ImportedContract contract,
        Domain domain,
        ContractEndpoint endpoint,
        Property localParameter,
        bool hasBody) {
        var obligations = Obligations(contract, domain, endpoint, localParameter);
        if (obligations is null)
            return null;

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
            // An unreadable obligation is entity state, or a property the argument does not
            // carry: the caller cannot clear it by tightening anything, so a fix-up hint would
            // be unactionable. Only a decided-but-wider obligation gets one.
            var message = obligation.Unreadable
                ? $"Contract binding '{binding.Name}' cannot prove endpoint '{endpoint.Name}' public " +
                  $"constraint '{obligation.Name}': it is not decidable from the bound argument. " +
                  "Lower is not blocked."
                : $"Contract binding '{binding.Name}' cannot prove endpoint '{endpoint.Name}' public " +
                  $"constraint '{obligation.Name}': the caller's constraints do not decide it. " +
                  "Lower is not blocked; tighten the caller's constraint to prove it.";
            context.ReportWarning(
                binding,
                message,
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

    /// <summary>
    /// Every obligation the argument must satisfy, or <c>null</c> when one of them leaves the
    /// payload relation as the only judge (see <see cref="ConstraintObligation"/>).
    /// </summary>
    private static List<Obligation>? Obligations(
        ImportedContract contract,
        Domain domain,
        ContractEndpoint endpoint,
        Property localParameter) {
        var obligations = new List<Obligation>();
        var payload = FindValueType(contract, domain, endpoint.PayloadType.TypeName);
        var local = FindValueType(contract, domain, localParameter.Type.TypeName);

        if (payload is not null && local is not null) {
            foreach (var constraint in payload.Constraints) {
                var obligation = ConstraintObligation(payload.Name, constraint, local.Constraints);
                if (obligation is null)
                    return null;
                obligations.Add(obligation);
            }

            foreach (var property in payload.Properties) {
                var localProperty = local.Properties.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, property.Name, StringComparison.Ordinal));
                if (localProperty is null) {
                    obligations.Add(new Obligation(
                        $"{property.Name} on payload '{payload.Name}'",
                        ContractBoundaryVerdict.Unknown,
                        Unreadable: true));
                    continue;
                }
                foreach (var constraint in property.Constraints) {
                    var obligation = ConstraintObligation(property.Name, constraint, localProperty.Constraints);
                    if (obligation is null)
                        return null;
                    obligations.Add(obligation);
                }
            }
        }

        foreach (var policy in endpoint.Preconditions)
            obligations.Add(PreconditionObligation(policy, localParameter, payload));

        return obligations;
    }

    /// <summary>
    /// The caller must imply the payload's constraint. Identity proves it; an empty
    /// intersection violates it; a net that is not the caller's own constraint (the caller is
    /// wider) is unknown.
    /// <para>
    /// <c>Empty</c> is a disjointness proof only for <c>range</c>, <c>length</c> and
    /// <c>equals</c>. Two different <c>pattern</c> strings or two different <c>default</c>s also
    /// merge to <c>Empty</c> without being disjoint (#442 left those arms reading cannot-decide
    /// as empty), so a mismatch there returns <c>null</c> and the bind keeps the generic
    /// incompatibility refusal it had on <c>origin/master</c>.
    /// </para>
    /// </summary>
    private static Obligation? ConstraintObligation(
        string subject,
        Constraint obligation,
        IReadOnlyList<Constraint> caller) {
        var obligationText = Printer.PrintConstraintText(obligation);
        var name = $"{subject} {obligationText}";
        var disjointIsProven = obligation is RangeConstraint or LengthConstraint or EqualityConstraint;

        var sawSameKind = false;
        foreach (var candidate in caller) {
            var merge = obligation.MergeResult(candidate);
            if (merge.IsEmpty)
                return disjointIsProven
                    ? new Obligation(name, ContractBoundaryVerdict.Violated, Unreadable: false)
                    : null;
            if (!merge.IsNet)
                continue; // Unmergeable: this constraint says nothing about the obligation.

            sawSameKind = true;
            if (string.Equals(
                    Printer.PrintConstraintText(merge.Net!),
                    Printer.PrintConstraintText(candidate),
                    StringComparison.Ordinal))
                return new Obligation(name, ContractBoundaryVerdict.Proven, Unreadable: false);
        }

        // A same-kind constraint that did not imply the obligation means the caller is wider.
        if (sawSameKind || disjointIsProven)
            return new Obligation(name, ContractBoundaryVerdict.Unknown, Unreadable: false);

        // The caller carries no constraint of a kind master refused; keep refusing.
        return null;
    }

    /// <summary>
    /// A precondition is decided only when it names the bound argument: a parameter access on a
    /// primitive payload, compared to a literal. A bare <see cref="PropertyAccess"/> comes from
    /// an exported <c>require</c>, which is written against the source <b>entity</b>, so reading
    /// it on the payload would judge entity state by the argument. That is undecidable here and
    /// is unknown, never a violation.
    /// </summary>
    private static Obligation PreconditionObligation(
        Policy policy,
        Property localParameter,
        ValueType? payload) {
        var bounds = new List<(ComparisonKind Kind, object Value)>();
        if (!TryReadBounds(policy.Expression, localParameter.Name, payload, bounds))
            return new Obligation(policy.Name, ContractBoundaryVerdict.Unknown, Unreadable: true);

        var worst = ContractBoundaryVerdict.Proven;
        foreach (var (kind, value) in bounds) {
            var verdict = ComparisonVerdict(localParameter.Constraints, kind, value);
            if (verdict == ContractBoundaryVerdict.Violated)
                return new Obligation(policy.Name, ContractBoundaryVerdict.Violated, Unreadable: false);
            if (verdict == ContractBoundaryVerdict.Unknown)
                worst = ContractBoundaryVerdict.Unknown;
        }
        return new Obligation(policy.Name, worst, Unreadable: false);
    }

    private static bool TryReadBounds(
        DomainExpression expression,
        string parameterName,
        ValueType? payload,
        List<(ComparisonKind Kind, object Value)> bounds) {
        // Only a primitive payload has a scalar argument to compare; a value-type payload is an
        // object here, so `request > 0` says nothing about it.
        if (payload is not null)
            return false;

        switch (expression) {
            case And and:
                return TryReadBounds(and.Left, parameterName, payload, bounds)
                       && TryReadBounds(and.Right, parameterName, payload, bounds);
            case Comparison { Left: ParameterAccess access, Right: Literal literal } comparison
                when string.Equals(access.Name, parameterName, StringComparison.Ordinal)
                     && literal.Value is not null
                     && comparison.Kind is not ComparisonKind.NotEqual:
                bounds.Add((comparison.Kind, literal.Value));
                return true;
            case Comparison { Left: Literal literal, Right: ParameterAccess access } comparison
                when string.Equals(access.Name, parameterName, StringComparison.Ordinal)
                     && literal.Value is not null
                     && comparison.Kind is not ComparisonKind.NotEqual:
                bounds.Add((Flip(comparison.Kind), literal.Value));
                return true;
            default:
                return false;
        }
    }

    private static ComparisonKind Flip(ComparisonKind kind) => kind switch {
        ComparisonKind.LessThan => ComparisonKind.GreaterThan,
        ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThanOrEqual,
        ComparisonKind.GreaterThan => ComparisonKind.LessThan,
        ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThanOrEqual,
        _ => kind,
    };

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