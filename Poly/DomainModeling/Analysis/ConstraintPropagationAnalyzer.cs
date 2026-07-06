using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed record DownstreamConstraintsMetadata(IReadOnlyList<Constraint> Constraints) : IAnalysisMetadata;

internal sealed class ConstraintPropagationAnalyzer : INodeAnalyzer {
    public const string Id = "DomainConstraintPropagationAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        if (node is Domain domain) {
            ValidateDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomain(AnalysisContext context, Domain domain) {
        if (!context.TryBeginAnalyzerVisit<ConstraintPropagationAnalyzer>(domain)) {
            return;
        }

        foreach (var type in domain.Types) {
            if (type is Entity entity) {
                foreach (var action in entity.Actions) {
                    AnalyzeAction(context, action, entity);
                }
            }
        }
    }

    private static void AnalyzeAction(AnalysisContext context, Action action, Entity entity) {
        foreach (var param in action.Parameters) {
            var visited = new HashSet<Effect>(ReferenceEqualityComparer.Instance);
            var constraints = new List<Constraint>();
            CollectDownstreamConstraints(action.Effects, param, entity, constraints, visited);
            if (constraints.Count > 0) {
                context.SetMetadata(param, new DownstreamConstraintsMetadata(constraints.AsReadOnly()));
            }
        }
    }

    private static void CollectDownstreamConstraints(
        IReadOnlyList<Effect> effects,
        Property param,
        Entity entity,
        List<Constraint> accumulated,
        HashSet<Effect> visited) {

        foreach (var effect in effects) {
            if (!visited.Add(effect)) continue;

            switch (effect) {
                case InvokeActionEffect iae:
                    CollectFromInvokeAction(iae, param, entity, accumulated, visited);
                    break;
                case ConditionalEffect ce:
                    CollectDownstreamConstraints(ce.ThenEffects, param, entity, accumulated, visited);
                    if (ce.ElseEffects is not null) {
                        CollectDownstreamConstraints(ce.ElseEffects, param, entity, accumulated, visited);
                    }
                    break;
                case AssignEffect ae:
                    CollectFromAssign(ae, param, entity, accumulated);
                    break;
            }
        }
    }

    private static void CollectFromInvokeAction(
        InvokeActionEffect iae,
        Property param,
        Entity entity,
        List<Constraint> accumulated,
        HashSet<Effect> visited) {

        var targetAction = entity.Actions.FirstOrDefault(a =>
            string.Equals(a.Name, iae.ActionName, StringComparison.Ordinal));
        if (targetAction is null) return;

        foreach (var binding in iae.ParameterBindings) {
            if (!ExpressionReferencesParameter(binding.Expression, param.Name)) continue;

            var targetParam = targetAction.Parameters
                .FirstOrDefault(p => string.Equals(p.Name, binding.PropertyName, StringComparison.Ordinal));
            if (targetParam is not null) {
                accumulated.AddRange(targetParam.Constraints);
            }

            CollectFromAction(targetAction, param, entity, accumulated, visited);
        }
    }

    private static void CollectFromAssign(
        AssignEffect ae,
        Property param,
        Entity entity,
        List<Constraint> accumulated) {

        if (!ExpressionReferencesParameter(ae.Value, param.Name)) return;

        if (ae.Target is PropertyAccess propAccess) {
            var targetProp = entity.Properties
                .FirstOrDefault(p => string.Equals(p.Name, propAccess.Name, StringComparison.Ordinal));
            if (targetProp is not null) {
                accumulated.AddRange(targetProp.Constraints);
            }
        }

        if (ae.Value is Add or Subtract) {
            var offset = GetExpressionOffset(ae.Value, param.Name);
            if (offset is not null && offset != 0 && ae.Target is PropertyAccess offsetTarget) {
                var targetProp = entity.Properties
                    .FirstOrDefault(p => string.Equals(p.Name, offsetTarget.Name, StringComparison.Ordinal));
                if (targetProp is not null) {
                    foreach (var constraint in targetProp.Constraints) {
                        if (constraint is RangeConstraint range) {
                            var adjusted = AdjustRangeRange(range, offset.Value);
                            if (adjusted is not null) {
                                accumulated.Add(adjusted);
                            }
                        }
                    }
                }
            }
        }
    }

    private static void CollectFromAction(
        Action target,
        Property param,
        Entity entity,
        List<Constraint> accumulated,
        HashSet<Effect> visited) {

        foreach (var effect in target.Effects) {
            if (!visited.Add(effect)) continue;

            switch (effect) {
                case AssignEffect ae:
                    CollectFromAssign(ae, param, entity, accumulated);
                    break;
                case InvokeActionEffect iae:
                    var nestedTarget = entity.Actions.FirstOrDefault(a =>
                        string.Equals(a.Name, iae.ActionName, StringComparison.Ordinal));
                    if (nestedTarget is not null) {
                        CollectFromAction(nestedTarget, param, entity, accumulated, visited);
                    }
                    break;
                case ConditionalEffect ce:
                    CollectDownstreamConstraints(ce.ThenEffects, param, entity, accumulated, visited);
                    if (ce.ElseEffects is not null) {
                        CollectDownstreamConstraints(ce.ElseEffects, param, entity, accumulated, visited);
                    }
                    break;
            }
        }
    }

    private static RangeConstraint? AdjustRangeRange(RangeConstraint original, long offset) {
        object? newMin = original.Minimum is not null && TryConvertToDouble(original.Minimum) is double minVal
            ? minVal - offset : null;
        object? newMax = original.Maximum is not null && TryConvertToDouble(original.Maximum) is double maxVal
            ? maxVal - offset : null;

        if (newMin is null && newMax is null) return null;
        if (newMin is double minD && newMax is double maxD && minD > maxD) return null;

        return new RangeConstraint(newMin, newMax);
    }

    private static double? TryConvertToDouble(object value) {
        try { return Convert.ToDouble(value); }
        catch { return null; }
    }

    private static bool ExpressionReferencesParameter(DomainExpression expr, string paramName) {
        switch (expr) {
            case ParameterAccess pa:
                return string.Equals(pa.Name, paramName, StringComparison.Ordinal);
            case Add add:
                return ExpressionReferencesParameter(add.Left, paramName)
                    || ExpressionReferencesParameter(add.Right, paramName);
            case Subtract sub:
                return ExpressionReferencesParameter(sub.Left, paramName)
                    || ExpressionReferencesParameter(sub.Right, paramName);
            case OwnedAccess oa:
                return ExpressionReferencesParameter(oa.Inner, paramName);
            default:
                return false;
        }
    }

    private static long? GetExpressionOffset(DomainExpression expr, string paramName) {
        switch (expr) {
            case Add add:
                if (IsParameterAccess(add.Left, paramName) && GetLiteralValue(add.Right) is long c1) return c1;
                if (IsParameterAccess(add.Right, paramName) && GetLiteralValue(add.Left) is long c2) return c2;
                return null;
            case Subtract sub:
                if (IsParameterAccess(sub.Left, paramName) && GetLiteralValue(sub.Right) is long c3) return -c3;
                return null;
            case ParameterAccess pa:
                return string.Equals(pa.Name, paramName, StringComparison.Ordinal) ? 0 : null;
            default:
                return null;
        }
    }

    private static bool IsParameterAccess(DomainExpression expr, string paramName) =>
        expr is ParameterAccess pa && string.Equals(pa.Name, paramName, StringComparison.Ordinal);

    private static long? GetLiteralValue(DomainExpression expr) {
        if (expr is Literal { Value: not null } lit) {
            try { return Convert.ToInt64(lit.Value); }
            catch { return null; }
        }
        return null;
    }
}