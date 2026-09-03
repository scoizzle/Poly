using Poly.Analysis;
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
    /// Result of a compilation attempt.
    /// </summary>
    public sealed record CompileResult(
        bool Success,
        IReadOnlyList<(string FileName, string Source)>? Files,
        IReadOnlyList<string>? Errors
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
            var persist = outcome.Analysis.GetMetadata<PersistenceSurfaceMetadata>(domain);
            var http = outcome.Analysis.GetMetadata<HttpSurfaceMetadata>(domain);
            var storageModel = outcome.Analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage;

            if (persist is not null) {
                if (storageModel is null)
                    throw new InvalidOperationException(
                        "Infrastructure pipeline did not produce storage mapping metadata.");
                var dbContextName = $"{domain.Name}DbContext";
                files.Add(($"{dbContextName}.cs",
                    new CSharpGenerator().Generate(
                        new DbContextGenerator(domain, storageModel).GenerateCompilationUnit())));
            }

            if (http is not null) {
                if (storageModel is null
                    || BehaviorMetadata.From(domain, outcome.Analysis) is null
                    || outcome.Analysis.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate is null) {
                    throw new InvalidOperationException(
                        "HTTP artifacts require storage, behavior, and aggregate analysis metadata.");
                }
                foreach (var file in new MinimalApiHostArtifactContributor(dbms: dbms)
                    .Contribute(domain, outcome.Analysis))
                    files.Add(file);
            }

            foreach (var contributor in session.Artifacts.Concat(_extraArtifacts))
                foreach (var file in contributor.Contribute(domain, outcome.Analysis))
                    files.Add(file);
            return new CompileResult(Success: true, Files: files, Errors: null);
        }
        catch (Exception ex) {
            return Fail($"Code generation failed: {ex.Message}");
        }
    }

    private static readonly ExtensionCatalog CompilerCatalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new SqlServerLibrary())
        .With(new HttpLibrary());

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
        if (mode is CompileMode.Db or CompileMode.All
            && !ids.Exists(id => id is "sqlite" or "sqlserver" or "mysql" or "persistence")
            && seen.Add("persistence"))
            ids.Add("persistence");
        return DomainSession.ForExtensions(ids, catalog);
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
}
