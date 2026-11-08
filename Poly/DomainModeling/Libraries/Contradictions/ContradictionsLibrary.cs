namespace Poly.DomainModeling.Libraries.Contradictions;

/// <summary>
/// <c>uses contradictions</c>: tolerates bound data-flow claims that cannot lower. Changes no
/// syntax. The platform binds the claims on the session; analysis merges them into a
/// <see cref="Poly.DomainModeling.Contradictions.ContradictionBag"/> and the Lower gate
/// refuses while any entry is open.
/// </summary>
public sealed class ContradictionsLibrary : IDomainLibrary {
    public const string ExtensionId = "contradictions";

    public string Id => ExtensionId;

    public void Register(SessionBuilder builder) {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddAnalyzer(new ContradictionFlowPass(builder.ContradictionClaims));
    }
}