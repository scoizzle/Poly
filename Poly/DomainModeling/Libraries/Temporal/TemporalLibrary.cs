using Poly.DomainModeling.Analysis;

namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>Legacy bag type; vocab presence is session Meaning / loaded ids, not analysis.</summary>
public sealed record TemporalVocabularyMetadata : IAnalysisMetadata;

/// <summary>
/// Temporal concepts: Date/Time/DateTime/Duration catalog types (seeded when this
/// library is loaded), clocks, and duration units on existing expression shapes.
/// Product sessions load it implicitly at Load; an explicit <c>uses temporal</c>
/// still resolves. Meaning is session-scoped.
/// </summary>
public sealed class TemporalLibrary : IDomainLibrary {
    public string Id => "temporal";

    public IReadOnlyList<(string Name, TypeCategory Category)> PrimitiveSeeds =>
        TemporalTypeCatalog.Definitions;

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ExpressionForms.RegisterBinaryFold(new DateOperationFold());
        TemporalExpressionPrintBinders.Register(builder.ExpressionForms);
        TemporalExpressionPrintBinders.RegisterFolds(builder.ExpressionForms);
        TemporalMeaning.Register(builder.Meaning);
        TemporalTypeMaps.Apply(builder.TypeMaps);
    }
}