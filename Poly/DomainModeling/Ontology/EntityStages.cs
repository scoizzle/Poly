namespace Poly.DomainModeling.Ontology;

/// <summary>
/// The one answer to "which stage does a new instance start in".
/// </summary>
/// <remarks>
/// A new instance starts in the entity's first declared stage. That is the stage whose
/// <c>entry</c> effects the exported constructor runs, the stage the generated
/// <c>Create</c> test asserts against, and the stage a stage-action merge treats as covered —
/// so it is answered here once instead of at each call site.
/// </remarks>
public static class EntityStages {
    /// <summary>
    /// The stage a new instance starts in: the first declared stage, never null when
    /// <see cref="Entity.Stages"/> is non-empty, and null when the entity declares no stages.
    /// </summary>
    public static Stage? InitialStage(this Entity entity) =>
        entity.Stages.Count == 0 ? null : entity.Stages[0];
}