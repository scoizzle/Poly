namespace Poly.DomainModeling.Compile;

/// <summary>One library's entry into one <see cref="PipelineStage"/>.</summary>
public sealed record StageContribution(PipelineStage Stage, string LibraryId);
