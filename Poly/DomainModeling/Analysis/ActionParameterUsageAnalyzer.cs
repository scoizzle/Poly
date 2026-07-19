using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class ActionParameterUsageAnalyzer : INodeAnalyzer {
    public const string Id = "DomainActionParameterUsageAnalyzer";
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
        if (!context.TryBeginAnalyzerVisit<ActionParameterUsageAnalyzer>(domain)) {
            return;
        }

        foreach (var type in domain.Types) {
            if (type is Entity entity) {
                foreach (var action in entity.Actions) {
                    ValidateAction(context, action);
                }
            }
        }
    }

    private static void ValidateAction(AnalysisContext context, Action action) {
        if (action.Parameters.Count == 0) return;

        var paramNames = new HashSet<string>(
            action.Parameters.Select(p => p.Name),
            StringComparer.Ordinal);
        var usedParams = CollectParameterReferences(action.Effects, paramNames);
        var unused = action.Parameters
            .Where(p => !usedParams.Contains(p.Name))
            .ToArray();

        foreach (var param in unused) {
            context.ReportHint(
                param,
                $"Action parameter '{param.Name}' on '{action.Name}' is declared but never referenced by any effect expression.",
                DomainModelDiagnosticCodes.EffectUnusedParameter);
        }
    }

    private static HashSet<string> CollectParameterReferences(
        IReadOnlyList<Effect> effects,
        HashSet<string> paramNames) {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var effect in effects) {
            CollectFromEffect(effect, referenced, paramNames);
        }

        return referenced;
    }

    private static void CollectFromEffect(
        Effect effect,
        HashSet<string> referenced,
        HashSet<string> paramNames) {
        switch (effect) {
            case ConditionalEffect ce:
                CollectFromExpression(ce.Condition, referenced, paramNames);
                foreach (var e in ce.ThenEffects) CollectFromEffect(e, referenced, paramNames);
                if (ce.ElseEffects is not null)
                    foreach (var e in ce.ElseEffects) CollectFromEffect(e, referenced, paramNames);
                break;
            case CompositeEffect ce:
                foreach (var e in ce.Effects) CollectFromEffect(e, referenced, paramNames);
                break;
            case AssignEffect ae:
                CollectFromExpression(ae.Target, referenced, paramNames);
                CollectFromExpression(ae.Value, referenced, paramNames);
                break;
            case CreateEntityInstance cei:
                foreach (var init in cei.Initializers) {
                    CollectFromExpression(init.Expression, referenced, paramNames);
                }
                break;
            case InvokeActionEffect iae:
                foreach (var binding in iae.ParameterBindings) {
                    CollectFromExpression(binding.Expression, referenced, paramNames);
                }
                if (iae.Filter is not null)
                    CollectFromExpression(iae.Filter, referenced, paramNames);
                break;

            case StageTransitionEffect:
            case DeleteEntityInstance:
            case LinkRelationshipEffect:
            case UnlinkRelationshipEffect:
            case TransitionRelationshipEffect:
                break;
        }
    }

    private static void CollectFromExpression(
        DomainExpression expr,
        HashSet<string> referenced,
        HashSet<string> paramNames) {
        // DSL bare identifiers lower as PropertyAccess; IR authors may use ParameterAccess.
        // Both count when the name matches a declared action parameter.
        switch (expr) {
            case ParameterAccess pa when paramNames.Contains(pa.Name):
                referenced.Add(pa.Name);
                break;
            case PropertyAccess prop when paramNames.Contains(prop.Name):
                referenced.Add(prop.Name);
                break;
        }

        foreach (var child in expr.Children.OfType<DomainExpression>()) {
            CollectFromExpression(child, referenced, paramNames);
        }
    }
}