using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Runtime;

using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// AssignEffect and its assign-time constraint-check machinery.
public sealed partial class EffectLoweringPass {
    protected override Node? Assign(AssignEffect a) {
        var target = _expressionPass.LowerExpression(a.Target, Subject);
        // The target property decides the value's type: enum members, string literals,
        // the default(...) fold, Decimal literals and clock/session folds all adapt here.
        var assignProp = a.Target is PropertyAccess propAccess
            ? _entity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, propAccess.Name, StringComparison.Ordinal))
            : null;
        var value = assignProp is not null
            ? _expressionPass.AdaptTo(a.Value, assignProp.Type, Subject)
            : _expressionPass.LowerExpression(a.Value, Subject);

        // Date arithmetic (DueDate + 14 → AddDays) is lowered by the expression
        // pass (Add/Subtract) so it applies in every context — nothing more here.

        if (_analysis.GetMetadata<AssignedMemberConversionMetadata>(a) is { } conversion) {
            value = value with {
                Value = new Invoke(
                    new Member(value.Value, conversion.MethodName),
                    [.. conversion.Arguments.Select(arg =>
                        new Member(new NamedTypeReference(arg.TypeName), arg.MemberName))])
            };
        }

        // Item 6: assign-time required/range/length/pattern/enum (+ unique) in the
        // same Lower tree as simulate + C# print. Evaluate RHS once, fail-before-
        // mutate, then assign (EnsureUnique when unique).
        var uniqueName = assignProp is not null && IsUniqueProperty(assignProp.Name)
            ? assignProp.Name
            : null;

        var needsConstraintWrap = assignProp is not null
            && HasAssignableConstraints(assignProp)
            && !SatisfiesAssignConstraints(assignProp, value.Value)
            && !CopiesStrongerGuardClosedProperty(assignProp, a, value);
        Node assignment = uniqueName is null && !needsConstraintWrap
            ? new Assignment(target.Value, value.Value)
            : WrapConstrainedAssign(
                target.Value,
                needsConstraintWrap ? assignProp : null,
                value.Value,
                uniqueName,
                _expressionPass.MayBeAbsent(a.Value, name => _binderEntities?.GetValueOrDefault(name)));
        return LoweredExpression.Before([target, value], assignment);
    }
    /// <summary>
    /// True when a constant/enum-member RHS provably satisfies every emit-time
    /// constraint on the property, so the assignment needs no fail-before-mutate
    /// wrap. Non-constant RHS, unique properties, and any unsatisfied check keep
    /// the wrap.
    /// </summary>
    private bool SatisfiesAssignConstraints(Property prop, Node value) {
        if (IsUniqueProperty(prop.Name))
            return false;
        // D14: a constant RHS that provably satisfies a bound is not checked again.
        // Anything this loop cannot prove (an equality guard, a non-constant RHS, an
        // unknown constraint kind) keeps the guard — the default is fail closed.
        foreach (var constraint in prop.Constraints) {
            switch (constraint) {
                case RangeConstraint range when TryNumeric(value, out var number):
                    // Exact comparison: a bound and a value above 2^53 compare equal through double,
                    // which would elide a guard that must fire (#643).
                    if (range.Minimum is not null
                        && (ExactNumber.Compare(number, range.Minimum) is not { } minCompare || minCompare < 0))
                        return false;
                    if (range.Maximum is not null
                        && (ExactNumber.Compare(number, range.Maximum) is not { } maxCompare || maxCompare > 0))
                        return false;
                    break;
                case RangeConstraint:
                    return false;
                case LengthConstraint length when value is Constant { Value: string text }:
                    if (text.Length < length.MinLength || text.Length > length.MaxLength)
                        return false;
                    break;
                case LengthConstraint:
                    return false;
                case PatternConstraint pattern when value is Constant { Value: string text }:
                    if (!PatternSemantics.TryMatch(text, pattern.Pattern))
                        return false;
                    break;
                case PatternConstraint:
                    return false;
                // Markers, not checks: they decide requiredness/defaults, never a guard.
                case DefaultValueConstraint or OptionalConstraint or UniqueConstraint:
                    break;
                default:
                    return false;
            }
        }
        // Requiredness is a fact on this branch (a property is required unless it is
        // marked 'optional'), not a constraint object, so it is decided here. Required
        // means non-null, so any string constant, "" included, satisfies it; a length or
        // pattern that refuses "" was checked in the loop above.
        if (NeedsRequiredValueCheck(prop)) {
            if (prop.Type.TypeName is "Text" or "String") {
                if (value is not Constant { Value: string })
                    return false;
            }
            else if (value is Constant { Value: null }) {
                return false;
            }
        }
        if (DomainToCSharpExporter.TryResolveEnumType(
                _domain, _analysis, prop.Type.TypeName, out var enumType)
            && enumType is not null
            && !(value is Member { Value: NamedTypeReference typeRef, MemberName: var member }
                && string.Equals(typeRef.TypeName, enumType.Name, StringComparison.Ordinal)
                && enumType.MemberNames.Contains(member, StringComparer.Ordinal)))
            return false;
        return true;
    }

    /// <summary>
    /// #367 C-1. <c>assign Target to Source</c> needs no assign guard when (a) Source is a different plain
    /// property of this entity that the analysis proves guard-closed (every writer of Source leaves it within
    /// Source's own constraints), and (b) Source's constraints imply every check the guard would emit for Target
    /// (<see cref="SourceConstraintsImplyTarget"/>). The checks that remain are Source's own guards on each write
    /// to Source (Create factory, in-range literal default/assign), which guard-closed guarantees exist, so a
    /// violating value is still rejected where it enters Source. Anything not proven keeps the guard.
    /// Restricted to an action body (it has an <see cref="LoweringContext.ActionName"/>): not in the initial entry or a
    /// subscription handler, where Source may still hold its CLR default before it is written. Both return a
    /// <c>DomainResult</c> since L-04a, so a result type no longer tells an action from them.
    /// </summary>
    private bool CopiesStrongerGuardClosedProperty(Property target, AssignEffect a, LoweredExpression value) {
        if (a.Value is not PropertyAccess sourceAccess
            || _context.ActionName is null
            || _entriesInlining.Count > 0
            || _exitsInlining.Count > 0
            || value.Statements.Count > 0
            || string.Equals(sourceAccess.Name, target.Name, StringComparison.Ordinal)
            || _context.ActionParameterNames?.Contains(sourceAccess.Name) == true
            || (_context.Parameters?.ContainsKey(sourceAccess.Name) ?? false))
            return false;

        // The lowered RHS must be exactly Subject.Source: no clock/guid fold, no conversion, no enum rewrite.
        if (value.Value is not Member { MemberName: var memberName } member
            || !string.Equals(memberName, sourceAccess.Name, StringComparison.Ordinal)
            || !Equals(member.Value, Subject))
            return false;

        var source = _entity.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, sourceAccess.Name, StringComparison.Ordinal));
        if (source is null)
            return false;

        var closed = _analysis.GetStructure(_entity)?.GuardClosedPropertyNames;
        if (closed is null || !closed.Contains(source.Name))
            return false;

        return SourceConstraintsImplyTarget(source, target);
    }

    /// <summary>
    /// The implication, per kind. Every constraint on <paramref name="target"/> must be matched by a
    /// constraint on <paramref name="source"/> at least as strong; an unmatched or unknown kind refuses.
    /// <list type="bullet">
    /// <item>type: identical Text/String, numeric or Boolean type name; enums and everything else refuse.</item>
    /// <item>required: a required target needs a required source (and a source never <c>optional</c>), so a
    ///   null/empty source cannot reach the target; guard-closed makes the source's own required guard hold.</item>
    /// <item>range: every target bound needs a source bound at least as tight (min_s &gt;= min_t, max_s &lt;= max_t);
    ///   a union (Intervals) on the target refuses.</item>
    /// <item>length: source MinLength &gt;= target MinLength, source MaxLength &lt;= target MaxLength.</item>
    /// <item>pattern: only a source pattern with the identical regex string. Regex containment is not decided.</item>
    /// <item>equality: only the identical expected value (same runtime type).</item>
    /// <item>excluded, never elided: unique, relational, any unmodelled constraint record, enum types.</item>
    /// </list>
    /// </summary>
    private bool SourceConstraintsImplyTarget(Property source, Property target) {
        var typeName = target.Type.TypeName;
        if (!string.Equals(source.Type.TypeName, typeName, StringComparison.Ordinal))
            return false;
        var isText = typeName is "Text" or "String";
        var isNumber = typeName is "Number" or "Int" or "Decimal";
        var isBoolean = typeName is "Boolean";
        if (!isText && !isNumber && !isBoolean)
            return false;
        if (IsUniqueProperty(target.Name)
            || DomainToCSharpExporter.TryResolveEnumType(_domain, _analysis, typeName, out _))
            return false;
        if (source.Constraints.OfType<OptionalConstraint>().Any())
            return false;
        if (NeedsRequiredValueCheck(target) && !NeedsRequiredValueCheck(source))
            return false;

        var sourceRanges = source.Constraints.OfType<RangeConstraint>().ToList();
        var sourceLengths = source.Constraints.OfType<LengthConstraint>().ToList();
        var sourcePatterns = source.Constraints.OfType<PatternConstraint>().ToList();
        var sourceEqualities = source.Constraints.OfType<EqualityConstraint>().ToList();

        foreach (var constraint in target.Constraints) {
            switch (constraint) {
                case RangeConstraint range when isNumber:
                    if (range.Intervals is not null)
                        return false;
                    if (range.Minimum is not null
                        && !sourceRanges.Any(s => s.Intervals is null && s.Minimum is not null
                            && ConstraintMerge.TryCompare(s.Minimum, range.Minimum) is >= 0))
                        return false;
                    if (range.Maximum is not null
                        && !sourceRanges.Any(s => s.Intervals is null && s.Maximum is not null
                            && ConstraintMerge.TryCompare(s.Maximum, range.Maximum) is <= 0))
                        return false;
                    break;
                case LengthConstraint length when isText:
                    if (length.MinLength > 0 && !sourceLengths.Any(s => s.MinLength >= length.MinLength))
                        return false;
                    if (length.MaxLength < int.MaxValue && !sourceLengths.Any(s => s.MaxLength <= length.MaxLength))
                        return false;
                    break;
                case PatternConstraint pattern when isText:
                    if (!sourcePatterns.Any(s => string.Equals(s.Pattern, pattern.Pattern, StringComparison.Ordinal)))
                        return false;
                    break;
                case EqualityConstraint equality:
                    if (!sourceEqualities.Any(s => s.ExpectedValue.GetType() == equality.ExpectedValue.GetType()
                        && Equals(s.ExpectedValue, equality.ExpectedValue)))
                        return false;
                    break;
                case DefaultValueConstraint or OptionalConstraint or UniqueConstraint:
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    private static bool TryNumeric(Node value, out object number) {
        switch (value) {
            case Constant { Value: long or int or double or decimal } constant:
                number = constant.Value!;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    private bool HasAssignableConstraints(Property prop) =>
        NeedsRequiredValueCheck(prop)
        || prop.Constraints.Any(c => c is RangeConstraint
            or LengthConstraint
            or PatternConstraint
            or EqualityConstraint)
        || (DomainToCSharpExporter.TryResolveEnumType(
                _domain, _analysis, prop.Type.TypeName, out var enumType)
            && enumType is { MemberNames.Count: > 0 });

    /// <summary>
    /// Unique is the declared <see cref="UniqueConstraint"/> on the entity property — the same
    /// expression the storage analyzer records, read from the model so the two cannot disagree.
    /// </summary>
    private bool IsUniqueProperty(string propertyName) =>
        _entity.Properties.Any(p =>
            string.Equals(p.Name, propertyName, StringComparison.Ordinal)
            && p.Constraints.OfType<UniqueConstraint>().Any());

    /// <summary>
    /// Fail-before-mutate wrap for assign: local RHS, constraint Failure checks
    /// (required → range → length → pattern → equality → enum member), optional EnsureUnique, then assign.
    /// </summary>
    private Node WrapConstrainedAssign(
        Node destination,
        Property? property,
        Node value,
        string? uniquePropertyName,
        bool valueMayBeAbsent) {
        var assignedVar = _names.Next("assignValue");
        var locals = new List<Node> { assignedVar };
        var nodes = new List<Node> {
            new Assignment(assignedVar, value)
        };

        if (property is not null)
            AppendAssignConstraintChecks(nodes, property, assignedVar, valueMayBeAbsent);

        if (uniquePropertyName is not null) {
            var checkVar = _names.Next("uniqueCheck");
            locals.Add(checkVar);
            nodes.Add(new Assignment(checkVar, new Invoke(
                new Member(Subject, "EnsureUnique"),
                new Constant(uniquePropertyName),
                assignedVar)));
            nodes.Add(new IfStatement(
                new Syntactic.Not(new Member(checkVar, "IsSuccess")),
                new Block([ReturnOwnGuardFailureFrom(checkVar)])));
        }

        nodes.Add(new Assignment(destination, assignedVar));
        return new Block(nodes, locals);
    }

    /// <summary>
    /// Emits assign-time constraint guards matching Create factory /
    /// <c>ValidateConstraints</c> messages. Enum-typed properties get a
    /// not-any-member guard. An <c>optional</c> length skips empty values.
    /// Uses <see cref="ClrTypeReference"/> for string/Regex statics so the VM
    /// resolves MethodInfo (NamedTypeReference is export-shaped only).
    /// Void export contexts (ctor / OnEntry, no ActionResultType) throw like create-in.
    /// </summary>
    private void AppendAssignConstraintChecks(
        List<Node> nodes, Property prop, Node valueRef, bool valueMayBeAbsent) {

        Node Fail(ConstraintCheckWriter.Check check) =>
            AssignConstraintFailure(ConstraintCheckWriter.FailureSpec(check, _entity.Name, prop.Name));

        // Optional length checks share one outer !IsNullOrEmpty guard; the writer owns
        // the predicate (check.Presence), the call site owns the grouping Block.
        var lengthChecks = new List<ConstraintCheckWriter.Check>();
        Node? lengthGroup = null;

        void FlushLength() {
            if (lengthChecks.Count == 0)
                return;
            var inner = new List<Node>();
            foreach (var c in lengthChecks)
                inner.Add(new IfStatement(c.Condition, new Block([Fail(c)])));
            if (lengthGroup is not null)
                nodes.Add(new IfStatement(lengthGroup, new Block(inner)));
            else
                nodes.AddRange(inner);
            lengthChecks.Clear();
            lengthGroup = null;
        }

        foreach (var check in BuiltinConstraintCompiler.Checks(
                     _entity, prop, _domain, _analysis,
                     ConstraintCheckWriter.ConstraintCheckStyle.Assign,
                     valueRef, nullableValue: valueMayBeAbsent && !EntityStructureAnalyzer.IsRequired(prop), includeRequired: true)) {
            if (check.Kind is ConstraintCheckWriter.Kind.LengthMinimum
                or ConstraintCheckWriter.Kind.LengthMaximum) {
                lengthChecks.Add(check);
                lengthGroup = check.Presence;
                continue;
            }
            FlushLength();
            nodes.Add(new IfStatement(check.GuardedCondition, new Block([Fail(check)])));
        }
        FlushLength();
    }

    /// <summary>
    /// Requiredness is "no <c>optional</c>": a reference-typed property, Text included,
    /// rejects null. Value types have no null to reject.
    /// </summary>
    private bool NeedsRequiredValueCheck(Property prop) =>
        ConstraintCheckWriter.RequiredApplies(_entity, prop, _domain, _analysis);
}