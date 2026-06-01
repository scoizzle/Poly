using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

namespace Poly.DomainModeling.Analysis;

internal sealed class ActionParameterUsageAnalyzer : INodeAnalyzer {
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

        var usedParams = CollectParameterReferences(action.Effects);
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

    private static HashSet<string> CollectParameterReferences(IReadOnlyList<Effect> effects) {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var effect in effects) {
            CollectFromEffect(effect, referenced);
        }

        return referenced;
    }

    private static void CollectFromEffect(Effect effect, HashSet<string> referenced) {
        switch (effect) {
            case ConditionalEffect ce:
                CollectFromExpression(ce.Condition, referenced);
                foreach (var e in ce.ThenEffects) CollectFromEffect(e, referenced);
                if (ce.ElseEffects is not null)
                    foreach (var e in ce.ElseEffects) CollectFromEffect(e, referenced);
                break;
            case CompositeEffect ce:
                foreach (var e in ce.Effects) CollectFromEffect(e, referenced);
                break;
            case AssignEffect ae:
                CollectFromExpression(ae.Target, referenced);
                CollectFromExpression(ae.Value, referenced);
                break;
            case CreateEntityInstance cei:
                foreach (var init in cei.Initializers) {
                    CollectFromExpression(init.Expression, referenced);
                }
                break;
            case InvokeActionEffect iae:
                foreach (var binding in iae.ParameterBindings) {
                    CollectFromExpression(binding.Expression, referenced);
                }
                break;
            case PublishEventEffect pee:
                foreach (var binding in pee.PropertyBindings) {
                    CollectFromExpression(binding.Expression, referenced);
                }
                break;
            case StageTransitionEffect:
            case DeleteEntityInstance:
            case LinkRelationshipEffect:
            case UnlinkRelationshipEffect:
            case TransitionRelationshipEffect:
                break;
        }
    }

    private static void CollectFromExpression(DomainExpression expr, HashSet<string> referenced) {
        if (expr is ParameterAccess pa) {
            referenced.Add(pa.Name);
        }

        foreach (var child in expr.Children.OfType<DomainExpression>()) {
            CollectFromExpression(child, referenced);
        }
    }
}