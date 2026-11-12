namespace Poly.DomainModeling.Ontology;

/// <summary>
/// The one answer to "which stage does a new instance start in".
/// </summary>
/// <remarks>
/// A new instance starts in the entity's initial stage: the stage its <c>initial(Stage)</c> header
/// clause designates, or the first declared stage when no stage is designated. That is the stage whose
/// <c>entry</c> effects the exported constructor runs, the stage the generated
/// <c>Create</c> test asserts against, and the stage a stage-action merge treats as covered —
/// so it is answered here once instead of at each call site.
/// </remarks>
internal static class EntityStages {
    /// <summary>
    /// The stage a new instance starts in: the designated initial stage, else the first declared
    /// stage, never null when <see cref="Entity.Stages"/> is non-empty, and null when the entity
    /// declares no stages. When the designation names no stage, this falls back to the first declared
    /// stage; analysis reports the error.
    /// </summary>
    public static Stage? InitialStage(this Entity entity) {
        if (entity.Stages.Count == 0)
            return null;
        if (entity.InitialStageName is { } name) {
            foreach (var stage in entity.Stages)
                if (string.Equals(stage.Name, name, StringComparison.Ordinal))
                    return stage;
        }
        return entity.Stages[0];
    }
}