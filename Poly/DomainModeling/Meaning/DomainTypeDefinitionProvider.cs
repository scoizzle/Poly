using System.Collections.Concurrent;

using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// Resolves a domain type name to an Introspection <see cref="ITypeDefinition"/>.
/// Enums, entities and value types become synthetic definitions, memoised per name so
/// reference identity is the identity relation. Primitives go through
/// <see cref="TypeMappingRegistry.ToClrTypeName"/> so pack overrides apply.
/// Bound to one (<see cref="Domain"/>, <see cref="TypeMappingRegistry"/>) pair: a new session needs a new provider.
/// </summary>
internal sealed class DomainTypeDefinitionProvider : IQualifiedNameSource, ITypeNameLookup {
    private readonly Dictionary<string, DomainType> _types = new(StringComparer.Ordinal);
    private readonly TypeMappingRegistry _typeMaps;
    private readonly ConcurrentDictionary<string, ITypeDefinition> _synthetic = new(StringComparer.Ordinal);

    private readonly ITypeSystem _system;

    /// <param name="system">The system the domain's own declarations belong to: the model's (#998 T12c).</param>
    public DomainTypeDefinitionProvider(Domain domain, TypeMappingRegistry typeMaps, ITypeSystem system) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(typeMaps);
        ArgumentNullException.ThrowIfNull(system);
        _typeMaps = typeMaps;
        _system = system;
        foreach (var type in domain.Types)
            _types.TryAdd(type.Name, type);
    }

    /// <summary>What the domain declared <paramref name="name"/> as, when it is one of the domain's own declarations
    /// (an entity, a value type or an enum), else null.</summary>
    public DomainDeclaration? DeclarationOf(string name) => _types.GetValueOrDefault(name) switch {
        Entity => DomainDeclaration.Entity,
        Ontology.ValueType => DomainDeclaration.Value,
        EnumType => DomainDeclaration.Enum,
        _ => null,
    };

    public ITypeDefinition? GetTypeDefinition(string name) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var declared = _types.GetValueOrDefault(name);
        if (declared is not null and not Ontology.PrimitiveType)
            return _synthetic.GetOrAdd(name, _ => Synthesize(name, declared));

        if (FromClrName(name) is { } clr)
            return clr;

        // A declared primitive with its own name (Cents: decimal) maps through its semantics. A temporal one keeps its
        // meaning without the temporal library's map (T-05b, A3): a hand-built Date is a date.
        if (declared is Ontology.PrimitiveType primitive) {
            if (primitive.Semantics is PrimitiveSemantics.Binary)
                return Clr(typeof(byte[]));
            return FromClrName(CanonicalName(primitive.Semantics)) ?? TemporalMeaning(primitive.Semantics);
        }
        return null;
    }

    public ITypeDefinition? GetTypeDefinition(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    /// <summary>The synthetic declarations this provider has built so far.</summary>
    public IEnumerable<ITypeDefinition> Types => _synthetic.Values;

    /// <summary>The synthetic declarations stated in <paramref name="namespace"/>: this domain as a type source.</summary>
    public IEnumerable<ITypeDefinition> TypesIn(string? @namespace) =>
        _synthetic.Values.Where(type => string.Equals(type.Namespace, @namespace, StringComparison.Ordinal));

    ITypeDefinition? IQualifiedNameSource.ByQualifiedName(string name) => GetTypeDefinition(name);

    ITypeDefinition? ITypeNameLookup.Lookup(string name) => GetTypeDefinition(name);

    private ITypeDefinition? FromClrName(string name) =>
        DomainTypeMapping.TryClrType(_typeMaps.ToClrTypeName(name), out var ground)
            ? Clr(ground)
            : null;

    private static ITypeDefinition Clr(Type type) =>
        ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type)
        ?? throw new InvalidOperationException($"No CLR definition for '{type}'.");

    private AstTypeDefinition Synthesize(string name, DomainType declared) {
        var (kind, declaresEquality) = declared switch {
            EnumType => (TypeDeclarationForm.Enum, true),
            Ontology.ValueType => (TypeDeclarationForm.Struct, true),
            Entity => (TypeDeclarationForm.Class, true),
            _ => throw new InvalidOperationException($"Domain type '{name}' is a '{declared.GetType().Name}', which has no definition shape.")
        };
        MethodDefinitionNode[]? methods = declaresEquality
            ? [Equality(OperatorKind.Equal, name), Equality(OperatorKind.NotEqual, name)]
            : null;
        // A domain synthetic belongs to the system its model was built with (#998 T12c, owner rulings 10, 13): an
        // analysis' layer inside an analysis, the session's target when lowering builds a model of its own.
        return new AstTypeDefinition(new TypeDefinitionNode(name, Form: kind, Methods: methods), this, _system);
    }

    /// <summary>
    /// The one operator an enum or entity declares: <c>static Boolean op(T, T)</c>, a method marked as the operator.
    /// Marked methods are how a modeled definition answers <c>Operators</c>; ordering and arithmetic stay undeclared.
    /// </summary>
    private static MethodDefinitionNode Equality(OperatorKind kind, string typeName) =>
        new(kind.ToString(),
            TypeReference.Of(typeof(bool)),
            [new Parameter("left", new NamedTypeReference(typeName)), new Parameter("right", new NamedTypeReference(typeName))],
            IsStatic: true) { Operator = kind };

    private static ITypeDefinition? TemporalMeaning(PrimitiveSemantics semantics) => semantics switch {
        PrimitiveSemantics.Date => Clr(typeof(DateOnly)),
        PrimitiveSemantics.Time => Clr(typeof(TimeOnly)),
        PrimitiveSemantics.DateTime => Clr(typeof(DateTime)),
        PrimitiveSemantics.Duration => Clr(typeof(TimeSpan)),
        _ => null,
    };

    private static string CanonicalName(PrimitiveSemantics semantics) => semantics switch {
        PrimitiveSemantics.Boolean => "Boolean",
        PrimitiveSemantics.Number => "Number",
        PrimitiveSemantics.Decimal => "Decimal",
        PrimitiveSemantics.Text => "Text",
        PrimitiveSemantics.Uuid => "Uuid",
        PrimitiveSemantics.Date => "Date",
        PrimitiveSemantics.Time => "Time",
        PrimitiveSemantics.DateTime => "DateTime",
        PrimitiveSemantics.Duration => "Duration",
        _ => throw new InvalidOperationException($"Unmapped primitive semantics '{semantics}'.")
    };
}