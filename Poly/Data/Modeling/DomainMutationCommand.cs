namespace Poly.Data.Modeling;

/// <summary>
/// Encapsulates a single atomic mutation: carries the data, owns apply/rollback, and declares
/// which nodes are affected for incremental analysis.
/// </summary>
internal abstract record DomainMutationCommand {
    public abstract void Apply();
    public abstract void Rollback();
    public virtual IEnumerable<Node> AffectedNodes => [];
}