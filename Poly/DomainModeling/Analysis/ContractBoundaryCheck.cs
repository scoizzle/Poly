using Poly.Ast.Nodes;
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
/// The postcondition half walks the contributed body. A body that provably writes a declared
/// payload constraint outside its bound, or nulls a required payload member, is
/// <see cref="ContractBoundaryVerdict.Violated"/> and Lower refuses. A write the walk cannot
/// decide, or a payload handed to a call that may mutate it, is unknown: one
/// <c>DMCON015</c> naming the member, and Lower is not blocked. A surface-only contribution
/// has no body to walk, so its postcondition half stays unknown and silent.
/// </para>
/// </summary>
internal static class ContractBoundaryCheck {
    /// <summary>The bound payload parameter name the splice contract fixes.</summary>
    private const string PayloadParameter = "request";
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

        var payload = FindValueType(contract, domain, endpoint.PayloadType.TypeName);
        var precondition = Verdict(obligations);
        var compatibility = Compatibility(obligations, precondition);
        var postcondition = Postcondition(context, binding, contract, domain, endpoint, payload, hasBody);

        Report(context, binding, endpoint, obligations);

        return new ContractBoundaryMetadata(precondition, postcondition, compatibility);
    }

    // ── Postcondition half ──────────────────────────────────────

    /// <summary>
    /// The body's obligations against the endpoint's declared payload. Every node is walked,
    /// including the final <c>return</c> expression, which can still hand the payload to a call.
    /// </summary>
    private static ContractBoundaryVerdict Postcondition(
        AnalysisContext context,
        ContractBinding binding,
        ImportedContract contract,
        Domain domain,
        ContractEndpoint endpoint,
        ValueType? payload,
        bool hasBody) {
        // A surface-only contribution has no body to prove. Unknown, and silent: the caller
        // half is the only thing the bind can decide (#425 §4).
        if (!hasBody)
            return ContractBoundaryVerdict.Unknown;

        var body = ContributedBody(domain, contract, endpoint);
        if (body is null || body.Nodes.Count == 0) {
            context.ReportWarning(
                binding,
                $"Contract binding '{binding.Name}' endpoint '{contract.Name}.{endpoint.Name}' " +
                "records a contributed body that analysis could not read (missing, empty, or its " +
                "factory threw). The postcondition half is unknown; Lower will refuse the body.",
                DomainModelDiagnosticCodes.ContractBoundaryUnknown);
            return ContractBoundaryVerdict.Unknown;
        }

        var obligations = BodyObligations(payload, body);
        foreach (var obligation in obligations.Where(o => o.Verdict == ContractBoundaryVerdict.Violated)) {
            context.ReportError(
                binding,
                $"Contract binding '{binding.Name}' endpoint '{contract.Name}.{endpoint.Name}' " +
                $"body cannot satisfy payload '{payload?.Name ?? endpoint.PayloadType.TypeName}' " +
                $"constraint '{obligation.Name}': the body writes a value outside it. A contributed " +
                "body must satisfy the surface it implements; Lower refuses.",
                DomainModelDiagnosticCodes.ContractBoundaryViolation);
        }

        foreach (var obligation in obligations.Where(o => o.Verdict == ContractBoundaryVerdict.Unknown)) {
            var payloadName = payload?.Name ?? endpoint.PayloadType.TypeName;
            var message = obligation.Unreadable
                ? $"Contract binding '{binding.Name}' endpoint '{contract.Name}.{endpoint.Name}' " +
                  $"cannot prove payload '{payloadName}' from the contributed body: " +
                  $"{obligation.Name}. " + (obligation.Name.StartsWith("the body does not end", StringComparison.Ordinal)
                      ? "Lower will refuse the body."
                      : "Lower is not blocked.")
                : $"Contract binding '{binding.Name}' endpoint '{contract.Name}.{endpoint.Name}' " +
                  $"cannot prove payload '{payloadName}' constraint '{obligation.Name}' from the " +
                  "contributed body: the assigned value cannot be decided by the walk. " +
                  "Lower is not blocked.";
            context.ReportWarning(
                binding,
                message,
                DomainModelDiagnosticCodes.ContractBoundaryUnknown);
        }

        return Verdict(obligations);
    }

    private static Block? ContributedBody(Domain domain, ImportedContract contract, ContractEndpoint endpoint) {
        if (contract.UsesId is null)
            return null;
        var contribution = RuntimeAnalysisCache.Session(domain).FindContractContribution(contract.UsesId);
        if (contribution is null || !contribution.Bodies.TryGetValue(endpoint.Name, out var factory))
            return null;
        try {
            return factory();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception) {
            // A throwing factory is a body analysis cannot read, not a crash in Analyze.
            return null;
        }
    }

    /// <summary>
    /// One obligation per payload member the body can change, decided from the member's final
    /// value on every path. Requiredness is implicit: a payload member is required unless it is
    /// marked optional, and the DSL cannot mark one optional.
    /// </summary>
    private static IReadOnlyList<Obligation> BodyObligations(ValueType? payload, Block body) {
        var obligations = new List<Obligation>();
        if (payload is null)
            return obligations;

        // Lower requires a final `return <value>`; analysis must not call any other shape proven.
        if (body.Nodes.Count == 0 || body.Nodes[^1] is not Return { Value: not null }) {
            obligations.Add(new Obligation(
                "the body does not end in `return <value>`",
                ContractBoundaryVerdict.Unknown,
                Unreadable: true));
            return obligations;
        }

        // The final return is walked too: its expression can hand the payload to a call.
        var state = new BodyState();
        WalkBody(body.Nodes, state, payload);

        if (state.PayloadMayChange) {
            obligations.Add(new Obligation(
                "the body may mutate or alias the payload in a way the walk cannot read",
                ContractBoundaryVerdict.Unknown,
                Unreadable: true));
            return obligations;
        }

        foreach (var (name, wrote) in state.Properties) {
            var property = payload.Properties.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (property is null) {
                obligations.Add(new Obligation(
                    $"{name} on payload '{payload.Name}'",
                    ContractBoundaryVerdict.Unknown,
                    Unreadable: false));
                continue;
            }

            if (!wrote.Known) {
                obligations.Add(new Obligation(
                    $"{name} on payload '{payload.Name}'",
                    ContractBoundaryVerdict.Unknown,
                    Unreadable: false));
                continue;
            }

            AddWriteObligation(property, wrote.Value, obligations);
        }

        return obligations;
    }

    private static void AddWriteObligation(
        Property property,
        object? value,
        List<Obligation> obligations) {
        // Presence is the DMEFF015-class invariant: a null into a required payload member
        // breaks it, whatever the member's other constraints say.
        var optional = property.Constraints.Any(constraint => constraint is OptionalConstraint);
        if (value is null && !optional) {
            obligations.Add(new Obligation(
                $"{property.Name} required (non-null)",
                ContractBoundaryVerdict.Violated,
                Unreadable: false));
            return;
        }

        if (value is null)
            return;

        Constraint? violated = null;
        Constraint? unknown = null;
        foreach (var constraint in property.Constraints) {
            var verdict = ConstantVerdict(constraint, value);
            if (verdict == ContractBoundaryVerdict.Violated && violated is null)
                violated = constraint;
            else if (verdict == ContractBoundaryVerdict.Unknown && unknown is null)
                unknown = constraint;
        }

        if (violated is not null) {
            obligations.Add(new Obligation(
                $"{property.Name} {Printer.PrintConstraintText(violated)}",
                ContractBoundaryVerdict.Violated,
                Unreadable: false));
        }
        else if (unknown is not null) {
            obligations.Add(new Obligation(
                $"{property.Name} {Printer.PrintConstraintText(unknown)}",
                ContractBoundaryVerdict.Unknown,
                Unreadable: false));
        }
    }

    /// <summary>
    /// A literal against one declared constraint. Only a kind whose meaning the value can be
    /// checked against is decided; a type mismatch, or a kind with no static check, is unknown
    /// so a body is never called violated on a comparison the walk cannot make.
    /// </summary>
    private static ContractBoundaryVerdict ConstantVerdict(Constraint constraint, object value) {
        switch (constraint) {
            case RequiredConstraint or OptionalConstraint or DefaultValueConstraint:
                return ContractBoundaryVerdict.Proven;
            case RangeConstraint range:
                return IsNumber(value) && HasNumericBounds(range)
                    ? ConstraintValidation.IsSatisfiedBy(range, value)
                        ? ContractBoundaryVerdict.Proven
                        : ContractBoundaryVerdict.Violated
                    : ContractBoundaryVerdict.Unknown;
            case LengthConstraint length:
                return value is string
                    ? ConstraintValidation.IsSatisfiedBy(length, value)
                        ? ContractBoundaryVerdict.Proven
                        : ContractBoundaryVerdict.Violated
                    : ContractBoundaryVerdict.Unknown;
            case PatternConstraint pattern:
                if (value is not string text)
                    return ContractBoundaryVerdict.Unknown;
                try {
                    return Regex.IsMatch(text, pattern.Pattern)
                        ? ContractBoundaryVerdict.Proven
                        : ContractBoundaryVerdict.Violated;
                }
                catch (RegexParseException) {
                    return ContractBoundaryVerdict.Unknown;
                }
            case EqualityConstraint equality:
                return NumberEquals(value, equality.ExpectedValue) is { } equal
                    ? equal ? ContractBoundaryVerdict.Proven : ContractBoundaryVerdict.Violated
                    : ContractBoundaryVerdict.Unknown;
            default:
                // unique has no static check.
                return ContractBoundaryVerdict.Unknown;
        }
    }

    private static bool? NumberEquals(object value, object? expected) {
        if (IsNumber(value) && IsNumber(expected))
            return Convert.ToDouble(value) == Convert.ToDouble(expected);
        return null;
    }

    private static bool HasNumericBounds(RangeConstraint range) =>
        (range.Minimum is null || IsNumber(range.Minimum))
        && (range.Maximum is null || IsNumber(range.Maximum));

    private static bool IsNumber(object? value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong
            or float or double or decimal;

    // ── Body walk ───────────────────────────────────────────────

    /// <summary>One payload member's final value, or unknown when a path leaves it undecided.</summary>
    private readonly record struct Wrote(bool Known, object? Value) {
        public static Wrote Unknown => new(false, null);
        public static Wrote Of(object? value) => new(true, value);
    }

    /// <summary>The payload writes a straight-line walk can see, and whether the payload escapes.</summary>
    private sealed class BodyState {
        public bool PayloadMayChange { get; set; }
        public Dictionary<string, Wrote> Properties { get; } = new(StringComparer.Ordinal);

        public BodyState Clone() {
            var copy = new BodyState { PayloadMayChange = PayloadMayChange };
            foreach (var pair in Properties)
                copy.Properties[pair.Key] = pair.Value;
            return copy;
        }

        /// <summary>
        /// A member is decided only when both branches leave it at the same known value. A
        /// member written on one path only is unknown: the other path leaves the caller's value.
        /// </summary>
        public void Merge(BodyState then, BodyState other) {
            PayloadMayChange = then.PayloadMayChange || other.PayloadMayChange;
            Properties.Clear();
            foreach (var name in then.Properties.Keys.Concat(other.Properties.Keys).Distinct(StringComparer.Ordinal)) {
                var inThen = then.Properties.TryGetValue(name, out var a);
                var inOther = other.Properties.TryGetValue(name, out var b);
                Properties[name] = inThen && inOther && a.Known && b.Known && Equals(a.Value, b.Value)
                    ? Wrote.Of(a.Value)
                    : Wrote.Unknown;
            }
        }
    }

    private static void WalkBody(IEnumerable<Node> statements, BodyState state, ValueType payload) {
        foreach (var statement in statements)
            WalkStatement(statement, state, payload);
    }

    private static void WalkStatement(Node node, BodyState state, ValueType payload) {
        switch (node) {
            case Assignment { Destination: Member { Value: Parameter parameter } member, Value: Constant constant }
                when IsPayload(parameter):
                state.Properties[member.MemberName] = Wrote.Of(constant.Value);
                break;
            case Assignment { Destination: Member { Value: Parameter parameter } member } assignment
                when IsPayload(parameter):
                state.Properties[member.MemberName] = Wrote.Unknown;
                if (ContainsBarePayload(assignment.Value, payload) || MayMutatePayload(assignment.Value, payload))
                    state.PayloadMayChange = true;
                break;
            case Assignment assignment:
                if (MayMutatePayload(assignment, payload))
                    state.PayloadMayChange = true;
                break;
            case IfStatement conditional: {
                    if (MayMutatePayload(conditional.Condition, payload))
                        state.PayloadMayChange = true;
                    var whenTrue = state.Clone();
                    var whenFalse = state.Clone();
                    WalkBody(Nodes(conditional.ThenBranch), whenTrue, payload);
                    if (conditional.ElseBranch is not null)
                        WalkBody(Nodes(conditional.ElseBranch), whenFalse, payload);
                    state.Merge(whenTrue, whenFalse);
                    break;
                }
            default:
                if (MayMutatePayload(node, payload))
                    state.PayloadMayChange = true;
                break;
        }
    }

    private static IEnumerable<Node> Nodes(Node branch) =>
        branch is Block block ? block.Nodes : [branch];

    private static bool IsPayload(Parameter parameter) =>
        string.Equals(parameter.Name, PayloadParameter, StringComparison.Ordinal);

    /// <summary>
    /// True when the node writes a payload member, rebinds the payload, calls a method on it, or
    /// hands the payload object (or a member that may be an object) to a call. Nulling it is not
    /// needed: any of those means the final payload cannot be read from the body.
    /// </summary>
    private static bool MayMutatePayload(Node? node, ValueType payload) {
        switch (node) {
            case null:
                return false;
            case Assignment assignment:
                return RootedAtPayload(assignment.Destination)
                       || ContainsBarePayload(assignment.Value, payload)
                       || MayMutatePayload(assignment.Value, payload);
            case IfStatement conditional:
                return MayMutatePayload(conditional.Condition, payload)
                       || MayMutatePayload(conditional.ThenBranch, payload)
                       || conditional.ElseBranch is not null && MayMutatePayload(conditional.ElseBranch, payload);
            case Invoke invoke:
                return invoke.Delegate is Member { Value: var receiver } && RootedAtPayload(receiver)
                       || invoke.Arguments.Any(argument => ContainsBarePayload(argument, payload))
                       || MayMutatePayload(invoke.Delegate, payload)
                       || invoke.Arguments.Any(argument => MayMutatePayload(argument, payload));
            case New created:
                return created.Arguments.Any(argument => ContainsBarePayload(argument, payload))
                       || created.Arguments.Any(argument => MayMutatePayload(argument, payload));
            default:
                return node.Children.Any(child => MayMutatePayload(child, payload));
        }
    }

    /// <summary>
    /// True when the tree holds a reference to the payload that a scalar member read does not
    /// consume: the bare payload object, or a member or index of it that could itself be an
    /// object. Such a reference leaves the body's control wherever it sits (a cast, a
    /// conditional, a lambda, a call argument), so the walk cannot read the final payload.
    /// </summary>
    private static bool ContainsBarePayload(Node? node, ValueType payload) {
        switch (node) {
            case null:
                return false;
            case Parameter parameter:
                return IsPayload(parameter);
            case IndexAccess index when RootedAtPayload(index):
                return true;
            case Member member when RootedAtPayload(member):
                // One level only: a nested member is an object the walk cannot see through.
                if (member.Value is not Parameter)
                    return true;
                var property = payload.Properties.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, member.MemberName, StringComparison.Ordinal));
                return property is null || !IsScalar(property.Type.TypeName);
            default:
                return node.Children.Any(child => ContainsBarePayload(child, payload));
        }
    }

    private static bool RootedAtPayload(Node? node) => node switch {
        Parameter parameter => IsPayload(parameter),
        Member member => RootedAtPayload(member.Value),
        IndexAccess index => RootedAtPayload(index.Value),
        _ => false,
    };

    private static bool IsScalar(string typeName) => typeName is
        "Text" or "String" or "Number" or "Int" or "Int32" or "Int64"
        or "Boolean" or "Bool" or "Guid" or "Decimal" or "Double" or "Float";

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