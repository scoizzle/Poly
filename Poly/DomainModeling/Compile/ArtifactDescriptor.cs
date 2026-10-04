namespace Poly.DomainModeling.Compile;

/// <summary>
/// Describes one compiled artifact: its id (name path plus type) and the
/// producer that registered it, for example <c>Lower</c>.
/// </summary>
public sealed record ArtifactDescriptor(ArtifactId Id, string Producer);
