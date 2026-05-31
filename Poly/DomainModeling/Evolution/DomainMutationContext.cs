using Poly.DomainModeling;

namespace Poly.DomainModeling.Evolution;

/// <summary>
/// Mutable working state used during batch application of changes.
/// 
/// This allows an entire batch of changes to be applied while only
/// allocating one final Domain at the end, instead of one per change.
/// </summary>
internal sealed class DomainMutationContext {
    public string DomainName { get; }

    public List<DomainType> Types { get; }

    public List<Relationship> Relationships { get; }

    public DomainMutationContext(Domain source) {
        DomainName = source.Name;
        Types = new List<DomainType>(source.Types);
        Relationships = new List<Relationship>(source.Relationships);
    }

    public Domain ToDomain() => new Domain(DomainName, Types, Relationships);
}