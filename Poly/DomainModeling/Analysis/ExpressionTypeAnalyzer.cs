using Poly.DomainModeling.Meaning;
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

    private readonly ExpressionMeaning _meaning;
    private readonly ExpressionFormRegistry _forms;

    public ExpressionTypeAnalyzer(
        ExpressionMeaning? meaning = null,
        ExpressionFormRegistry? forms = null) {
        _meaning = meaning ?? ExpressionMeaning.Empty;
        _forms = forms ?? new ExpressionFormRegistry();
    }

    private DomainExpression RewriteLibraryIdent(DomainExpression expr) =>
        expr is PropertyAccess pa && _forms.TryRewriteIdent(pa.Name, out var claimed)
            ? claimed
            : expr;

    public void Analyze(AnalysisContext context, Node node) {

        if (node is Domain domain) {
            AnalyzeObservedFlows(context, domain);
            FunctionAnalysis.CheckDeclarations(context, domain);
            AnalyzeFunctionBodies(context, domain);
        }
        if (node is FunctionCall call)
            FunctionAnalysis.CheckCall(context, call);
        if (node is Property property)
            FunctionAnalysis.CheckProperty(context, property);
        if (node is Entity entity)
            AnalyzeEntity(context, entity);

        this.AnalyzeChildren(context, node);
    }

    /// <summary>
    /// Each resolvable <see cref="ObservedFlow"/> is checked as an assign of the source property
    /// to the target (<see cref="CheckCompatible"/>). A mismatch is DMSEM003 on the target
    /// property; a matching allowance makes it a warning. Unresolvable paths are reported by
    /// <see cref="EffectAnalyzer"/>.
    /// </summary>
    private void AnalyzeObservedFlows(AnalysisContext context, Domain domain) {
        var flows = RuntimeAnalysisCache.Session(domain).ObservedFlows;
        if (flows.Count == 0)
            return;
        var enumTypes = ResolveEnums(context);
        foreach (var flow in flows) {
            var source = ObservedFlowChecks.Resolve(domain, flow.SourcePath);
            var target = ObservedFlowChecks.Resolve(domain, flow.TargetPath);
            if (source is null || target is null)
                continue;
            var props = source.Value.Entity.Properties
                .ToDictionary(p => p.Name, p => p.Type.TypeName, StringComparer.Ordinal);
            CheckCompatible(
                context, new PropertyAccess(source.Value.Property.Name), target.Value.Property.Type.TypeName,
                enumTypes, $"observed flow '{flow.SourcePath}' -> '{flow.TargetPath}'", props, parameters: null,
                report: message => context.ReportDiagnostic(
                    target.Value.Property,
                    ObservedFlowChecks.SeverityFor(
                        context, domain, target.Value.Property, DomainModelDiagnosticCodes.SemanticTypeCompatibility),
                    message,
                    DomainModelDiagnosticCodes.SemanticTypeCompatibility));
        }
    }

    /// <summary>
    /// Each function body is walked with its parameters as the scope and its type must be
    /// assignable to the declared return type (DMFUN006). Unknown body types are skipped,
    /// as everywhere else in this pass.
    /// </summary>
    private void AnalyzeFunctionBodies(AnalysisContext context, Domain domain) {
        var enumTypes = ResolveEnums(context);
        var noProps = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var function in domain.Functions) {
            var parameters = function.Parameters
                .GroupBy(p => p.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Type.TypeName, StringComparer.Ordinal);
            WalkExpression(context, function.Body, noProps, parameters, enumTypes);
            var bodyType = InferValueType(context, function.Body, noProps, parameters, enumTypes);
            var returnName = function.ReturnType.TypeName;
            var returnType = new TypeInfo(CategoryOf(context, returnName, enumTypes), returnName);
            if (bodyType.Category is not TypeCategory.Unknown
                && returnType.Category is not TypeCategory.Unknown
                && !Compatible(context, bodyType, returnType, assigning: true))
                context.ReportError(function,
                    $"Function '{function.Name}' body is '{Describe(bodyType)}' but it is declared to return '{returnName}'.",
                    DomainModelDiagnosticCodes.FunctionReturnType);
        }
    }

    /// <summary>
    /// <see cref="InferType"/> plus the arithmetic result types it leaves Unknown: numeric
    /// arithmetic is Number, and Text + Text is Text. Used for call arguments and function bodies.
    /// </summary>
    private TypeInfo InferValueType(
        AnalysisContext context, DomainExpression expr,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        if (expr is Add or Subtract or Multiply or Divide) {
            var operands = expr.Children.OfType<DomainExpression>()
                .Select(c => InferValueType(context, c, props, parameters, enumTypes)).ToList();
            if (operands.All(o => IsNumeric(o.Category)))
                return new(TypeCategory.Number);
            if (expr is Add && operands.All(o => o.Category is TypeCategory.Text))
                return new(TypeCategory.Text);
            return new(TypeCategory.Unknown);
        }
        return InferType(context, expr, props, parameters, enumTypes);
    }

    private void CheckCallArguments(
        AnalysisContext context, FunctionCall call,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        foreach (var argument in call.Arguments)
            WalkExpression(context, argument, props, parameters, enumTypes);
        // Unknown name and wrong arity are reported once per call by FunctionAnalysis.CheckCall.
        var function = FunctionAnalysis.Find(context, call.Name);
        if (function is null || function.Parameters.Count != call.Arguments.Count)
            return;
        for (var i = 0; i < call.Arguments.Count; i++) {
            var parameterType = function.Parameters[i].Type.TypeName;
            var target = new TypeInfo(CategoryOf(context, parameterType, enumTypes), parameterType);
            var argument = InferValueType(context, call.Arguments[i], props, parameters, enumTypes);
            if (argument.Category is not TypeCategory.Unknown
                && target.Category is not TypeCategory.Unknown
                && !Compatible(context, argument, target, assigning: true)) {
                var where = call.Line > 0 ? $" (line {call.Line}, col {call.Col})" : "";
                context.ReportError(call,
                    $"Argument '{function.Parameters[i].Name}' of '{call.Name}' expects '{parameterType}', " +
                    $"got '{Describe(argument)}'{where}.",
                    DomainModelDiagnosticCodes.FunctionArgumentType);
            }
        }
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
                _meaning.TryAdviseAssignConversion(context, assign,
                    new ExpressionTypeCheckScope(props, parameters, enumTypes));
                break;
            case ConditionalEffect cond:
                WalkExpression(context, cond.Condition, props, parameters, enumTypes);
                var condType = InferType(context, cond.Condition, props, parameters, enumTypes);
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
            case ForEachEffect fe:
                // The body is checked in the caller's scope. `where` is binder-typed and is
                // not type-checked until lowering (slice 6) gives the binder a type.
                CheckEffectTree(context, fe.Body, callerEntityName, props, parameters, enumTypes);
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
                    : InferLiteralAware(context, binding.Expression, paramType, enumTypes, callerProps, parameters);
                var targetCategory = CategoryOf(context, paramType, enumTypes);
                if (inferred.Category is not TypeCategory.Unknown
                    && targetCategory is not TypeCategory.Unknown
                    && !Compatible(context, inferred, new TypeInfo(targetCategory, paramType), assigning: true)) {
                    Report(context, binding.Expression,
                        $"type mismatch in argument '{binding.PropertyName}' of invoke '{actionName}': " +
                        $"cannot assign '{Describe(inferred)}' to '{paramType}'");
                }
                // Entity type names (Widget, Gadget, …) resolve to Unknown CategoryOf, so
                // the primitive-category check above misses entity-type mismatches.
                // Reject when the inferred type is an entity (Unknown category with a known
                // name) and it differs from the target parameter type.
                if (inferred.Category is TypeCategory.Unknown
                    && inferred.TypeName is { } argTypeName
                    && !string.Equals(argTypeName, paramType, StringComparison.Ordinal)) {
                    Report(context, binding.Expression,
                        $"type mismatch in argument '{binding.PropertyName}' of invoke '{actionName}': " +
                        $"cannot assign '{argTypeName}' to '{paramType}'");
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
                return propType is null ? new(TypeCategory.Unknown) : new(CategoryOf(context, propType, enumTypes), propType);
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
                CheckArithmetic(context, add.Left, add.Right, props, parameters, enumTypes, allowTextConcat: true);
                return;
            case Subtract sub:
                if (sub.Left is PropertyAccess && sub.Right is PropertyAccess) {
                    var leftType = InferType(context, sub.Left, props, parameters, enumTypes);
                    var rightType = InferType(context, sub.Right, props, parameters, enumTypes);
                    // Same-family date minus date is a difference, not arithmetic; mixed families stay errors.
                    if (leftType.Category is TypeCategory.Date && rightType.Category is TypeCategory.Date
                        && !MixedDateGranularity(context, leftType, rightType)) {
                        WalkExpression(context, sub.Left, props, parameters, enumTypes);
                        WalkExpression(context, sub.Right, props, parameters, enumTypes);
                        return;
                    }
                }
                CheckArithmetic(context, sub.Left, sub.Right, props, parameters, enumTypes);
                return;
            case Multiply mul:
                CheckNumericArithmetic(context, mul.Left, mul.Right, props, parameters, enumTypes);
                return;
            case Divide div:
                CheckNumericArithmetic(context, div.Left, div.Right, props, parameters, enumTypes);
                return;
            case Not not:
                var operand = InferType(context, not.Operand, props, parameters, enumTypes);
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
            case MatchExpr match:
                CheckMatch(context, match, props, parameters, enumTypes);
                return;
            case FunctionCall call:
                CheckCallArguments(context, call, props, parameters, enumTypes);
                return;
            case RelationshipNavigation:
            case Exists or NotExists or AnyExpr or AllExpr or NoneExpr or CountExpr:
                // target-scoped (related-entity properties / store-aware) — no local type check
                return;
            default:
                foreach (var child in expr.Children.OfType<DomainExpression>())
                    WalkExpression(context, child, props, parameters, enumTypes);
                _meaning.Checks.TryCheck(context, expr,
                    new ExpressionTypeCheckScope(props, parameters, enumTypes));
                return;
        }
    }

    /// <summary>
    /// <c>match</c> (#409): every arm condition is Boolean (DMMAT001), the arm values and the
    /// <c>else</c> unify to one type (DMMAT002), and no two arms share a condition (DMMAT003).
    /// Unknown types are skipped, as everywhere else in this pass. Every sub-expression is walked.
    /// </summary>
    private void CheckMatch(
        AnalysisContext context, MatchExpr match,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var where = match.Line > 0 ? $" (line {match.Line}, col {match.Col})" : "";
        foreach (var arm in match.Arms) {
            var condition = InferType(context, arm.Condition, props, parameters, enumTypes);
            if (condition.Category is not (TypeCategory.Boolean or TypeCategory.Unknown))
                context.ReportError(match,
                    $"'match' arm condition must be Boolean (got '{Describe(condition)}'){where}.",
                    DomainModelDiagnosticCodes.MatchConditionNotBoolean);
            WalkExpression(context, arm.Condition, props, parameters, enumTypes);
            WalkExpression(context, arm.Value, props, parameters, enumTypes);
            CheckMatchNames(context, match, arm.Condition, props, parameters, enumTypes, where);
            CheckMatchNames(context, match, arm.Value, props, parameters, enumTypes, where);
        }
        WalkExpression(context, match.Else, props, parameters, enumTypes);
        CheckMatchNames(context, match, match.Else, props, parameters, enumTypes, where);

        TypeInfo? reference = null;
        foreach (var value in match.Arms.Select(a => a.Value).Append(match.Else)) {
            var type = InferType(context, value, props, parameters, enumTypes);
            if (type.Category is TypeCategory.Unknown or TypeCategory.Null) continue;
            if (reference is null) { reference = type; continue; }
            if (!Unifies(reference.Value, type))
                context.ReportError(match,
                    $"'match' arm values do not unify: '{Describe(reference.Value)}' and '{Describe(type)}'{where}.",
                    DomainModelDiagnosticCodes.MatchArmTypeMismatch);
        }

        var seen = new List<string>();
        foreach (var arm in match.Arms) {
            var key = StructuralKey(arm.Condition);
            if (seen.Contains(key))
                context.ReportError(match,
                    $"'match' has two arms with the same condition; the later arm is unreachable{where}.",
                    DomainModelDiagnosticCodes.MatchDuplicateCondition);
            else
                seen.Add(key);
        }
    }

    /// <summary>
    /// Two known arm types unify when they share a category (enums: the same enum), or one is an
    /// enum and the other Text, as in comparisons. Not <see cref="Compatible"/>: that treats two
    /// untyped values as equal by name.
    /// </summary>
    private static bool Unifies(TypeInfo a, TypeInfo b) =>
        a.Category == b.Category
            ? a.Category is not TypeCategory.Enum || string.Equals(a.TypeName, b.TypeName, StringComparison.Ordinal)
            : (a.Category is TypeCategory.Enum && b.Category is TypeCategory.Text)
              || (b.Category is TypeCategory.Enum && a.Category is TypeCategory.Text);

    /// <summary>
    /// A position- and identity-free structural key for an expression: type, scalar members (not
    /// <c>Id</c>, <c>Line</c>, <c>Col</c>), then children. Two conditions written on different lines
    /// with the same shape produce the same key.
    /// </summary>
    private static string StructuralKey(Node? node) {
        if (node is null) return "_";
        var scalars = node.GetType().GetProperties()
            .Where(p => p.GetIndexParameters().Length == 0
                && p.Name is not ("Id" or "Line" or "Col" or "Children")
                && (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum
                    || p.PropertyType == typeof(string) || p.PropertyType == typeof(object)
                    || p.PropertyType == typeof(decimal)))
            .Select(p => {
                var value = p.GetValue(node);
                return $"{p.Name}={value?.GetType().Name}:{value}";
            });
        return $"{node.GetType().Name}{{{string.Join(",", scalars)}}}({string.Join(",", node.Children.Select(StructuralKey))})";
    }

    /// <summary>
    /// Fail closed on a bare name that resolves to nothing (it would lower to a member the entity
    /// does not have). Descends only value-level nodes; quantifier and navigation bodies are
    /// target-scoped. A bare name is allowed as a comparison operand against an enum, and as a
    /// value when some enum declares that member.
    /// </summary>
    private void CheckMatchNames(
        AnalysisContext context, MatchExpr match, DomainExpression expr,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes, string where) {
        switch (expr) {
            case PropertyAccess pa:
                var rewritten = RewriteLibraryIdent(pa);
                if (rewritten is not PropertyAccess
                    || ResolvePropertyType(pa.Name, props, parameters) is not null
                    || string.Equals(pa.Name, "Guid", StringComparison.Ordinal)
                    || enumTypes.Values.Any(e => e.MemberNames.Contains(pa.Name, StringComparer.Ordinal)))
                    return;
                context.ReportError(match,
                    $"'{pa.Name}' in 'match' is not a property, parameter, or enum member{where}.",
                    DomainModelDiagnosticCodes.MatchUnresolvedName);
                return;
            case Comparison cmp:
                CheckMatchNames(context, match, cmp.Left, props, parameters, enumTypes, where);
                CheckMatchNames(context, match, cmp.Right, props, parameters, enumTypes, where);
                return;
            case And or Or or Not or Add or Subtract or Multiply or Divide:
                foreach (var child in expr.Children.OfType<DomainExpression>())
                    CheckMatchNames(context, match, child, props, parameters, enumTypes, where);
                return;
            default:
                return; // nested match reports its own names when it is walked
        }
    }

    /// <summary>The unified value type of a <c>match</c>: the first arm type that is known and not null.</summary>
    private TypeInfo InferMatch(
        AnalysisContext context, MatchExpr match,
        Dictionary<string, string>? props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        var types = match.Arms.Select(a => a.Value).Append(match.Else)
            .Select(v => InferType(context, v, props, parameters, enumTypes)).ToList();
        if (types.Any(t => t.Category is TypeCategory.Unknown))
            return new(TypeCategory.Unknown);
        return types.FirstOrDefault(t => t.Category is not TypeCategory.Null) is { Category: not TypeCategory.Unknown } known
            ? known
            : new(TypeCategory.Null);
    }

    private void CheckArithmetic(
        AnalysisContext context, DomainExpression left, DomainExpression right,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes,
        bool allowTextConcat = false) {
        var leftType = InferType(context, left, props, parameters, enumTypes);
        var rightType = InferType(context, right, props, parameters, enumTypes);
        ReportFractionalLiteral(context, left, leftType, right, rightType);
        // numeric + numeric, text + text (concatenation; `+` only), date + number (AddDays
        // lowering), or date + duration
        // (a parsed `N days` offset with a temporal left operand); Unknown operands
        // (path-prefix reads, peer binders) are out of this scope — skip. A duration
        // without a temporal left operand (Number + days, days + date) is an unresolved
        // temporal specialization — reject, never a silent numeric constant.
        if (leftType.Category is not TypeCategory.Unknown && rightType.Category is not TypeCategory.Unknown
            && !(IsNumeric(leftType.Category) && IsNumeric(rightType.Category))
            && !(allowTextConcat && leftType.Category is TypeCategory.Text && rightType.Category is TypeCategory.Text)
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
        var leftType = InferType(context, left, props, parameters, enumTypes);
        var rightType = InferType(context, right, props, parameters, enumTypes);
        ReportFractionalLiteral(context, left, leftType, right, rightType);
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
        var leftType = InferType(context, left, props, parameters, enumTypes);
        var rightType = InferType(context, right, props, parameters, enumTypes);
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
        var left = InferType(context, cmp.Left, props, parameters, enumTypes);
        var right = InferType(context, cmp.Right, props, parameters, enumTypes);

        // enum member literal validity: Enum prop compared to a string must be a member
        if (left.Category is TypeCategory.Enum && cmp.Right is Literal { Value: string s })
            CheckEnumMember(context, cmp.Right, left.TypeName!, s, enumTypes);
        if (right.Category is TypeCategory.Enum && cmp.Left is Literal { Value: string s2 })
            CheckEnumMember(context, cmp.Left, right.TypeName!, s2, enumTypes);

        // C4e (c): if (Level is Nope) — bare identifier on the other side of an enum
        // comparison must be a member of that enum (string literals already checked).
        if (left.Category is TypeCategory.Enum && cmp.Right is PropertyAccess { Name: var rightName }
            && ResolvePropertyType(rightName, props, parameters) is null)
            CheckEnumMember(context, cmp.Right, left.TypeName!, rightName, enumTypes);
        if (right.Category is TypeCategory.Enum && cmp.Left is PropertyAccess { Name: var leftName }
            && ResolvePropertyType(leftName, props, parameters) is null)
            CheckEnumMember(context, cmp.Left, right.TypeName!, leftName, enumTypes);

        // Date (DateOnly) and DateTime do not compare. Now is DateTime; Today is Date.
        // Assign still widens Date onto DateTime; this rejection is comparison only.
        // A fractional literal against Number / Int / Int64 is rejected here. Both
        // sides are category Number, so Compatible would allow the pair.
        ReportFractionalLiteral(context, cmp.Left, left, cmp.Right, right);
        if (MixedDateGranularity(context, left, right) || !Compatible(context, left, right))
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
        Dictionary<string, string>? parameters = null,
        Action<string>? report = null) {
        void Report(AnalysisContext ctx, Node where, string message) {
            if (report is null)
                ExpressionTypeAnalyzer.Report(ctx, where, message);
            else
                report(message);
        }

        value = RewriteLibraryIdent(value);
        if (_meaning.TryClaimAssign(context, value, targetTypeName,
                new ExpressionTypeCheckScope(
                    props ?? new Dictionary<string, string>(StringComparer.Ordinal),
                    parameters, enumTypes, targetTypeName)))
            return;
        if (value is PropertyAccess { Name: "Guid" } kw)
            CheckDefault(context, kw, targetTypeName, enumTypes);

        if (value is Literal { Value: decimal } fractional && IsIntegralTypeName(targetTypeName)) {
            Report(context, value, FractionalLiteralMessage(fractional, targetTypeName));
            return;
        }
        var inferred = InferLiteralAware(context, value, targetTypeName, enumTypes, props, parameters);
        var targetCategory = CategoryOf(context, targetTypeName, enumTypes);
        // Unresolved bare identifier (`assign CheckIn to now`, `assign Name to Bogus` →
        // `this.Bogus`). Enum targets keep the member diagnostic. `Guid` lowers to
        // Guid.NewGuid, not a member access, and CheckDefault owns that keyword.
        if (inferred.Category is TypeCategory.Unknown
            && value is PropertyAccess { Name: var bare }
            && ResolvePropertyType(bare, props, parameters) is null
            && !string.Equals(bare, "Guid", StringComparison.Ordinal)) {
            if (targetCategory is TypeCategory.Enum)
                Report(context, value, $"'{bare}' is not a member of enum '{targetTypeName}'");
            else
                Report(context, value, $"'{bare}' is not a property, parameter, or enum member");
            return;
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
        expr = RewriteLibraryIdent(expr);
        if (_meaning.Checks.TryCheck(context, expr,
                new ExpressionTypeCheckScope(
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    null, enumTypes, propTypeName)))
            return;

        var targetCategory = CategoryOf(context, propTypeName, enumTypes);
        if (targetCategory is TypeCategory.Unknown)
            return;

        switch (expr) {
            case Literal { Value: string s } when targetCategory is TypeCategory.Enum:
                CheckEnumMember(context, expr, propTypeName, s, enumTypes);
                return;
            case Literal { Value: decimal } fractional when IsIntegralTypeName(propTypeName):
                Report(context, expr, FractionalLiteralMessage(fractional, propTypeName));
                return;
            case Literal lit:
                var inferred = InferType(context, expr, null, null, enumTypes);
                if (!Compatible(context, inferred, new TypeInfo(targetCategory, propTypeName), assigning: true))
                    Report(context, expr,
                        $"default value of type '{Describe(inferred)}' is not compatible with property type '{propTypeName}'");
                return;
            case PropertyAccess pa:
                if (pa.Name is "Guid") {
                    if (targetCategory is not TypeCategory.Guid
                        && targetCategory is not TypeCategory.Text)
                        Report(context, expr,
                            $"default(Guid) is not compatible with property type '{propTypeName}' (use a Uuid/Guid or Text property)");
                    return;
                }
                if (targetCategory is TypeCategory.Enum) {
                    // C4e (b): Level: Sev default(Nope) — bare identifier must be a member of the enum.
                    CheckEnumMember(context, expr, propTypeName, pa.Name, enumTypes);
                    return;
                }
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

    internal enum TypeCategory { Text, Number, Boolean, Date, Time, Duration, Enum, Guid, Null, Unknown }

    private readonly record struct TypeInfo(TypeCategory Category, string? TypeName = null);

    private TypeInfo InferLiteralAware(
        AnalysisContext context,
        DomainExpression expr, string targetTypeName, Dictionary<string, EnumType> enumTypes,
        Dictionary<string, string>? props = null, Dictionary<string, string>? parameters = null) {
        // For the assign RHS / default check, a bare enum-member identifier (PropertyAccess)
        // is valid when the target is enum-typed and the name is a member.
        if (expr is PropertyAccess pa && CategoryOf(context, targetTypeName, enumTypes) is TypeCategory.Enum) {
            if (enumTypes.TryGetValue(targetTypeName, out var enumType)
                && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal))
                return new(TypeCategory.Enum, targetTypeName);
        }
        return InferType(context, expr, props, parameters, enumTypes);
    }

    private TypeInfo InferType(
        AnalysisContext context,
        DomainExpression expr,
        Dictionary<string, string>? props,
        Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes) {
        expr = RewriteLibraryIdent(expr);
        if (context.GetMetadata<CatalogTypedExpressionMetadata>(expr) is { TypeName: { } stamped })
            return new(CategoryOf(context, stamped, enumTypes), stamped);

        if (_meaning.Inference.TryDispatch(
                expr,
                child => InferType(context, child, props, parameters, enumTypes).TypeName ?? "",
                out var inferredName)
            && !string.IsNullOrEmpty(inferredName)) {
            context.SetMetadata(expr, new CatalogTypedExpressionMetadata(inferredName));
            return new(CategoryOf(context, inferredName, enumTypes), inferredName);
        }

        // Text + Text is concatenation and yields Text; every other arithmetic result stays Unknown here.
        if (expr is Add add
            && InferType(context, add.Left, props, parameters, enumTypes).Category is TypeCategory.Text
            && InferType(context, add.Right, props, parameters, enumTypes).Category is TypeCategory.Text)
            return new(TypeCategory.Text);

        return expr switch {
            PropertyAccess pa => ResolvePropertyType(pa.Name, props, parameters) is { } pt
                ? new(CategoryOf(context, pt, enumTypes), pt)
                : new(TypeCategory.Unknown),
            ParameterAccess pa => parameters?.TryGetValue(pa.Name, out var pt) == true
                ? new(CategoryOf(context, pt, enumTypes), pt)
                : new(TypeCategory.Unknown),
            Literal { Value: null } => new(TypeCategory.Null),
            Literal { Value: string } => new(TypeCategory.Text),
            Literal { Value: bool } => new(TypeCategory.Boolean),
            Literal { Value: decimal } => new(TypeCategory.Number, "Decimal"),
            Literal { Value: long or int or double or float or short or byte } => new(TypeCategory.Number),
            Exists or NotExists or AnyExpr or AllExpr or NoneExpr or Comparison or And or Or or Not => new(TypeCategory.Boolean),
            CountExpr => new(TypeCategory.Number),
            SumExpr => new(TypeCategory.Number),
            MatchExpr match => InferMatch(context, match, props, parameters, enumTypes),
            FunctionCall call => FunctionAnalysis.Find(context, call.Name) is { } function
                ? new(CategoryOf(context, function.ReturnType.TypeName, enumTypes), function.ReturnType.TypeName)
                : new(TypeCategory.Unknown),
            _ => new(TypeCategory.Unknown),
        };
    }

    private static string? ResolvePropertyType(string name, Dictionary<string, string>? props, Dictionary<string, string>? parameters) {
        if (props?.TryGetValue(name, out var pt) == true) return pt;
        if (parameters?.TryGetValue(name, out var ptype) == true) return ptype;
        return null;
    }

    private static TypeCategory CategoryOf(
        AnalysisContext context, string typeName, Dictionary<string, EnumType> enumTypes) {
        if (enumTypes.ContainsKey(typeName)) return TypeCategory.Enum;
        var lookup = context.GetTypeLookup();
        if (lookup?.Domain.Types.OfType<Ontology.PrimitiveType>().FirstOrDefault(p =>
                string.Equals(p.Name, typeName, StringComparison.Ordinal)) is { } primitive)
            return FromSemantics(primitive.Semantics);
        return typeName switch {
            "Text" or "String" => TypeCategory.Text,
            "Number" or "Int" or "Int64" or "Int32" or "Decimal" or "Float" or "Double" => TypeCategory.Number,
            "Boolean" or "Bool" => TypeCategory.Boolean,
            "Uuid" or "Guid" => TypeCategory.Guid,
            _ => TypeCategory.Unknown,
        };
    }

    private static TypeCategory FromSemantics(PrimitiveSemantics semantics) => semantics switch {
        PrimitiveSemantics.Boolean => TypeCategory.Boolean,
        PrimitiveSemantics.Number => TypeCategory.Number,
        PrimitiveSemantics.Decimal => TypeCategory.Number,
        PrimitiveSemantics.Text => TypeCategory.Text,
        PrimitiveSemantics.Uuid => TypeCategory.Guid,
        PrimitiveSemantics.Binary => TypeCategory.Unknown,
        PrimitiveSemantics.Date => TypeCategory.Date,
        // DateTime keeps the Date category; granularity is compared through SemanticsOf.
        PrimitiveSemantics.DateTime => TypeCategory.Date,
        PrimitiveSemantics.Time => TypeCategory.Time,
        PrimitiveSemantics.Duration => TypeCategory.Duration,
        _ => throw new InvalidOperationException($"Unmapped primitive semantics '{semantics}'."),
    };

    private static bool IsNumeric(TypeCategory c) => c is TypeCategory.Number;

    private static bool IsDate(TypeCategory c) => c is TypeCategory.Date;

    private static string Describe(TypeInfo t) => t.Category switch {
        TypeCategory.Text => "Text",
        TypeCategory.Number => "Number",
        TypeCategory.Boolean => "Boolean",
        TypeCategory.Date => $"Date ({t.TypeName ?? "date"})",
        TypeCategory.Time => $"Time ({t.TypeName ?? "time"})",
        TypeCategory.Duration => "duration",
        TypeCategory.Enum => $"enum {t.TypeName ?? "?"}",
        TypeCategory.Guid => "Guid",
        TypeCategory.Null => "null",
        TypeCategory.Unknown => "unknown",
        _ => "unknown",
    };

    /// <param name="assigning">
    /// When true, <paramref name="left"/> is the source and <paramref name="right"/> is the
    /// target. Catalog semantics: Date onto DateTime is a widen; DateTime onto Date is not.
    /// </param>
    private static bool Compatible(AnalysisContext context, TypeInfo left, TypeInfo right, bool assigning = false) {
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
            var leftSemantics = SemanticsOf(context, left.TypeName);
            var rightSemantics = SemanticsOf(context, right.TypeName);
            if (leftSemantics is PrimitiveSemantics.Date && rightSemantics is PrimitiveSemantics.DateTime)
                return true;
            if (leftSemantics is PrimitiveSemantics.DateTime && rightSemantics is PrimitiveSemantics.Date)
                return false;
            if (left.Category != right.Category)
                return false;
            // Number, Int, and Int64 widen onto Decimal at the assign site only.
            if (right.TypeName is "Decimal" && IsIntegralTypeName(left.TypeName))
                return true;
            if (left.TypeName is { } ln && right.TypeName is { } rn)
                return string.Equals(CanonicalName(ln), CanonicalName(rn), StringComparison.Ordinal);
            return true;
        }

        return left.Category == right.Category
            || string.Equals(CanonicalName(left.TypeName), CanonicalName(right.TypeName), StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>decimal</c> literal combined with <c>Number</c>, <c>Int</c>, or <c>Int64</c>.
    /// A <c>Decimal</c> property combined with a <c>Number</c> property stays legal.
    /// </summary>
    private bool ReportFractionalLiteral(
        AnalysisContext context,
        DomainExpression left, TypeInfo leftType,
        DomainExpression right, TypeInfo rightType) {
        if (left is Literal { Value: decimal } leftLit && IsIntegralTypeName(rightType.TypeName)) {
            Report(context, leftLit, FractionalLiteralMessage(leftLit, rightType.TypeName!));
            return true;
        }
        if (right is Literal { Value: decimal } rightLit && IsIntegralTypeName(leftType.TypeName)) {
            Report(context, rightLit, FractionalLiteralMessage(rightLit, leftType.TypeName!));
            return true;
        }
        return false;
    }

    private static string FractionalLiteralMessage(Literal literal, string typeName) {
        var message = $"Fractional literal is not legal on {typeName}. Use Decimal.";
        if (literal.Line > 0)
            message += $" (line {literal.Line}, col {literal.Col})";
        return message;
    }

    private static bool IsIntegralTypeName(string? typeName) =>
        typeName is "Number" or "Int" or "Int64";

    private static string? CanonicalName(string? typeName) => typeName switch {
        "String" => "Text",
        "Bool" => "Boolean",
        "Int" or "Int64" => "Number",
        "Guid" => "Uuid",
        _ => typeName
    };

    private static PrimitiveSemantics? SemanticsOf(AnalysisContext context, string? typeName) {
        if (typeName is null) return null;
        var lookup = context.GetTypeLookup();
        var primitive = lookup?.Domain.Types.OfType<Ontology.PrimitiveType>().FirstOrDefault(p =>
            string.Equals(p.Name, typeName, StringComparison.Ordinal));
        return primitive?.Semantics;
    }

    private static bool MixedDateGranularity(AnalysisContext context, TypeInfo left, TypeInfo right) {
        var leftSemantics = SemanticsOf(context, left.TypeName);
        var rightSemantics = SemanticsOf(context, right.TypeName);
        return leftSemantics is PrimitiveSemantics.Date && rightSemantics is PrimitiveSemantics.DateTime
            || leftSemantics is PrimitiveSemantics.DateTime && rightSemantics is PrimitiveSemantics.Date;
    }

    private static void Report(AnalysisContext context, Node where, string message) =>
        context.ReportError(where, message, DomainModelDiagnosticCodes.SemanticTypeCompatibility);
}