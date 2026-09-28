namespace Poly.DomainModeling.Compile;

/// <summary>
/// One Lower-sentinel entry in the session's ArtifactCatalog.
/// Kind/Name/Source stay minimal — not an emit/contributor file inventory.
/// </summary>
public sealed record ArtifactDescriptor(string Kind, string Name, string Source);
