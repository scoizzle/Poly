using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// C# over the CLR: the CLR supplies the definitions (reflection), C# supplies the words (its type keywords) and the
/// rules (the C# standard's conversions, promotion and operators). One shared instance backs every CLR definition.
/// Interim (#998 T1, removed by T9): the rules still delegate to the definitions' own doors.
/// </summary>
internal sealed class CSharpTypeSystem : ITypeSystem {
    public static CSharpTypeSystem Shared { get; } = new();

    internal CSharpTypeSystem() { }

    public TypeSystemRules Rules { get; } = new();

    public ITypeSystem? Target => null;

    public IReadOnlyList<ITypeSource> Sources { get; } = [ClrTypeDefinitionRegistry.Shared];

    /// <summary>A qualified name through the CLR registry. Interim (#998 S2, its removing row): the registry still resolves
    /// a bare name that spells a C# built-in (<c>Int32</c>, <c>DateTime</c>) without a using.</summary>
    public ITypeDefinition? Resolve(string qualifiedName) =>
        ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(qualifiedName);

    public ITypeDefinition? Bind(string word) =>
        CSharpTypeNames.TypeOf(word) is { } type ? ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type) : null;

    public string? Spell(ITypeDefinition definition) => CSharpTypeNames.SpellingOf(definition);

    public bool IsSame(ITypeDefinition a, ITypeDefinition b) => TypeDefinitionExtensions.IsSameType(a, b);

    public ITypeDefinition? NullableOf(ITypeDefinition definition) => definition.NullableOf;

    public ITypeDefinition? ElementType(ITypeDefinition definition) => definition.GetElementType();

    public bool IsAssignableWithoutConversion(ITypeDefinition destination, ITypeDefinition source) =>
        destination.IsAssignableWithoutConversion(source);

    public ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination) =>
        destination.ResolveConversionFrom(source);

    public ITypeOperator? Operator(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right) =>
        left.GetOperator(kind, right);

    public TRule? Rule<TRule>() where TRule : class, ITypeSystemRule =>
        Rules.Find<TRule>() ?? this as TRule ?? Target?.Rule<TRule>();
}