using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;
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
    /// Lower and Emit declare <c>file</c>, <c>host-tree</c>, and <c>http-file</c>.
    /// Emit registers one <c>file</c> per printed C# file, each pointing at the tree it
    /// came from. The compiler then registers contributor artifacts (host trees, their
    /// printed files, and <c>demo.http</c>) and writes the text files from this catalog.
    /// The next Lower or Emit drops anything registered after that.
    /// </summary>
    public ArtifactCatalog ArtifactCatalog { get; private set; } = new();

    private const string ScaffoldingType = "scaffolding";
    private const string EntityType = "entity";
    private const string AnalysisReportType = "analysis-report";
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
        catalog.DeclareType(HostTree.Type, mayPointAt: []);
        catalog.DeclareType(ContributedFile.Type, mayPointAt: [EntityType, ScaffoldingType, HostTree.Type]);
        catalog.DeclareType(HttpFile.Type, mayPointAt: []);
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
        var interpAnalysis = TryAnalyzeForEmit(module);
        var generator = interpAnalysis is not null
            ? new CSharpGenerator(interpAnalysis)
            : new CSharpGenerator();
        // Files come in registration order: entities in domain order, then the scaffolding.
        // Each printed file is a catalog artifact pointing at the tree it was printed from.
        var entityTrees = catalog.Artifacts.Where(a => a.Descriptor.Id.Type == EntityType).ToList();
        foreach (var tree in entityTrees) {
            var name = $"{tree.Descriptor.Id.Segments[^1]}.cs";
            var source = generator.Generate(TypesOf(tree));
            catalog.Register(ContributedFile.Create(domain, name, source, EmitProducer, tree.Descriptor.Id));
            files.Add((name, source));
        }
        var scaffolding = catalog.Find(ArtifactId.Create([domain.Name], ScaffoldingType))!;
        var scaffoldingSource = generator.Generate(TypesOf(scaffolding));
        catalog.Register(ContributedFile.Create(
            domain, "Poly.Types.cs", scaffoldingSource, EmitProducer, scaffolding.Descriptor.Id));
        files.Add(("Poly.Types.cs", scaffoldingSource));
        return files;
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

    private static IReadOnlyList<TypeDefinitionNode> TypesOf(Artifact tree) =>
        (IReadOnlyList<TypeDefinitionNode>)tree.Payload!;

    /// <summary>
    /// Runs interpretation analysis on lowered type definitions so the C# generator
    /// can use type-aware features (variable type resolution, DCE).
    /// </summary>
    internal static AnalysisResult? TryAnalyzeForEmit(IReadOnlyList<TypeDefinitionNode> allTypes) {
        if (allTypes.Count == 0)
            return null;
        var unit = new CompilationUnitNode([], null, allTypes, null);
        return Interpretation.Interpreter.Analyzer.Analyze(unit);
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