using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;
using Poly.Interpretation;
using Poly.Interpretation.CSharp;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Compilation unit: Domain facts plus the concepts its <see cref="Domain.Extensions"/>
/// load (language tables, folds, meaning, type maps, artifacts). Not an MCP session.
/// The only assembler — unknown extension ids fail closed.
/// </summary>
public sealed class DomainSession {
    public Domain? Domain { get; }

    public IReadOnlyList<string> Extensions { get; }

    public Language<DslToken, DslTokenKind> Language { get; }

    public AnnotationRegistry Annotations { get; }

    public ExpressionFormRegistry ExpressionForms { get; }

    public TypeMappingRegistry TypeMaps { get; }

    public IReadOnlyList<IStorageConvention> StorageConventions { get; }

    public ExpressionFoldTable Folds { get; }

    public ExpressionMeaning Meaning { get; }

    /// <summary>Primitive types loaded libraries seed, keyed by library id.</summary>
    public IReadOnlyList<(string LibraryId, string Name, TypeCategory Category)> PrimitiveSeeds { get; }

    public IReadOnlyList<IArtifactContributor> Artifacts { get; }

    internal IReadOnlyList<INodeAnalyzer> ExtraAnalyzers { get; }

    /// <summary>
    /// Empty before the first Lower or Emit. Each <see cref="Lower"/> or <see cref="Emit"/> replaces
    /// it with a new catalog holding the trees it made and the domain elements they came from
    /// (when calls overlap, the last to finish wins): one <c>scaffolding</c> tree for the domain
    /// pointing at a <c>source-domain</c> artifact, one <c>entity</c> tree per entity
    /// (the entity type and its stage enum) pointing at a <c>source-entity</c> artifact,
    /// and one <c>analysis-report</c> for the domain's findings.
    /// Lower declares <c>file</c> and <c>tree</c> and registers neither. Emit registers
    /// one <c>vm-analysis-report</c> for the interpretation analysis of the whole module
    /// (empty when the analyzer reported nothing) and each printed <c>.cs</c> file with
    /// a reference to the entity or scaffolding tree it printed. After the catalog is
    /// complete, Lower and Emit each register one <c>catalog-reference-report</c>
    /// (empty when every reference resolves). The compiler then
    /// registers contributor trees and text files here and writes the text files from
    /// this catalog. The next Lower or Emit drops anything registered after that,
    /// including those files.
    /// </summary>
    public ArtifactCatalog ArtifactCatalog { get; private set; } = new();

    private const string ScaffoldingType = "scaffolding";
    private const string EntityType = "entity";
    private const string AnalysisReportType = "analysis-report";
    private const string VmAnalysisReportType = "vm-analysis-report";
    private const string CatalogReferenceReportType = "catalog-reference-report";
    private const string SourceDomainType = "source-domain";
    private const string SourceEntityType = "source-entity";
    private const string LowerProducer = "Lower";
    private const string EmitProducer = "Emit";

    private Analyzer? _analyzer;

    /// <summary>The session's analysis pipeline: core product passes plus library analyzers.</summary>
    private Analyzer Analyzer =>
        _analyzer ??= DomainModelAnalyzer.BuildPipeline(ExtraAnalyzers, Meaning, ExpressionForms);

    internal DomainSession(
        Domain? domain,
        IReadOnlyList<string> extensions,
        Language<DslToken, DslTokenKind> language,
        AnnotationRegistry annotations,
        ExpressionFormRegistry expressionForms,
        TypeMappingRegistry typeMaps,
        IReadOnlyList<IStorageConvention> storageConventions,
        ExpressionFoldTable folds,
        ExpressionMeaning meaning,
        IReadOnlyList<IArtifactContributor>? artifacts = null,
        IReadOnlyList<INodeAnalyzer>? extraAnalyzers = null,
        IReadOnlyList<(string LibraryId, string Name, TypeCategory Category)>? primitiveSeeds = null) {
        Domain = domain;
        Extensions = extensions;
        Language = language;
        Annotations = annotations;
        ExpressionForms = expressionForms;
        TypeMaps = typeMaps;
        StorageConventions = storageConventions;
        Folds = folds;
        Meaning = meaning;
        Artifacts = artifacts ?? [];
        ExtraAnalyzers = extraAnalyzers ?? [];
        PrimitiveSeeds = primitiveSeeds ?? [];
    }

