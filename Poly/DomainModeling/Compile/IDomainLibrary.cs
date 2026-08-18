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

namespace Poly.DomainModeling.Compile;

/// <summary>
/// A library that registers <b>concepts</b> into a session: meaning for existing
/// spellings, type maps, conventions, and/or artifact files. It does not add
/// language shapes. Duplicate <see cref="Id"/> fails closed. Not a discovery host.
/// </summary>
public interface IDomainLibrary {
    /// <summary>Unique, ordinal-compared identity. Duplicates fail closed.</summary>
    string Id { get; }

    /// <summary>Registers this library onto the session builder.</summary>
    void Register(SessionBuilder builder);
}