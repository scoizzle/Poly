using System.Reflection;
using System.Text;

using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Packs.Sqlite;
using Poly.Tests.Docs;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The shipped corpus the lowered-tree compatibility analysis runs on: every golden-catalog domain (the emit set, the
/// lowerable probes, the skipped probes and the swarm repros) and every guide block composed with its wrapper. For a
/// document that analyzes, <see cref="Print"/> lowers it, analyzes each module method as the executor does, and
/// prints the body, every node replacement the analysis records (followed into replacements), the result type of
/// each operator node, the diagnostics and the compile outcome. T-18 compares the prints before and after its change
/// once, as proof; only the enumeration and the print's determinism are pinned here.
/// </summary>
internal static class CompatibilitySweep {
    public sealed record SweepDocument(string Name, string Text);

    private static readonly ExtensionCatalog Catalog = ExtensionCatalog.Core.With(new SqliteLibrary()).With(new HttpLibrary());

    private static readonly string[] Seed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo ModuleField = typeof(ModuleInstanceExecutor).GetField("_module", Private)
        ?? throw new MissingFieldException(nameof(ModuleInstanceExecutor), "_module");

    private static readonly FieldInfo TypesField = typeof(ModuleInstanceExecutor).GetField("_types", Private)
        ?? throw new MissingFieldException(nameof(ModuleInstanceExecutor), "_types");

    private static readonly MethodInfo AnnotateThis = typeof(ModuleInstanceExecutor).GetMethod("AnnotateThis", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(ModuleInstanceExecutor), "AnnotateThis");

    private static readonly MethodInfo BindRootParameterUses = typeof(ModuleInstanceExecutor).GetMethod("BindRootParameterUses", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(ModuleInstanceExecutor), "BindRootParameterUses");

    public static IReadOnlyList<SweepDocument> Documents() {
        var documents = GoldenDomains.All.Select(static d => new SweepDocument(d.Path, d.Poly)).ToList();
        var guide = GuideBlocks.Load();
        foreach (var block in guide.Blocks)
            documents.Add(new SweepDocument($"guide:{block.Line}", GuideBlocks.Compose(block, guide.Wrappers).Text));
        return documents;
    }

    public static string Print(SweepDocument document) {
        var text = new StringBuilder();
        text.Append("# ").AppendLine(document.Name);
        Poly.DomainModeling.Ontology.Domain domain;
        try {
            var session = DomainSession.ForSource(document.Text, Seed, Catalog);
            var changes = DomainCompilation.WithSeed(new PolyDslParser(document.Text, session).Parse(), Seed);
            var outcome = new DomainEvolution(new Poly.DomainModeling.Ontology.Domain("_", [])).ApplyAndAnalyze(changes, session: session);
            if (!outcome.Succeeded) {
                text.AppendLine("domain rejected:");
                foreach (var diagnostic in outcome.Analysis.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error))
                    text.Append("  ").Append(diagnostic.Code).Append(": ").AppendLine(diagnostic.Message);
                return text.ToString();
            }
            domain = outcome.Root!;
            RuntimeAnalysisCache.Bind(domain, session, outcome.Analysis);
        }
        catch (Exception ex) {
            return text.Append("domain throws ").Append(ex.GetType().Name).Append(": ").AppendLine(ex.Message).ToString();
        }

        ModuleInstanceExecutor executor;
        try {
            executor = new ModuleInstanceExecutor(domain);
        }
        catch (Exception ex) {
            return text.Append("lower throws ").Append(ex.GetType().Name).Append(": ").AppendLine(ex.Message).ToString();
        }
        var module = (IReadOnlyList<TypeDefinitionNode>)ModuleField.GetValue(executor)!;
        var types = (ITypeDefinitionProvider)TypesField.GetValue(executor)!;
        foreach (var type in module) {
            foreach (var method in type.Methods ?? []) {
                if (method.Body is not { } body)
                    continue;
                text.Append("## ").Append(type.Name).Append('.').AppendLine(method.Name);
                text.AppendLine(body.ToString());
                var parameters = method.Parameters ?? [];
                AnalysisResult analysis;
                try {
                    var entityType = types.GetTypeDefinition(type.Name);
                    analysis = Interpreter.Analyze(body, types, setup: ctx => {
                        if (entityType is not null)
                            AnnotateThis.Invoke(null, [ctx, body, entityType]);
                        if (parameters.Count > 0)
                            BindRootParameterUses.Invoke(null, [ctx, body, parameters]);
                    });
                }
                catch (Exception ex) {
                    text.Append("analysis throws ").Append(ex.GetType().Name).Append(": ").AppendLine(ex.Message);
                    continue;
                }
                Walk(analysis, body, text, new HashSet<Node>(System.Collections.Generic.ReferenceEqualityComparer.Instance));
                foreach (var diagnostic in analysis.Diagnostics.Where(static d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning))
                    text.Append("  diagnostic ").Append(diagnostic.Severity).Append(' ').Append(diagnostic.Code).Append(": ").AppendLine(diagnostic.Message);
                try {
                    _ = parameters.Count == 0 ? Interpreter.Compile(body, analysis) : Interpreter.Compile(body, analysis, parameters);
                    text.AppendLine("  compile ok");
                }
                catch (Exception ex) {
                    text.Append("  compile ").Append(ex.GetType().Name).Append(": ").AppendLine(ex.Message);
                }
            }
        }
        return text.ToString();
    }

    /// <summary>A replacement may contain the node it replaces; the path set stops that walk where it would repeat.</summary>
    private static void Walk(AnalysisResult analysis, Node node, StringBuilder text, HashSet<Node> path) {
        if (!path.Add(node)) {
            text.Append("  revisit ").AppendLine(node.ToString());
            return;
        }
        if (node is Add or Subtract or Multiply or Divide or Modulo or Equal or NotEqual
            or LessThan or LessThanOrEqual or GreaterThan or GreaterThanOrEqual)
            text.Append("  type ").Append(node).Append(" : ").AppendLine(analysis.Metadata.GetResolvedType(node)?.FullName ?? "unresolved");
        if (analysis.Metadata.GetNodeReplacement(node) is { } replacement) {
            text.Append("  replace ").Append(node).Append(" => ").AppendLine(replacement.ToString());
            Walk(analysis, replacement, text, path);
        }
        foreach (var child in node.Children) {
            if (child is not null)
                Walk(analysis, child, text, path);
        }
        path.Remove(node);
    }
}

public class CompatibilitySweepTests {
    [Test]
    public async Task Sweep_EnumeratesEveryShippedDocument() {
        var documents = CompatibilitySweep.Documents();
        await Assert.That(documents.Count).IsEqualTo(GoldenDomains.All.Count + GuideBlocks.PolyFences);
        await Assert.That(documents.Select(static d => d.Name).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(documents.Count);
        await Assert.That(documents.Count).IsEqualTo(SweepDocumentCount);
    }

    // Pinned on 0f79ba45 (70), +1 for V-14's DMDEP002 guide fence: the golden catalog and the guide's poly fences.
    private const int SweepDocumentCount = 72;

    /// <summary>
    /// Two prints of one document are equal, so a before/after difference is a change. The guide documents keep it
    /// cheap: each module method's analysis covers the whole module, so the large catalog domains cost minutes.
    /// </summary>
    [Test]
    public async Task Sweep_PrintIsDeterministic() {
        foreach (var document in CompatibilitySweep.Documents().Where(static d => d.Name.StartsWith("guide:", StringComparison.Ordinal))) {
            var first = CompatibilitySweep.Print(document);
            var second = CompatibilitySweep.Print(document);
            await Assert.That(second).IsEqualTo(first);
        }
    }
}