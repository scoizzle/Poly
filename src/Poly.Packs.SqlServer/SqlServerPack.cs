namespace Poly.Packs.SqlServer;

/// <summary>
/// SQL Server persistence library: type-map overrides and identifier-length convention.
/// When constructed with an <see cref="IArtifactContributor"/>, Register adds it
/// (Load-time, not bag invent).
/// </summary>
public sealed class SqlServerLibrary : IDomainLibrary {
    private readonly IArtifactContributor? _contributor;

    public SqlServerLibrary(IArtifactContributor? contributor = null) {
        _contributor = contributor;
    }

    public string Id => "sqlserver";

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        SqlServerDefaults.ApplyTypeMaps(builder.TypeMaps);
        builder.AddStorageConvention(new SqlServerIdentifierConvention());
        builder.AddAnalyzer(new StoragePass(builder.TypeMaps, builder.StorageConventions));
        builder.AddAnalyzer(new PersistenceSurfacePass());
        if (_contributor is not null)
            builder.AddArtifactContributor(_contributor);
    }
}