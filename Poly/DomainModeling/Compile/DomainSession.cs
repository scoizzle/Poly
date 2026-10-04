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
    /// Empty before Lower. Each <see cref="Lower"/> replaces it with a new catalog holding the
    /// trees it made: one <c>scaffolding</c> tree for the domain and one <c>entity</c> tree per
    /// entity (the entity type and its stage enum). It is not an emit/contributor file inventory.
    /// This is the live instance: anything registered on it by hand is dropped by the next Lower.
    /// </summary>
    public ArtifactCatalog ArtifactCatalog { get; private set; } = new();

    private const string ScaffoldingType = "scaffolding";
    private const string EntityType = "entity";

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

    /// <summary>Analyzes <paramref name="domain"/> with this session's pipeline (type maps included).</summary>
    public AnalysisResult Analyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
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
    /// entity-level policy methods, subscription handlers, and OnEntry/OnExit
    /// bodies). Action- and stage-scoped policies are not carried here.
    /// Simulate and print consume this result.
    /// </summary>
    public IReadOnlyList<TypeDefinitionNode> Lower(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);
        var (module, catalog) = LowerToCatalog(domain, analysis);
        ArtifactCatalog = catalog;
        return module;
    }

    // Emit uses the catalog returned here, not the ArtifactCatalog property, which another
    // Lower on this session may replace at any time.
    private (IReadOnlyList<TypeDefinitionNode> Module, ArtifactCatalog Catalog) LowerToCatalog(
        Domain domain, AnalysisResult analysis) {
        var module = RuntimeAnalysisCache.GetOrLower(domain, this, analysis);
        return (module, RegisterTrees(domain, module));
    }

    /// <summary>
    /// Splits the module into one tree per entity (the entity type and its stage enum) and one
    /// scaffolding tree holding everything else (domain enums, DomainResult, and so on).
    /// Each tree's payload is a read-only copy of its type definitions. A type that two trees
    /// would hold, or that the module defines twice (for example an entity named
    /// <c>DomainResult</c>, or entities <c>X</c> and <c>XStage</c>), is refused: the printed C#
    /// would not compile either.
    /// </summary>
    private static ArtifactCatalog RegisterTrees(Domain domain, IReadOnlyList<TypeDefinitionNode> module) {
        var catalog = new ArtifactCatalog();
        catalog.DeclareType(ScaffoldingType, mayPointAt: []);
        catalog.DeclareType(EntityType, mayPointAt: []);
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
            if (trees.Length == 0)
                throw new InvalidOperationException(
                    $"DomainProgramProjection produced no type definitions for entity '{entity.Name}'.");
            catalog.Register(new Artifact(
                new ArtifactDescriptor(ArtifactId.Create([domain.Name, entity.Name], EntityType), "Lower"),
                Payload: trees.AsReadOnly()));
        }
        var scaffolding = module.Where(t => !entities.Any(e => Belongs(t, e))).ToArray();
        catalog.Register(new Artifact(
            new ArtifactDescriptor(ArtifactId.Create([domain.Name], ScaffoldingType), "Lower"),
            Payload: scaffolding.AsReadOnly()));
        return catalog;
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
        foreach (var tree in catalog.Artifacts.Where(a => a.Descriptor.Id.Type == EntityType))
            files.Add(($"{tree.Descriptor.Id.Segments[^1]}.cs", generator.Generate(TypesOf(tree))));
        var scaffolding = catalog.Find(ArtifactId.Create([domain.Name], ScaffoldingType))!;
        files.Add(("Poly.Types.cs", generator.Generate(TypesOf(scaffolding))));
        return files;
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