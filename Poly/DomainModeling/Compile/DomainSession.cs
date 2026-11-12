using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Grammar;
using Poly.Interpretation;
using Poly.Interpretation.CSharp;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Compilation unit: Domain facts plus the concepts its <see cref="Domain.Extensions"/>
/// load (language tables, folds, meaning, type maps, artifacts). Not an MCP session.
/// The only assembler — unknown extension ids fail closed.
/// </summary>
public sealed partial class DomainSession {
    public Domain? Domain { get; }

    public IReadOnlyList<string> Extensions { get; }

    /// <summary>One entry per library and pipeline stage it enters, in registration order.</summary>
    public IReadOnlyList<StageContribution> Contributions { get; }

    public Language<DslToken, DslTokenKind> Language { get; }

    public AnnotationRegistry Annotations { get; }

    public ExpressionFormRegistry ExpressionForms { get; }

    public TypeMappingRegistry TypeMaps { get; }

    public IReadOnlyList<IStorageConvention> StorageConventions { get; }

    public ExpressionFoldTable Folds { get; }

    public ExpressionMeaning Meaning { get; }

    /// <summary>Primitive types loaded libraries seed, keyed by library id.</summary>
    public IReadOnlyList<(string LibraryId, string Name, PrimitiveSemantics Semantics)> PrimitiveSeeds { get; }

    /// <summary>
    /// Contract surfaces libraries contributed from <see cref="IDomainLibrary.Register"/>.
    /// <c>uses &lt;id&gt;</c> seeds the matching surface onto the resolved domain.
    /// </summary>
    public IReadOnlyList<ContractContribution> ContractContributions { get; }

    /// <summary>The inputs this session's libraries were selected from; a reload reuses them.</summary>
    public SessionOptions Options { get; }

    /// <summary>
    /// The host target this compilation is for (<c>posix</c>, <c>windows</c>), or <c>null</c>
    /// when the host named none. A host fact (spike #413 decision 3): not a <c>uses</c> id,
    /// not a <see cref="Domain"/> fact, and never implied by the ids the source lists.
    /// Analysis compares it with each target-declaring contract contribution.
    /// </summary>
    public string? Target => Options.Target;

    public IReadOnlyList<IArtifactContributor> Artifacts { get; }

    internal IReadOnlyList<INodeAnalyzer> Analyzers { get; }

    /// <summary>
    /// Copies the platform's reader found, bound at build time and frozen for this session.
    /// Analysis checks each with the assign checks.
    /// </summary>
    public IReadOnlyList<ObservedFlow> ObservedFlows { get; }

    /// <summary>
    /// Acknowledgements of the diagnostics the <see cref="ObservedFlows"/> checks emit, frozen
    /// with the session. A change needs a new session (and a new <see cref="Domain"/> instance
    /// before lowering).
    /// </summary>
    public IReadOnlyList<DiagnosticAllowance> DiagnosticAllowances { get; }

    /// <summary>
    /// Compilers libraries added at <see cref="SessionBuilder.AddConstraintCompiler"/>.
    /// Empty when no loaded library compiles a constraint into the produced method.
    /// Core applies them once, inside lowering, so print and the interpreter share the result.
    /// </summary>
    public IReadOnlyList<IConstraintCompiler> ConstraintCompilers { get; }

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
    /// a reference to the entity or scaffolding tree it printed. Emit then runs each
    /// <see cref="Artifacts"/> contributor once and registers what
    /// <see cref="IArtifactContributor.Contribute"/> returns. After that catalog is
    /// complete, Lower and Emit each register one <c>catalog-reference-report</c>
    /// (empty when every reference resolves). The compiler may still register extra
    /// contributors that are not on the session, after Emit returns. The next Lower
    /// or Emit drops anything registered after that, including those files.
    /// </summary>
    public ArtifactCatalog ArtifactCatalog { get; private set; } = new();

    /// <summary>
    /// Times this session has run VM analysis of a lowered module.
    /// <see cref="Compile"/> increments it. The same domain, domain analysis,
    /// and module reference do not increment it again.
    /// </summary>
    internal int VmAnalysisCount { get; private set; }

    private readonly object _vmAnalysisGate = new();
    private CompiledDomain? _compiled;

