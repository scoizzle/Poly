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
internal sealed partial class ExpressionTypeAnalyzer : INodeAnalyzer {
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

    /// <summary>Marker: this call was already checked in this analysis run (#483).</summary>
    private sealed class CheckedCallMetadata : IAnalysisMetadata { }

    /// <summary>
    /// True the first time a call node is checked in this run: a <c>require</c> copy shares its
    /// policy's expression object, so the tree walk reaches one call node twice. The fact sits on
    /// the node in the run's own metadata store (<see cref="ContractCallResolver"/>'s idiom) — per
    /// analysis, never a static table or a field on the reused pass instance.
    /// </summary>
    private static bool FirstVisitOfCall(AnalysisContext context, FunctionCall call) {
        if (context.GetMetadata<CheckedCallMetadata>(call) is not null) return false;
        context.SetMetadata(call, new CheckedCallMetadata());
        return true;
    }

    public void Analyze(AnalysisContext context, Node node) {

        if (node is Domain domain) {
            AnalyzeObservedFlows(context, domain);
            FunctionAnalysis.CheckDeclarations(context, domain);
            AnalyzeFunctionBodies(context, domain);
        }
        if (node is FunctionCall call && FirstVisitOfCall(context, call))
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
        // #483: a `require P` copy holds P's expression object, so one tree is walked twice and
        // would report twice. The set is per entity walk (the session re-runs this pass) and keyed
        // on the parameter scope, because an action's parameters change what a name infers to.
        var walked = new WalkedExpressions();
        foreach (var policy in entity.Policies)
            WalkExpression(context, policy.Expression, props, null, enumTypes, walked);
        foreach (var stage in entity.Stages) {
            foreach (var policy in stage.Policies)
                WalkExpression(context, policy.Expression, props, null, enumTypes, walked);
        }
        foreach (var action in entity.Actions)
            foreach (var policy in action.Policies)
                WalkExpression(context, policy.Expression, props, ParamsOf(action), enumTypes, walked);
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions)
                foreach (var policy in action.Policies)
                    WalkExpression(context, policy.Expression, props, ParamsOf(action), enumTypes, walked);

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

    /// <summary>
    /// Policy expressions already walked for this entity, per parameter scope (#483). A
    /// <c>require</c> copy shares its policy's expression object, so one tree is reachable by two
    /// paths; the scope is part of the key because an action's parameters can change how a name
    /// infers, and a finding is deduped only when the context is the same.
    /// </summary>
    private sealed class WalkedExpressions {
        private readonly Dictionary<string, HashSet<Node>> _byScope = new(StringComparer.Ordinal);

        public bool TryEnter(
            DomainExpression expr, Dictionary<string, string>? parameters, Dictionary<string, string> props) {
            var scope = ScopeOf(expr, parameters, props);
            if (!_byScope.TryGetValue(scope, out var seen))
                _byScope[scope] = seen = new HashSet<Node>(ReferenceEqualityComparer.Instance);
            return seen.Add(expr);
        }

        /// <summary>
        /// The parameters this expression reads, minus the entity's own properties. A parameter the
        /// policy never names cannot change what it infers, so it must not open a second scope; a
        /// name the entity declares stays a property whatever the action calls its parameter.
        /// </summary>
        private static string ScopeOf(
            DomainExpression expr, Dictionary<string, string>? parameters, Dictionary<string, string> props) {
            if (parameters is null || parameters.Count == 0) return "";
            return string.Join('\u001f', NamesRead(expr)
                .Where(name => parameters.ContainsKey(name) && !props.ContainsKey(name))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(name => $"{name}:{parameters[name]}"));
        }

        /// <summary>Every name read through a property or parameter access, call arguments included.</summary>
        private static IEnumerable<string> NamesRead(DomainExpression expr) {
            if (expr is PropertyAccess or ParameterAccess) yield return NameOf(expr);
            foreach (var child in expr.Children.OfType<DomainExpression>())
                foreach (var name in NamesRead(child))
                    yield return name;
        }

        private static string NameOf(DomainExpression expr) => expr switch {
            PropertyAccess property => property.Name,
            ParameterAccess parameter => parameter.Name,
            _ => "",
        };
    }

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
                // The body is checked in the caller's scope. The `where` clause is
                // binder-typed against the iterated relationship's target entity (#455 F4):
                // a non-Boolean `where` lowers but cannot compile.
                if (fe.Where is { } where) {
                    var iteratedTypeName = ResolveRelationshipTargetTypeName(
                        context, callerEntityName, fe.RelationshipName);
                    WalkExpression(context, where, props, parameters, enumTypes);
                    var whereType = BindsToBinder(where, fe.BinderName)
                        ? InferBinderExpressionType(context, iteratedTypeName, fe.BinderName, where, enumTypes)
                        : InferType(context, where, props, parameters, enumTypes);
                    if (whereType.Category is not (TypeCategory.Boolean or TypeCategory.Unknown))
                        Report(context, where,
                            $"'for' where condition must be Boolean (got '{Describe(whereType)}')");
                }
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
        Dictionary<string, EnumType> enumTypes,
        WalkedExpressions? walked = null) {
        if (walked is not null && !walked.TryEnter(expr, parameters, props)) return;
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
                        WalkExpression(context, sub.Left, props, parameters, enumTypes, walked);
                        WalkExpression(context, sub.Right, props, parameters, enumTypes, walked);
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
                WalkExpression(context, not.Operand, props, parameters, enumTypes, walked);
                return;
            case And and:
                CheckBooleanOperands(context, and.Left, and.Right, props, parameters, enumTypes, walked);
                return;
            case Or or:
                CheckBooleanOperands(context, or.Left, or.Right, props, parameters, enumTypes, walked);
                return;
            case MatchExpr match:
                CheckMatch(context, match, props, parameters, enumTypes);
                return;
            case FunctionCall call:
                CheckCallArguments(context, call, props, parameters, enumTypes);
                return;
            case RelationshipNavigation:
            case Exists or NotExists or AnyExpr or AllExpr or NoneExpr or CountExpr:
                // target-scoped (related-entity properties / store-aware) — no local type check,
                // but an imported endpoint call in the body is still unlowerable (#426).
                RejectEndpointCallsInBody(context, expr);
                return;
            default:
                foreach (var child in expr.Children.OfType<DomainExpression>())
                    WalkExpression(context, child, props, parameters, enumTypes, walked);
                _meaning.Checks.TryCheck(context, expr,
                    new ExpressionTypeCheckScope(props, parameters, enumTypes));
                return;
        }
    }

    private void CheckBooleanOperands(
        AnalysisContext context, DomainExpression left, DomainExpression right,
        Dictionary<string, string> props, Dictionary<string, string>? parameters,
        Dictionary<string, EnumType> enumTypes, WalkedExpressions? walked = null) {
        var leftType = InferType(context, left, props, parameters, enumTypes);
        var rightType = InferType(context, right, props, parameters, enumTypes);
        if (leftType.Category is not (TypeCategory.Boolean or TypeCategory.Unknown)
            || rightType.Category is not (TypeCategory.Boolean or TypeCategory.Unknown))
            Report(context, left,
                $"'and'/'or' requires Boolean operands (got '{Describe(leftType)}' and '{Describe(rightType)}')");
        WalkExpression(context, left, props, parameters, enumTypes, walked);
        WalkExpression(context, right, props, parameters, enumTypes, walked);
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
}