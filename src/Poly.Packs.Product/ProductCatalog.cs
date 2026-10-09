using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Libraries.Http;
using Poly.Packs.Sqlite;
using Poly.Packs.SqlServer;

namespace Poly.Packs.Product;

/// <summary>
/// Core plus sqlite, sqlserver, and http. DslCompiler and MCP both resolve from here.
/// </summary>
public static class ProductCatalog {
    public static ExtensionCatalog Catalog { get; } = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new SqlServerLibrary())
        .With(new HttpLibrary());
}
