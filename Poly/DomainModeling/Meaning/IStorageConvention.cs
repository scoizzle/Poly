using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// A storage convention that can adjust column- and entity-level storage
/// projections after baseline analysis and annotation application. Packs
/// implement this for vendor-specific defaults (identifier length, quoting, …).
/// </summary>
/// <remarks>
/// Return <c>null</c> to leave the current projection unchanged; return a
/// modified instance to replace it. Conventions run in registration order;
/// later conventions see earlier projections.
/// </remarks>
public interface IStorageConvention {
    /// <summary>Post-processes an entity storage projection.</summary>
    StorageEntity? ProjectEntity(Entity entity, StorageEntity baseline);

    /// <summary>Post-processes a property column projection.</summary>
    StorageColumn? ProjectColumn(Property property, StorageColumn baseline);
}