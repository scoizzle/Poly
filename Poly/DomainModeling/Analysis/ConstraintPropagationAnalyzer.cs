using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

internal sealed record DownstreamConstraintsMetadata(IReadOnlyList<Constraint> Constraints) : IAnalysisMetadata;

/// <summary>
/// Publishes <see cref="DownstreamConstraintsMetadata"/> from the effect tree.
/// No upstream analysis bags required.
/// </summary>
internal sealed class ConstraintPropagationAnalyzer : INodeAnalyzer {
    public const string Id = "DomainConstraintPropagationAnalyzer";
    public string PassName => Id;
    public string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) {

        if (node is Domain domain) {
            ValidateDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomain(AnalysisContext context, Domain domain) {
        DomainAnalysis.ForEachEntity(domain, entity =>
            DomainAnalysis.ForEachAction(entity, action => AnalyzeAction(context, action, entity)));
    }

    private static void AnalyzeAction(AnalysisContext context, Action action, Entity entity) {
        var currentStage = DomainAnalysis.StageNameOf(entity, action);
        foreach (var param in action.Parameters) {
            var visited = new HashSet<Effect>(ReferenceEqualityComparer.Instance);
            var constraints = new List<Constraint>();
            CollectDownstreamConstraints(action.Effects, param, entity, constraints, visited, currentStage);
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
        HashSet<Effect> visited,
        string? currentStage) {

        foreach (var effect in effects) {
            if (!visited.Add(effect)) continue;

            switch (effect) {
                case InvokeActionEffect iae:
                    CollectFromInvokeAction(iae, param, entity, accumulated, visited, currentStage);
                    break;
                case ConditionalEffect ce:
                    CollectDownstreamConstraints(ce.ThenEffects, param, entity, accumulated, visited, currentStage);
                    if (ce.ElseEffects is not null) {
                        CollectDownstreamConstraints(ce.ElseEffects, param, entity, accumulated, visited, currentStage);
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
        HashSet<Effect> visited,
        string? currentStage) {

        var targetAction = DomainAnalysis.FindAction(entity, iae.ActionName, currentStage);
        if (targetAction is null) return;
        var nestedStage = DomainAnalysis.StageNameOf(entity, targetAction) ?? currentStage;

        foreach (var binding in iae.ParameterBindings) {
            if (!ExpressionReferencesParameter(binding.Expression, param.Name)) continue;

            var targetParam = targetAction.Parameters
                .FirstOrDefault(p => string.Equals(p.Name, binding.PropertyName, StringComparison.Ordinal));
            if (targetParam is not null) {
                accumulated.AddRange(targetParam.Constraints);
            }

            CollectFromAction(targetAction, param, entity, accumulated, visited, nestedStage);
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
        HashSet<Effect> visited,
        string? currentStage) {

        foreach (var effect in target.Effects) {
            if (!visited.Add(effect)) continue;

            switch (effect) {
                case AssignEffect ae:
                    CollectFromAssign(ae, param, entity, accumulated);
                    break;
                case InvokeActionEffect iae:
                    var nestedTarget = DomainAnalysis.FindAction(entity, iae.ActionName, currentStage);
                    if (nestedTarget is not null) {
                        var nestedStage = DomainAnalysis.StageNameOf(entity, nestedTarget) ?? currentStage;
                        CollectFromAction(nestedTarget, param, entity, accumulated, visited, nestedStage);
                    }
                    break;
                case ConditionalEffect ce:
                    CollectDownstreamConstraints(ce.ThenEffects, param, entity, accumulated, visited, currentStage);
                    if (ce.ElseEffects is not null) {
                        CollectDownstreamConstraints(ce.ElseEffects, param, entity, accumulated, visited, currentStage);
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
        if (IsParameterAccess(expr, paramName))
            return true;
        foreach (var child in expr.Children.OfType<DomainExpression>()) {
            if (ExpressionReferencesParameter(child, paramName))
                return true;
        }
        return false;
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
            case ParameterAccess or PropertyAccess:
                return IsParameterAccess(expr, paramName) ? 0 : null;
            default:
                return null;
        }
    }

    private static bool IsParameterAccess(DomainExpression expr, string paramName) =>
        expr switch {
            ParameterAccess pa => string.Equals(pa.Name, paramName, StringComparison.Ordinal),
            PropertyAccess pa => string.Equals(pa.Name, paramName, StringComparison.Ordinal),
            _ => false,
        };

    private static long? GetLiteralValue(DomainExpression expr) {
        if (expr is Literal { Value: not null } lit) {
            try { return Convert.ToInt64(lit.Value); }
            catch { return null; }
        }
        return null;
    }
}