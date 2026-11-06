using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Core's analyzer passes and builtin constraint compiler, loaded implicitly at
/// <see cref="PipelineStage.Load"/>; its passes enter at <see cref="PipelineStage.Analyze"/>
/// and its compiler at <see cref="PipelineStage.Lower"/>. <c>core</c> is never an authored
/// <c>uses</c> id.
/// </summary>
public sealed class CoreLibrary : IDomainLibrary {
    public const string CoreId = "core";

    public string Id => CoreId;

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (var pass in DomainModelAnalyzer.CorePasses(builder.Meaning, builder.ExpressionForms))
            builder.AddAnalyzer(pass);
        builder.AddConstraintCompiler(new BuiltinConstraintCompiler());

        // Ontology term metadata is core vocabulary, not a vendor pack: every session can
        // author iri/label/description/term, and the printer can round-trip them.
        foreach (var keyword in TermFacets.Keywords)
            builder.Annotations.Register(new TermAnnotationSyntax(keyword));
    }
}