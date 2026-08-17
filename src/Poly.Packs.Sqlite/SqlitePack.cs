using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Libraries.Storage;

namespace Poly.Packs.Sqlite;

/// <summary>
/// SQLite persistence library: vendor type-map overrides through the host load seam.
/// Id is <c>sqlite</c>.
/// </summary>
public sealed class SqliteLibrary : IDomainLibrary {
    public string Id => "sqlite";

    public void Register(SessionBuilder builder) =>
        SqliteDefaults.ApplyTypeMaps(builder.TypeMaps);
}