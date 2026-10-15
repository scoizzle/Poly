using System.Diagnostics;

using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Interpretation.CSharp;

using CompileMode = Poly.DslCompiler.CompileMode;
using DbmsPack = Poly.DslCompiler.DbmsPack;
using DslCompileResult = Poly.DslCompiler.DslCompiler.CompileResult;

namespace Poly.Demo.Shared.Services;

public sealed record IdeArtifact(
    string Type,
    string Path,
    string Producer,
    IReadOnlyList<string> References,
    string Text);

public sealed record IdeCompileResult(
    bool Success,
    IReadOnlyList<string> Errors,
    IReadOnlyList<IdeArtifact> Artifacts,
    double ElapsedMs);

/// <summary>
/// Compiles .poly source through the Poly.DslCompiler facade and renders every
/// catalog artifact payload to text for display.
/// </summary>
public sealed class PolyIdeCompiler {
    public IdeCompileResult Compile(string polyText, CompileMode mode, DbmsPack dbms) {
        var watch = Stopwatch.StartNew();
        DslCompileResult result;
        try {
            result = new Poly.DslCompiler.DslCompiler().Compile(polyText, mode, dbms);
        }
        catch (Exception ex) {
            watch.Stop();
            return new IdeCompileResult(false, [$"Compiler crashed: {ex.Message}"], [], watch.Elapsed.TotalMilliseconds);
        }
        watch.Stop();
        if (!result.Success)
            return new IdeCompileResult(false, result.Errors ?? ["Compilation failed."], [], watch.Elapsed.TotalMilliseconds);

        var catalog = result.Catalog!;
        var artifacts = RenderCatalog(catalog);
        artifacts.Add(new IdeArtifact(
            "catalog-index",
            "catalog",
            "-",
            [],
            catalog.ToText()));
        return new IdeCompileResult(true, [], artifacts, watch.Elapsed.TotalMilliseconds);
    }

    private static List<IdeArtifact> RenderCatalog(ArtifactCatalog catalog) {
        // Entity and scaffolding payloads are slices of one lowered module; print them
        // through a single whole-module analysis, the same one DomainSession.Emit uses.
        var module = catalog.Artifacts
            .Where(artifact => artifact.Descriptor.Id.Type is "entity" or "scaffolding")
            .SelectMany(artifact => (IReadOnlyList<TypeDefinitionNode>)artifact.Payload!)
            .ToList();
        var moduleAnalysis = module.Count == 0
            ? null
            : Interpreter.Analyzer.Analyze(new CompilationUnitNode([], null, module, null));
        return catalog.Artifacts.Select(artifact => Render(artifact, moduleAnalysis)).ToList();
    }

    private static IdeArtifact Render(Artifact artifact, AnalysisResult? moduleAnalysis) {
        var descriptor = artifact.Descriptor;
        return new IdeArtifact(
            descriptor.Id.Type,
            descriptor.Id.Path,
            descriptor.Producer,
            descriptor.References.Select(reference => reference.ToString()).ToList(),
            RenderPayload(artifact.Payload, moduleAnalysis));
    }

    private static string RenderPayload(object? payload, AnalysisResult? moduleAnalysis) => payload switch {
        string text => text,
        CompilationUnitNode unit => new CSharpGenerator().Generate(unit),
        IReadOnlyList<TypeDefinitionNode> types => GenerateModule(types, moduleAnalysis),
        AnalysisReport report => FormatReport(report),
        Domain domain => FormatDomain(domain),
        Entity entity => FormatEntity(entity),
        null => string.Empty,
        _ => payload.ToString() ?? string.Empty,
    };

    private static string GenerateModule(IReadOnlyList<TypeDefinitionNode> types, AnalysisResult? moduleAnalysis) {
        var analysis = moduleAnalysis
            ?? Interpreter.Analyzer.Analyze(new CompilationUnitNode([], null, types, null));
        return new CSharpGenerator(analysis).Generate(types);
    }

    private static string FormatReport(AnalysisReport report) {
        if (report.Findings.Count == 0)
            return "(no findings)";
        return string.Join('\n', report.Findings.Select(finding =>
            $"[{finding.Severity}] {finding.ElementPath}: {finding.Message}" +
            (finding.Code is null ? string.Empty : $" ({finding.Code})")));
    }

    private static string FormatDomain(Domain domain) {
        var lines = new List<string> { $"domain {domain.Name}" };
        if (domain.Extensions.Count > 0)
            lines.Add($"uses {string.Join(", ", domain.Extensions)}");
        lines.Add(string.Empty);
        foreach (var type in domain.Types)
            lines.Add($"  {type.Name}: {Describe(type)}");
        return string.Join('\n', lines);
    }

    private static string Describe(DomainType type) => type switch {
        Entity entity =>
            $"entity ({entity.Properties.Count} properties, {entity.Actions.Count} actions, " +
            $"{entity.Policies.Count} policies, {entity.Stages.Count} stages)",
        _ => type.GetType().Name,
    };

    private static string FormatEntity(Entity entity) {
        var lines = new List<string> {
            $"entity {entity.Name}",
            $"  properties: {string.Join(", ", entity.Properties.Select(property => property.Name))}",
        };
        if (entity.Navigations.Count > 0)
            lines.Add($"  navigations: {string.Join(", ", entity.Navigations.Select(nav => nav.Name))}");
        if (entity.Policies.Count > 0)
            lines.Add($"  policies: {string.Join(", ", entity.Policies.Select(policy => policy.Name))}");
        if (entity.Actions.Count > 0)
            lines.Add($"  actions: {string.Join(", ", entity.Actions.Select(action => action.Name))}");
        if (entity.Stages.Count > 0)
            lines.Add($"  stages: {string.Join(", ", entity.Stages.Select(stage => stage.Name))}");
        if (entity.Subscriptions.Count > 0)
            lines.Add($"  subscriptions: {entity.Subscriptions.Count}");
        return string.Join('\n', lines);
    }
}