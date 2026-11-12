using System.Reflection;
using System.Text.RegularExpressions;

using Poly.Compile;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;
using Poly.DslCompiler;
using Poly.Packs.Product;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.Polyc;

/// <summary>
/// C-04: <c>DslCompiler</c> and polyc pick libraries through the one
/// <see cref="LibrarySelection"/> path. The facade owns no selection rule of its own.
/// </summary>
public sealed class DslCompilerSelectionTests {
    private const string Entity = "A: entity {\n  X: Text unique\n}\n";

    private static string Doc(params string[] uses) =>
        "domain D\n" + string.Concat(uses.Select(id => $"uses {id}\n")) + Entity;

    private static string VendorId(DbmsPack pack) => pack switch {
        DbmsPack.Sqlite => "sqlite",
        DbmsPack.SqlServer => "sqlserver",
        _ => "persistence",
    };

    private static SessionOptions DbOptions(ExtensionCatalog catalog, DbmsPack pack) =>
        new(catalog, ProductCatalog.ImplicitIds) { StorageVendor = VendorId(pack) };

    private static string DbContextOf(Compiler.CompileResult result) =>
        result.Files!.Single(f => f.FileName.EndsWith("DbContext.cs", StringComparison.Ordinal)).Source;

    /// <summary>
    /// C-04 row test: a Db-mode document that lists a vendor loads exactly that vendor. Master
    /// seeds <c>sqlite</c> on top, so two storage compilers load and the compile is refused.
    /// </summary>
    [Test]
    public async Task DbMode_DocumentListingAVendor_AddsNoSecondStorageLibrary() {
        var result = new Compiler().Compile(Doc("sqlserver"), CompileMode.Db, DbmsPack.Sqlite);

        await Assert.That(result.Success).IsTrue().Because(string.Join("; ", result.Errors ?? []));
        await Assert.That(DbContextOf(result)).Contains("nvarchar(max)");
    }

    /// <summary>
    /// C-04 row test (D2): the facade selects what polyc's session path selects. The oracle is an
    /// independent compile of the document with the ids written out and no facade rule.
    /// </summary>
    [Test]
    [Arguments(DbmsPack.Sqlite, new string[0])]
    [Arguments(DbmsPack.Sqlite, new[] { "temporal", "persistence" })]
    [Arguments(DbmsPack.Sqlite, new[] { "http" })]
    [Arguments(DbmsPack.Generic, new string[0])]
    [Arguments(DbmsPack.Generic, new[] { "temporal", "persistence" })]
    [Arguments(DbmsPack.Generic, new[] { "http" })]
    public async Task DbMode_SelectsSameLibrariesAsPolyc(DbmsPack pack, string[] uses) {
        var vendor = VendorId(pack);
        var catalog = ProductCatalog.Create(pack == DbmsPack.Generic ? "generic" : vendor);
        var viaFacade = new Compiler().Compile(Doc(uses), CompileMode.Db, pack);

        var expectedIds = LibrarySelection.Resolve(
            DslSyntaxTree.Parse(Doc(uses)).Uses, DbOptions(catalog, pack));
        var oracle = new PolyCompiler(catalog, SessionOptions.Bare(catalog))
            .Compile(Doc(expectedIds.ToArray()), "oracle.poly");

        await Assert.That(viaFacade.Success).IsTrue().Because(string.Join("; ", viaFacade.Errors ?? []));
        await Assert.That(oracle.Succeeded).IsTrue();
        await Assert.That(Sorted(viaFacade.Files!)).IsEquivalentTo(Sorted(oracle.Files));
    }

    /// <summary>A4: the ids stamped on the domain are the ids the session loaded.</summary>
    [Test]
    [Arguments(DbmsPack.Sqlite, new string[0], new[] { "storage", "sqlite" })]
    [Arguments(DbmsPack.Sqlite, new[] { "persistence" }, new[] { "sqlite" })]
    [Arguments(DbmsPack.Sqlite, new[] { "http" }, new[] { "http", "sqlite" })]
    [Arguments(DbmsPack.Generic, new string[0], new[] { "storage", "persistence" })]
    public async Task DomainExtensions_CarryTheResolvedIds(DbmsPack pack, string[] uses, string[] expected) {
        var catalog = ProductCatalog.Create(VendorId(pack));
        var compiled = new PolyCompiler(catalog, DbOptions(catalog, pack)).Compile(Doc(uses));

        await Assert.That(compiled.Succeeded).IsTrue();
        await Assert.That(compiled.Session!.Extensions.SequenceEqual(expected)).IsTrue()
            .Because(string.Join(",", compiled.Session.Extensions));
        await Assert.That(compiled.Domain!.Extensions.SequenceEqual(compiled.Session.Extensions)).IsTrue()
            .Because(string.Join(",", compiled.Domain.Extensions));
    }

