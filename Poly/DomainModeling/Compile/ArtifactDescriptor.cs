namespace Poly.DomainModeling.Compile;

/// <summary>
/// Describes one compiled artifact: its id (name path plus type), the producer that
/// registered it (for example <c>Lower</c>), and the artifacts it points at. A
/// reference is an <see cref="ArtifactId"/>: the name path of the target and the
/// type the referrer expects it to have.
/// </summary>
public sealed record ArtifactDescriptor(ArtifactId Id, string Producer, IReadOnlyList<ArtifactId> References) {
    public ArtifactDescriptor(ArtifactId id, string producer) : this(id, producer, []) { }
}
