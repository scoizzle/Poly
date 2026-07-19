using System.Text;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Parsing;
using Poly.Interpretation.CSharp;
using Poly.Syntax.Analysis;

namespace Poly.DslCompiler;

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
    /// Compiles .poly DSL text into C# source files.
    /// </summary>
    public CompileResult Compile(string polyText) {
        if (string.IsNullOrWhiteSpace(polyText))
            return Fail("DSL text is empty.");

        // ── 1. Parse ─────────────────────────────────────────────
        List<DomainChange> changes;
        try {
            var parser = new PolyDslParser(polyText);
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
        var files = GenerateFiles(domain);

        return new CompileResult(
            Success: true,
            Files: files,
            Errors: null
        );
    }

    // ── C# generation ───────────────────────────────────────────

    private static IReadOnlyList<(string FileName, string Source)> GenerateFiles(Domain domain) {
        var exporter = new DomainToCSharpExporter();
        var combinedDefs = exporter.Export(domain);
        var entities = domain.Types.OfType<Entity>().ToList();
        var perEntityFiles = new List<(string FileName, string Source)>();

        // Combined single file with all types
        var combinedGenerator = new CSharpGenerator();
        var combinedSource = combinedGenerator.Generate(combinedDefs);
        perEntityFiles.Add(("_all.cs", combinedSource));

        // Per-entity files
        foreach (var entity in entities) {
            var entityDefs = exporter.Export(new Domain(domain.Name, [entity], []));
            var generator = new CSharpGenerator();
            var csharp = generator.Generate(entityDefs);
            perEntityFiles.Add(($"{entity.Name}.cs", csharp));
        }

        return perEntityFiles;
    }

    private static CompileResult Fail(string message) =>
        new(Success: false, Files: null, Errors: [message]);
}