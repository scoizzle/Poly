using Poly.DomainModeling.Analysis;

namespace Poly.Packs.Sqlite;

/// <summary>
/// SQLite persistence library: vendor type-map overrides through the host load seam.
/// Id is <c>sqlite</c>.
/// When constructed with an <see cref="IArtifactContributor"/>, Register adds it
/// (Load-time, not bag invent).
/// </summary>
public sealed class SqliteLibrary : IDomainLibrary {
    private readonly IArtifactContributor? _contributor;

    public SqliteLibrary(IArtifactContributor? contributor = null) {
        _contributor = contributor;
    }

    public string Id => "sqlite";

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        SqliteDefaults.ApplyTypeMaps(builder.TypeMaps);
        builder.AddAnalyzer(new StoragePass(builder.TypeMaps, builder.StorageConventions));
        builder.AddAnalyzer(new PersistenceSurfacePass());
        if (_contributor is not null)
            builder.AddArtifactContributor(_contributor);
    }
}