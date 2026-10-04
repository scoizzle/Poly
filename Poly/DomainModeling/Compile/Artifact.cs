namespace Poly.DomainModeling.Compile;

/// <summary>A descriptor plus what the producer made (a tree, text, and so on).</summary>
public sealed record Artifact(ArtifactDescriptor Descriptor, object? Payload);