    private const string ScaffoldingType = "scaffolding";
    internal const string EntityType = "entity";
    private const string AnalysisReportType = "analysis-report";
    private const string CatalogReferenceReportType = "catalog-reference-report";
    private const string SourceDomainType = "source-domain";
    private const string SourceEntityType = "source-entity";
    private const string LowerProducer = "Lower";

    private Analyzer? _analyzer;

    /// <summary>The session's analysis pipeline: core's passes plus library analyzers.</summary>
    private Analyzer Analyzer => _analyzer ??= BuildAnalyzer();

    private Analyzer BuildAnalyzer() {
        RequireCoreLoaded();
        return new Analyzer(Analyzers);
    }

    private void RequireCoreLoaded() {
        if (!Contributions.Any(contribution =>
                contribution.Stage == PipelineStage.Load && contribution.LibraryId == CoreLibrary.CoreId))
            throw new InvalidOperationException("The compilation pipeline requires the core library loaded at Load.");
    }

    internal DomainSession(
        Domain? domain,
        IReadOnlyList<string> extensions,
        SessionOptions options,
        Language<DslToken, DslTokenKind> language,
        AnnotationRegistry annotations,
        ExpressionFormRegistry expressionForms,
        TypeMappingRegistry typeMaps,
        IReadOnlyList<IStorageConvention> storageConventions,
        ExpressionFoldTable folds,
        ExpressionMeaning meaning,
        IReadOnlyList<IArtifactContributor>? artifacts = null,
        IReadOnlyList<INodeAnalyzer>? analyzers = null,
        IReadOnlyList<StageContribution>? contributions = null,
        IReadOnlyList<(string LibraryId, string Name, PrimitiveSemantics Semantics)>? primitiveSeeds = null,
        IReadOnlyList<IConstraintCompiler>? constraintCompilers = null,
        IReadOnlyList<ObservedFlow>? observedFlows = null,
        IReadOnlyList<DiagnosticAllowance>? diagnosticAllowances = null,
        IReadOnlyList<ContractContribution>? contractContributions = null) {
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
        Analyzers = analyzers ?? [];
        Contributions = contributions ?? [];
        PrimitiveSeeds = primitiveSeeds ?? [];
        ConstraintCompilers = constraintCompilers ?? [];
        ObservedFlows = observedFlows ?? [];
        DiagnosticAllowances = diagnosticAllowances ?? [];
        ContractContributions = contractContributions ?? [];
        Options = options;
    }

