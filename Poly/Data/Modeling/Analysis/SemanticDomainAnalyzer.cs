namespace Poly.Data.Modeling;

internal sealed class SemanticDomainAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node)) {
            return;
        }

        switch (node) {
            case Domain request:
                AnalyzeDomain(context, request.Domain);
                break;
            case Entity entity:
                AnalyzeEntitySemantics(context, entity);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        foreach (var entity in domain.Types.OfType<Entity>().Where(context.ShouldAnalyze)) {
            AnalyzeEntitySemantics(context, entity);
        }
    }

    private static void AnalyzeEntitySemantics(AnalysisContext context, Entity entity) {
        ValidateStageInheritance(context, entity);
        ValidateStageActionVisibility(context, entity);
        ValidateTypeCompatibility(context, entity);
    }

    private static void ValidateStageInheritance(AnalysisContext context, Entity entity) {
        if (entity.ParentEntity is null || entity.ParentEntity.Stages.Count == 0) {
            return;
        }

        foreach (var stage in entity.Stages) {
            if (stage.Parent is null) {
                context.ReportError(
                    stage,
                    $"Stage '{stage.Name}' on child entity '{entity.Name}' must have a parent stage when parent entity '{entity.ParentEntity.Name}' defines stages.",
                    DomainModelDiagnosticCodes.SemanticStageInheritance);
                continue;
            }

            if (!entity.ParentEntity.Stages.Contains(stage.Parent)) {
                context.ReportError(
                    stage,
                    $"Stage '{stage.Name}' on child entity '{entity.Name}' must directly inherit from a stage defined on parent entity '{entity.ParentEntity.Name}'.",
                    DomainModelDiagnosticCodes.SemanticStageInheritance);
            }
        }
    }

    private static void ValidateStageActionVisibility(AnalysisContext context, Entity entity) {
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions) {
                if (!ReferenceEquals(action.Entity, entity)) {
                    context.ReportError(
                        action,
                        $"Action '{action.Name}' on stage '{stage.Name}' must belong to entity '{entity.Name}'.",
                        DomainModelDiagnosticCodes.SemanticActionVisibility);
                }
            }
        }
    }

    private static void ValidateTypeCompatibility(AnalysisContext context, Entity entity) {
        foreach (var property in entity.Properties) {
            if (!ReferenceEquals(property.Type.Domain, entity.Domain)) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' uses type '{property.Type.Name}' from a different domain.",
                    DomainModelDiagnosticCodes.SemanticTypeCompatibility);
            }
        }

        foreach (var action in entity.Actions) {
            foreach (var parameter in action.Parameters.OfType<Property>()) {
                if (!ReferenceEquals(parameter.Type.Domain, entity.Domain)) {
                    context.ReportError(
                        parameter,
                        $"Action '{action.Name}' parameter '{parameter.Name}' uses a type from a different domain.",
                        DomainModelDiagnosticCodes.SemanticTypeCompatibility);
                }
            }
        }
    }
}