using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Syntax.Nodes;

namespace Poly.Data.Modeling.Analysis;

using Conditional = Effects.Conditional;
using SysConditional = Syntax.Nodes.Conditional;

internal sealed record DownstreamConstraintsMetadata(IReadOnlyList<Constraint> Constraints) : IAnalysisMetadata;

/// <summary>
/// Propagates constraints from downstream effect property accesses to Action parameters.
/// This allows code generation to validate parameters earlier based on how they're used.
///
/// Handles four propagation patterns:
///   1. Direct parameter-to-parameter: paramA → InvokeAction(binding: paramB → paramA) → B's constraints on paramB
///   2. EffectValueRef parameter: paramA → effect output → InvokeAction → B's constraints
///   3. Value-side assign: paramA → Assign(target=ConstrainedProp, value=paramA) → ConstrainedProp's constraints
///   4. ExpressionValue with provable inverse: paramA → Expression(Add(paramA, c)) → Assign → offset range constraints
/// </summary>
internal sealed class ConstraintPropagationAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain domain:
                AnalyzeDomain(context, domain);
                break;
            case Action action:
                AnalyzeAction(context, action);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        foreach (var entity in domain.Types.OfType<Entity>().Where(context.ShouldAnalyze)) {
            foreach (var action in entity.Actions.Where(context.ShouldAnalyze)) {
                AnalyzeAction(context, action);
            }
        }
    }

    private static void AnalyzeAction(AnalysisContext context, Action action) {
        if (!context.TryBeginAnalyzerVisit<ConstraintPropagationAnalyzer>(action)) {
            return;
        }

        foreach (var param in action.Parameters.OfType<Property>()) {
            var visited = new HashSet<Effect>();
            var constraints = new List<Constraint>();
            CollectDownstreamConstraints(action.Effects, param, constraints, visited);

            if (constraints.Count > 0) {
                context.SetMetadata(param, new DownstreamConstraintsMetadata(constraints.AsReadOnly()));
            }
        }
    }

    private static void CollectDownstreamConstraints(
        IEnumerable<Effect> effects,
        Property? param,
        List<Constraint> accumulated,
        HashSet<Effect> visited) {

        foreach (var effect in effects) {
            if (!visited.Add(effect)) continue;

            switch (effect) {
                case InvokeAction invoke:
                    CollectFromInvokeAction(invoke, param, accumulated, visited);
                    break;

                case Conditional cond:
                    if (param is null) {
                        CollectFromEffects(cond.ChildEffects, accumulated, visited);
                    }
                    else {
                        CollectDownstreamConstraints(cond.ChildEffects, param, accumulated, visited);
                    }
                    break;

                case Assign assign:
                    CollectFromAssign(assign, param, accumulated);
                    break;
            }
        }
    }

    private static void CollectFromInvokeAction(
        InvokeAction invoke,
        Property? param,
        List<Constraint> accumulated,
        HashSet<Effect> visited) {

        if (param is not null) {
            foreach (var binding in invoke.ParameterBindings) {
                if (ReferenceEquals(binding.Value, param)) {
                    // Direct Property binding: param flows directly to callee's parameter
                    if (invoke.TargetAction is not null) {
                        // Collect from the callee parameter's constraints directly
                        CollectFromTargetParameter(invoke.TargetAction, binding.Key, accumulated);
                        // Also collect from the callee's downstream effects
                        CollectFromAction(invoke.TargetAction, accumulated, visited);
                    }
                }
                else if (binding.Value is EffectValueRef eRef && eRef.SourceEffectName == param.Name) {
                    // EffectValueRef binding: param flows through an effect output
                    if (invoke.TargetAction is not null) {
                        CollectFromTargetParameter(invoke.TargetAction, binding.Key, accumulated);
                        CollectFromAction(invoke.TargetAction, accumulated, visited);
                    }
                }
            }
        }
        else {
            // No specific param — collect from ALL invokes
            if (invoke.TargetAction is not null) {
                CollectFromAction(invoke.TargetAction, accumulated, visited);
            }
        }
    }

    private static void CollectFromTargetParameter(Action targetAction, string parameterName, List<Constraint> accumulated) {
        var targetParam = targetAction.Parameters
            .OfType<Property>()
            .FirstOrDefault(p => string.Equals(p.Name, parameterName, StringComparison.Ordinal));
        if (targetParam is not null) {
            accumulated.AddRange(targetParam.EffectiveConstraints);
        }
    }

    private static void CollectFromAssign(
        Assign assign,
        Property? param,
        List<Constraint> accumulated) {

        if (param is null) {
            // Collect from all assign targets
            if (assign.Target is Property targetProp) {
                accumulated.AddRange(targetProp.EffectiveConstraints);
            }
            return;
        }

        // VALUE side: param is used as the value being assigned to a constrained target
        if (ReferenceEquals(assign.Value, param)) {
            if (assign.Target is Property targetProp) {
                accumulated.AddRange(targetProp.EffectiveConstraints);
            }
        }
        else if (assign.Value is ExpressionValue exprVal) {
            // VALUE side: param flows through an expression to a constrained target
            CollectFromExpressionValue(exprVal, param, assign.Target, accumulated);
        }
    }

    /// <summary>
    /// Walks an ExpressionValue's AST to detect when a parameter flows through
    /// a provably invertible transformation into a constrained target.
    /// Handles: param + c, param - c, c + param, c - param
    /// </summary>
    private static void CollectFromExpressionValue(
        ExpressionValue exprVal,
        Property param,
        DomainValue? target,
        List<Constraint> accumulated) {

        if (target is not Property targetProp) return;
        if (exprVal.Expression is not { } expr) return;

        // Check that the expression references the parameter
        if (!ExpressionReferencesParameter(expr, param.Name)) return;

        // Determine the offset applied to the parameter
        var offset = GetExpressionOffset(expr, param.Name);
        if (offset is null) return; // Non-invertible or unrecognized expression

        // Collect constraints from the target and apply the inverse offset
        foreach (var constraint in targetProp.EffectiveConstraints) {
            if (constraint is RangeConstraint range) {
                var adjusted = AdjustRangeConstraint(range, offset.Value);
                if (adjusted is not null) {
                    accumulated.Add(adjusted);
                }
            }
            else if (constraint is LengthConstraint length && offset.Value == 0) {
                // Length constraints propagate directly when no offset
                accumulated.Add(length);
            }
            else if (constraint is EqualityConstraint eq && offset.Value == 0) {
                accumulated.Add(eq);
            }
        }
    }

    /// <summary>
    /// Checks if an AST expression contains a reference to a parameter by name.
    /// Walks Member chains to find Parameter/Variable nodes.
    /// </summary>
    private static bool ExpressionReferencesParameter(Node expr, string paramName) {
        return expr switch {
            Parameter p => string.Equals(p.Name, paramName, StringComparison.Ordinal),
            Variable v => string.Equals(v.Name, paramName, StringComparison.Ordinal),
            Member m => ExpressionReferencesParameter(m.Value, paramName),
            Add a => ExpressionReferencesParameter(a.LeftHandValue, paramName)
                     || ExpressionReferencesParameter(a.RightHandValue, paramName),
            Subtract s => ExpressionReferencesParameter(s.LeftHandValue, paramName)
                         || ExpressionReferencesParameter(s.RightHandValue, paramName),
            _ => false
        };
    }

    /// <summary>
    /// Computes the numeric offset applied to a parameter in an expression.
    /// Returns null if the expression is not a simple Add/Subtract of the parameter with a constant.
    /// The offset is the value that, when applied, transforms the parameter to the expression's result.
    /// e.g., "param + 5" → 5, "param - 3" → -3, "10 - param" → null (non-trivial inverse)
    /// </summary>
    private static long? GetExpressionOffset(Node expr, string paramName) {
        switch (expr) {
            case Add add:
                // param + c or c + param
                if (IsParameterRef(add.LeftHandValue, paramName) && GetConstantValue(add.RightHandValue) is long c1)
                    return c1;
                if (IsParameterRef(add.RightHandValue, paramName) && GetConstantValue(add.LeftHandValue) is long c2)
                    return c2;
                return null;

            case Subtract sub:
                // param - c
                if (IsParameterRef(sub.LeftHandValue, paramName) && GetConstantValue(sub.RightHandValue) is long c3)
                    return -c3; // result = param - c, so offset is -c (param = result + c)
                // c - param: inverse is not identity-preserving, skip
                return null;

            case Parameter p:
                return string.Equals(p.Name, paramName, StringComparison.Ordinal) ? 0 : null;
            case Variable v:
                return string.Equals(v.Name, paramName, StringComparison.Ordinal) ? 0 : null;
            case Member m:
                return ExpressionReferencesParameter(m, paramName) ? 0 : null;

            default:
                return null;
        }
    }

    private static bool IsParameterRef(Node expr, string paramName) {
        return expr switch {
            Parameter p => string.Equals(p.Name, paramName, StringComparison.Ordinal),
            Variable v => string.Equals(v.Name, paramName, StringComparison.Ordinal),
            _ => false
        };
    }

    private static long? GetConstantValue(Node expr) {
        if (expr is Constant c && c.Value is not null) {
            return TryConvertToInt64(c.Value);
        }
        return null;
    }

    /// <summary>
    /// Adjusts a RangeConstraint by applying the inverse of an offset.
    /// If target requires result in [min, max] and result = param + offset,
    /// then param must be in [min - offset, max - offset].
    /// </summary>
    private static RangeConstraint? AdjustRangeConstraint(RangeConstraint original, long offset) {
        if (offset == 0) return original;

        var minValue = original.MinValue is null ? null : TryConvertToDouble(original.MinValue);
        if (original.MinValue is not null && minValue is null) {
            return null;
        }

        var maxValue = original.MaxValue is null ? null : TryConvertToDouble(original.MaxValue);
        if (original.MaxValue is not null && maxValue is null) {
            return null;
        }

        object? newMin = minValue is not null ? minValue.Value - offset : null;
        object? newMax = maxValue is not null ? maxValue.Value - offset : null;

        return new RangeConstraint(newMin, newMax);
    }

    private static long? TryConvertToInt64(object value) {
        try {
            return Convert.ToInt64(value);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) {
            return null;
        }
    }

    private static double? TryConvertToDouble(object value) {
        try {
            return Convert.ToDouble(value);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) {
            return null;
        }
    }

    private static void CollectFromAction(
        Action target,
        List<Constraint> accumulated,
        HashSet<Effect> visited) {

        foreach (var effect in target.Effects) {
            if (!visited.Add(effect)) continue;

            switch (effect) {
                case Assign assign:
                    if (assign.Target is Property prop) {
                        accumulated.AddRange(prop.EffectiveConstraints);
                    }
                    break;

                case InvokeAction nestedInvoke:
                    if (nestedInvoke.TargetAction is not null) {
                        CollectFromAction(nestedInvoke.TargetAction, accumulated, visited);
                    }
                    break;

                case Conditional cond:
                    CollectFromEffects(cond.ChildEffects, accumulated, visited);
                    break;
            }
        }
    }

    private static void CollectFromEffects(
        IEnumerable<Effect> effects,
        List<Constraint> accumulated,
        HashSet<Effect> visited) {

        foreach (var effect in effects) {
            if (!visited.Add(effect)) continue;

            switch (effect) {
                case Assign assign:
                    if (assign.Target is Property prop) {
                        accumulated.AddRange(prop.EffectiveConstraints);
                    }
                    break;

                case InvokeAction nestedInvoke:
                    if (nestedInvoke.TargetAction is not null) {
                        CollectFromAction(nestedInvoke.TargetAction, accumulated, visited);
                    }
                    break;

                case Conditional cond:
                    CollectFromEffects(cond.ChildEffects, accumulated, visited);
                    break;
            }
        }
    }
}