using Poly.Data.Modeling.TypeSystem;
using Poly.Syntax;

namespace Poly.Data.Modeling;

/// <summary>
/// Represents a named, reusable expression value (AST) for use in effects, properties, or other domain members.
/// </summary>
public sealed record ExpressionValue(Domain Domain, string Name, DomainType? Type = null) : DomainMember(Domain, Name) {
    /// <summary>
    /// The Poly.Syntax AST node representing the expression.
    /// </summary>
    public required Node Expression { get; init; }

    /// <summary>
    /// Optional type for assignable/typed values. Null for untyped reusable expressions.
    /// </summary>
    public DomainType? Type { get; init; } = Type;

    // Optionally, add Description, Category, etc. for UI/UX
    public string? Description { get; init; }
}