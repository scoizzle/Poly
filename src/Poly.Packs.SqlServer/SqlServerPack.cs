using Poly.DomainModeling;
using Poly.DomainModeling.Packs;

namespace Poly.Packs.SqlServer;

/// <summary>
/// SQL Server persistence library: type-map overrides and identifier-length convention.
/// </summary>
public sealed class SqlServerLibrary : IDomainLibrary {
    public string Id => "sqlserver";

    public void Register(DomainHostBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        SqlServerDefaults.ApplyTypeMaps(builder.TypeMaps);
        builder.AddStorageConvention(new SqlServerIdentifierConvention());
    }
}