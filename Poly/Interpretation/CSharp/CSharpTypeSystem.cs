using System.Collections.Frozen;

using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Interpretation.CSharp;

/// <summary>
/// C# over the CLR: the CLR supplies the definitions (reflection), C# supplies the words (its type keywords) and the
/// rules (the C# standard's conversions, promotion and operators). One shared instance backs every CLR definition.
/// It decides every pair it is asked about, whichever system an operand comes from (#998 T3): it reads what both types
/// declare and applies C#'s rules; a foreign definition's own answer never decides. Its rules for its own definitions
/// live here or in the CLR host's internals it calls (#998 T9: the definitions answer none of them).
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

    /// <summary>
    /// C#'s identity (#998 T9, moved from the definitions): a CLR definition and a declared type are the same only as
    /// the same object; a synthetic collection and a generic parameter answer through the body an analysis layer shares
    /// (#998 T12c, <see cref="Poly.Interpretation.Analysis.Semantics.AstTypeIdentity"/>).
    /// </summary>
    public bool IsSame(ITypeDefinition a, ITypeDefinition b) =>
        Poly.Interpretation.Analysis.Semantics.AstTypeIdentity.IsSame(a, b);

    /// <summary>The CLR's nullable form wraps its underlying type; C#'s declarations and foreign definitions wrap nothing
    /// (#998 T9).</summary>
    public ITypeDefinition? NullableOf(ITypeDefinition definition) =>
        definition is ClrTypeDefinition clr ? clr.NullableOf : null;

    /// <summary>
    /// C#'s element type of a sequence: a collection's positional indexer (an <c>Item</c> whose parameters are all C#'s
    /// fixed-width integers; the fewest parameters, then one that does not return <c>object</c>), else the sequence's
    /// element (#998 T6c: the pick is C#'s rule, asked of its own simple types and words).
    /// </summary>
    public ITypeDefinition? ElementType(ITypeDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);
        var top = Bind("object");
        var positional = definition.Properties
            .Where(static property => property.Name == "Item" && property.Parameters.Any())
            .Select(property => new {
                Property = property,
                Parameters = property.Parameters.ToArray(),
                ReturnsObject = top is not null && IsSame(top, property.MemberTypeDefinition)
            })
            .Where(candidate => candidate.Parameters.All(parameter => SimpleTypeOf(parameter.ParameterTypeDefinition)
                is SimpleType.SByte or SimpleType.Byte or SimpleType.Short or SimpleType.UShort
                or SimpleType.Int or SimpleType.UInt or SimpleType.Long or SimpleType.ULong))
            .OrderBy(static candidate => candidate.Parameters.Length)
            .ThenBy(static candidate => candidate.ReturnsObject)
            .FirstOrDefault();
        if (positional is not null && IsCollectionShape(definition))
            return positional.Property.MemberTypeDefinition;
        return SequenceElementType(definition);
    }

    /// <summary>C#'s identity and reference conversions without a conversion applied: the CLR host's body for its own
    /// definitions (variance, array covariance, the lift into <c>T?</c>, the box into <c>object</c>), else the declared
    /// hierarchy (#998 T9).</summary>
    // A sequence shape (#998 T9, merged into ElementType): the CLR host's own (an array or a closed IEnumerable<>, not a
    // primitive, so byte[] stays binary) or a declared synthetic collection; anything else, a foreign definition
    // included, is no C# sequence.
    private static bool IsCollectionShape(ITypeDefinition definition) => definition switch {
        ClrTypeDefinition clr => clr.IsCollectionShape,
        Poly.Interpretation.Analysis.Semantics.AstCollectionTypeDefinition => true,
        _ => false,
    };

    private static ITypeDefinition? SequenceElementType(ITypeDefinition definition) => definition switch {
        ClrTypeDefinition clr => clr.SequenceElementType,
        Poly.Interpretation.Analysis.Semantics.AstCollectionTypeDefinition collection => collection.ElementType,
        _ => null,
    };

    public bool IsAssignableWithoutConversion(ITypeDefinition destination, ITypeDefinition source) =>
        destination is ClrTypeDefinition clr
            ? clr.AssignsWithoutConversion(source)
            : TypeDefinitionExtensions.IsAssignableByHierarchy(destination, source);

    /// <summary>Into C#'s own definition, its door (built-in rules plus both ends' declared conversions). Into a foreign
    /// definition, only a conversion either end declares with a method: a foreign system's own built-in conversion is not
    /// a C# conversion.</summary>
    public ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination) =>
        destination is ClrTypeDefinition clr
            ? CSharpBuiltIns.ConversionInto(clr, source)
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
        return ClrOperatorResolution.Resolve(left, kind, right, Conversion, OfferedBy);
    }

    /// <summary>The candidates C# offers for <paramref name="definition"/> beside <paramref name="other"/> (#998 T10): for a
    /// CLR definition, C#'s built-ins and its declared operators; for any other, the operators it declares.</summary>
    private static IEnumerable<ITypeOperator> OfferedBy(ITypeDefinition definition, OperatorKind kind, ITypeDefinition? other) =>
        definition is ClrTypeDefinition clr
            ? CSharpBuiltIns.OperatorsBeside(clr, kind, other)
            : definition.Operators.Where(o => o.Kind == kind);

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
        definition is ClrTypeDefinition clr ? SimpleTypeOf(clr.RuntimeType) : null;

    /// <summary>The C# simple type a CLR runtime type denotes; any other type is none.</summary>
    internal static SimpleType? SimpleTypeOf(Type? type) =>
        type is not null && SimpleTypes.TryGetValue(type, out var simple) ? simple : null;

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