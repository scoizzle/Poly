using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// The domain language as a type system layered on its target (#998 T5; decision §3.1 and §4). It owns its words: a
/// domain type name (<c>Number</c>, <c>Int</c>, <c>Decimal</c>, <c>Text</c>, …) binds the target's definition through the
/// session's type maps, and a definition is spelled by the domain word that binds it. A family is the set of definitions
/// the domain's words bind, compared by identity, never what a type answers about itself: a type no word binds is in no
/// family. Everything it does not own (identity, conversions, operators, rules) its target answers.
/// Interim (removed by #998 S3, the prelude): the words and their bindings are <see cref="DomainTypeMapping"/>'s table.
/// </summary>
internal sealed class DomainTypeSystem(TypeMappingRegistry typeMaps, ITypeSystem target)
    : ITypeSystem, Poly.Interpretation.Analysis.Semantics.IImplicitUsings {
    /// <summary>The domain language imports no host namespace (#998 S2b): it answers the question itself, so C#'s
    /// implicit usings never reach a domain analysis and a bare host name (<c>File</c>) stays unbound.</summary>
    public IReadOnlyList<string> Namespaces => [];

    // The canonical word for each definition a domain word binds by default; aliases (Int, Int64, String, Bool, Double,
    // Guid) bind the same definitions. Pack overrides change what a word binds, not what a definition is called.
    private static readonly (ITypeDefinition Definition, string Word)[] Canonical = [
        (Clr(typeof(long)), "Number"), (Clr(typeof(int)), "Int32"), (Clr(typeof(float)), "Float32"),
        (Clr(typeof(double)), "Float"), (Clr(typeof(decimal)), "Decimal"), (Clr(typeof(string)), "Text"),
        (Clr(typeof(bool)), "Boolean"), (Clr(typeof(DateOnly)), "Date"), (Clr(typeof(DateTime)), "DateTime"),
        (Clr(typeof(TimeOnly)), "Time"), (Clr(typeof(TimeSpan)), "Duration"), (Clr(typeof(Guid)), "Uuid"),
        (Clr(typeof(byte[])), "Binary"),
    ];

    // The domain language's own rules: core's constraint applicability (#998 T7). The system is the one asked, so the
    // rule spells a definition by this system's words.
    public TypeSystemRules Rules => _rules ??= CoreRules(this);
    private TypeSystemRules? _rules;

    private static TypeSystemRules CoreRules(DomainTypeSystem domain) {
        var rules = new TypeSystemRules();
        rules.Add<IConstraintApplicability>(new CoreConstraintApplicability(domain));
        return rules;
    }

    public ITypeSystem? Target => target;
    public IReadOnlyList<ITypeSource> Sources => target.Sources;
    public ITypeDefinition? Resolve(string qualifiedName) => target.Resolve(qualifiedName);

    /// <summary>The definition a domain word binds on this session (pack overrides included), or null.</summary>
    public ITypeDefinition? Bind(string word) =>
        DomainTypeMapping.TryClrType(typeMaps.ToClrTypeName(word), out var type) ? Clr(type) : null;

    public string? Spell(ITypeDefinition definition) => WordOf(definition);

    /// <summary>The canonical domain word that binds <paramref name="definition"/>, or null when no domain word binds it.
    /// A definition's identity decides, never what the definition answers about itself.</summary>
    public static string? WordOf(ITypeDefinition? definition) {
        if (definition is null)
            return null;
        foreach (var (bound, word) in Canonical) {
            if (TypeDefinitionExtensions.IsSameType(bound, definition))
                return word;
        }
        return null;
    }

    public bool IsSame(ITypeDefinition a, ITypeDefinition b) => target.IsSame(a, b);
    public ITypeDefinition? NullableOf(ITypeDefinition definition) => target.NullableOf(definition);
    public ITypeDefinition? ElementType(ITypeDefinition definition) => target.ElementType(definition);
    public bool IsAssignableWithoutConversion(ITypeDefinition destination, ITypeDefinition source) =>
        target.IsAssignableWithoutConversion(destination, source);
    public ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination) => target.Conversion(source, destination);
    public ITypeOperator? Operator(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right) => target.Operator(kind, left, right);

    public TRule? Rule<TRule>() where TRule : class, ITypeSystemRule =>
        Rules.Find<TRule>() ?? this as TRule ?? target.Rule<TRule>();

    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);
}