    /// <summary>Loads libraries for an existing domain's extension ids. Unknown id throws.</summary>
    public static DomainSession Open(Domain domain, ExtensionCatalog? catalog = null) {
        ArgumentNullException.ThrowIfNull(domain);
        return ForExtensions(domain.Extensions, catalog ?? ExtensionCatalog.Core).WithDomain(domain);
    }

    /// <summary>Loads libraries for explicit extension ids (parse/print/analyze before a domain exists). Unknown id throws.</summary>
    public static DomainSession ForExtensions(
        IReadOnlyList<string> extensions,
        ExtensionCatalog? catalog = null) {
        ArgumentNullException.ThrowIfNull(extensions);
        var resolved = catalog ?? ExtensionCatalog.Core;
        var builder = SessionBuilder.CreateEmpty();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in extensions) {
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Domain extension id must be non-empty.");
            if (!seen.Add(id))
                throw new InvalidOperationException($"Domain lists extension '{id}' more than once.");
            builder.Load(resolved.Resolve(id));
        }
        return builder.Build();
    }

    /// <summary>Peeks <c>uses</c> (or <paramref name="seed"/>) and loads those libraries. Unknown id throws.</summary>
    public static DomainSession ForSource(
        string poly,
        IReadOnlyList<string> seed,
        ExtensionCatalog? catalog = null) {
        ArgumentNullException.ThrowIfNull(poly);
        ArgumentNullException.ThrowIfNull(seed);
        var ids = DomainCompilation.PeekExtensions(poly);
        if (ids.Count == 0)
            ids = seed;
        return ForExtensions(ids, catalog);
    }

    /// <summary>Keeps tables when <c>uses</c> is unchanged; reloads when it changes.</summary>
    public DomainSession WithDomain(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        if (SameExtensions(Extensions, domain.Extensions))
            return new DomainSession(domain, domain.Extensions, Language, Annotations, ExpressionForms, TypeMaps, StorageConventions, Folds, Meaning, Artifacts, ExtraAnalyzers, PrimitiveSeeds);
        return Open(domain);
    }

    /// <summary>
    /// Analyzes <paramref name="domain"/> with this session's pipeline (type maps included).
    /// The session is bound before the pipeline so passes that ask the cache for maps
    /// see this compilation, not a second session built from <see cref="ExtensionCatalog.Core"/>.
    /// </summary>
    public AnalysisResult Analyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        RuntimeAnalysisCache.Bind(domain, this);
        var analysis = Analyzer.Analyze(domain);
        RuntimeAnalysisCache.Bind(domain, this, analysis);
        return analysis;
    }

    /// <summary>
    /// Analyze without rebinding the cache. Used by the cache itself to avoid
    /// recursion when a fallback session must produce bags.
    /// </summary>
    internal AnalysisResult AnalyzeWithoutBind(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return Analyzer.Analyze(domain);
    }

    /// <summary>
    /// Stage 3: one operation module (type definitions, action bodies,
    /// policy methods, subscription handlers, and OnEntry/OnExit bodies).
    /// Simulate and print consume this result. Throws <see cref="InvalidOperationException"/>
    /// naming the first analysis error before the catalog is replaced. Throws
    /// <see cref="FormatException"/> when a domain or entity name cannot be part of an
    /// artifact id, and <see cref="InvalidOperationException"/> when two lowered types
    /// would collide (see <c>RegisterTrees</c>).
    /// </summary>
    public IReadOnlyList<TypeDefinitionNode> Lower(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);
        var (module, catalog) = LowerToCatalog(domain, analysis);
        RegisterCatalogReferenceReport(catalog, domain, LowerProducer);
        ArtifactCatalog = catalog;
        return module;
    }

    // Lower and Emit both refuse here, before any catalog exists. Emit uses the catalog
    // returned here, not the ArtifactCatalog property, which another Lower or Emit on
    // this session may replace at any time.
    private (IReadOnlyList<TypeDefinitionNode> Module, ArtifactCatalog Catalog) LowerToCatalog(
        Domain domain, AnalysisResult analysis) {
        DomainModelAnalyzer.ThrowIfHasErrors(analysis);
        var module = RuntimeAnalysisCache.GetOrLower(domain, this, analysis);
        var catalog = new ArtifactCatalog();
        RegisterSourceElements(catalog, domain);
        RegisterTrees(catalog, domain, module);
        RegisterAnalysisReport(catalog, domain, analysis);
        catalog.DeclareType(GeneratedTree.Type, mayPointAt: []);
        catalog.DeclareType(ContributedFile.Type, mayPointAt: [EntityType, ScaffoldingType, GeneratedTree.Type]);
        return (module, catalog);
    }

    /// <summary>
    /// Registers the domain as a <c>source-domain</c> artifact and each entity as a
    /// <c>source-entity</c> artifact. Tree artifacts point at these so a debugger step
    /// can be traced back to the authored element.
    /// </summary>
    internal static void RegisterSourceElements(ArtifactCatalog catalog, Domain domain) {
        catalog.DeclareType(SourceDomainType, mayPointAt: []);
        catalog.DeclareType(SourceEntityType, mayPointAt: []);
        catalog.Register(new Artifact(
            new ArtifactDescriptor(ArtifactId.Create([domain.Name], SourceDomainType), LowerProducer),
            Payload: domain));
        foreach (var entity in domain.Types.OfType<Entity>()) {
            catalog.Register(new Artifact(
                new ArtifactDescriptor(ArtifactId.Create([domain.Name, entity.Name], SourceEntityType), LowerProducer),
                Payload: entity));
        }
    }

    /// <summary>
    /// Splits the module into one tree per entity (the entity type and its stage enum) and one
    /// scaffolding tree holding everything else (domain enums, DomainResult, and so on).
    /// Each tree's payload is a read-only copy of its type definitions. Each entity tree points
    /// at the matching <c>source-entity</c> artifact and the scaffolding tree at <c>source-domain</c>.
    /// A type that two trees would hold, or that the module defines twice (for example an entity
    /// named <c>DomainResult</c>, or entities <c>X</c> and <c>XStage</c>), is refused: the printed
    /// C# would not compile either.
    /// </summary>
    internal static void RegisterTrees(ArtifactCatalog catalog, Domain domain, IReadOnlyList<TypeDefinitionNode> module) {
        catalog.DeclareType(ScaffoldingType, mayPointAt: [SourceDomainType]);
        catalog.DeclareType(EntityType, mayPointAt: [SourceEntityType]);
        var entities = domain.Types.OfType<Entity>().ToList();
        static bool Belongs(TypeDefinitionNode type, Entity entity) =>
            type.Name == entity.Name || type.Name == $"{entity.Name}Stage";
        foreach (var type in module) {
            var owners = entities.Where(e => Belongs(type, e)).Select(e => e.Name).ToList();
            if (owners.Count > 1)
                throw new InvalidOperationException(
                    $"Type '{type.Name}' belongs to the trees of entities '{owners[0]}' and '{owners[1]}'.");
        }
        var twice = module
            .GroupBy(t => (t.Name, Arity: t.GenericParameters?.Count ?? 0))
            .FirstOrDefault(g => g.Count() > 1);
        if (twice is not null)
            throw new InvalidOperationException($"The lowered module defines type '{twice.Key.Name}' more than once.");
        foreach (var entity in entities) {
            var trees = module.Where(t => Belongs(t, entity)).ToArray();
            catalog.Register(new Artifact(
                new ArtifactDescriptor(
                    ArtifactId.Create([domain.Name, entity.Name], EntityType),
                    LowerProducer,
                    [ArtifactId.Create([domain.Name, entity.Name], SourceEntityType)]),
                Payload: trees.AsReadOnly()));
        }
        var scaffolding = module.Where(t => !entities.Any(e => Belongs(t, e))).ToArray();
        catalog.Register(new Artifact(
            new ArtifactDescriptor(
                ArtifactId.Create([domain.Name], ScaffoldingType),
                LowerProducer,
                [ArtifactId.Create([domain.Name], SourceDomainType)]),
            Payload: scaffolding.AsReadOnly()));
    }

    /// <summary>
    /// Entity-module C# files from the lowered module. Persistence and HTTP host
    /// call-sites come from Load-registered <see cref="IArtifactContributor"/>s,
    /// not this method.
    /// </summary>
    public IReadOnlyList<(string FileName, string Source)> Emit(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);
        var files = new List<(string FileName, string Source)>();
        // The files come from this call's own catalog; the analysis below still covers the
        // whole module, because the generator resolves types across entities from one analysis.
        var (module, catalog) = LowerToCatalog(domain, analysis);
        ArtifactCatalog = catalog;
        var interpAnalysis = Interpreter.Analyzer.Analyze(new CompilationUnitNode([], null, module, null));
        RegisterVmAnalysisReport(catalog, domain, interpAnalysis);
        var generator = new CSharpGenerator(interpAnalysis);
        // Files come in registration order: entities in domain order, then the scaffolding.
        // Snapshot first: registering a file appends to the catalog being enumerated.
        var entityTrees = catalog.Artifacts.Where(a => a.Descriptor.Id.Type == EntityType).ToList();
        foreach (var tree in entityTrees) {
            AddPrintedFile(
                catalog,
                domain,
                files,
                $"{tree.Descriptor.Id.Segments[^1]}.cs",
                generator.Generate(TypesOf(tree)),
                tree);
        }
        var scaffolding = catalog.Find(ArtifactId.Create([domain.Name], ScaffoldingType))!;
        AddPrintedFile(catalog, domain, files, "Poly.Types.cs", generator.Generate(TypesOf(scaffolding)), scaffolding);
        RegisterCatalogReferenceReport(catalog, domain, EmitProducer);
        return files;
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

    /// <summary>
    /// One <c>analysis-report</c> per domain. Each finding carries the id path of the
    /// domain element the diagnostic was reported on. Always registered (empty when
    /// analysis produced no diagnostics) so Lower and Emit catalogs share the type.
    /// </summary>
    private static void RegisterAnalysisReport(ArtifactCatalog catalog, Domain domain, AnalysisResult analysis) {
        catalog.DeclareType(AnalysisReportType, mayPointAt: []);
        var findings = analysis.Diagnostics
            .Select(d => new AnalysisFinding(
                d.Code,
                d.Severity,
                d.Message,
                DomainElementPath.Resolve(domain, d.Node)))
            .ToArray();
        catalog.Register(new Artifact(
            new ArtifactDescriptor(ArtifactId.Create([domain.Name], AnalysisReportType), "Analyze"),
            Payload: new AnalysisReport(findings)));
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

    /// <summary>
    /// Maps each dangling or wrong-type catalog reference to an Error finding.
    /// <see cref="AnalysisFinding.ElementPath"/> is the referrer's id path.
    /// </summary>
    internal static IReadOnlyList<AnalysisFinding> CatalogReferenceFindings(ArtifactCatalog catalog) {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.FindDanglingOrWrongType()
            .Select(p => new AnalysisFinding(
                p.Kind.ToString(),
                DiagnosticSeverity.Error,
                $"{p.From} points at {p.Target}",
                p.From.Path))
            .ToArray();
    }

    /// <summary>
    /// One <c>catalog-reference-report</c> per catalog, from
    /// <see cref="ArtifactCatalog.FindDanglingOrWrongType"/>. Always registered
    /// after Lower or Emit has filled the catalog (empty when every reference
    /// resolves).
    /// </summary>
    private static void RegisterCatalogReferenceReport(ArtifactCatalog catalog, Domain domain, string producer) {
        catalog.DeclareType(CatalogReferenceReportType, mayPointAt: []);
        catalog.Register(new Artifact(
            new ArtifactDescriptor(ArtifactId.Create([domain.Name], CatalogReferenceReportType), producer),
            Payload: new AnalysisReport(CatalogReferenceFindings(catalog))));
    }

    private static IReadOnlyList<TypeDefinitionNode> TypesOf(Artifact tree) =>
        (IReadOnlyList<TypeDefinitionNode>)tree.Payload!;

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> naming the first VM analysis error.
    /// </summary>
    internal static void ThrowIfVmHasErrors(AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(analysis);
        if (!analysis.HasErrors)
            return;
        var first = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        throw new InvalidOperationException(first?.Message ?? "VM analysis reported errors.");
    }

    internal static ExpressionFoldTable FoldsFor(ExpressionFormRegistry forms) {
        var folds = ExpressionFoldTable.Core();
        forms.ContributeFolds(folds);
        return folds;
    }

    private static bool SameExtensions(IReadOnlyList<string> left, IReadOnlyList<string> right) {
        if (left.Count != right.Count)
            return false;
        for (var i = 0; i < left.Count; i++) {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}