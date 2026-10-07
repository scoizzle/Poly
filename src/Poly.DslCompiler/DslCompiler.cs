using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.Interpretation.CSharp;
using Poly.Packs.Sqlite;
using Poly.Packs.SqlServer;

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
/// Compiles .poly DSL text into C# type definitions.
///
/// Uses <see cref="DomainSession.Emit"/> which projects the analyzed domain
/// through <see cref="DomainProgramProjection"/> and <see cref="CSharpGenerator"/>,
/// the same pipeline behind the MCP <c>export_domain_to_csharp</c> tool.
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

        DomainSession session;
        List<DomainChange> changes;
        try {
            session = OpenCompileSession(polyText, mode, dbms, extraLibraries);
            var parser = new PolyDslParser(polyText, session);
            changes = DomainCompilation.WithSeed(parser.Parse(), SeedFor(dbms, mode)).ToList();
        }
        catch (FormatException ex) {
            return Fail($"Parse error: {ex.Message}");
        }
        catch (InvalidOperationException ex) {
            return Fail(ex.Message);
        }

        if (changes.Count == 0)
            return Fail("No domain changes parsed from the DSL text.");

        var nameChange = changes.OfType<SetDomainNameChange>().FirstOrDefault();
        var domainName = nameChange?.Name ?? "PolyDomain";
        var emptyDomain = new Domain(domainName, []);
        // OpenCompileSession links sqlite/sqlserver in place of generic persistence.
        // The domain must name that unit, or DomainEvolution.Apply throws on an id the
        // session did not load.
        if (LinkedVendor(mode, dbms) is { } vendor) {
            changes = SwapPersistence(changes, vendor,
                c => (c as AddDomainExtensionChange)?.ExtensionId,
                new AddDomainExtensionChange(vendor));
        }
        EvolutionResult outcome;
        try {
            outcome = new DomainEvolution(emptyDomain).Apply(changes, session: session);
        }
        catch (Exception ex) {
            return Fail($"Evolution failed: {ex.Message}");
        }

        if (!outcome.Succeeded) {
            var errors = outcome.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(10)
                .Select(d => d.Message)
                .ToList();

            return new CompileResult(
                Success: false,
                Files: null,
                Errors: errors.Count > 0
                    ? errors
                    : ["Analysis rejected the domain."]
            );
        }

        var domain = outcome.Root;
        try {
            var files = session.Emit(domain, outcome.Analysis).ToList();
            var catalog = session.ArtifactCatalog;
            foreach (var contributor in session.Artifacts.Concat(_extraArtifacts)) {
                foreach (var artifact in contributor.Contribute(domain, outcome.Analysis, catalog))
                    catalog.Register(StampContributor(contributor, artifact));
            }
            var written = files.Select(f => f.FileName).ToHashSet(StringComparer.Ordinal);
            foreach (var artifact in catalog.Artifacts) {
                if (!TryTextFile(artifact, out var fileName, out var text) || !written.Add(fileName))
                    continue;
                files.Add((fileName, text));
            }
            return new CompileResult(Success: true, Files: files, Errors: null, Catalog: catalog);
        }
        catch (Exception ex) {
            return Fail($"Code generation failed: {ex.Message}");
        }
    }

    private static readonly ExtensionCatalog CompilerCatalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new SqlServerLibrary())
        .With(new HttpLibrary());

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
    /// Drops every <c>persistence</c> item from an ordered extension list and puts
    /// <paramref name="vendorItem"/> where the first one was, unless the vendor is
    /// already listed. Items with no extension id pass through unchanged.
    /// </summary>
    private static List<T> SwapPersistence<T>(
        IReadOnlyList<T> items, string vendor, Func<T, string?> extensionId, T vendorItem) {
        var hasVendor = items.Any(item => extensionId(item) == vendor);
        var swapped = new List<T>(items.Count);
        foreach (var item in items) {
            if (extensionId(item) != "persistence") {
                swapped.Add(item);
                continue;
            }
            if (!hasVendor) {
                swapped.Add(vendorItem);
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
    /// One session for parse, analyze, and artifacts. Source <c>uses</c> wins
    /// over the DBMS seed; <paramref name="extraLibraries"/> are always loaded.
    /// CompileMode never seeds <c>http</c> — that id arrives via source <c>uses</c>
    /// or an extra library with that catalog id.
    /// </summary>
    private static DomainSession OpenCompileSession(
        string polyText,
        CompileMode mode,
        DbmsPack dbms,
        IReadOnlyList<IDomainLibrary> extraLibraries) {
        var peeked = DomainCompilation.PeekExtensions(polyText);
        var ids = new List<string>(peeked.Count > 0 ? peeked : SeedFor(dbms, mode));
        var seen = new HashSet<string>(ids, StringComparer.Ordinal);
        var catalog = CompilerCatalog;
        foreach (var library in extraLibraries) {
            if (!catalog.Contains(library.Id))
                catalog = catalog.With(library);
            if (seen.Add(library.Id))
                ids.Add(library.Id);
        }
        if (LinkedVendor(mode, dbms) is { } vendor) {
            ids = SwapPersistence(ids, vendor, id => id, vendor);
            if (!ids.Contains(vendor))
                ids.Add(vendor);
        }
        else if (mode is CompileMode.Db or CompileMode.All
                 && !ids.Exists(id => id is "sqlite" or "sqlserver" or "mysql" or "persistence")) {
            ids.Add("persistence");
        }

        // Load-time registration (not bag invent): host producers for loaded doors.
        var builder = SessionBuilder.CreateEmpty();
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids) {
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Domain extension id must be non-empty.");
            if (!loaded.Add(id))
                throw new InvalidOperationException($"Domain lists extension '{id}' more than once.");
            builder.Load(catalog.Resolve(id));
        }
        if (ids.Exists(id => id is "persistence" or "sqlite" or "sqlserver" or "mysql"))
            builder.AddArtifactContributor(new DbContextArtifactContributor());
        if (ids.Exists(id => id == "http"))
            builder.AddArtifactContributor(new MinimalApiHostArtifactContributor(dbms: dbms));
        return builder.Build();
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

    /// <summary>
    /// The catalog producer is the contributor that was registered, not a name it chose.
    /// A return that is not a printed file, an http file, or a host tree fails closed.
    /// </summary>
    private static Artifact StampContributor(IArtifactContributor contributor, Artifact artifact) {
        ArgumentNullException.ThrowIfNull(contributor);
        ArgumentNullException.ThrowIfNull(artifact);
        var type = artifact.Descriptor.Id.Type;
        var payloadOk = type == HostTree.Type
            ? artifact.Payload is CompilationUnitNode
            : artifact.Payload is string && type is ContributedFile.Type or HttpFile.Type;
        if (!payloadOk)
            throw new InvalidOperationException(
                $"Contributor '{contributor.GetType().Name}' returned '{artifact.Descriptor.Id}', which is not a printed file, an http file, or a host tree.");
        return new Artifact(
            new ArtifactDescriptor(artifact.Descriptor.Id, contributor.GetType().Name, artifact.Descriptor.References),
            artifact.Payload);
    }

    private static bool TryTextFile(Artifact artifact, out string fileName, out string text) {
        if (artifact.Descriptor.Id.Type == ContributedFile.Type) {
            fileName = ContributedFile.FileName(artifact);
            text = ContributedFile.Text(artifact);
            return true;
        }
        if (artifact.Descriptor.Id.Type == HttpFile.Type) {
            fileName = HttpFile.FileName(artifact);
            text = HttpFile.Text(artifact);
            return true;
        }
        fileName = "";
        text = "";
        return false;
    }
}