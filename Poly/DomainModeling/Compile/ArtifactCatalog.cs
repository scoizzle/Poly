namespace Poly.DomainModeling.Compile;

/// <summary>
/// The artifacts one compile produced, keyed by <see cref="ArtifactId"/>. A compile
/// builds a new catalog; artifacts can be added but never removed or replaced.
/// Every artifact type is declared first, together with the types it may point at.
/// </summary>
public sealed class ArtifactCatalog {
    private readonly List<Artifact> _artifacts = [];
    private readonly Dictionary<ArtifactId, Artifact> _byId = [];
    private readonly Dictionary<string, HashSet<string>> _allowedTargets = [];

    /// <summary>The artifacts in the order they were registered.</summary>
    public IReadOnlyList<Artifact> Artifacts => _artifacts.AsReadOnly();

    /// <summary>
    /// Declares an artifact type and the types its artifacts may point at. Call it once, before
    /// registering artifacts of that type; which producer calls it is a convention, the catalog does
    /// not check. Type names follow the same rule as in <see cref="ArtifactId"/>, and <paramref name="mayPointAt"/>
    /// is copied. A type in <paramref name="mayPointAt"/> need not be declared (another producer may
    /// declare it later); if it never is, a reference to it can only show up as dangling. Throws
    /// <see cref="InvalidOperationException"/> when the type is already declared and
    /// <see cref="FormatException"/> for a malformed type name.
    /// </summary>
    public void DeclareType(string type, IEnumerable<string> mayPointAt) {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(mayPointAt);
        var allowed = mayPointAt.ToHashSet(StringComparer.Ordinal);
        ArtifactId.RequireValid(type, "type");
        foreach (var target in allowed)
            ArtifactId.RequireValid(target, "type");
        if (!_allowedTargets.TryAdd(type, allowed))
            throw new InvalidOperationException($"Artifact type '{type}' is already declared.");
    }

    /// <summary>
    /// Adds an artifact. Throws <see cref="InvalidOperationException"/> when its id is already
    /// registered, its type is not declared, or it points at a type its type may not point at.
    /// Whether the targets exist is checked later, by <see cref="FindDanglingOrWrongType"/>.
    /// </summary>
    public void Register(Artifact artifact) {
        ArgumentNullException.ThrowIfNull(artifact);
        var id = artifact.Descriptor.Id;
        if (!_allowedTargets.TryGetValue(id.Type, out var allowed))
            throw new InvalidOperationException($"Artifact '{id}' has type '{id.Type}', which is not declared.");
        foreach (var target in artifact.Descriptor.References)
            if (!allowed.Contains(target.Type))
                throw new InvalidOperationException(
                    $"Artifact '{id}' points at '{target}', but type '{id.Type}' may not point at type '{target.Type}'.");
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

    /// <summary>
    /// Every reference whose target is not registered with the expected type: no artifact has the
    /// target's name path (dangling), or artifacts have it but none with the expected type (wrong type).
    /// Paths are compared exactly, case included. Sorted ordinally by referrer id, then target id, so the
    /// list does not depend on registration order; a reference listed twice is reported twice.
    /// </summary>
    public IReadOnlyList<ArtifactReferenceProblem> FindDanglingOrWrongType() {
        var problems = new List<ArtifactReferenceProblem>();
        foreach (var artifact in _artifacts)
            foreach (var target in artifact.Descriptor.References) {
                if (_byId.ContainsKey(target))
                    continue;
                var kind = _artifacts.Any(a => a.Descriptor.Id.Path == target.Path)
                    ? ArtifactReferenceProblemKind.WrongType
                    : ArtifactReferenceProblemKind.Dangling;
                problems.Add(new ArtifactReferenceProblem(artifact.Descriptor.Id, target, kind));
            }
        return problems
            .OrderBy(p => p.From.ToString(), StringComparer.Ordinal)
            .ThenBy(p => p.Target.ToString(), StringComparer.Ordinal)
            .ToArray()
            .AsReadOnly();
    }
}

/// <summary>A reference <see cref="ArtifactCatalog.FindDanglingOrWrongType"/> could not resolve.</summary>
public sealed record ArtifactReferenceProblem(ArtifactId From, ArtifactId Target, ArtifactReferenceProblemKind Kind);

public enum ArtifactReferenceProblemKind {
    /// <summary>No artifact has the target's name path.</summary>
    Dangling,

    /// <summary>Artifacts have the target's name path, but none with the expected type.</summary>
    WrongType,
}
