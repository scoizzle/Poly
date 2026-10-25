using Poly.DomainModeling.Analysis;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Core's analyzer passes, loaded implicitly at <see cref="PipelineStage.Load"/>.
/// <c>core</c> is never an authored <c>uses</c> id.
/// </summary>
public sealed class CoreLibrary : IDomainLibrary {
    public const string CoreId = "core";

    public string Id => CoreId;

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var pass in DomainModelAnalyzer.CorePasses(builder.Meaning, builder.ExpressionForms))
            builder.AddAnalyzer(pass);
    }
}
