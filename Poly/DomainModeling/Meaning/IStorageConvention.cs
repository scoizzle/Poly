using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

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