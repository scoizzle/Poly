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
///   <item><b>Entry-assigned</b> (first stage's top-level entry assign): not a Create argument; the entry assign
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
        IReadOnlyDictionary<string, string> Blockers);

    internal static Result Compute(Entity entity, Domain domain) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(domain);

        var assigns = new Dictionary<string, List<AssignEffect>>(StringComparer.Ordinal);
        string? entityWide = null;
        var entryAssigned = EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(entity);

        void Walk(IEnumerable<Effect> effects) {
            foreach (var effect in effects) {
                switch (effect) {
                    case AssignEffect { Target: PropertyAccess target } assign:
                        if (!assigns.TryGetValue(target.Name, out var list))
                            assigns[target.Name] = list = [];
                        list.Add(assign);
                        break;
                    case AssignEffect:
                        entityWide ??= "an assign targets something other than a plain property";
                        break;
                    case CompositeEffect composite:
                        Walk(composite.Effects);
                        break;
                    case ConditionalEffect conditional:
                        Walk(conditional.ThenEffects);
                        if (conditional.ElseEffects is not null)
                            Walk(conditional.ElseEffects);
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
            Walk(action.Effects);
        }
        foreach (var subscription in entity.Subscriptions)
            Walk(subscription.Effects);
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions) {
                actionNames.Add(action.Name);
                Walk(action.Effects);
            }
            Walk(stage.OnEntryEffects);
            Walk(stage.OnExitEffects);
            foreach (var subscription in stage.Subscriptions)
                Walk(subscription.Effects);
        }

        if (domain.ContractBindings.Any(binding => actionNames.Contains(binding.ActionName)))
            entityWide ??= "an action is bound to an imported contract (library-contributed body is opaque)";

        var closed = new HashSet<string>(StringComparer.Ordinal);
        var blockers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in entity.Properties) {
            var blocker = entityWide
                ?? Blocker(property, entryAssigned.Contains(property.Name),
                    assigns.GetValueOrDefault(property.Name));
            if (blocker is null)
                closed.Add(property.Name);
            else
                blockers[property.Name] = blocker;
        }
        return new Result(closed, blockers);
    }

    private static string? Blocker(Property property, bool isEntryAssigned, List<AssignEffect>? assigns) {
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

        foreach (var assign in assigns ?? []) {
            if (assign.Value is not Literal literal)
                return $"an assign writes {assign.Value.GetType().Name}, which is not a proven in-range literal";
            if (!LiteralSatisfies(property, literal.Value, isText, isNumber, isBoolean))
                return $"an assign writes {Describe(literal.Value)}, which does not satisfy its constraints";
        }
        return null;
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