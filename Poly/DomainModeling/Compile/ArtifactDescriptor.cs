namespace Poly.DomainModeling.Compile;

/// <summary>
/// One entry in the session's post-Lower artifact-set catalog.
/// Kind/Name/Source stay minimal and honest — not a second pipeline.
/// </summary>
public sealed record ArtifactDescriptor(string Kind, string Name, string Source);
