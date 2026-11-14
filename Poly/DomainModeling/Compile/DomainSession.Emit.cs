// DomainSession emit path: module printing and contributed artifacts.
using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

namespace Poly.DomainModeling.Compile;

public sealed partial class DomainSession {
    private const string VmAnalysisReportType = "vm-analysis-report";
    private const string EmitProducer = "Emit";

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
        var ns = CSharpNamespace(domain);
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
        var entityTrees = catalog.Artifacts.Where(a => a.Descriptor.Id.Type == EntityType).ToList();
        foreach (var tree in entityTrees) {
            var entityName = tree.Descriptor.Id.Segments[^1];
            // Host top-level statements are global Program, so an entity of that name keeps its
            // class name but lands in Program.Entity.cs: the file name is deduped against the host Program.cs.
            AddPrintedFile(
                catalog,
                domain,
                files,
                entityName == "Program" ? "Program.Entity.cs" : $"{entityName}.cs",
                writer.Write(Unit(ns, TypesOf(tree))),
                tree);
        }
        var scaffolding = catalog.Find(ArtifactId.Create([domain.Name], ScaffoldingType))!;
        var scaffoldingTypes = TypesOf(scaffolding);
        // An empty types file is the nullable header alone (R-03 / O-R2(a)): no banner, no namespace.
        var scaffoldingSource = scaffoldingTypes.Count == 0
            ? writer.Write(scaffoldingTypes)
            : writer.Write(Unit(ns, scaffoldingTypes));
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

    /// <summary>
    /// One <c>vm-analysis-report</c> per domain, from interpretation analysis of the
    /// whole lowered module. Always registered on Emit (empty when the analyzer
    /// reported nothing).
    /// </summary>
    private static void RegisterVmAnalysisReport(ArtifactCatalog catalog, Domain domain, AnalysisResult analysis) {
        catalog.DeclareType(VmAnalysisReportType, mayPointAt: []);
        var findings = analysis.Diagnostics
            .Select(d => new AnalysisFinding(
                d.Code,
                d.Severity,
                d.Message,
                DomainElementPath.Resolve(domain, d.Node)))
            .ToArray();
        catalog.Register(new Artifact(
            new ArtifactDescriptor(ArtifactId.Create([domain.Name], VmAnalysisReportType), EmitProducer),
            Payload: new AnalysisReport(findings)));
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

    /// <summary>The domain's C# namespace: its name, when every dot-separated segment is an ASCII
    /// identifier that is not a C# keyword and the first is not <c>Poly</c> or <c>System</c>. Anything
    /// else fails closed: keyword and non-ASCII names are rejected until E-10 (@-escaping).</summary>
    public static string CSharpNamespace(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        var name = domain.Name;
        var segments = name.Split('.');
        foreach (var segment in segments) {
            if (!CSharpWriter.IsIdentifierSegment(segment))
                throw RefusedNamespace(name, $"segment '{segment}' is not an ASCII C# identifier");
            if (CSharpWriter.IsKeyword(segment))
                throw RefusedNamespace(name, $"segment '{segment}' is a C# keyword");
        }
        if (segments[0] is "Poly" or "System")
            throw RefusedNamespace(name, $"'{segments[0]}' is reserved for the generated runtime namespaces");
        return name;
    }

    private static InvalidOperationException RefusedNamespace(string name, string reason) =>
        new($"Domain '{name}' cannot name a C# namespace: {reason}. Keyword and non-ASCII names are rejected until E-10 escapes them.");

    /// <summary>A host file (global scope, <c>using &lt;ns&gt;;</c>) sees the global <c>Program</c>, the domain's
    /// own root namespace and every type its other usings import (an entity named <c>Task</c> is ambiguous with
    /// <c>System.Threading.Tasks.Task</c>) ahead of or beside a using-imported domain type, so a generated domain type is
    /// spelled <c>&lt;ns&gt;.&lt;name&gt;</c> there, with a keyword name escaped (<c>&lt;ns&gt;.@event</c>). Any other
    /// name stays bare: a CLR type, and a library primitive such as the temporal <c>DateTime</c>, which is no generated type.</summary>
    internal static string QualifyForHost(Domain domain, string typeName) =>
        domain.Types.Any(t => t is not Ontology.PrimitiveType && t.Name == typeName)
            ? CSharpNamespace(domain) + "." + (CSharpWriter.IsKeyword(typeName) ? "@" + typeName : typeName)
            : typeName;

    private static CompilationUnitNode Unit(string ns, IReadOnlyList<TypeDefinitionNode> types) =>
        new([], ns, types, null);

    private static IReadOnlyList<TypeDefinitionNode> TypesOf(Artifact tree) =>
        (IReadOnlyList<TypeDefinitionNode>)tree.Payload!;
}