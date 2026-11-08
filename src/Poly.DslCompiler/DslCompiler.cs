using Poly.Ast.Nodes;
using Poly.Compile;
using Poly.DomainModeling.Compile;
using Poly.Packs.Product;

namespace Poly.DslCompiler;

/// <summary>
/// Controls which seed extension ids the DslCompiler loads when source lists no <c>uses</c>.
/// CompileMode never invents a process door; HTTP host files require <c>uses http</c>
/// (or an honest seed of that catalog id) and the HTTP analysis bag.
/// </summary>
public enum CompileMode {
    /// <summary>Entity type definitions only (language seed).</summary>
    Entities,
    /// <summary>Language seed plus persistence (vendor from <see cref="DbmsPack"/>).</summary>
    Db,
    /// <summary>Same persistence seed as <see cref="Db"/>. Does not seed <c>http</c>.</summary>
    All,
}

/// <summary>
/// DBMS pack selection for storage defaults (type maps + conventions).
/// Annotation keywords (<c>column</c>/<c>table</c>) come from the core Sql pack
/// in all cases; this selects vendor default projections only.
/// </summary>
public enum DbmsPack {
    /// <summary>Core generic SQL defaults (varchar, boolean, timestamp, …).</summary>
    Generic,
    /// <summary>SQLite affinities / EF Core SQLite types (first shippable pack).</summary>
    Sqlite,
    /// <summary>SQL Server types (nvarchar, bit, datetime2, …).</summary>
    SqlServer,
}

/// <summary>
/// Compiles .poly DSL text into C# files. A thin facade over <see cref="PolyCompiler"/> and the
/// product catalog for hosts that still pick a <see cref="CompileMode"/> and a <see cref="DbmsPack"/>;
/// polyc does not.
/// </summary>
public sealed class DslCompiler {
    private readonly List<IDomainLibrary> _extraLibraries = [];
    private readonly List<IArtifactContributor> _extraArtifacts = [];

    /// <summary>
    /// Result of a compilation attempt. On success, <see cref="Catalog"/> is the catalog
    /// the files were written from, including contributed text files. Null when compile failed.
    /// </summary>
    public sealed record CompileResult(
        bool Success,
        IReadOnlyList<(string FileName, string Source)>? Files,
        IReadOnlyList<string>? Errors,
        ArtifactCatalog? Catalog = null
    );

    /// <summary>
    /// Loads an extra library for subsequent compiles. The library is on the
    /// same session as parse and analyze — not a side bag.
    /// </summary>
    public DslCompiler Load(IDomainLibrary library) {
        ArgumentNullException.ThrowIfNull(library);
        _extraLibraries.Add(library);
        return this;
    }

    /// <summary>
    /// Registers a one-off artifact contributor (not a <c>uses</c> id).
    /// </summary>
    public DslCompiler AddArtifactContributor(IArtifactContributor contributor) {
        ArgumentNullException.ThrowIfNull(contributor);
        _extraArtifacts.Add(contributor);
        return this;
    }

    /// <summary>
    /// Compiles .poly DSL text into C# source files (entities only — default mode).
    /// </summary>
    public CompileResult Compile(string polyText) =>
        Compile(polyText, CompileMode.Entities, DbmsPack.Generic);

    /// <summary>
    /// Compiles .poly DSL text into C# source files with the given mode
    /// and generic SQL storage defaults.
    /// </summary>
    public CompileResult Compile(string polyText, CompileMode mode) =>
        Compile(polyText, mode, DbmsPack.Generic);

    /// <summary>
    /// Compiles .poly. One session: seed (or source <c>uses</c>) plus
    /// <see cref="Load"/> libraries. <paramref name="dbms"/> seeds the vendor
    /// id and selects the Minimal API provider when the HTTP bag is present.
    /// </summary>
    public CompileResult Compile(string polyText, CompileMode mode, DbmsPack dbms) =>
        CompileCore(polyText, mode, dbms, _extraLibraries);

