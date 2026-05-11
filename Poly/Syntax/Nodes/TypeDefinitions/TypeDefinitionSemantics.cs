namespace Poly.Syntax.Nodes;

public sealed record TypeDefinitionSemantics(
    TypeDefinitionMutability Mutability,
    TypeDefinitionEqualitySemantics EqualitySemantics
) {
    public static TypeDefinitionSemantics MutableReference { get; } =
        new(TypeDefinitionMutability.Mutable, TypeDefinitionEqualitySemantics.Reference);

    public static TypeDefinitionSemantics ImmutableValue { get; } =
        new(TypeDefinitionMutability.Immutable, TypeDefinitionEqualitySemantics.Value);

    public bool IsImmutable => Mutability == TypeDefinitionMutability.Immutable;
    public bool HasValueEquality => EqualitySemantics == TypeDefinitionEqualitySemantics.Value;
}