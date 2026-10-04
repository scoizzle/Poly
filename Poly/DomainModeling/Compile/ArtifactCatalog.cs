namespace Poly.DomainModeling.Compile;

/// <summary>
/// The artifacts one compile produced, keyed by <see cref="ArtifactId"/>. A compile
/// builds a new catalog; artifacts can be added but never removed or replaced.
/// </summary>
public sealed class ArtifactCatalog {
    private readonly List<Artifact> _artifacts = [];
    private readonly Dictionary<ArtifactId, Artifact> _byId = [];

    /// <summary>The artifacts in the order they were registered.</summary>
    public IReadOnlyList<Artifact> Artifacts => _artifacts.AsReadOnly();

    /// <summary>Adds an artifact. Throws <see cref="InvalidOperationException"/> when its id is already registered.</summary>
    public void Register(Artifact artifact) {
        ArgumentNullException.ThrowIfNull(artifact);
        var id = artifact.Descriptor.Id;
        if (!_byId.TryAdd(id, artifact))
            throw new InvalidOperationException($"Artifact '{id}' is already registered.");
        _artifacts.Add(artifact);
    }

    /// <summary>The artifact with this id, or null when none is registered.</summary>
    public Artifact? Find(ArtifactId id) => _byId.GetValueOrDefault(id);

    /// <summary>
    /// One line per artifact, <c>Type|Path|Producer</c>, sorted ordinally by id
    /// (<c>Path#Type</c>) so the text does not depend on registration order. No trailing newline.
    /// </summary>
    public string ToText() => string.Join("\n", _artifacts
        .Select(a => a.Descriptor)
        .OrderBy(d => d.Id.ToString(), StringComparer.Ordinal)
        .Select(d => $"{d.Id.Type}|{d.Id.Path}|{d.Producer}"));
}
