using Poly.DomainModeling;
using Poly.DomainModeling.Packs;

namespace Poly.Packs.SqlServer;

/// <summary>
/// SQL Server persistence library: type-map overrides and identifier-length convention.
/// </summary>
public sealed class SqlServerLibrary : IDomainLibrary {
    public string Id => "sqlserver";

    public void Register(HostSurfaces surfaces) {
        ArgumentNullException.ThrowIfNull(surfaces);
        SqlServerDefaults.ApplyTypeMaps(surfaces.TypeMaps);
        surfaces.AddStorageConvention(new SqlServerIdentifierConvention());
    }
}