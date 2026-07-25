using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Parsing;
using Poly.Interpretation.CSharp;
using Poly.Packs.Sqlite;
using Poly.Packs.SqlServer;
using Poly.Syntax.Analysis;

namespace Poly.DslCompiler;

/// <summary>
/// Controls which artifacts the DslCompiler generates.
/// </summary>
public enum CompileMode {
    /// <summary>Entity type definitions only (current behavior).</summary>
    Entities,
    /// <summary>Entity types + EF Core DbContext.</summary>
    Db,
    /// <summary>Entity types + DbContext + Minimal API. (Not yet implemented)</summary>
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
    /// <summary>
    /// Result of a compilation attempt.
    /// </summary>
    public sealed record CompileResult(
        bool Success,
        IReadOnlyList<(string FileName, string Source)>? Files,
        IReadOnlyList<string>? Errors
    );

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
        Compile(polyText, mode, CreateAuthoring(dbms));

    /// <summary>
    /// Compiles .poly DSL text with an explicit authoring context (packs, maps, conventions).
    /// </summary>
    public CompileResult Compile(
        string polyText,
        CompileMode mode,
        DomainAuthoringContext authoring) {
        ArgumentNullException.ThrowIfNull(authoring);

        if (string.IsNullOrWhiteSpace(polyText))
            return Fail("DSL text is empty.");

        // ── 1. Parse ─────────────────────────────────────────────
        List<DomainChange> changes;
        try {
            var parser = new PolyDslParser(polyText, authoring);
            changes = parser.Parse();
        }
        catch (FormatException ex) {
            return Fail($"Parse error: {ex.Message}");
        }

        if (changes.Count == 0)
            return Fail("No domain changes parsed from the DSL text.");

        // ── 2. Evolve from empty domain ──────────────────────────
        var nameChange = changes.OfType<SetDomainNameChange>().FirstOrDefault();
        var domainName = nameChange?.Name ?? "PolyDomain";

        var emptyDomain = new Domain(domainName, [], []);
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
            var files = GenerateAllFiles(domain, outcome.Analysis, mode, authoring);
            return new CompileResult(Success: true, Files: files, Errors: null);
        }
        catch (InvalidOperationException ex) {
            return Fail(ex.Message);
        }
    }

    /// <summary>
    /// Builds the authoring context for a DBMS pack selection.
    /// Always includes portable <c>column</c>/<c>table</c> annotation syntax.
    /// </summary>
    public static DomainAuthoringContext CreateAuthoring(DbmsPack dbms) {
        var ctx = DomainAuthoringContext.CreateWithSqlPack();
        return dbms switch {
            DbmsPack.Generic => ctx,
            DbmsPack.Sqlite => ctx.AddSqliteDefaults(),
            DbmsPack.SqlServer => ctx.AddSqlServerDefaults(),
            _ => throw new ArgumentOutOfRangeException(nameof(dbms), dbms, "Unknown DBMS pack."),
        };
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

    private static IReadOnlyList<(string FileName, string Source)> GenerateAllFiles(
        Domain domain, Poly.Syntax.Analysis.AnalysisResult analysis,
        CompileMode mode = CompileMode.Entities,
        DomainAuthoringContext? authoring = null) {

        var files = new List<(string FileName, string Source)>();

        // Entity types (always generated) — from EntitySyntaxMetadata on AnalysisResult
        var entitySyntax = analysis.GetMetadata<EntitySyntaxMetadata>(domain);
        if (entitySyntax is not null) {
            var combinedSource = new CSharpGenerator().Generate(entitySyntax.Types);
            files.Add(("_all.cs", combinedSource));

            // Per-entity files
            var entities = domain.Types.OfType<Entity>().ToList();
            foreach (var entity in entities) {
                var entityNames = new HashSet<string>(StringComparer.Ordinal) {
                    entity.Name,
                    $"{entity.Name}Stage"
                };
                var entityDefs = entitySyntax.Types
                    .Where(d => entityNames.Contains(d.Name))
                    .ToList();
                if (entityDefs.Count == 0) continue;
                var csharp = new CSharpGenerator().Generate(entityDefs);
                files.Add(($"{entity.Name}.cs", csharp));
            }
        }

        // Infrastructure analysis — codegen-specific passes only.
        // Topology, aggregate, and behavior are now produced by the domain pipeline
        // (UseDomainModelAnalysisPipeline) and available on the analysis argument.
        var infraPipelineBuilder = new AnalyzerBuilder()
            .AddAnalyzer(new Poly.DomainModeling.Analysis.StoragePass(
                typeMaps: authoring?.TypeMaps,
                conventions: authoring?.StorageConventions,
                analysis: analysis))
            .AddAnalyzer(new Poly.DomainModeling.Analysis.TransportPass());

        // Issue 19: Wire authoring.Passes.Build() into pipeline
        if (authoring != null) {
            foreach (var pass in authoring.Passes.Build())
                infraPipelineBuilder.AddAnalyzer(pass);
        }

        var infraPipeline = infraPipelineBuilder.Build();
        // Issue 14: Thread prior domain analysis into infra pipeline
        var infraResult = infraPipeline.Analyze(domain, priorAnalysis: analysis, invalidatedNodes: [domain]);

        // Storage model from the codegen pipeline; behavior and aggregate from domain analysis
        var storageModel = infraResult.GetMetadata<Poly.DomainModeling.Analysis.StorageMappingMetadata>(domain)?.Storage;
        var behaviorModel = analysis.GetMetadata<Poly.DomainModeling.Analysis.BehaviorMetadata>(domain)?.Behavior;
        var aggregateModel = analysis.GetMetadata<Poly.DomainModeling.Analysis.OwnershipAggregateMetadata>(domain)?.Aggregate;

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

        // TransportPass is in pipeline but unused — no consumer yet.
        // CrossReferencePass deferred — wire when a consumer needs dependency graphs.

        // DbContext (mode: db or all)
        if (mode == CompileMode.Db || mode == CompileMode.All) {
            var dbContextName = $"{domain.Name}DbContext";
            var dbGen = new DbContextGenerator(domain, storageModel!);
            files.Add(($"{dbContextName}.cs",
                new CSharpGenerator().Generate(dbGen.GenerateCompilationUnit())));

            // Minimal API + .http file (mode: all only)
            if (mode == CompileMode.All) {
                var apiGen = new MinimalApiGenerator(domain,
                    storageModel: storageModel!,
                    behaviorModel: behaviorModel!,
                    aggregateModel: aggregateModel!);
                files.Add(("Program.cs",
                    new CSharpGenerator().Generate(apiGen.GenerateCompilationUnit(dbContextName))));

                var httpGen = new HttpFileGenerator(domain,
                    storageModel: storageModel!,
                    behaviorModel: behaviorModel!,
                    aggregateModel: aggregateModel!);
                files.Add(("demo.http", httpGen.Generate()));
            }
        }

        return files;
    }

    private static CompileResult Fail(string message) =>
        new(Success: false, Files: null, Errors: [message]);
}