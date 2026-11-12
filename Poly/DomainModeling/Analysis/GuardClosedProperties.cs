using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The guard-closed fact (#367 C-1): a property is guard-closed when every writer of it, at rest
/// (after Create returns and after each action completes), leaves it within its declared
/// constraints. Default false; a property is closed only when every writer kind below is proven.
///
/// Writer kinds and how each is proven:
/// <list type="bullet">
///   <item><b>Create argument</b> (caller value, incl. <c>create</c> / <c>create in</c> initializers, which call the
///     Create factory): proven by the factory's constraint guards (<c>BuildCreateConstraintChecks</c>), but only
///     when that guard covers every declared constraint — the type gates are mirrored here and a constraint the guard
///     does not emit, or emits only for its hull (a range union), blocks the property.</item>
///   <item><b>Default</b> (<c>default(...)</c>): proven only for a literal that satisfies every constraint; a runtime
///     default (<c>now</c>, <c>guid</c>, an expression) is applied after the factory guard and is never proven.</item>
///   <item><b>Assign</b> in actions, stage actions, stage entry/exit and subscriptions (a callee reached by
///     <c>invoke</c> is just another action of its own entity, so it is one of these): proven only for a literal
///     that satisfies every constraint. A parameter, property copy or arithmetic value is NOT proven — action
///     parameter constraints are not enforced when an action runs, and the assign guard is the thing C-1 elides.</item>
///   <item><b>Entry-assigned</b> (initial stage's top-level entry assign): not a Create argument; the entry assign
///     overwrites the CLR default and is proven as an assign.</item>
/// </list>
/// Entity-wide blockers (every property false): an action bound to an imported contract (library-contributed body is
/// opaque), an assign target that is not a plain property, an effect kind this analysis does not model.
/// Out of the fact's scope, by design: <c>ModuleInstanceExecutor.SetProperty</c> (an unguarded host write) and
/// rows rehydrated from storage; both sit outside the model's writers.
/// </summary>
internal static class GuardClosedProperties {
    internal sealed record Result(
        IReadOnlySet<string> Closed,
        IReadOnlyDictionary<string, string> Blockers,
        int Iterations = 0);

    /// <summary>One assign writer; <see cref="ActionParameters"/> is null outside an action body.</summary>
    private sealed record AssignWriter(AssignEffect Assign, IReadOnlySet<string>? ActionParameters);

    internal static Result Compute(Entity entity, Domain domain) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(domain);

        var assigns = new Dictionary<string, List<AssignWriter>>(StringComparer.Ordinal);
        string? entityWide = null;
        var entryAssigned = EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(entity);

        void Walk(IEnumerable<Effect> effects, IReadOnlySet<string>? actionParameters = null) {
            foreach (var effect in effects) {
                switch (effect) {
                    case AssignEffect { Target: PropertyAccess target } assign:
                        if (!assigns.TryGetValue(target.Name, out var list))
                            assigns[target.Name] = list = [];
                        list.Add(new AssignWriter(assign, actionParameters));
                        break;
                    case AssignEffect:
                        entityWide ??= "an assign targets something other than a plain property";
                        break;
                    case CompositeEffect composite:
                        Walk(composite.Effects, actionParameters);
                        break;
                    case ConditionalEffect conditional:
                        Walk(conditional.ThenEffects, actionParameters);
                        if (conditional.ElseEffects is not null)
                            Walk(conditional.ElseEffects, actionParameters);
                        break;
                    case StageTransitionEffect or InvokeActionEffect or ForEachInvokeEffect
                        or CreateEntityInstance or CreateEntityInRelationshipEffect:
                        break; // no write to this entity's own properties; created entities go through their Create factory
                    default:
                        entityWide ??= $"effect '{effect.GetType().Name}' is not modelled by the guard-closed analysis";
                        break;
                }
            }
        }

        var actionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in entity.Actions) {
            actionNames.Add(action.Name);
            Walk(action.Effects, ParameterNames(action));
        }
        foreach (var subscription in entity.Subscriptions)
            Walk(subscription.Effects);
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions) {
                actionNames.Add(action.Name);
                Walk(action.Effects, ParameterNames(action));
            }
            Walk(stage.OnEntryEffects);
            Walk(stage.OnExitEffects);
            foreach (var subscription in stage.Subscriptions)
                Walk(subscription.Effects);
        }

        if (domain.ContractBindings.Any(binding => actionNames.Contains(binding.ActionName)))
            entityWide ??= "an action is bound to an imported contract (library-contributed body is opaque)";

        // Fixpoint (least): a property joins `closed` once every writer is covered, where a property copy is
        // covered only when its source is already in `closed` and the source's constraints imply this
        // property's. `closed` only grows and is bounded by the property count, so the loop runs at most
        // N + 1 rounds (the last changes nothing). Cycles (A = B, B = A) never get a first member, so they
        // stay open. Parameter sources are never covered.
        var closed = new HashSet<string>(StringComparer.Ordinal);
        var blockers = new Dictionary<string, string>(StringComparer.Ordinal);
        var iterations = 0;
        bool changed;
        do {
            changed = false;
            iterations++;
            blockers.Clear();
            foreach (var property in entity.Properties) {
                if (closed.Contains(property.Name))
                    continue;
                var blocker = entityWide
                    ?? Blocker(property, entryAssigned.Contains(property.Name),
                        assigns.GetValueOrDefault(property.Name), entity, closed);
                if (blocker is null) {
                    closed.Add(property.Name);
                    changed = true;
                }
                else
                    blockers[property.Name] = blocker;
            }
        } while (changed);
        return new Result(closed, blockers, iterations);
    }

    private static IReadOnlySet<string> ParameterNames(Poly.DomainModeling.Ontology.Action action) =>
        new HashSet<string>(action.Parameters.Select(p => p.Name), StringComparer.Ordinal);

    private static string? Blocker(
        Property property, bool isEntryAssigned, List<AssignWriter>? assigns, Entity entity, IReadOnlySet<string> closed) {
        var typeName = property.Type.TypeName;
        var isText = typeName is "Text" or "String";
        var isNumber = typeName is "Number" or "Int" or "Decimal";
        var isBoolean = typeName is "Boolean";
        if (!isText && !isNumber && !isBoolean)
            return $"type '{typeName}' has no constraint guard coverage";

        foreach (var constraint in property.Constraints) {
            var gate = ConstraintGate(constraint, isText, isNumber);
            if (gate is not null)
                return gate;
        }

        var hasDefault = property.Constraints.OfType<DefaultValueConstraint>().ToList();
        if (!isEntryAssigned) {
            // Create factory guard: a defaulted parameter skips the requiredness check, so a required text
            // value could be written empty.
            if (hasDefault.Count > 0 && isText && EntityStructureAnalyzer.IsRequired(property))
                return "a required text property with a default has no create-time requiredness guard";

            foreach (var def in hasDefault) {
                if (def.Expression is not Literal literal)
                    return "its default is not a literal (a runtime default is applied after the create guard)";
                if (!LiteralSatisfies(property, literal.Value, isText, isNumber, isBoolean))
                    return $"its default {Describe(literal.Value)} does not satisfy its constraints";
            }
        }

        foreach (var writer in assigns ?? []) {
            var assign = writer.Assign;
            if (IsProvenCopy(property, writer, entity, closed))
                continue;
            if (assign.Value is not Literal literal)
                return $"an assign writes {assign.Value.GetType().Name}, which is not a proven in-range literal";
            if (!LiteralSatisfies(property, literal.Value, isText, isNumber, isBoolean))
                return $"an assign writes {Describe(literal.Value)}, which does not satisfy its constraints";
        }
        return null;
    }

    /// <summary>
    /// A copy <c>assign target to Source</c> is a proven writer when it sits in an action body, Source is a
    /// different plain property of the entity (not shadowed by an action parameter), Source is already in the
    /// closed set, and Source's constraints imply <paramref name="target"/>'s. Every value Source can hold is
    /// then already within Source's constraints, hence within the target's.
    /// </summary>
    private static bool IsProvenCopy(Property target, AssignWriter writer, Entity entity, IReadOnlySet<string> closed) {
        if (writer.Assign.Value is not PropertyAccess access
            || writer.ActionParameters is null
            || writer.ActionParameters.Contains(access.Name)
            || string.Equals(access.Name, target.Name, StringComparison.Ordinal)
            || !closed.Contains(access.Name))
            return false;
        var source = entity.Properties.FirstOrDefault(p => string.Equals(p.Name, access.Name, StringComparison.Ordinal));
        return source is not null && SourceConstraintsImplyTarget(source, target);
    }

    /// <summary>
    /// Mirrors the guard-elision implication (EffectLoweringPass.SourceConstraintsImplyTarget): identical
    /// Text/String, numeric or Boolean type; source never optional; no unique target; every range/length/
    /// pattern/equality constraint on the target matched by an at-least-as-strong one on the source; a range
    /// union on the target, a relational or any unmodelled constraint refuses.
    /// </summary>
    private static bool SourceConstraintsImplyTarget(Property source, Property target) {
        var typeName = target.Type.TypeName;
        if (!string.Equals(source.Type.TypeName, typeName, StringComparison.Ordinal))
            return false;
        var isText = typeName is "Text" or "String";
        var isNumber = typeName is "Number" or "Int" or "Decimal";
        var isBoolean = typeName is "Boolean";
        if (!isText && !isNumber && !isBoolean)
            return false;
        if (target.Constraints.OfType<UniqueConstraint>().Any()
            || source.Constraints.OfType<OptionalConstraint>().Any())
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
                            && TryCompare(s.Minimum, range.Minimum) is >= 0))
                        return false;
                    if (range.Maximum is not null
                        && !sourceRanges.Any(s => s.Intervals is null && s.Maximum is not null
                            && TryCompare(s.Maximum, range.Maximum) is <= 0))
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

    private static int? TryCompare(object left, object right) {
        try {
            if (left is int or long && right is int or long)
                return Convert.ToInt64(left).CompareTo(Convert.ToInt64(right));
            return Convert.ToDouble(left).CompareTo(Convert.ToDouble(right));
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException) {
            return null;
        }
    }

    /// <summary>Why the create guard does not fully cover <paramref name="constraint"/>, or null when it does.</summary>
    private static string? ConstraintGate(Constraint constraint, bool isText, bool isNumber) {
        switch (constraint) {
            case RangeConstraint range:
                if (!isNumber) return "a range on a non-numeric property has no guard";
                if (range.Intervals is not null) return "a range union is guarded only by its hull";
                if ((range.Minimum is not null && !IsNumeric(range.Minimum))
                    || (range.Maximum is not null && !IsNumeric(range.Maximum)))
                    return "a range bound is not numeric";
                return null;
            case LengthConstraint:
                return isText ? null : "a length on a non-text property has no guard";
            case PatternConstraint:
                return isText ? null : "a pattern on a non-text property has no guard";
            case EqualityConstraint equality:
                return equality.ExpectedValue is null ? "an equality to null has no guard" : null;
            case OptionalConstraint or DefaultValueConstraint or UniqueConstraint:
                return null; // optionality is requiredness; default is a writer; unique is storage-owned
            case RelationalConstraint:
                return "a relational constraint depends on another property's value";
            default:
                return $"constraint '{constraint.GetType().Name}' is not modelled by the guard-closed analysis";
        }
    }

    private static bool LiteralSatisfies(Property property, object? value, bool isText, bool isNumber, bool isBoolean) {
        if (value is null)
            return !EntityStructureAnalyzer.IsRequired(property);
        if (isText && value is not string) return false;
        if (isNumber && !IsNumeric(value)) return false;
        if (isBoolean && value is not bool) return false;
        if (isText && EntityStructureAnalyzer.IsRequired(property) && ((string)value).Length == 0)
            return false;

        foreach (var constraint in property.Constraints) {
            if (constraint is RangeConstraint or LengthConstraint or PatternConstraint or EqualityConstraint
                && ConstraintValidation.Evaluate(constraint, value) != ConstraintVerdict.Satisfied)
                return false;
        }
        return true;
    }

    private static bool IsNumeric(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static string Describe(object? value) => value is null ? "null" : $"'{value}'";
}