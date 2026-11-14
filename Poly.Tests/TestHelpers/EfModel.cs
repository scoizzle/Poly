using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Poly.Compile;
using Poly.Packs.Product;
using Poly.Tests.DomainModeling.Lowering;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// The generated DbContext of a fixture, built in-process: polyc's own compile over the product catalog,
/// Roslyn-emitted into a collectible load context, instantiated over an open in-memory SQLite connection.
/// The EF model (<see cref="Model"/>) is the authority for what the generator declared; the connection
/// carries the schema EF creates from it (<see cref="Create"/> runs <c>EnsureCreated</c>), so a test can also
/// read the real SQLite schema and try raw SQL against it.
/// </summary>
public sealed class EfModel : IDisposable {
    private static int _sequence;
    private readonly AssemblyLoadContext _context;
    private bool _created;

    private EfModel(DbContext db, SqliteConnection connection, Assembly assembly, AssemblyLoadContext context) {
        Db = db;
        Connection = connection;
        Assembly = assembly;
        _context = context;
    }

    public DbContext Db { get; }

    public SqliteConnection Connection { get; }

    public Assembly Assembly { get; }

    public IModel Model => Db.Model;

    /// <summary>Null when EF accepts the model; otherwise EF's own message (the generated mapping is invalid).</summary>
    public string? BuildError {
        get {
            try {
                _ = Db.Model;
                return null;
            }
            catch (InvalidOperationException ex) {
                return ex.Message;
            }
        }
    }

    /// <summary>Creates the schema EF derives from the model in the open connection (once).</summary>
    public EfModel EnsureSchema() {
        if (!_created) {
            Db.Database.EnsureCreated();
            _created = true;
        }
        return this;
    }

    /// <summary>The entity type of the generated CLR type named <paramref name="entity"/> (any namespace).</summary>
    public IEntityType Entity(string entity) =>
        Model.GetEntityTypes().Single(e => e.ClrType.Name == entity);

    /// <summary>The one foreign key held by <paramref name="dependent"/> that references <paramref name="principal"/>.</summary>
    public IForeignKey Fk(string dependent, string principal) =>
        Entity(dependent).GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType.Name == principal);

    public static EfModel Create(string poly, string name = "Probe") {
        var result = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring))
            .Compile(poly, name + ".poly");
        if (!result.Succeeded) {
            throw new InvalidOperationException(
                "polyc refused the fixture: " + string.Join("; ", result.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        }

        var trees = result.Files
            // The host (Program.cs) needs the ASP.NET Core shared framework, which the test process does not load;
            // the entities and the DbContext are all the EF model needs.
            .Where(f => f.FileName.EndsWith(".cs", StringComparison.Ordinal) && f.FileName != "Program.cs")
            .Select(f => CSharpSyntaxTree.ParseText(f.Source, path: f.FileName))
            .ToList();
        trees.Add(CSharpSyntaxTree.ParseText(ImplicitUsings, path: "ImplicitUsings.cs"));
        var assemblyName = "EfModel" + Interlocked.Increment(ref _sequence);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            DslCompilerCompileOracleTests.GatherReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        if (!emitted.Success) {
            throw new InvalidOperationException("The generated host did not compile:\n" + string.Join(
                "\n", emitted.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Take(20)));
        }

        stream.Position = 0;
        var context = new AssemblyLoadContext(assemblyName, isCollectible: true);
        var assembly = context.LoadFromStream(stream);
        var contextType = assembly.GetTypes().Single(t => !t.IsAbstract && typeof(DbContext).IsAssignableFrom(t));

        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var optionsType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(optionsType)!;
        builder.UseSqlite(connection);
        var db = (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
        return new EfModel(db, connection, assembly, context);
    }

    public void Dispose() {
        Db.Dispose();
        Connection.Dispose();
        _context.Unload();
    }

    private const string ImplicitUsings = """
        global using System;
        global using System.Linq;
        global using System.Threading.Tasks;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Net.Http;
        global using Microsoft.AspNetCore.Builder;
        global using Microsoft.AspNetCore.Hosting;
        global using Microsoft.AspNetCore.Http;
        global using Microsoft.AspNetCore.Routing;
        global using Microsoft.Extensions.Configuration;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Hosting;
        global using Microsoft.Extensions.Logging;
        """;
}
