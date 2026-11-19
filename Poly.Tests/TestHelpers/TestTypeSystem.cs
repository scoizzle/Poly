using Poly.Introspection;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// The type system a test double belongs to (#998 T9, decision §2.4): it made the double, so it decides the double's
/// identity (the same object) and states no other rule, conversion or operator.
/// </summary>
public sealed class TestTypeSystem : ITypeSystem {
    public static readonly TestTypeSystem Instance = new();

    public TypeSystemRules Rules { get; } = new();
    public ITypeSystem? Target => null;
    public IReadOnlyList<ITypeSource> Sources => [];
    public ITypeDefinition? Resolve(string qualifiedName) => null;
    public ITypeDefinition? Bind(string word) => null;
    public string? Spell(ITypeDefinition definition) => null;
    public bool IsSame(ITypeDefinition a, ITypeDefinition b) => ReferenceEquals(a, b);
    public ITypeDefinition? NullableOf(ITypeDefinition definition) => null;
    public ITypeDefinition? ElementType(ITypeDefinition definition) => null;
    public bool IsAssignableWithoutConversion(ITypeDefinition destination, ITypeDefinition source) => IsSame(destination, source);
    public ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination) => null;
    public ITypeOperator? Operator(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right) => null;
    public TRule? Rule<TRule>() where TRule : class, ITypeSystemRule => Rules.Find<TRule>();
}