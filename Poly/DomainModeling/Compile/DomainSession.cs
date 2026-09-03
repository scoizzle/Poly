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

    public IReadOnlyList<IArtifactContributor> Artifacts { get; }

    internal IReadOnlyList<INodeAnalyzer> ExtraAnalyzers { get; }

    private Analyzer? _analyzer;

    /// <summary>The session's analysis pipeline: core product passes plus library analyzers, with storage type maps wired into <see cref="StoragePass"/>.</summary>
    private Analyzer Analyzer =>
        _analyzer ??= DomainModelAnalyzer.BuildPipeline(TypeMaps, StorageConventions, ExtraAnalyzers);

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
        IReadOnlyList<INodeAnalyzer>? extraAnalyzers = null) {
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
            return new DomainSession(domain, domain.Extensions, Language, Annotations, ExpressionForms, TypeMaps, StorageConventions, Folds, Meaning, Artifacts, ExtraAnalyzers);
        return Open(domain);
    }

    /// <summary>Analyzes <paramref name="domain"/> with this session's pipeline (type maps included).</summary>
    public AnalysisResult Analyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return Analyzer.Analyze(domain);
    }

    /// <summary>
    /// Entity-module C# files from analyzed facts. Persistence and HTTP files are
    /// compiler/host emitters gated on analysis bags, not this method.
    /// </summary>
    public IReadOnlyList<(string FileName, string Source)> Emit(Domain domain, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);
        var files = new List<(string FileName, string Source)>();
        var types = DomainProgramProjection.ToSyntax(domain, analysis);
        var interpAnalysis = TryAnalyzeForEmit(types);
        var generator = interpAnalysis is not null
            ? new CSharpGenerator(interpAnalysis)
            : new CSharpGenerator();
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
            files.Add(($"{entity.Name}.cs", generator.Generate(entityDefs)));
        }

        var scaffoldingDefs = types
            .Where(d => !entities.Any(e =>
                d.Name == e.Name || d.Name == $"{e.Name}Stage"))
            .ToList();
        if (scaffoldingDefs.Count > 0)
            files.Add(("Poly.Types.cs", generator.Generate(scaffoldingDefs)));
        return files;
    }

    /// <summary>
    /// Runs interpretation analysis on lowered type definitions so the C# generator
    /// can use type-aware features (variable type resolution, DCE).
    /// Falls back gracefully: analysis errors produce diagnostics but do not block emit.
    /// </summary>
    private static AnalysisResult? TryAnalyzeForEmit(IReadOnlyList<TypeDefinitionNode> allTypes) {
        try {
            var unit = new CompilationUnitNode([], null, allTypes, null);
            return Interpretation.Interpreter.Analyzer.Analyze(unit);
        }
        catch {
            return null;
        }
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