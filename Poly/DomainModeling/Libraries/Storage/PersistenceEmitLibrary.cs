using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;

namespace Poly.DomainModeling.Libraries.Storage;

/// <summary>
/// Generic persistence emit flag (<c>uses persistence</c>) plus storage mapping.
/// Vendor libraries (<c>sqlite</c>, …) register the same overlay; do not load both.
/// When constructed with an <see cref="IArtifactContributor"/>, Register adds it
/// (Load-time, not bag invent).
/// </summary>
public sealed class PersistenceEmitLibrary : IDomainLibrary {
    private readonly IArtifactContributor? _contributor;

    public PersistenceEmitLibrary(IArtifactContributor? contributor = null) {
        _contributor = contributor;
    }

    /// <summary>The catalog id of the generic storage library, the one a vendor library replaces.</summary>
    public const string LibraryId = "persistence";

    public string Id => LibraryId;

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddAnalyzer(new StoragePass(builder.TypeMaps, builder.StorageConventions));
        builder.AddAnalyzer(new PersistenceSurfacePass());
        builder.AddConstraintCompiler(new UniqueConstraintCompiler(Id));
        if (_contributor is not null)
            builder.AddArtifactContributor(_contributor);
    }
}