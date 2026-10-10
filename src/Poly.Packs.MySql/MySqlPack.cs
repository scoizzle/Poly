namespace Poly.Packs.MySql;

/// <summary>
/// MySQL persistence library: vendor type maps via the same host load seam.
/// When constructed with an <see cref="IArtifactContributor"/>, Register adds it
/// (Load-time, not bag invent).
/// </summary>
public sealed class MySqlLibrary : IDomainLibrary {
    private readonly IArtifactContributor? _contributor;

    public MySqlLibrary(IArtifactContributor? contributor = null) {
        _contributor = contributor;
    }

    public string Id => "mysql";

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        MySqlDefaults.ApplyTypeMaps(builder.TypeMaps);
        builder.AddAnalyzer(new StoragePass(builder.TypeMaps, builder.StorageConventions));
        builder.AddAnalyzer(new PersistenceSurfacePass());
        if (_contributor is not null)
            builder.AddArtifactContributor(_contributor);
    }
}