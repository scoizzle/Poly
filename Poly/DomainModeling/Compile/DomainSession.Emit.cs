// DomainSession emit path: module printing and contributed artifacts.
using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

namespace Poly.DomainModeling.Compile;

public sealed partial class DomainSession {
    /// <summary>
    /// Entity-module C# files from the lowered module, then each new
    /// <see cref="ContributedFile"/> from <see cref="Artifacts"/>. The writer consumes
    /// the <see cref="CompiledDomain"/> from <see cref="Compile"/>. VM analysis of the
    /// module runs once for a domain, its domain analysis, and that module reference
    /// on this session. Contributors run once per call, against that call's catalog,
    /// so a second Emit of the same compilation lists each host file once.
    /// </summary>
    public IReadOnlyList<(string FileName, string Source)> Emit(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);
        RequireCoreLoaded();
        var files = new List<(string FileName, string Source)>();
        // The files come from this call's own catalog; the analysis below still covers the
        // whole module, because the writer resolves types across entities from one analysis.
        var (_, catalog) = LowerToCatalog(domain, analysis);
        ArtifactCatalog = catalog;
        var compiled = Compile(domain, analysis);
        RegisterVmAnalysisReport(catalog, domain, compiled.VmAnalysis);
        try {
            ThrowIfVmHasErrors(compiled.VmAnalysis);
        }
        catch {
            // A refused Emit leaves the new catalog on the session (and the VM report
            // above) so the failure is inspectable; the reference report is normally
            // registered after printing.
            RegisterCatalogReferenceReport(catalog, domain, EmitProducer);
            throw;
        }
        var writer = new CSharpWriter(compiled.VmAnalysis);
        // Files come in registration order: entities in domain order, then the scaffolding.
        // Snapshot first: registering a file appends to the catalog being enumerated.
        // Host top-level statements are global Program. An entity of that name stays
        // class Program, but in Poly.Generated (with the DbContext) and not in Program.cs,
        // so the file and the fully-qualified type both differ from the entry point.
        var nestProgram = domain.Types.OfType<Entity>().Any(e => e.Name == "Program");
        var entityTrees = catalog.Artifacts.Where(a => a.Descriptor.Id.Type == EntityType).ToList();
        foreach (var tree in entityTrees) {
            var entityName = tree.Descriptor.Id.Segments[^1];
            var source = writer.Write(TypesOf(tree));
            if (nestProgram)
                source = InGeneratedNamespace(source);
            AddPrintedFile(
                catalog,
                domain,
                files,
                entityName == "Program" ? "Program.Entity.cs" : $"{entityName}.cs",
                source,
                tree);
        }
        var scaffolding = catalog.Find(ArtifactId.Create([domain.Name], ScaffoldingType))!;
        var scaffoldingSource = writer.Write(TypesOf(scaffolding));
        if (nestProgram)
            scaffoldingSource = InGeneratedNamespace(scaffoldingSource);
        AddPrintedFile(catalog, domain, files, "Poly.Types.cs", scaffoldingSource, scaffolding);
        RegisterContributedArtifacts(domain, analysis, catalog, files);
        RegisterCatalogReferenceReport(catalog, domain, EmitProducer);
        return files;
    }

    /// <summary>
    /// Runs each <see cref="Artifacts"/> contributor once and registers what it
    /// returns. A contributed text file whose name is not already in
    /// <paramref name="files"/> is appended. The artifact is stored as returned.
    /// </summary>
    private void RegisterContributedArtifacts(
        Domain domain,
        AnalysisResult analysis,
        ArtifactCatalog catalog,
        List<(string FileName, string Source)> files) {
        var seen = new HashSet<string>(files.Select(f => f.FileName), StringComparer.Ordinal);
        foreach (var contributor in Artifacts) {
            foreach (var artifact in contributor.Contribute(domain, analysis, catalog)) {
                catalog.Register(artifact);
                if (artifact.Descriptor.Id.Type != ContributedFile.Type)
                    continue;
                var name = ContributedFile.FileName(artifact);
                if (!seen.Add(name))
                    continue;
                files.Add((name, ContributedFile.Text(artifact)));
            }
        }
    }

    private static void AddPrintedFile(
        ArtifactCatalog catalog,
        Domain domain,
        List<(string FileName, string Source)> files,
        string fileName,
        string source,
        Artifact tree) {
        files.Add((fileName, source));
        catalog.Register(ContributedFile.Create(domain, fileName, source, EmitProducer, [tree.Descriptor.Id]));
    }

    /// <summary>Moves printed types into <c>Poly.Generated</c>. Usings stay file-scoped
    /// so they still apply. DbContext already lives in that namespace.</summary>
    private static string InGeneratedNamespace(string source) {
        const string ns = "Poly.Generated";
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var headerCount = 0;
        while (headerCount < lines.Length) {
            var trim = lines[headerCount].Trim();
            if (trim is "" or "#nullable enable" or "#nullable disable" or "#nullable restore"
                || trim.StartsWith("using ", StringComparison.Ordinal)) {
                headerCount++;
                continue;
            }
            break;
        }
        while (headerCount > 0 && lines[headerCount - 1].Trim().Length == 0)
            headerCount--;

        var sb = new StringBuilder();
        for (var i = 0; i < headerCount; i++)
            sb.AppendLine(lines[i]);
        sb.AppendLine();
        sb.Append("namespace ").Append(ns).AppendLine(" {");
        for (var i = headerCount; i < lines.Length; i++) {
            if (lines[i].Length == 0)
                sb.AppendLine();
            else
                sb.Append("    ").AppendLine(lines[i]);
        }
        sb.Append('}');
        return sb.ToString().TrimEnd();
    }

    private static IReadOnlyList<TypeDefinitionNode> TypesOf(Artifact tree) =>
        (IReadOnlyList<TypeDefinitionNode>)tree.Payload!;
}