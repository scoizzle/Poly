using Poly.Analysis;
using Poly.DomainModeling.Lowering;

namespace Poly.DomainModeling;

/// <summary>
/// Upstream convenience bundle for parse+analyze authoring flows.
/// Analysis APIs should depend on <see cref="DomainAnalysisInputs"/> directly.
/// </summary>
public sealed record DomainInputSet(
    DomainParserInputs Parser,
    DomainAnalysisInputs Analysis
) {
    public static DomainInputSet Default { get; } = DomainInputBuilder.Create().Build();
    public static DomainInputSet Sql { get; } = DomainInputBuilder.CreateWithSqlPack().Build();
}

/// <summary>
/// Canonical parser/analysis defaults with no requirement to bundle them.
/// </summary>
public static class DomainInputDefaults {
    public static DomainParserInputs Parser { get; } =
        DomainInputBuilder.Create().BuildParserInputs();

    public static DomainAnalysisInputs Analysis { get; } =
        DomainInputBuilder.Create().BuildAnalysisInputs();

    public static DomainParserInputs SqlParser { get; } =
        DomainInputBuilder.CreateWithSqlPack().BuildParserInputs();

    public static DomainAnalysisInputs SqlAnalysis { get; } =
        DomainInputBuilder.CreateWithSqlPack().BuildAnalysisInputs();
}

/// <summary>
/// Immutable parser inputs for a parse/print session.
/// </summary>
public sealed class DomainParserInputs {
    public static DomainParserInputs Default { get; } = new(new AnnotationRegistry());

    public AnnotationRegistry Annotations { get; }

    public DomainParserInputs(AnnotationRegistry annotations) {
        ArgumentNullException.ThrowIfNull(annotations);
        Annotations = new AnnotationRegistry(annotations);
    }
}

/// <summary>
/// Immutable analyzer inputs for one analysis session.
/// </summary>
public sealed class DomainAnalysisInputs {
    public static DomainAnalysisInputs Default { get; } = new(
        new TypeMappingRegistry(),
        [],
        []);

    public TypeMappingRegistry TypeMaps { get; }
    public IReadOnlyList<IStorageConvention> StorageConventions { get; }
    public IReadOnlyList<INodeAnalyzer> AdditionalPasses { get; }

    public DomainAnalysisInputs(
        TypeMappingRegistry typeMaps,
        IReadOnlyList<IStorageConvention> storageConventions,
        IReadOnlyList<INodeAnalyzer> additionalPasses) {
        ArgumentNullException.ThrowIfNull(typeMaps);
        ArgumentNullException.ThrowIfNull(storageConventions);
        ArgumentNullException.ThrowIfNull(additionalPasses);

        var duplicatePass = additionalPasses
            .GroupBy(p => p.PassName, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicatePass is not null) {
            throw new InvalidOperationException(
                $"Duplicate analyzer pass '{duplicatePass.Key}' in explicit analysis inputs.");
        }

        TypeMaps = typeMaps.Clone();
        StorageConventions = storageConventions.ToArray();
        AdditionalPasses = additionalPasses.ToArray();
    }
}

/// <summary>
/// Builder used to construct explicit immutable parse/analyze inputs.
/// </summary>
public sealed class DomainInputBuilder {
    private readonly List<IStorageConvention> _storageConventions = [];
    private readonly List<INodeAnalyzer> _analysisPasses = [];

    public AnnotationRegistry Annotations { get; } = new();
    public TypeMappingRegistry TypeMaps { get; } = new();

    public static DomainInputBuilder Create() => new();

    public static DomainInputBuilder CreateWithSqlPack() {
        var builder = Create();
        builder.Annotations.Register(new ColumnAnnotationSyntax());
        builder.Annotations.Register(new TableAnnotationSyntax());
        return builder;
    }

    public DomainInputBuilder RegisterAnnotation(IAnnotationSyntax syntax) {
        ArgumentNullException.ThrowIfNull(syntax);
        Annotations.Register(syntax);
        return this;
    }

    public DomainInputBuilder AddStorageConvention(IStorageConvention convention) {
        ArgumentNullException.ThrowIfNull(convention);
        _storageConventions.Add(convention);
        return this;
    }

    public DomainInputBuilder AddAnalysisPass(INodeAnalyzer pass) {
        ArgumentNullException.ThrowIfNull(pass);
        _analysisPasses.Add(pass);
        return this;
    }

    public DomainParserInputs BuildParserInputs() =>
        new(Annotations);

    public DomainAnalysisInputs BuildAnalysisInputs() =>
        new(TypeMaps, _storageConventions, _analysisPasses);

    public DomainInputSet Build() =>
        new(BuildParserInputs(), BuildAnalysisInputs());
}