    /// <summary>A4: host ids land on the domain as well as the session.</summary>
    [Test]
    public async Task DomainExtensions_CarryHostIds_WhenDocumentOmitsUses() {
        var catalog = ProductCatalog.Create("generic");
        var options = new SessionOptions(catalog, ProductCatalog.ImplicitIds) {
            StorageVendor = "persistence",
            HostIds = ["http"],
        };
        var compiled = new PolyCompiler(catalog, options).Compile(Doc());

        await Assert.That(compiled.Succeeded).IsTrue();
        await Assert.That(compiled.Session!.Extensions).Contains("http");
        await Assert.That(compiled.Domain!.Extensions.SequenceEqual(compiled.Session.Extensions)).IsTrue();
    }

    /// <summary>A3: an unknown id is a positioned DSL0002 diagnostic, never an exception.</summary>
    [Test]
    public async Task UnknownUses_WithAStorageVendor_IsADiagnosticNotAnException() {
        var catalog = ProductCatalog.Create("sqlite");
        var result = new PolyCompiler(catalog, DbOptions(catalog, DbmsPack.Sqlite)).Compile(Doc("nope"), "x.poly");

        var diagnostic = result.Diagnostics.Single(d => d.Code == DiagnosticCodes.UnknownExtension);
        await Assert.That(diagnostic.Message).Contains("nope");
        await Assert.That(diagnostic.Placed).IsTrue();
    }

    /// <summary>The seed and hook overloads are gone; the catalog and the options are the whole input.</summary>
    [Test]
    public async Task PolyCompiler_Ctors_TakeSessionOptionsOnly() {
        var ctors = typeof(PolyCompiler).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var parameters = ctors.SelectMany(c => c.GetParameters()).ToList();

        await Assert.That(parameters.Any(p => p.ParameterType == typeof(string))).IsFalse();
        await Assert.That(parameters.Any(p => p.ParameterType.Name == "CompileHooks")).IsFalse();
        await Assert.That(typeof(PolyCompiler).Assembly.GetType("Poly.Compile.CompileHooks")).IsNull();
    }

    /// <summary>A6: the catalog argument and the options' catalog are one catalog.</summary>
    [Test]
    public async Task PolyCompiler_CatalogDisagreeingWithOptions_FailsClosed() {
        var options = new SessionOptions(ProductCatalog.Catalog, ProductCatalog.ImplicitIds);

        Assert.Throws<ArgumentException>(() => new PolyCompiler(ProductCatalog.Create("sqlite"), options));
        await Assert.That(new PolyCompiler(ProductCatalog.Catalog, options)).IsNotNull();
    }

    /// <summary>
    /// C-04: the facade has no selection rule of its own. The vendor ids appear once, in the
    /// pack-to-vendor mapping and the host-vendor reader; every other id comes from the catalog.
    /// </summary>
    [Test]
    public async Task DslCompilerSource_HasNoOwnSelectionRule() {
        var dir = Path.Combine(PolycTestSupport.RepoRoot(), "src/Poly.DslCompiler");
        var text = string.Concat(Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal))
            .Select(File.ReadAllText));

        foreach (var name in new[] { "SwapPersistence", "SessionIds", "SeedFor", "LinkedVendor" })
            await Assert.That(text.Contains(name, StringComparison.Ordinal)).IsFalse().Because(name);
        await Assert.That(Regex.Matches(text, "\"persistence\"").Count).IsEqualTo(0);
        await Assert.That(Regex.Matches(text, "\"mysql\"").Count).IsEqualTo(0);
        await Assert.That(Regex.Matches(text, "\"sqlite\"").Count).IsLessThanOrEqualTo(2);
        await Assert.That(Regex.Matches(text, "\"sqlserver\"").Count).IsLessThanOrEqualTo(2);
    }

    /// <summary>The three public factories that carried their own seed or authoring rule are gone.</summary>
    [Test]
    public async Task Factories_ForDocument_ForAuthoring_AreGone() {
        var names = typeof(DomainSession)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.Name)
            .ToHashSet();

        await Assert.That(names.Contains("ForDocument")).IsFalse();
        await Assert.That(names.Contains("ForAuthoringDocument")).IsFalse();
        await Assert.That(names.Contains("ForAuthoringSource")).IsFalse();
    }

    private static List<string> Sorted(IEnumerable<(string FileName, string Source)> files) =>
        files.OrderBy(f => f.FileName, StringComparer.Ordinal).Select(f => $"{f.FileName}\n{f.Source}").ToList();
}