using Poly.Analysis;
using Poly.DomainModeling.Effects;

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
    public string[] Dependencies => [SemanticDomainAnalyzer.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node))
            return;

        if (node is Entity entity)
            AnalyzeEntity(context, entity);

        this.AnalyzeChildren(context, node);
    }

    // ── Entry points per entity ────────────────────────────────

    private static void AnalyzeEntity(AnalysisContext context, Entity entity) {
        var enumTypes = ResolveEnums(context);
        var props = entity.Properties
            .ToDictionary(p => p.Name, p => p.Type.TypeName, StringComparer.Ordinal);

        // default(...) on properties
        foreach (var prop in entity.Properties) {
            foreach (var dv in prop.Constraints.OfType<Constraints.DefaultValueConstraint>()) {
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
            CheckEffectTree(context, action.Effects, props, ParamsOf(action), enumTypes);
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions)
                CheckEffectTree(context, action.Effects, props, ParamsOf(action), enumTypes);
            CheckEffectTree(context, stage.OnEntryEffects, props, null, enumTypes);
            CheckEffectTree(context, stage.OnExitEffects, props, null, enumTypes);
            foreach (var sub in stage.Subscriptions)
                CheckEffectTree(context, sub.Effects, props, null, enumTypes);
        }
        foreach (var sub in entity.Subscriptions)
            CheckEffectTree(context, sub.Effects, props, null, enumTypes);
    }

    private static Dictionary<string, string>? ParamsOf(Action action) =>
        action.Parameters.Count > 0
            ? action.Parameters.ToDictionary(p => p.Name, p => p.Type.TypeName, StringComparer.Ordinal)
            : null;

    private static Dictionary<string, EnumType> ResolveEnums(AnalysisContext context) {
        var lookup = context.GetMetadata<DomainTypeLookupMetadata>(default);
        if (lookup?.Domain is not { } domain) return new(StringComparer.Ordinal);
        return domain.Types.OfType<EnumType>()
            .ToDictionary(e => e.Name, StringComparer.Ordinal);
    }

    // ── Effects ─────────────────────────────────────────────────

    private static void CheckEffectTree(
        AnalysisContext context,
        IEnumerable<Effect> effects,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        foreach (var effect in effects)
            CheckEffect(context, effect, props, parameters, enumTypes);
    }

    private static void CheckEffect(
        AnalysisContext context,
        Effect effect,
        Dictionary<string, string> props,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        switch (effect) {
            case AssignEffect assign:
                if (assign.Target is PropertyAccess target) {
                    var targetType = ResolvePropertyType(target.Name, props, parameters);
                    if (targetType is not null)
                        CheckCompatible(context, assign.Value, targetType, enumTypes,
                            $"assign to property '{target.Name}'");
                }
                WalkExpression(context, assign.Value, props, parameters, enumTypes);
                break;
            case ConditionalEffect cond:
                WalkExpression(context, cond.Condition, props, parameters, enumTypes);
                CheckEffectTree(context, cond.ThenEffects, props, parameters, enumTypes);
                if (cond.ElseEffects is { } elseEffects)
                    CheckEffectTree(context, elseEffects, props, parameters, enumTypes);
                break;
            case CompositeEffect composite:
                CheckEffectTree(context, composite.Effects, props, parameters, enumTypes);
                break;
            case CreateEntityInstance create:
                foreach (var init in create.Initializers)
                    WalkExpression(context, init.Expression, props, parameters, enumTypes);
                break;
            case CreateEntityInRelationshipEffect createIn:
                foreach (var init in createIn.Initializers)
                    WalkExpression(context, init.Expression, props, parameters, enumTypes);
                break;
            case InvokeActionEffect invoke:
                foreach (var binding in invoke.ParameterBindings)
                    WalkExpression(context, binding.Expression, props, parameters, enumTypes);
                if (invoke.Filter is not null)
                    WalkExpression(context, invoke.Filter, props, parameters, enumTypes);
                break;
            case ForEachInvokeEffect efe:
                foreach (var binding in efe.ParameterBindings)
                    WalkExpression(context, binding.Expression, props, parameters, enumTypes);
                break;
        }
    }

    // ── Expressions ─────────────────────────────────────────────

    private static void WalkExpression(
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
            case DateOperation:
            case RelationshipNavigation:
            case Exists or NotExists or AnyExpr or AllExpr or NoneExpr or CountExpr:
                // target-scoped (related-entity properties / store-aware) — no local type check
                return;
            default:
                foreach (var child in expr.Children.OfType<DomainExpression>())
                    WalkExpression(context, child, props, parameters, enumTypes);
                return;
        }
    }

    private static void CheckArithmetic(
        AnalysisContext context, DomainExpression left, DomainExpression right,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var leftType = InferType(left, props, parameters, enumTypes);
        var rightType = InferType(right, props, parameters, enumTypes);
        // numeric + numeric, or date + number (AddDays lowering); Unknown operands
        // (path-prefix reads, peer binders) are out of this scope — skip.
        if (leftType.Category is not TypeCategory.Unknown && rightType.Category is not TypeCategory.Unknown
            && !(IsNumeric(leftType.Category) && IsNumeric(rightType.Category))
            && !(IsDate(leftType.Category) && IsNumeric(rightType.Category)))
            Report(context, left,
                $"arithmetic operand is not numeric (got '{Describe(leftType)}' and '{Describe(rightType)}')");
        WalkExpression(context, left, props, parameters, enumTypes);
        WalkExpression(context, right, props, parameters, enumTypes);
    }

    private static void CheckNumericArithmetic(
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

    private static void CheckBooleanOperands(
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

    private static void CheckComparison(
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

        if (!Compatible(left, right))
            Report(context, cmp,
                $"comparison between incompatible types '{Describe(left)}' and '{Describe(right)}'");

        WalkExpression(context, cmp.Left, props, parameters, enumTypes);
        WalkExpression(context, cmp.Right, props, parameters, enumTypes);
    }

    private static void CheckCompatible(
        AnalysisContext context,
        DomainExpression value,
        string targetTypeName,
        Dictionary<string, EnumType> enumTypes,
        string what) {
        var inferred = InferLiteralAware(value, targetTypeName, enumTypes);
        var targetCategory = CategoryOf(targetTypeName, enumTypes);
        if (inferred.Category is TypeCategory.Unknown || targetCategory is TypeCategory.Unknown)
            return;
        // enum member validity for the RHS
        if (targetCategory is TypeCategory.Enum && value is Literal { Value: string s })
            CheckEnumMember(context, value, targetTypeName, s, enumTypes);
        if (!Compatible(inferred, new TypeInfo(targetCategory, targetTypeName)))
            Report(context, value,
                $"type mismatch in {what}: cannot assign '{Describe(inferred)}' to '{targetTypeName}'");
    }

    private static void CheckDefault(
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
                if (!Compatible(inferred, new TypeInfo(targetCategory, propTypeName)))
                    Report(context, expr,
                        $"default value of type '{Describe(inferred)}' is not compatible with property type '{propTypeName}'");
                return;
            case PropertyAccess pa:
                // runtime keyword (now/today/guid) or enum member — keyword handled; enum
                // member is valid only for enum-typed props; anything else is a mismatch.
                if (pa.Name is "now" or "utcnow" or "today" or "guid") {
                    if (targetCategory is not TypeCategory.Date && pa.Name is "now" or "utcnow" or "today")
                        Report(context, expr,
                            $"default({pa.Name}) is not compatible with property type '{propTypeName}' (use a date property, or 'guid' for identifiers)");
                    else if (pa.Name is "guid" && targetCategory is not TypeCategory.Guid
                             && targetCategory is not TypeCategory.Text)
                        Report(context, expr,
                            $"default(guid) is not compatible with property type '{propTypeName}' (use a Uuid/Guid or Text property)");
                    return;
                }
                if (targetCategory is not TypeCategory.Enum)
                    Report(context, expr,
                        $"default({pa.Name}) on property '{propTypeName}' is not an enum member of that property's type");
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

    private enum TypeCategory { Text, Number, Boolean, Date, Enum, Guid, Null, Unknown }

    private readonly record struct TypeInfo(TypeCategory Category, string? TypeName = null);

    private static TypeInfo InferLiteralAware(DomainExpression expr, string targetTypeName, Dictionary<string, EnumType> enumTypes) {
        // For the assign RHS / default check, a bare enum-member identifier (PropertyAccess)
        // is valid when the target is enum-typed and the name is a member.
        if (expr is PropertyAccess pa && CategoryOf(targetTypeName, enumTypes) is TypeCategory.Enum) {
            if (enumTypes.TryGetValue(targetTypeName, out var enumType)
                && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal))
                return new(TypeCategory.Enum, targetTypeName);
        }
        return InferType(expr, null, null, enumTypes);
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
            DateOperation d => new(TypeCategory.Date, "Date"),
            Exists or NotExists or AnyExpr or AllExpr or NoneExpr or Comparison or And or Or or Not => new(TypeCategory.Boolean),
            CountExpr => new(TypeCategory.Number),
            _ => new(TypeCategory.Unknown),
        };

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
        TypeCategory.Enum => $"enum {t.TypeName ?? "?"}",
        TypeCategory.Guid => "Guid",
        TypeCategory.Null => "null",
        TypeCategory.Unknown => "unknown",
        _ => "unknown",
    };

    private static bool Compatible(TypeInfo left, TypeInfo right) {
        if (left.Category is TypeCategory.Unknown or TypeCategory.Null
            || right.Category is TypeCategory.Unknown or TypeCategory.Null)
            return true;
        if (left.Category is TypeCategory.Enum && right.Category is TypeCategory.Enum)
            return string.Equals(left.TypeName, right.TypeName, StringComparison.Ordinal);
        if (left.Category is TypeCategory.Enum && right.Category is TypeCategory.Text)
            return true; // member validity checked separately
        if (right.Category is TypeCategory.Enum && left.Category is TypeCategory.Text)
            return true;
        if (left.Category is TypeCategory.Date && right.Category is TypeCategory.Date) {
            // Date/DateOnly vs DateTime/Timestamp differ in CLR type — incompatible.
            bool leftDateTime = left.TypeName is "DateTime" or "Timestamp";
            bool rightDateTime = right.TypeName is "DateTime" or "Timestamp";
            return leftDateTime == rightDateTime;
        }
        return left.Category == right.Category;
    }

    private static void Report(AnalysisContext context, Node where, string message) =>
        context.ReportError(where, message, DomainModelDiagnosticCodes.SemanticTypeCompatibility);
}