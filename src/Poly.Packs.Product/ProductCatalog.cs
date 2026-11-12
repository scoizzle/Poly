using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Libraries.Platform;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.Packs.Sqlite;
using Poly.Packs.SqlServer;

namespace Poly.Packs.Product;

/// <summary>
/// Core plus sqlite, sqlserver, http, and the posix platform header. <see cref="Catalog"/> has
/// the host-file contributors wired: persistence, sqlite and sqlserver emit the DbContext, http
/// emits Program.cs and demo.http. polyc and the DslCompiler facade resolve from it. The posix
/// package is surface-only until the Lower splice door carries a syscall body, and only a caller
/// that names a host target can load it.
/// </summary>
public static class ProductCatalog {
    public static ExtensionCatalog Catalog { get; } = Build(wired: true, hostVendor: null);

    /// <summary>
    /// The one implicit library set a document that lists no <c>uses</c> loads.
    /// </summary>
    public static IReadOnlyList<string> ImplicitIds => ExtensionCatalog.ProductAuthoring;

    /// <summary>
    /// The same libraries with no host-file contributors: the model and its C# types only. The
    /// MCP harness simulates and exports from this, so a workspace never emits a DbContext or a
    /// Program.cs it did not ask for.
    /// </summary>
    public static ExtensionCatalog Model { get; } = Build(wired: false, hostVendor: null);

    /// <summary>
    /// A fresh wired catalog. <paramref name="hostVendor"/> pins the vendor the Minimal API host
    /// emits (<c>sqlite</c>, <c>sqlserver</c>, anything else means the in-memory provider);
    /// null takes it from the vendor id the domain loads.
    /// </summary>
    public static ExtensionCatalog Create(string? hostVendor = null) => Build(wired: true, hostVendor);

    private static ExtensionCatalog Build(bool wired, string? hostVendor) => new ExtensionCatalog()
        .With(new TemporalLibrary())
        .With(new StorageFacetLibrary())
        .With(new PersistenceEmitLibrary(wired ? new DbContextArtifactContributor() : null))
        .With(new SqliteLibrary(wired ? new DbContextArtifactContributor() : null))
        .With(new SqlServerLibrary(wired ? new DbContextArtifactContributor() : null))
        .With(new HttpLibrary(wired ? new MinimalApiHostArtifactContributor(vendor: hostVendor) : null))
        .With(new PosixLibrary());
}