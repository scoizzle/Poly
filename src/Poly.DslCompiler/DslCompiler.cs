using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
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
        Compile(polyText, mode, CreateInputs(dbms), dbms);

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
    public CompileResult Compile(string polyText, CompileMode mode, DomainInputSet inputs, DbmsPack dbms) =>
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
            var parser = new PolyDslParser(polyText, parserInputs);
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
            var files = GenerateAllFiles(domain, outcome.Analysis, mode, analysisInputs, dbms);
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
        DomainInputSet inputs) {
        ArgumentNullException.ThrowIfNull(inputs);
        return Compile(polyText, mode, inputs.Parser, inputs.Analysis);
    }

    /// <summary>
    /// Builds explicit parse/analyze inputs for a DBMS pack selection.
    /// Always includes portable <c>column</c>/<c>table</c> annotation syntax.
    /// </summary>
    public static DomainInputSet CreateInputs(DbmsPack dbms) {
        var builder = DomainInputBuilder.CreateWithSqlPack();
        var configured = dbms switch {
            DbmsPack.Generic => builder,
            DbmsPack.Sqlite => builder.AddSqliteDefaults(),
            DbmsPack.SqlServer => builder.AddSqlServerDefaults(),
            _ => throw new ArgumentOutOfRangeException(nameof(dbms), dbms, "Unknown DBMS pack."),
        };

        return configured.Build();
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
        Domain domain, AnalysisResult analysis,
        CompileMode mode = CompileMode.Entities,
        DomainAnalysisInputs? analysisInputs = null,
        DbmsPack dbms = DbmsPack.Generic) {

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
        // StoragePass + TransportPass via UseDomainModelAnalysisPipeline). Fall back
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
        if (mode == CompileMode.Db || mode == CompileMode.All) {
            var dbContextName = $"{domain.Name}DbContext";
            var dbGen = new DbContextGenerator(domain, storageModel!);
            files.Add(($"{dbContextName}.cs",
                new CSharpGenerator().Generate(dbGen.GenerateCompilationUnit())));

            // Minimal API + .http file (mode: all only)
            if (mode == CompileMode.All) {
                var apiGen = new MinimalApiGenerator(domain,
                    analysis: analysis,
                    storageModel: storageModel!,
                    behaviorModel: behaviorModel!,
                    aggregateModel: aggregateModel!,
                    dbms: dbms);
                files.Add(("Program.cs",
                    new CSharpGenerator().Generate(apiGen.GenerateCompilationUnit(dbContextName))));

                var httpGen = new HttpFileGenerator(domain,
                    analysis: analysis,
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