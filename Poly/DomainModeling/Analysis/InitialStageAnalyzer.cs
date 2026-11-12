using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// #675: an entity's <c>initial(Stage)</c> designation must name one of its own stages. The name is
/// metadata on <see cref="Entity.InitialStageName"/> and <see cref="EntityStages.InitialStage"/> falls
/// back to the first declared stage, so every create path still works; this pass is what makes a wrong
/// designation visible, and it is what the accessor's contract points at when it promises an error.
/// The finding is placed at the entity declaration, the DMSTR007 precedent.
/// </summary>
internal sealed class InitialStageAnalyzer : INodeAnalyzer {
    public const string Id = "DomainInitialStageDesignation";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        if (node is Entity entity)
            Report(context, entity);

        this.AnalyzeChildren(context, node);
    }

    private static void Report(AnalysisContext context, Entity entity) {
        if (entity.InitialStageName is not { } name)
            return;
        if (entity.Stages.Any(s => string.Equals(s.Name, name, StringComparison.Ordinal)))
            return;

        var kind = entity.Properties.Any(p => string.Equals(p.Name, name, StringComparison.Ordinal)) ? "property"
            : entity.Navigations.Any(n => string.Equals(n.Name, name, StringComparison.Ordinal)) ? "navigation"
            : null;
        var message = kind is null
            ? $"Entity '{entity.Name}' designates '{name}' as its initial stage, but declares no stage of that name."
            : $"Entity '{entity.Name}' designates '{name}' as its initial stage, but '{name}' is a {kind}, not a stage.";
        context.ReportError(entity, message, DomainModelDiagnosticCodes.InvalidInitialStageDesignation);
    }
}
