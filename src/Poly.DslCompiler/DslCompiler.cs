using Poly.Analysis;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.Interpretation.CSharp;
using Poly.Packs.Sqlite;
using Poly.Packs.SqlServer;

namespace Poly.DslCompiler;

/// <summary>
/// Controls which artifacts the DslCompiler generates.
/// </summary>
public enum CompileMode {
    /// <summary>Entity type definitions only (current behavior).</summary>
    Entities,
    /// <summary>Entity types + EF Core DbContext.</summary>
    Db,
    /// <summary>Entity types + EF Core DbContext + Minimal API Program.cs + demo.http.</summary>
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
/// Reuses <see cref="DomainToCSharpExporter"/> from the lowering subsystem,
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
    /// id and selects the Minimal API provider in <c>--mode all</c>.
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
            session = OpenCompileSession(polyText, dbms, extraLibraries);
            var parser = new PolyDslParser(polyText, session);
            changes = DomainCompilation.WithSeed(parser.Parse(), SeedFor(dbms)).ToList();
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
            var output = GenerateAllFiles(domain, outcome.Analysis, mode);
            var files = output.Files.ToList();

            if (mode == CompileMode.All
                && (output.Storage is null || output.Behavior is null
                    || output.Aggregate is null || output.DbContextName is null)) {
                throw new InvalidOperationException(
                    "CompileMode.All requires storage, behavior, and aggregate analysis metadata.");
            }

            foreach (var contributor in CollectArtifacts(session, mode, dbms))
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
        .With(new SqlServerLibrary());

    private static IReadOnlyList<string> SeedFor(DbmsPack dbms) => dbms switch {
        DbmsPack.Sqlite => [.. ExtensionCatalog.ProductAuthoring, "sqlite"],
        DbmsPack.SqlServer => [.. ExtensionCatalog.ProductAuthoring, "sqlserver"],
        _ => ExtensionCatalog.ProductAuthoring,
    };

    /// <summary>
    /// One session for parse, analyze, and artifacts. Source <c>uses</c> wins
    /// over the DBMS seed; <paramref name="extraLibraries"/> are always loaded.
    /// </summary>
    private static DomainSession OpenCompileSession(
        string polyText,
        DbmsPack dbms,
        IReadOnlyList<IDomainLibrary> extraLibraries) {
        var peeked = DomainCompilation.PeekExtensions(polyText);
        var ids = new List<string>(peeked.Count > 0 ? peeked : SeedFor(dbms));
        var seen = new HashSet<string>(ids, StringComparer.Ordinal);
        var catalog = CompilerCatalog;
        foreach (var library in extraLibraries) {
            if (!catalog.Contains(library.Id))
                catalog = catalog.With(library);
            if (seen.Add(library.Id))
                ids.Add(library.Id);
        }
        return DomainSession.ForExtensions(ids, catalog);
    }

    private IEnumerable<IArtifactContributor> CollectArtifacts(
        DomainSession session,
        CompileMode mode,
        DbmsPack dbms) {
        if (mode == CompileMode.All)
            yield return new MinimalApiHostArtifactContributor(dbms: dbms);
        foreach (var artifact in session.Artifacts)
            yield return artifact;
        foreach (var artifact in _extraArtifacts)
            yield return artifact;
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

    // ── C# generation ───────────────────────────────────────────

    /// <summary>Output of <see cref="GenerateAllFiles"/>: generated files plus the
    /// infrastructure models the CompileMode.All host contributor needs.</summary>
    private sealed record GenerationOutput(
        IReadOnlyList<(string FileName, string Source)> Files,
        StorageModel? Storage,
        BehaviorModel? Behavior,
        AggregateModel? Aggregate,
        string? DbContextName);

    private static GenerationOutput GenerateAllFiles(
        Domain domain, AnalysisResult analysis,
        CompileMode mode = CompileMode.Entities) {

        var files = new List<(string FileName, string Source)>();

        // Entity types (always generated) — export-time projection on finished AnalysisResult.
        // No mid-pipeline EntitySyntaxMetadata; failures throw (caught as compile errors).
        var types = DomainProgramProjection.ToSyntax(domain, analysis);
        var entities = domain.Types.OfType<Entity>().ToList();
        foreach (var entity in entities) {
            var entityNames = new HashSet<string>(StringComparer.Ordinal) {
                entity.Name,
                $"{entity.Name}Stage"
            };
            var entityDefs = types
                .Where(d => entityNames.Contains(d.Name))
                .ToList();
            if (entityDefs.Count == 0)
                throw new InvalidOperationException(
                    $"DomainProgramProjection produced no type definitions for entity '{entity.Name}'.");
            var csharp = new CSharpGenerator().Generate(entityDefs);
            files.Add(($"{entity.Name}.cs", csharp));
        }

        // Scaffolding (enums + DomainResult infrastructure) — the entity files
        // reference these (Genre, DomainResult<T>); without them the output does
        // not compile. One shared file, emitted before the entity files' content
        // is referenced by the rest of the project.
        var scaffoldingDefs = types
            .Where(d => !entities.Any(e =>
                d.Name == e.Name || d.Name == $"{e.Name}Stage"))
            .ToList();
        if (scaffoldingDefs.Count > 0) {
            var scaffolding = new CSharpGenerator().Generate(scaffoldingDefs);
            files.Add(("Poly.Types.cs", scaffolding));
        }

        // Infrastructure metadata comes from the session analysis (which already ran
        // StoragePass with the session's type maps + conventions). No second StoragePass.
        var storageModel = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage;
        var behaviorModel = BehaviorMetadata.From(domain, analysis);
        var aggregateModel = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate;

        // Fail closed — no silent re-analyze (storage, behavior, aggregate).
        if ((mode == CompileMode.Db || mode == CompileMode.All) && storageModel == null)
            throw new InvalidOperationException(
                "Infrastructure pipeline did not produce storage mapping metadata.");

        if (mode == CompileMode.All) {
            if (behaviorModel == null)
                throw new InvalidOperationException(
                    "Could not project behavior from analysis (capability + entities).");
            if (aggregateModel == null)
                throw new InvalidOperationException(
                    "Domain analysis did not produce aggregate metadata. Ensure OwnershipAggregatePass is registered in the domain analysis pipeline.");
        }

        // DbContext (mode: db or all)
        string? dbContextName = null;
        if (mode == CompileMode.Db || mode == CompileMode.All) {
            dbContextName = $"{domain.Name}DbContext";
            var dbGen = new DbContextGenerator(domain, storageModel!);
            files.Add(($"{dbContextName}.cs",
                new CSharpGenerator().Generate(dbGen.GenerateCompilationUnit())));
        }

        // Program.cs + demo.http are emitted by MinimalApiHostArtifactContributor through
        // the artifact-contributor hook in CompileMode.All — never inline here.
        return new GenerationOutput(files, storageModel, behaviorModel, aggregateModel, dbContextName);
    }

    private static CompileResult Fail(string message) =>
        new(Success: false, Files: null, Errors: [message]);
}