    /// <summary>
    /// Returns a session with the same language tables, contributions, and domain and the given
    /// host target; the same session when the target is unchanged. The target must be non-blank
    /// when present: an empty host fact is a caller bug, not "no target".
    /// </summary>
    public DomainSession WithTarget(string? target) {
        if (target is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (string.Equals(Target, target, StringComparison.Ordinal))
            return this;
        return Clone(Domain, Options with { Target = target });
    }

    /// <summary>
    /// The one place a session copy is made. Every clone site (a changed domain, a new target)
    /// goes through here so a new session fact cannot be dropped by one of them.
    /// </summary>
    private DomainSession Clone(Domain? domain, SessionOptions options) =>
        new(domain, Extensions, options, Language, Annotations, ExpressionForms, TypeMaps, StorageConventions,
            Folds, Meaning, Artifacts, Analyzers, Contributions, PrimitiveSeeds, ConstraintCompilers,
            ObservedFlows, DiagnosticAllowances, ContractContributions);

    /// <summary>The contract the library with <paramref name="libraryId"/> contributed, or null.</summary>
    public ContractContribution? FindContractContribution(string libraryId) =>
        ContractContributions.FirstOrDefault(contribution =>
            string.Equals(contribution.LibraryId, libraryId, StringComparison.Ordinal));

    /// <summary>Loads libraries for an existing domain's extension ids. Unknown id throws.</summary>
    public static DomainSession Open(
        Domain domain,
        ExtensionCatalog? catalog = null) {
        ArgumentNullException.ThrowIfNull(domain);
        return ForExtensions(domain.Extensions, catalog ?? ExtensionCatalog.Core).WithDomain(domain);
    }

    /// <summary>Loads libraries for explicit extension ids (parse/print/analyze before a domain exists). Unknown id throws.</summary>
    public static DomainSession ForExtensions(
        IReadOnlyList<string> extensions,
        ExtensionCatalog? catalog = null) {
        ArgumentNullException.ThrowIfNull(extensions);
        return LibrarySelection.Load(extensions, SessionOptions.Bare(catalog ?? ExtensionCatalog.Core));
    }

    /// <summary>Loads the libraries <see cref="LibrarySelection.Resolve"/> selects for <paramref name="documentUses"/>.</summary>
    public static DomainSession Create(IReadOnlyList<string> documentUses, SessionOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        return LibrarySelection.Load(LibrarySelection.Resolve(documentUses, options), options);
    }

    /// <summary>Parses <paramref name="poly"/> and loads its libraries. Unknown id throws.</summary>
    public static DomainSession ForSource(
        string poly,
        IReadOnlyList<string> seed,
        ExtensionCatalog? catalog = null) {
        ArgumentNullException.ThrowIfNull(poly);
        ArgumentNullException.ThrowIfNull(seed);
        return Create(DslSyntaxTree.Parse(poly).Uses, new SessionOptions(catalog ?? ExtensionCatalog.Core, seed));
    }

    /// <summary>Keeps tables when <c>uses</c> is unchanged; reloads when it changes.</summary>
    public DomainSession WithDomain(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        if (SameExtensions(Extensions, domain.Extensions))
            return Clone(domain, Options);
        var reloaded = LibrarySelection.Load(domain.Extensions, Options);
        return new DomainSession(domain, reloaded.Extensions, Options, reloaded.Language, reloaded.Annotations, reloaded.ExpressionForms, reloaded.TypeMaps, reloaded.StorageConventions, reloaded.Folds, reloaded.Meaning, reloaded.Artifacts, reloaded.Analyzers, reloaded.Contributions, reloaded.PrimitiveSeeds, reloaded.ConstraintCompilers, ObservedFlows, DiagnosticAllowances, reloaded.ContractContributions);
    }

    /// <summary>
    /// Analyzes <paramref name="domain"/> with this session's pipeline (type maps included).
    /// The session is bound before the pipeline so passes that ask the cache for maps
    /// see this compilation, not a second session built from <see cref="ExtensionCatalog.Core"/>.
    /// </summary>
    public AnalysisResult Analyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        RequireCoreLoaded();
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
        RequireCoreLoaded();
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
    /// scaffolding tree holding everything else (domain enums and value types).
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
    /// Lowers <paramref name="domain"/> under <paramref name="analysis"/> and returns
    /// the <see cref="CompiledDomain"/> for that pair. A second call with the same
    /// domain, domain analysis, and module reference returns the cached object and
    /// does not run VM analysis again.
    /// </summary>
    internal CompiledDomain Compile(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);
        RequireCoreLoaded();
        var module = RuntimeAnalysisCache.GetOrLower(domain, this, analysis);
        return VmAnalysisOf(domain, analysis, module);
    }

    /// <summary>
    /// The VM analysis for this compilation. A hit reuses the stored
    /// <see cref="CompiledDomain"/> and does not call
    /// <see cref="Interpreter.Analyzer"/>. A miss analyzes the lowered module
    /// and keeps that <see cref="CompiledDomain"/>.
    /// </summary>
    private CompiledDomain VmAnalysisOf(
        Domain domain, AnalysisResult analysis, IReadOnlyList<TypeDefinitionNode> module) {
        lock (_vmAnalysisGate) {
            var cached = _compiled;
            if (cached is not null
                && ReferenceEquals(cached.Domain, domain)
                && ReferenceEquals(cached.DomainAnalysis, analysis)
                && ReferenceEquals(cached.Module, module))
                return cached;

            var vm = AnalyzeModule(module);
            VmAnalysisCount++;
            _compiled = new CompiledDomain(domain, analysis, module, vm);
            return _compiled;
        }
    }

    /// <summary>
    /// Analyzes a lowered operation module the way the runtime host does: the shared
    /// <c>Poly.Runtime</c> result family is bound through <see cref="DomainResultTypeProvider"/>
    /// over the default registry, and a module's own declarations still win over it.
    /// </summary>
    public static AnalysisResult AnalyzeModule(IReadOnlyList<TypeDefinitionNode> module) {
        ArgumentNullException.ThrowIfNull(module);
        return Interpreter.Analyzer.Analyze(
            new CompilationUnitNode([], null, module, null),
            typeDefinitions: DomainResultTypeProvider.Wrap(AnalysisContext.CreateDefault().TypeDefinitions));
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