namespace Poly.Runtime;

/// <summary>
/// The optimistic-concurrency token on a persisted domain entity, #677 R-06 (Q2).
/// A generated entity exposes <c>long Version</c> as a shadow concurrency column that the
/// store increments on save; a save whose token is stale raises
/// <see cref="DomainFailureKind.Conflict"/>. P-09 (<c>EfConcurrencyContract</c>) is the
/// intended implementer.
/// </summary>
/// <remarks>
/// A shape only: no setter, no default implementation and no behaviour. Generated plain
/// C# entities are not thread-safe by contract (#574); concurrent writes are the store's
/// concern, not the entity's.
/// </remarks>
public interface IVersioned {
    /// <summary>This entity's concurrency token; it changes on every persisted save.</summary>
    long Version { get; }
}