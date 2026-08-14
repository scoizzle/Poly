namespace Poly.DomainModeling.Packs;

/// <summary>
/// Fills an <see cref="ImportedContract"/> from a source spec or another loaded
/// <see cref="Domain"/>. Producers emit product IR only — <see cref="ImportedContract"/>
/// plus owned value types and endpoints — and never merge domains, invent a second
/// grammar, or add core keywords (lock: packs do not invent product IR).
/// </summary>
public interface IContractProducer {
    /// <summary>Produces an imported contract from <paramref name="source"/>.</summary>
    ImportedContract Produce(Domain source);
}