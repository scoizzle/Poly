using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Validate pack: expression type compatibility. The DSL previously had no type check
/// in parse or analysis — wrong-typed comparisons/assigns/arithmetic/defaults passed
/// analysis, the export then compile-failed (CSxxxx), and the runtime silently coerced
/// garbage (string → the constant 2, `Name + 5` dropping the operand, `assign Name to
/// true` storing the bag). This pass rejects the class at authoring time.
///
/// Checks, at the DSL-type-category level (Text/Number/Boolean/Date/Enum/Guid):
///  - comparison operands are compatible (and string literals against enum-typed
///    properties are valid members);
///  - arithmetic operands are numeric, or a date + a number (AddDays lowering);
///  - <c>not</c> operates on a Boolean;
///  - assign RHS is compatible with the target property;
///  - <c>default(...)</c> is compatible with the property type.
/// </summary>
internal sealed class ExpressionTypeAnalyzer : INodeAnalyzer {
    public const string Id = "DomainExpressionType";
    public string PassName => Id;
    // Lint-only: reads domain + entity structure; publishes no bags.
    public string[] Dependencies => [DomainCatalogPass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node))
            return;

        if (node is Entity entity)
            AnalyzeEntity(context, entity);

        this.AnalyzeChildren(context, node);
    }

    // ── Entry points per entity ────────────────────────────────

    private void AnalyzeEntity(AnalysisContext context, Entity entity) {
        var enumTypes = ResolveEnums(context);
        var props = entity.Properties
            .ToDictionary(p => p.Name, p => p.Type.TypeName, StringComparer.Ordinal);

        // default(...) on properties
        foreach (var prop in entity.Properties) {
            foreach (var dv in prop.Constraints.OfType<DefaultValueConstraint>()) {
                CheckDefault(context, dv.Expression, prop.Type.TypeName, enumTypes);
            }
        }

        // policies (entity, stage, action)
        foreach (var policy in entity.Policies)
            WalkExpression(context, policy.Expression, props, null, enumTypes);
        foreach (var stage in entity.Stages) {
            foreach (var policy in stage.Policies)
                WalkExpression(context, policy.Expression, props, null, enumTypes);
        }
        foreach (var action in entity.Actions)
            foreach (var policy in action.Policies)
                WalkExpression(context, policy.Expression, props, ParamsOf(action), enumTypes);
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions)
                foreach (var policy in action.Policies)
                    WalkExpression(context, policy.Expression, props, ParamsOf(action), enumTypes);

        // effects (actions, entry/exit, subscriptions)
        foreach (var action in entity.Actions)
            CheckEffectTree(context, action.Effects, entity.Name, props, ParamsOf(action), enumTypes);
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions)
                CheckEffectTree(context, action.Effects, entity.Name, props, ParamsOf(action), enumTypes);
            CheckEffectTree(context, stage.OnEntryEffects, entity.Name, props, null, enumTypes);
            CheckEffectTree(context, stage.OnExitEffects, entity.Name, props, null, enumTypes);
            foreach (var sub in stage.Subscriptions)
                CheckEffectTree(context, sub.Effects, entity.Name, props, null, enumTypes);
        }
        foreach (var sub in entity.Subscriptions)
            CheckEffectTree(context, sub.Effects, entity.Name, props, null, enumTypes);
    }

    private static Dictionary<string, string>? ParamsOf(Action action) =>
        action.Parameters.Count > 0
            ? action.Parameters.ToDictionary(p => p.Name, p => p.Type.TypeName, StringComparer.Ordinal)
            : null;

    private static Dictionary<string, EnumType> ResolveEnums(AnalysisContext context) {
        var lookup = context.GetTypeLookup();
        if (lookup?.Domain is not { } domain) return new(StringComparer.Ordinal);
        return domain.Types.OfType<EnumType>()
            .ToDictionary(e => e.Name, StringComparer.Ordinal);
    }

    // ── Effects ─────────────────────────────────────────────────

    private void CheckEffectTree(
        AnalysisContext context,
        IEnumerable<Effect> effects,
        string callerEntityName,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        foreach (var effect in effects)
            CheckEffect(context, effect, callerEntityName, props, parameters, enumTypes);
    }

    private void CheckEffect(
        AnalysisContext context,
        Effect effect,
        string callerEntityName,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        switch (effect) {
            case AssignEffect assign:
                if (assign.Target is PropertyAccess target) {
                    var targetType = ResolvePropertyType(target.Name, props, parameters);
                    if (targetType is not null)
                        CheckCompatible(context, assign.Value, targetType, enumTypes,
                            $"assign to property '{target.Name}'", props, parameters);
                }
                WalkExpression(context, assign.Value, props, parameters, enumTypes);
                break;
            case ConditionalEffect cond:
                WalkExpression(context, cond.Condition, props, parameters, enumTypes);
                var condType = InferType(cond.Condition, props, parameters, enumTypes);
                if (condType.Category is not (TypeCategory.Boolean or TypeCategory.Unknown))
                    Report(context, cond.Condition,
                        $"if-condition must be Boolean (got '{Describe(condType)}')");
                CheckEffectTree(context, cond.ThenEffects, callerEntityName, props, parameters, enumTypes);
                if (cond.ElseEffects is { } elseEffects)
                    CheckEffectTree(context, elseEffects, callerEntityName, props, parameters, enumTypes);
                break;
            case CompositeEffect composite:
                CheckEffectTree(context, composite.Effects, callerEntityName, props, parameters, enumTypes);
                break;
            case CreateEntityInstance create:
                CheckCreateInitializers(context, create.Type.TypeName, create.Initializers,
                    props, parameters, enumTypes);
                break;
            case CreateEntityInRelationshipEffect createIn:
                CheckCreateInitializers(context,
                    ResolveRelationshipTargetTypeName(context, callerEntityName, createIn.RelationshipName),
                    createIn.Initializers, props, parameters, enumTypes);
                break;
            case InvokeActionEffect invoke:
                CheckInvokeArgumentTypes(context, callerEntityName, invoke.TargetRelationship,
                    binderName: null, invoke.ActionName, invoke.ParameterBindings, props, parameters, enumTypes);
                break;
            case ForEachInvokeEffect efe:
                CheckInvokeArgumentTypes(context, callerEntityName, efe.RelationshipName,
                    binderName: efe.BinderName, efe.ActionName, efe.ParameterBindings, props, parameters, enumTypes);
                break;
        }
    }

    /// <summary>
    /// Type-checks invoke / for-fan-out argument bindings against the callee action's
    /// declared parameter types (discovery round5 F7): a Text expression bound to a
    /// Number parameter (invoke line.Mark(amount: line Status)) previously passed
    /// analysis and broke the export at compile (CS1503). Mirrors assign-RHS checking.
    /// </summary>
    private void CheckInvokeArgumentTypes(
        AnalysisContext context,
        string callerEntityName,
        string? relationshipName,
        string? binderName,
        string actionName,
        IReadOnlyList<PropertyBinding> bindings,
        Dictionary<string, string> callerProps,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        if (bindings.Count == 0) return;
        var calleeParams = ResolveActionParameterTypes(context, callerEntityName, relationshipName, actionName);
        if (calleeParams is null) return; // unresolvable callee — other passes report shape errors
        var targetEntityName = ResolveCalleeEntityName(context, callerEntityName, relationshipName);
        foreach (var binding in bindings) {
            if (calleeParams.TryGetValue(binding.PropertyName, out var paramType)) {
                // A binder-rooted arg (for lines as line invoke line.Mark(amount: line Status),
                // or arithmetic over it: line Qty + 1) resolves its type against the target
                // entity's property.
                var inferred = binderName is not null && BindsToBinder(binding.Expression, binderName)
                    ? InferBinderExpressionType(context, targetEntityName, binderName, binding.Expression, enumTypes)
                    : InferLiteralAware(binding.Expression, paramType, enumTypes, callerProps, parameters);
                var targetCategory = CategoryOf(paramType, enumTypes);
                if (inferred.Category is not TypeCategory.Unknown
                    && targetCategory is not TypeCategory.Unknown
                    && !Compatible(context, inferred, new TypeInfo(targetCategory, paramType), assigning: true)) {
                    Report(context, binding.Expression,
                        $"type mismatch in argument '{binding.PropertyName}' of invoke '{actionName}': " +
                        $"cannot assign '{Describe(inferred)}' to '{paramType}'");
                }
            }
            WalkExpression(context, binding.Expression, callerProps, parameters, enumTypes);
        }
    }

    /// <summary>True when an expression is a binder-root navigation or arithmetic over one
    /// (e.g. <c>line Qty</c> or <c>line Qty + 1</c>).</summary>
    private static bool BindsToBinder(DomainExpression expr, string binderName) {
        if (expr is RelationshipNavigation { RelationshipName: var rel })
            return string.Equals(rel, binderName, StringComparison.Ordinal);
        if (expr is Add or Subtract or Multiply or Divide)
            return expr.Children.OfType<DomainExpression>().Any(c => BindsToBinder(c, binderName));
        return false;
    }

    /// <summary>Infers the type of a binder-rooted argument expression: a binder-root property
    /// access, or arithmetic (numeric/numeric, date/number) over it. Non-scalar binder roots
    /// (nested path-prefix) resolve to Unknown.</summary>
    private static TypeInfo InferBinderExpressionType(
        AnalysisContext context, string? targetEntityName, string binderName,
        DomainExpression expr, Dictionary<string, EnumType> enumTypes) {
        switch (expr) {
            case RelationshipNavigation rn when string.Equals(rn.RelationshipName, binderName, StringComparison.Ordinal):
                if (rn.TargetProperty is not PropertyAccess pa) return new(TypeCategory.Unknown);
                var propType = ResolveTargetPropType(context, targetEntityName, pa.Name);
                return propType is null ? new(TypeCategory.Unknown) : new(CategoryOf(propType, enumTypes), propType);
            case Add or Subtract or Multiply or Divide:
                var operandTypes = expr.Children.OfType<DomainExpression>()
                    .Select(c => InferBinderExpressionType(context, targetEntityName, binderName, c, enumTypes))
                    .ToList();
                if (operandTypes.Any(t => t.Category is TypeCategory.Unknown)) return new(TypeCategory.Unknown);
                if (operandTypes.All(t => IsNumeric(t.Category))) return new(TypeCategory.Number);
                if (operandTypes.Count == 2 && IsDate(operandTypes[0].Category) && IsNumeric(operandTypes[1].Category))
                    return operandTypes[0];
                // Mixed non-numeric binder prop + number (line Status + 1): surface the
                // non-numeric type so the param-type mismatch fires instead of passing.
                return operandTypes.FirstOrDefault(t => !IsNumeric(t.Category));
            case Literal { Value: long or int or double or float or decimal or short or byte }:
                return new(TypeCategory.Number);
            case Literal { Value: string }:
                return new(TypeCategory.Text);
            default:
                return new(TypeCategory.Unknown);
        }
    }

    private static string? ResolveTargetPropType(AnalysisContext context, string? targetEntityName, string propName) {
        if (targetEntityName is null) return null;
        var lookup = context.GetTypeLookup();
        var entity = lookup?.Domain?.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, targetEntityName, StringComparison.Ordinal));
        return entity?.Properties.FirstOrDefault(p => string.Equals(p.Name, propName, StringComparison.Ordinal))?.Type.TypeName;
    }

    private static string? ResolveCalleeEntityName(
        AnalysisContext context, string callerEntityName, string? relationshipName) {
        if (relationshipName is null) return callerEntityName;
        return ResolveRelationshipTargetTypeName(context, callerEntityName, relationshipName);
    }

    /// <summary>
    /// Resolves the callee action's parameter name → type map. Self-invoke
    /// (relationshipName null) targets the caller entity; cross-entity invoke resolves
    /// the relationship's target entity. Stage-scoped actions are included.
    /// </summary>
    private static Dictionary<string, string>? ResolveActionParameterTypes(
        AnalysisContext context,
        string callerEntityName,
        string? relationshipName,
        string actionName) {
        var lookup = context.GetTypeLookup();
        if (lookup?.Domain is not { } domain) return null;

        var calleeEntityName = ResolveCalleeEntityName(context, callerEntityName, relationshipName);
        if (calleeEntityName is null) return null;

        var calleeEntity = domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, calleeEntityName, StringComparison.Ordinal));
        if (calleeEntity is null) return null;
        var action = calleeEntity.Actions.FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal))
            ?? calleeEntity.Stages.SelectMany(s => s.Actions)
                .FirstOrDefault(a => string.Equals(a.Name, actionName, StringComparison.Ordinal));
        if (action is null || action.Parameters.Count == 0) return null;
        return action.Parameters.ToDictionary(p => p.Name, p => p.Type.TypeName, StringComparer.Ordinal);
    }

    /// <summary>
    /// Type-checks <c>create</c>/<c>create in</c> initializers against the TARGET
    /// entity's property types (discovery round5 F4): a non-member enum literal
    /// (create in bins { Status: "Bogus" }) previously passed analysis and broke the
    /// export at compile (CS1503). Mirrors the assign-RHS compatibility check.
    /// </summary>
    private void CheckCreateInitializers(
        AnalysisContext context,
        string? targetEntityTypeName,
        IReadOnlyList<PropertyBinding> initializers,
        Dictionary<string, string> callerProps,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        if (targetEntityTypeName is null) {
            foreach (var init in initializers)
                WalkExpression(context, init.Expression, callerProps, parameters, enumTypes);
            return;
        }
        var targetProps = ResolveEntityProps(context, targetEntityTypeName);
        foreach (var init in initializers) {
            if (targetProps is not null
                && targetProps.TryGetValue(init.PropertyName, out var targetType)) {
                CheckCompatible(context, init.Expression, targetType, enumTypes,
                    $"create initializer for property '{init.PropertyName}'", callerProps, parameters);
            }
            WalkExpression(context, init.Expression, callerProps, parameters, enumTypes);
        }
    }

    private static string? ResolveRelationshipTargetTypeName(
        AnalysisContext context, string callerEntityName, string relationshipName) {
        var lookup = context.GetRelationshipLookup();
        return lookup is not null
            && lookup.TryGetRelationship(callerEntityName, relationshipName, out var relationship)
            ? relationship.Target.TypeName
            : null;
    }

    private static Dictionary<string, string>? ResolveEntityProps(AnalysisContext context, string entityTypeName) {
        // Catalog lookup (name → type) — no linear domain.Types scan (round5 F7).
        var lookup = context.GetTypeLookup();
        if (lookup is null || !lookup.Types.TryGetValue(entityTypeName, out var type) || type is not Entity entity)
            return null;
        return entity.Properties
            .ToDictionary(p => p.Name, p => p.Type.TypeName, StringComparer.Ordinal);
    }

    // ── Expressions ─────────────────────────────────────────────

    private void WalkExpression(
        AnalysisContext context,
        DomainExpression expr,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        switch (expr) {
            case Comparison cmp:
                CheckComparison(context, cmp, props, parameters, enumTypes);
                return; // operands already checked
            case Add add:
                CheckArithmetic(context, add.Left, add.Right, props, parameters, enumTypes);
                return;
            case Subtract sub:
                CheckArithmetic(context, sub.Left, sub.Right, props, parameters, enumTypes);
                return;
            case Multiply mul:
                CheckNumericArithmetic(context, mul.Left, mul.Right, props, parameters, enumTypes);
                return;
            case Divide div:
                CheckNumericArithmetic(context, div.Left, div.Right, props, parameters, enumTypes);
                return;
            case Not not:
                var operand = InferType(not.Operand, props, parameters, enumTypes);
                if (operand.Category is not (TypeCategory.Boolean or TypeCategory.Unknown))
                    Report(context, expr, $"'not' requires a Boolean operand (got '{Describe(operand)}')");
                WalkExpression(context, not.Operand, props, parameters, enumTypes);
                return;
            case And and:
                CheckBooleanOperands(context, and.Left, and.Right, props, parameters, enumTypes);
                return;
            case Or or:
                CheckBooleanOperands(context, or.Left, or.Right, props, parameters, enumTypes);
                return;
            case RelationshipNavigation:
            case Exists or NotExists or AnyExpr or AllExpr or NoneExpr or CountExpr:
                // target-scoped (related-entity properties / store-aware) — no local type check
                return;
            case DateOperation dateOp:
                CheckDateOperation(context, dateOp, props, parameters, enumTypes);
                foreach (var child in dateOp.Children.OfType<DomainExpression>())
                    WalkExpression(context, child, props, parameters, enumTypes);
                return;
            default:
                foreach (var child in expr.Children.OfType<DomainExpression>())
                    WalkExpression(context, child, props, parameters, enumTypes);
                return;
        }
    }

    private void CheckArithmetic(
        AnalysisContext context, DomainExpression left, DomainExpression right,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var leftType = InferType(left, props, parameters, enumTypes);
        var rightType = InferType(right, props, parameters, enumTypes);
        // numeric + numeric, date + number (AddDays lowering), or date + duration
        // (a parsed `N days` offset with a temporal left operand); Unknown operands
        // (path-prefix reads, peer binders) are out of this scope — skip. A duration
        // without a temporal left operand (Number + days, days + date) is an unresolved
        // temporal specialization — reject, never a silent numeric constant.
        if (leftType.Category is not TypeCategory.Unknown && rightType.Category is not TypeCategory.Unknown
            && !(IsNumeric(leftType.Category) && IsNumeric(rightType.Category))
            && !(IsDate(leftType.Category) && IsNumeric(rightType.Category))
            && !(IsDate(leftType.Category) && rightType.Category is TypeCategory.Duration))
            Report(context, left,
                $"arithmetic operand is not numeric (got '{Describe(leftType)}' and '{Describe(rightType)}')");
        WalkExpression(context, left, props, parameters, enumTypes);
        WalkExpression(context, right, props, parameters, enumTypes);
    }

    private void CheckNumericArithmetic(
        AnalysisContext context, DomainExpression left, DomainExpression right,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var leftType = InferType(left, props, parameters, enumTypes);
        var rightType = InferType(right, props, parameters, enumTypes);
        if (leftType.Category is not TypeCategory.Unknown && rightType.Category is not TypeCategory.Unknown
            && (!IsNumeric(leftType.Category) || !IsNumeric(rightType.Category)))
            Report(context, left,
                $"arithmetic operand is not numeric (got '{Describe(leftType)}' and '{Describe(rightType)}')");
        WalkExpression(context, left, props, parameters, enumTypes);
        WalkExpression(context, right, props, parameters, enumTypes);
    }

    private void CheckBooleanOperands(
        AnalysisContext context, DomainExpression left, DomainExpression right,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var leftType = InferType(left, props, parameters, enumTypes);
        var rightType = InferType(right, props, parameters, enumTypes);
        if (leftType.Category is not (TypeCategory.Boolean or TypeCategory.Unknown)
            || rightType.Category is not (TypeCategory.Boolean or TypeCategory.Unknown))
            Report(context, left,
                $"'and'/'or' requires Boolean operands (got '{Describe(leftType)}' and '{Describe(rightType)}')");
        WalkExpression(context, left, props, parameters, enumTypes);
        WalkExpression(context, right, props, parameters, enumTypes);
    }

    private void CheckComparison(
        AnalysisContext context,
        Comparison cmp,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var left = InferType(cmp.Left, props, parameters, enumTypes);
        var right = InferType(cmp.Right, props, parameters, enumTypes);

        // enum member literal validity: Enum prop compared to a string must be a member
        if (left.Category is TypeCategory.Enum && cmp.Right is Literal { Value: string s })
            CheckEnumMember(context, cmp.Right, left.TypeName!, s, enumTypes);
        if (right.Category is TypeCategory.Enum && cmp.Left is Literal { Value: string s2 })
            CheckEnumMember(context, cmp.Left, right.TypeName!, s2, enumTypes);

        if (!Compatible(context, left, right))
            Report(context, cmp,
                $"comparison between incompatible types '{Describe(left)}' and '{Describe(right)}'");

        WalkExpression(context, cmp.Left, props, parameters, enumTypes);
        WalkExpression(context, cmp.Right, props, parameters, enumTypes);
    }

    private void CheckCompatible(
        AnalysisContext context,
        DomainExpression value,
        string targetTypeName,
        Dictionary<string, EnumType> enumTypes,
        string what,
        Dictionary<string, string>? props = null,
        Dictionary<string, string>? parameters = null) {
        if (value is PropertyAccess { Name: "Now" or "UtcNow" or "Today" or "Guid" } kw)
            CheckDefault(context, kw, targetTypeName, enumTypes);

        var inferred = InferLiteralAware(value, targetTypeName, enumTypes, props, parameters);
        var targetCategory = CategoryOf(targetTypeName, enumTypes);
        // Bare non-member enum identifier on an enum-typed target: a PropertyAccess that is
        // neither an enum member nor an entity property resolves to Unknown — reject at
        // analysis (was a late CS1061 at compile time).
        if (inferred.Category is TypeCategory.Unknown && targetCategory is TypeCategory.Enum
            && value is PropertyAccess { Name: var name }
            && ResolvePropertyType(name, props, parameters) is null) {
            Report(context, value, $"'{name}' is not a member of enum '{targetTypeName}'");
        }
        if (inferred.Category is TypeCategory.Unknown || targetCategory is TypeCategory.Unknown)
            return;
        // enum member validity for the RHS
        if (targetCategory is TypeCategory.Enum && value is Literal { Value: string s })
            CheckEnumMember(context, value, targetTypeName, s, enumTypes);
        if (!Compatible(context, inferred, new TypeInfo(targetCategory, targetTypeName), assigning: true))
            Report(context, value,
                $"type mismatch in {what}: cannot assign '{Describe(inferred)}' to '{targetTypeName}'");
    }

    private void CheckDefault(
        AnalysisContext context,
        DomainExpression expr,
        string propTypeName,
        Dictionary<string, EnumType> enumTypes) {
        var targetCategory = CategoryOf(propTypeName, enumTypes);
        if (targetCategory is TypeCategory.Unknown)
            return;

        switch (expr) {
            case Literal { Value: string s } when targetCategory is TypeCategory.Enum:
                CheckEnumMember(context, expr, propTypeName, s, enumTypes);
                return;
            case Literal lit:
                var inferred = InferType(expr, null, null, enumTypes);
                if (!Compatible(context, inferred, new TypeInfo(targetCategory, propTypeName), assigning: true))
                    Report(context, expr,
                        $"default value of type '{Describe(inferred)}' is not compatible with property type '{propTypeName}'");
                return;
            case PropertyAccess pa:
                // runtime keyword (now/today/guid) or enum member — keyword handled; enum
                // member is valid only for enum-typed props; anything else is a mismatch.
                if (pa.Name is "Now" or "UtcNow" or "Today" or "Guid") {
                    if (targetCategory is not TypeCategory.Date && pa.Name is "Now" or "UtcNow" or "Today")
                        Report(context, expr,
                            $"default({pa.Name}) is not compatible with property type '{propTypeName}' (use a date property, or 'Guid' for identifiers)");
                    else if (pa.Name is "Guid" && targetCategory is not TypeCategory.Guid
                             && targetCategory is not TypeCategory.Text)
                        Report(context, expr,
                            $"default(Guid) is not compatible with property type '{propTypeName}' (use a Uuid/Guid or Text property)");
                    return;
                }
                if (targetCategory is not TypeCategory.Enum)
                    Report(context, expr,
                        $"default({pa.Name}) on property '{propTypeName}' is not an enum member of that property's type");
                return;
            case Now:
            case Today:
                if (targetCategory is not TypeCategory.Date)
                    Report(context, expr,
                        $"default({(expr is Now ? "Now" : "Today")}) is not compatible with property type '{propTypeName}' " +
                        "(use a date property, or 'Guid' for identifiers)");
                return;
            case Duration d:
                Report(context, expr,
                    $"default value '{d.Amount} {d.Unit}' is a bare duration with no temporal left operand");
                return;
            default:
                return;
        }
    }

    private static void CheckEnumMember(
        AnalysisContext context,
        Node where,
        string enumTypeName,
        string member,
        Dictionary<string, EnumType> enumTypes) {
        if (!enumTypes.TryGetValue(enumTypeName, out var enumType)
            || enumType.MemberNames.Contains(member, StringComparer.Ordinal))
            return;
        Report(context, where, $"'{member}' is not a member of enum '{enumTypeName}'");
    }

    // ── Type inference ──────────────────────────────────────────

    internal enum TypeCategory { Text, Number, Boolean, Date, Duration, Enum, Guid, Null, Unknown }

    private readonly record struct TypeInfo(TypeCategory Category, string? TypeName = null);

    private static TypeInfo InferLiteralAware(DomainExpression expr, string targetTypeName, Dictionary<string, EnumType> enumTypes,
        Dictionary<string, string>? props = null, Dictionary<string, string>? parameters = null) {
        // For the assign RHS / default check, a bare enum-member identifier (PropertyAccess)
        // is valid when the target is enum-typed and the name is a member.
        if (expr is PropertyAccess pa && CategoryOf(targetTypeName, enumTypes) is TypeCategory.Enum) {
            if (enumTypes.TryGetValue(targetTypeName, out var enumType)
                && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal))
                return new(TypeCategory.Enum, targetTypeName);
        }
        return InferType(expr, props, parameters, enumTypes);
    }

    private static TypeInfo InferType(
        DomainExpression expr,
        Dictionary<string, string>? props,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) => expr switch {
            PropertyAccess pa => ResolvePropertyType(pa.Name, props, parameters) is { } pt
                ? new(CategoryOf(pt, enumTypes), pt)
                : new(TypeCategory.Unknown),
            ParameterAccess pa => parameters?.TryGetValue(pa.Name, out var pt) == true
                ? new(CategoryOf(pt, enumTypes), pt)
                : new(TypeCategory.Unknown),
            Literal { Value: null } => new(TypeCategory.Null),
            Literal { Value: string } => new(TypeCategory.Text),
            Literal { Value: bool } => new(TypeCategory.Boolean),
            Literal { Value: long or int or double or float or decimal or short or byte } => new(TypeCategory.Number),
            Exists or NotExists or AnyExpr or AllExpr or NoneExpr or Comparison or And or Or or Not => new(TypeCategory.Boolean),
            CountExpr => new(TypeCategory.Number),
            Now or Today or DateOperation => new(TypeCategory.Date, "Date"),
            Duration => new(TypeCategory.Duration, "duration"),
            _ => new(TypeCategory.Unknown),
        };

    private void CheckDateOperation(
        AnalysisContext context,
        DateOperation dateOp,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var dateType = InferType(dateOp.Date, props, parameters, enumTypes);
        var dateLike = dateOp.Date is Now or Today or DateOperation
            || dateType.TypeName is "Date" or "DateOnly" or "DateTime" or "Timestamp";
        var timeLike = dateType.TypeName is "Time" or "TimeOnly";
        if (dateType.Category is TypeCategory.Unknown && !dateLike && !timeLike)
            return;
        if (!dateLike && !timeLike) {
            Report(context, dateOp,
                $"temporal offset requires a date left operand (got '{Describe(dateType)}'); " +
                "a duration needs a date or clock node ('Now'/'Today') to offset");
            return;
        }

        var clockTyped = dateOp.Date is Now
            || dateType.TypeName is "DateTime" or "Timestamp" or "Time" or "TimeOnly";
        if (DurationForm.IsClockResolution(dateOp.Kind) && !clockTyped) {
            Report(context, dateOp,
                $"clock-resolution duration ({DurationForm.Spell(dateOp.Kind)}) requires Now, DateTime, or Time " +
                $"(got '{Describe(dateType)}'); Date/Today have no time of day");
        }
        else if (DurationForm.IsCalendarResolution(dateOp.Kind) && timeLike) {
            Report(context, dateOp,
                $"calendar duration ({DurationForm.Spell(dateOp.Kind)}) requires a date or DateTime " +
                $"(got '{Describe(dateType)}'); Time has no calendar date");
        }
    }

    private static string? ResolvePropertyType(string name, Dictionary<string, string>? props, Dictionary<string, string>? parameters) {
        if (props?.TryGetValue(name, out var pt) == true) return pt;
        if (parameters?.TryGetValue(name, out var ptype) == true) return ptype;
        return null;
    }

    private static TypeCategory CategoryOf(string typeName, Dictionary<string, EnumType> enumTypes) {
        if (enumTypes.ContainsKey(typeName)) return TypeCategory.Enum;
        return typeName switch {
            "Text" or "String" => TypeCategory.Text,
            "Number" or "Int" or "Int64" or "Int32" or "Decimal" or "Float" or "Double" => TypeCategory.Number,
            "Boolean" or "Bool" => TypeCategory.Boolean,
            "DateTime" or "Timestamp" => TypeCategory.Date,
            "Date" or "DateOnly" => TypeCategory.Date,
            "Time" or "TimeOnly" or "Duration" or "TimeSpan" => TypeCategory.Date,
            "Uuid" or "Guid" => TypeCategory.Guid,
            _ => TypeCategory.Unknown,
        };
    }

    private static bool IsNumeric(TypeCategory c) => c is TypeCategory.Number;

    private static bool IsDate(TypeCategory c) => c is TypeCategory.Date;

    private static string Describe(TypeInfo t) => t.Category switch {
        TypeCategory.Text => "Text",
        TypeCategory.Number => "Number",
        TypeCategory.Boolean => "Boolean",
        TypeCategory.Date => $"Date ({t.TypeName ?? "date"})",
        TypeCategory.Duration => "duration",
        TypeCategory.Enum => $"enum {t.TypeName ?? "?"}",
        TypeCategory.Guid => "Guid",
        TypeCategory.Null => "null",
        TypeCategory.Unknown => "unknown",
        _ => "unknown",
    };

    /// <param name="assigning">
    /// When true, <paramref name="left"/> is the source and <paramref name="right"/> is the
    /// target. Catalog names: Date onto DateTime is a widen; DateTime onto Date is not.
    /// </param>
    private static bool Compatible(AnalysisContext _, TypeInfo left, TypeInfo right, bool assigning = false) {
        if (left.Category is TypeCategory.Unknown || right.Category is TypeCategory.Unknown)
            return true;
        if (left.Category is TypeCategory.Null)
            return right.Category is TypeCategory.Text or TypeCategory.Guid;
        if (right.Category is TypeCategory.Null)
            return left.Category is TypeCategory.Text or TypeCategory.Guid;
        if (left.Category is TypeCategory.Enum && right.Category is TypeCategory.Enum)
            return string.Equals(left.TypeName, right.TypeName, StringComparison.Ordinal);
        if (left.Category is TypeCategory.Enum && right.Category is TypeCategory.Text)
            return true;
        if (right.Category is TypeCategory.Enum && left.Category is TypeCategory.Text)
            return true;

        if (assigning) {
            if (IsDateTimeName(right.TypeName) && IsDateName(left.TypeName))
                return true;
            if (IsDateName(right.TypeName) && IsDateTimeName(left.TypeName))
                return false;
        }

        return left.Category == right.Category
            || string.Equals(CanonicalName(left.TypeName), CanonicalName(right.TypeName), StringComparison.Ordinal);
    }

    private static string? CanonicalName(string? typeName) => typeName switch {
        "String" => "Text",
        "Bool" => "Boolean",
        "Int" or "Int64" => "Number",
        "DateOnly" => "Date",
        "Timestamp" => "DateTime",
        "TimeOnly" => "Time",
        "TimeSpan" or "duration" => "Duration",
        "Guid" => "Uuid",
        _ => typeName
    };

    private static bool IsDateName(string? typeName) =>
        CanonicalName(typeName) is "Date";

    private static bool IsDateTimeName(string? typeName) =>
        CanonicalName(typeName) is "DateTime";

    private static void Report(AnalysisContext context, Node where, string message) =>
        context.ReportError(where, message, DomainModelDiagnosticCodes.SemanticTypeCompatibility);
}