    /// <summary>
    /// Compiles .poly with extra libraries on the same session.
    /// <see cref="DbmsPack"/> is derived from known vendor ids for Program.cs.
    /// </summary>
    public CompileResult Compile(string polyText, CompileMode mode, params IDomainLibrary[] libraries) {
        ArgumentNullException.ThrowIfNull(libraries);
        IReadOnlyList<IDomainLibrary> extras = [.. _extraLibraries, .. libraries];
        return CompileCore(polyText, mode, ResolveDbms(extras), extras);
    }

    private CompileResult CompileCore(
        string polyText,
        CompileMode mode,
        DbmsPack dbms,
        IReadOnlyList<IDomainLibrary> extraLibraries) {
        if (string.IsNullOrWhiteSpace(polyText))
            return Fail("DSL text is empty.");

        // The compile is Poly.Compile over the product catalog. The DbmsPack always wins for the
        // Minimal API provider, so it is the host vendor the catalog is built with.
        var catalog = ProductCatalog.Create(dbms switch {
            DbmsPack.Sqlite => "sqlite",
            DbmsPack.SqlServer => "sqlserver",
            _ => "generic",
        });
        foreach (var library in extraLibraries) {
            if (!catalog.Contains(library.Id))
                catalog = catalog.With(library);
        }

        var vendor = LinkedVendor(mode, dbms);
        var hooks = new CompileHooks {
            SessionExtensions = ids => SessionIds(ids, mode, vendor, extraLibraries),
            DomainExtensions = ids => vendor is null ? ids : SwapPersistence(ids, vendor),
        };
        var compiled = new PolyCompiler(catalog, SeedFor(dbms, mode), hooks).Compile(polyText);
        if (!compiled.Succeeded)
            return Fail(compiled);

        try {
            var domain = compiled.Domain!;
            var session = compiled.Session!;
            var files = compiled.Files.ToList();
            var artifacts = session.ArtifactCatalog;
            // Emit already ran session.Artifacts. An extra that is that same instance
            // is skipped. Running it again would register the same artifact id.
            foreach (var contributor in _extraArtifacts) {
                if (session.Artifacts.Contains(contributor))
                    continue;
                foreach (var artifact in contributor.Contribute(domain, compiled.Analysis!, artifacts))
                    artifacts.Register(FromContributor(contributor, artifact));
            }
            // Emit already returned its files. Appending the catalog copies would duplicate them.
            var seen = new HashSet<string>(files.Select(f => f.FileName), StringComparer.Ordinal);
            foreach (var artifact in artifacts.Artifacts) {
                if (artifact.Descriptor.Id.Type != ContributedFile.Type)
                    continue;
                var name = ContributedFile.FileName(artifact);
                if (!seen.Add(name))
                    continue;
                files.Add((name, ContributedFile.Text(artifact)));
            }
            return new CompileResult(Success: true, Files: files, Errors: null, Catalog: artifacts);
        }
        catch (Exception ex) {
            return Fail($"Code generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The vendor that Db and All link in place of generic <c>persistence</c>, so the host
    /// Program.cs provider matches <paramref name="dbms"/>. Null when nothing is swapped.
    /// </summary>
    private static string? LinkedVendor(CompileMode mode, DbmsPack dbms) {
        if (mode is not (CompileMode.Db or CompileMode.All))
            return null;
        return dbms switch {
            DbmsPack.Sqlite => "sqlite",
            DbmsPack.SqlServer => "sqlserver",
            _ => null
        };
    }

    /// <summary>
    /// Drops every <c>persistence</c> id from an ordered extension list and puts
    /// <paramref name="vendor"/> where the first one was, unless the vendor is already listed.
    /// </summary>
    private static List<string> SwapPersistence(IReadOnlyList<string> ids, string vendor) {
        var hasVendor = ids.Contains(vendor);
        var swapped = new List<string>(ids.Count);
        foreach (var id in ids) {
            if (id != "persistence") {
                swapped.Add(id);
                continue;
            }
            if (!hasVendor) {
                swapped.Add(vendor);
                hasVendor = true;
            }
        }
        return swapped;
    }

    private static IReadOnlyList<string> SeedFor(DbmsPack dbms, CompileMode mode) {
        if (mode is CompileMode.Entities)
            return ExtensionCatalog.ProductAuthoring;
        var seed = new List<string>(ExtensionCatalog.ProductAuthoring);
        if (dbms is DbmsPack.Sqlite)
            seed.Add("sqlite");
        else if (dbms is DbmsPack.SqlServer)
            seed.Add("sqlserver");
        return seed;
    }

    /// <summary>
    /// The ids one session loads: the document's <c>uses</c> (or the seed), the extra
    /// libraries, then the vendor in place of generic persistence. CompileMode never seeds
    /// <c>http</c> — that id arrives via source <c>uses</c> or an extra library.
    /// </summary>
    private static List<string> SessionIds(
        IReadOnlyList<string> requested,
        CompileMode mode,
        string? vendor,
        IReadOnlyList<IDomainLibrary> extraLibraries) {
        var ids = new List<string>(requested);
        var seen = new HashSet<string>(ids, StringComparer.Ordinal);
        foreach (var library in extraLibraries) {
            if (seen.Add(library.Id))
                ids.Add(library.Id);
        }
        if (vendor is not null) {
            ids = SwapPersistence(ids, vendor);
            if (!ids.Contains(vendor))
                ids.Add(vendor);
        }
        else if (mode is CompileMode.Db or CompileMode.All
                 && !ids.Exists(id => id is "sqlite" or "sqlserver" or "mysql" or "persistence")) {
            ids.Add("persistence");
        }
        return ids;
    }

    /// <summary>
    /// Derives the Minimal API provider selection from loaded library ids
    /// (last matching known id wins; unknown libraries fall back to generic).
    /// </summary>
    private static DbmsPack ResolveDbms(IReadOnlyCollection<IDomainLibrary> libraries) {
        var dbms = DbmsPack.Generic;
        foreach (var library in libraries) {
            switch (library.Id) {
                case "sqlite":
                    dbms = DbmsPack.Sqlite;
                    break;
                case "sqlserver":
                    dbms = DbmsPack.SqlServer;
                    break;
            }
        }
        return dbms;
    }

    /// <summary>Parses CLI/host DBMS pack names (fail-closed on unknown).</summary>
    public static DbmsPack ParseDbmsPack(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim().ToLowerInvariant() switch {
            "generic" or "sql" or "core" => DbmsPack.Generic,
            "sqlite" or "sqlite3" => DbmsPack.Sqlite,
            "sqlserver" or "mssql" or "sql-server" => DbmsPack.SqlServer,
            var other => throw new FormatException(
                $"Unknown DBMS pack '{other}'. Valid values: generic, sqlite, sqlserver"),
        };
    }

    private static CompileResult Fail(string message) =>
        new(Success: false, Files: null, Errors: [message]);

    private static CompileResult Fail(Poly.Compile.CompileResult compiled) {
        var errors = compiled.Diagnostics
            .Where(d => d.IsError)
            .Select(d => d.Code == DiagnosticCodes.Parse ? $"Parse error: {d.Message}" : d.Message)
            .ToList();
        return new CompileResult(Success: false, Files: null, Errors: errors.Count > 0 ? errors : ["Analysis rejected the domain."]);
    }

    /// <summary>
    /// The catalog producer is the contributor that was registered, not a name it chose.
    /// A return that is not a contributed text file or a generator tree fails closed.
    /// </summary>
    private static Artifact FromContributor(IArtifactContributor contributor, Artifact artifact) {
        ArgumentNullException.ThrowIfNull(contributor);
        ArgumentNullException.ThrowIfNull(artifact);
        var type = artifact.Descriptor.Id.Type;
        var file = type == ContributedFile.Type && artifact.Payload is string;
        var tree = type == GeneratedTree.Type && artifact.Payload is CompilationUnitNode;
        if (!file && !tree)
            throw new InvalidOperationException(
                $"Contributor '{contributor.GetType().Name}' returned '{artifact.Descriptor.Id}', which is not a contributed text file or tree.");
        return new Artifact(
            new ArtifactDescriptor(artifact.Descriptor.Id, contributor.GetType().Name, artifact.Descriptor.References),
            artifact.Payload);
    }
}