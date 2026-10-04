namespace Poly.DomainModeling.Compile;

/// <summary>
/// Describes one compiled artifact: its id (name path plus type), the producer that
/// registered it (for example <c>Lower</c>), and the artifacts it points at. A
/// reference is an <see cref="ArtifactId"/>: the name path of the target and the
/// type the referrer expects it to have. The descriptor keeps its own copy of the
/// references, and two descriptors are equal when their references are equal in order.
/// </summary>
public sealed record ArtifactDescriptor(ArtifactId Id, string Producer, IReadOnlyList<ArtifactId> References) {
    public ArtifactDescriptor(ArtifactId id, string producer) : this(id, producer, []) { }

    /// <summary>The artifacts this one points at. Cannot be changed.</summary>
    public IReadOnlyList<ArtifactId> References { get; } = CopyOf(References);

    public bool Equals(ArtifactDescriptor? other) =>
        other is not null && Id == other.Id && Producer == other.Producer && References.SequenceEqual(other.References);

    public override int GetHashCode() =>
        References.Aggregate(HashCode.Combine(Id, Producer), HashCode.Combine);

    private static IReadOnlyList<ArtifactId> CopyOf(IReadOnlyList<ArtifactId> references) {
        ArgumentNullException.ThrowIfNull(references);
        if (references.Any(r => r is null))
            throw new ArgumentException("References must not contain null.", nameof(references));
        return references.ToArray().AsReadOnly();
    }
}
