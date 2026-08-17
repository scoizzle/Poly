using Poly.DomainModeling.Parsing;

namespace Poly.DomainModeling.Packs.Temporal;

/// <summary>
/// Temporal concepts (clocks, duration units) on existing expression shapes.
/// Loaded via <see cref="ExtensionCatalog"/>; meaning is session-scoped.
/// </summary>
public sealed class TemporalLibrary : IDomainLibrary {
    public string Id => "temporal";

    public void Register(DomainHostBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        TemporalDispatchRegistration.Populate(builder.Meaning);
        builder.ExpressionForms.RegisterBinaryFold(new DateOperationFold());
        TemporalExpressionPrintBinders.Register(builder.ExpressionForms);
        builder.ExpressionForms.RegisterGrammarContributor(TemporalExpressionPrintBinders.ContributeGrammarPatterns);
    }
}