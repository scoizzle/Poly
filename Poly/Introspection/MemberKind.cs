namespace Poly.Introspection;

/// <summary>
/// The shape a member declares: what it is, never how a host stores or reads it.
/// A platform states this per member (<c>ClrTypeField</c>, <c>AstPropertyDefinition</c>, …).
/// </summary>
public enum MemberKind {
    /// <summary>A storage location with no parameters.</summary>
    Field,

    /// <summary>A named property with no index parameters.</summary>
    Property,

    /// <summary>A property indexed by parameters (a synthetic <c>Item</c> counts).</summary>
    Indexer,

    /// <summary>A method, including one an operator is declared with.</summary>
    Method,

    /// <summary>An instance or type constructor.</summary>
    Constructor,
}