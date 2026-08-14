using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Packs;
using Poly.DomainModeling.Parsing;
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
    private readonly List<IArtifactContributor> _artifactContributors = [];

    /// <summary>
    /// Result of a compilation attempt.
    /// </summary>
    public sealed record CompileResult(
        bool Success,
        IReadOnlyList<(string FileName, string Source)>? Files,
        IReadOnlyList<string>? Errors
    );

    /// <summary>
    /// Registers an artifact contributor, invoked after analysis succeeds to add
    /// extra output files (analyzed domain + analysis result). Structural analysis
    /// failures fail closed first — contributors are never asked.
    /// </summary>
    public DslCompiler AddArtifactContributor(IArtifactContributor contributor) {
        ArgumentNullException.ThrowIfNull(contributor);
        _artifactContributors.Add(contributor);
        return this;
    }

    /// <summary>
    /// Compiles .poly DSL text into C# source files (entities only — default mode).
    /// </summary>
    public CompileResult Compile(string polyText) =>
        Compile(polyText, CompileMode.Entities);

    /// <summary>
    /// Compiles .poly DSL text into C# source files with the given mode
    /// and generic SQL storage defaults.
    /// </summary>
    public CompileResult Compile(string polyText, CompileMode mode) =>
        Compile(polyText, mode, DbmsPack.Generic);

    /// <summary>
    /// Compiles .poly DSL text with the given mode and DBMS pack selection.
    /// </summary>
    public CompileResult Compile(string polyText, CompileMode mode, DbmsPack dbms) =>
        Compile(polyText, mode, CreateInputs(dbms), dbms);

    /// <summary>
    /// Compiles .poly DSL text with explicit domain libraries loaded in order.
    /// Each library joins parse/print/analysis via <see cref="DomainHostBuilder.Load"/>;
    /// the library list is the model (<c>DbmsPack</c> is a CLI convenience alias for
    /// the built-in persistence libraries). The derived <see cref="DbmsPack"/> drives Minimal API
    /// provider selection in <c>--mode all</c>.
    /// </summary>
    public CompileResult Compile(string polyText, CompileMode mode, params IDomainLibrary[] libraries) {
        ArgumentNullException.ThrowIfNull(libraries);
        return Compile(polyText, mode, CreateInputs(libraries), ResolveDbms(libraries));
    }

    /// <summary>
    /// Compiles .poly DSL text with explicit parse/analyze inputs.
    /// </summary>
    public CompileResult Compile(
        string polyText,
        CompileMode mode,
        DomainParserInputs parserInputs,
        DomainAnalysisInputs analysisInputs) =>
        Compile(polyText, mode, parserInputs, analysisInputs, DbmsPack.Generic);

    /// <summary>
    /// Compiles .poly DSL text from an upstream convenience parse/analyze bundle
    /// and a DBMS pack (the pack drives Program.cs provider selection in
    /// <c>--mode all</c>).
    /// </summary>
    public CompileResult Compile(string polyText, CompileMode mode, DomainHost inputs, DbmsPack dbms) =>
        Compile(polyText, mode, inputs.Parser, inputs.Analysis, dbms);

    /// <summary>
    /// Compiles .poly DSL text with explicit parse/analyze inputs and a DBMS pack
    /// (the pack drives Program.cs provider selection in <c>--mode all</c>).
    /// </summary>
    private CompileResult Compile(
        string polyText,
        CompileMode mode,
        DomainParserInputs parserInputs,
        DomainAnalysisInputs analysisInputs,
        DbmsPack dbms) {
        ArgumentNullException.ThrowIfNull(parserInputs);
        ArgumentNullException.ThrowIfNull(analysisInputs);

        if (string.IsNullOrWhiteSpace(polyText))
            return Fail("DSL text is empty.");

        // ── 1. Parse ─────────────────────────────────────────────
        List<DomainChange> changes;
        try {
            var seed = SeedFor(dbms);
            var parseHost = DomainCompilation.HostForSource(polyText, seed, CompilerCatalog);
            var parser = new PolyDslParser(polyText, parseHost.Parser);
            changes = DomainCompilation.WithSeed(parser.Parse(), seed).ToList();
        }
        catch (FormatException ex) {
            return Fail($"Parse error: {ex.Message}");
        }
        catch (InvalidOperationException ex) {
            return Fail(ex.Message);
        }

        if (changes.Count == 0)
            return Fail("No domain changes parsed from the DSL text.");

        // ── 2. Evolve from empty domain ──────────────────────────
        var nameChange = changes.OfType<SetDomainNameChange>().FirstOrDefault();
        var domainName = nameChange?.Name ?? "PolyDomain";

        var emptyDomain = new Domain(domainName, []);
        EvolutionResult outcome;
        try {
            outcome = new DomainEvolution(emptyDomain).Apply(changes);
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

        // ── 3. Generate C# ───────────────────────────────────────
        var domain = outcome.Root;
        try {
            var hostAnalysis = domain.Extensions.Count > 0
                ? domain.ResolveHost(CompilerCatalog).Analysis
                : analysisInputs;
            var output = GenerateAllFiles(domain, outcome.Analysis, mode, hostAnalysis);
            var files = output.Files.ToList();

            // CompileMode.All emits Program.cs + demo.http through the artifact-contributor
            // hook (composition-root host contributor), alongside any user-registered ones.
            // The host contributor runs first to preserve the historical file order
            // (Program.cs + demo.http before extra contributed files).
            var contributors = new List<IArtifactContributor>();
            if (mode == CompileMode.All) {
                if (output.Storage is null || output.Behavior is null
                    || output.Aggregate is null || output.DbContextName is null) {
                    throw new InvalidOperationException(
                        "CompileMode.All requires storage, behavior, and aggregate analysis metadata.");
                }
                contributors.Add(new MinimalApiHostArtifactContributor(
                    output.Storage, output.Behavior, output.Aggregate, output.DbContextName,
                    dbms: dbms));
            }
            contributors.AddRange(_artifactContributors);

            foreach (var contributor in contributors)
                foreach (var file in contributor.Contribute(domain, outcome.Analysis))
                    files.Add(file);
            return new CompileResult(Success: true, Files: files, Errors: null);
        }
        catch (Exception ex) {
            // Projection / emit failures must surface as compile errors (fail loud).
            return Fail($"Code generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Compiles .poly DSL text with an upstream convenience parse/analyze bundle.
    /// </summary>
    public CompileResult Compile(
        string polyText,
        CompileMode mode,
        DomainHost inputs) {
        ArgumentNullException.ThrowIfNull(inputs);
        return Compile(polyText, mode, inputs.Parser, inputs.Analysis);
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
    /// Builds explicit parse/analyze inputs for a DBMS pack selection.
    /// Always includes portable <c>column</c>/<c>table</c> annotation syntax.
    /// </summary>
    public static DomainHost CreateInputs(DbmsPack dbms) =>
        CreateInputs(DbmsPacks(dbms));

    /// <summary>
    /// Builds explicit parse/analyze inputs with the given domain libraries loaded
    /// in order. Always includes portable <c>column</c>/<c>table</c> annotation
    /// syntax (compiler host); duplicate library ids fail closed in
    /// <see cref="DomainHostBuilder.Load"/>.
    /// </summary>
    public static DomainHost CreateInputs(params IDomainLibrary[] libraries) {
        ArgumentNullException.ThrowIfNull(libraries);
        var builder = DomainHostBuilder.Create().WithStorageFacets();
        foreach (var library in libraries)
            builder.Load(library);
        return builder.Build();
    }

    /// <summary>
    /// The built-in persistence libraries behind each <see cref="DbmsPack"/> alias.
    /// The enum has no arms for future vendors — they register as
    /// <see cref="IDomainLibrary"/> only.
    /// </summary>
    private static IDomainLibrary[] DbmsPacks(DbmsPack dbms) => dbms switch {
        DbmsPack.Generic => [],
        DbmsPack.Sqlite => [new SqliteLibrary()],
        DbmsPack.SqlServer => [new SqlServerLibrary()],
        _ => throw new ArgumentOutOfRangeException(nameof(dbms), dbms, "Unknown DBMS pack."),
    };

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
        CompileMode mode = CompileMode.Entities,
        DomainAnalysisInputs? analysisInputs = null) {

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

        // Infrastructure metadata — prefer domain analysis result (which already ran
        // StoragePass via UseDomainModelAnalysisPipeline). Fall back
        // to a narrow StoragePass re-run only when pack-specific type maps or
        // conventions require refinement (e.g., Sqlite type mappings).
        var storageModel = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage;
        var behaviorModel = analysis.GetMetadata<BehaviorMetadata>(domain)?.Behavior;
        var aggregateModel = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate;

        var needsInfraPipeline = (mode == CompileMode.Db || mode == CompileMode.All)
            && (storageModel == null
                || (analysisInputs?.TypeMaps.HasOverrides ?? false)
                || (analysisInputs?.StorageConventions.Count ?? 0) > 0
                || (analysisInputs?.AdditionalPasses.Count ?? 0) > 0);

        if (needsInfraPipeline) {
            var context = AnalysisContext.CreateDefault();
            var storagePass = new StoragePass(
                typeMaps: analysisInputs?.TypeMaps,
                conventions: analysisInputs?.StorageConventions,
                analysis: analysis);
            storagePass.Analyze(context, domain);
            storageModel = context.GetMetadata<StorageMappingMetadata>(domain)?.Storage;
        }

        // Fail closed — no silent re-analyze (storage, behavior, aggregate).
        if ((mode == CompileMode.Db || mode == CompileMode.All) && storageModel == null)
            throw new InvalidOperationException(
                "Infrastructure pipeline did not produce storage mapping metadata.");

        if (mode == CompileMode.All) {
            if (behaviorModel == null)
                throw new InvalidOperationException(
                    "Domain analysis did not produce behavior metadata. Ensure BehaviorPass is registered in the domain analysis pipeline.");
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