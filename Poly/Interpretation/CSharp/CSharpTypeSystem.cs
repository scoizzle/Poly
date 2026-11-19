using System.Collections.Frozen;

using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// C# over the CLR: the CLR supplies the definitions (reflection), C# supplies the words (its type keywords) and the
/// rules (the C# standard's conversions, promotion and operators). One shared instance backs every CLR definition.
/// It decides every pair it is asked about, whichever system an operand comes from (#998 T3): it reads what both types
/// declare and applies C#'s rules; a foreign definition's own answer never decides. Interim (removed by T9, when a
/// definition names its system): a definition is C#'s own when it is an <see cref="IClrTypeDefinition"/>, and for its
/// own definitions the rules still delegate to their doors.
/// </summary>
internal sealed class CSharpTypeSystem : ITypeSystem, ICSharpTypeRules {
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

    /// <summary>Into C#'s own definition, its door (built-in rules plus both ends' declared conversions). Into a foreign
    /// definition, only a conversion either end declares with a method: a foreign system's own built-in conversion is not
    /// a C# conversion.</summary>
    public ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination) =>
        destination is IClrTypeDefinition
            ? destination.ResolveConversionFrom(source)
            : Poly.Interpretation.Analysis.Semantics.ModeledConversions.Find(source, destination);

    /// <summary>C#'s operator selection over the operators both operands declare, with applicability decided by this
    /// system's <see cref="Conversion"/>.</summary>
    public ITypeOperator? Operator(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right) {
        ArgumentNullException.ThrowIfNull(left);
        var arity = OperatorShape.ArityOf(kind);
        if (arity == 2)
            ArgumentNullException.ThrowIfNull(right);
        else if (right is not null)
            throw new ArgumentException($"Operator '{kind}' is unary; a right operand was given.", nameof(right));
        return ClrOperatorResolution.Resolve(left, kind, right, Conversion);
    }

    public TRule? Rule<TRule>() where TRule : class, ITypeSystemRule =>
        Rules.Find<TRule>() ?? this as TRule ?? Target?.Rule<TRule>();

    // ── C#'s own rules (ICSharpTypeRules, #998 T6a) ──

    private static readonly FrozenDictionary<Type, SimpleType> SimpleTypes = new Dictionary<Type, SimpleType> {
        [typeof(bool)] = SimpleType.Bool,
        [typeof(char)] = SimpleType.Char,
        [typeof(sbyte)] = SimpleType.SByte,
        [typeof(byte)] = SimpleType.Byte,
        [typeof(short)] = SimpleType.Short,
        [typeof(ushort)] = SimpleType.UShort,
        [typeof(int)] = SimpleType.Int,
        [typeof(uint)] = SimpleType.UInt,
        [typeof(long)] = SimpleType.Long,
        [typeof(ulong)] = SimpleType.ULong,
        [typeof(nint)] = SimpleType.NInt,
        [typeof(nuint)] = SimpleType.NUInt,
        [typeof(float)] = SimpleType.Float,
        [typeof(double)] = SimpleType.Double,
        [typeof(decimal)] = SimpleType.Decimal,
    }.ToFrozenDictionary();

    /// <summary>The C# simple type of a CLR definition; a declared or foreign definition is none.</summary>
    public SimpleType? SimpleTypeOf(ITypeDefinition definition) =>
        definition is ClrTypeDefinition clr && SimpleTypes.TryGetValue(clr.RuntimeType, out var simple) ? simple : null;

    /// <summary>A CLR enum, or a declaration written as an enum.</summary>
    public bool IsEnumType(ITypeDefinition definition) => definition switch {
        ClrTypeDefinition clr => clr.RuntimeType.IsEnum,
        Poly.Interpretation.Analysis.Semantics.AstTypeDefinition declared => declared.Form == Poly.Ast.Nodes.TypeDeclarationForm.Enum,
        _ => false,
    };

    /// <summary>A CLR value type, or a declaration written as a struct (a declared enum states no value semantics, as
    /// before).</summary>
    public bool HasValueSemantics(ITypeDefinition definition) => definition switch {
        ClrTypeDefinition clr => clr.RuntimeType.IsValueType,
        Poly.Interpretation.Analysis.Semantics.AstTypeDefinition declared => declared.Form == Poly.Ast.Nodes.TypeDeclarationForm.Struct,
        _ => false,
    };
}