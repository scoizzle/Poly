using System.Collections.Concurrent;

using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.Analysis.Semantics;
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
internal sealed class DomainTypeDefinitionProvider : ITypeDefinitionProvider {
    private readonly Dictionary<string, DomainType> _types = new(StringComparer.Ordinal);
    private readonly TypeMappingRegistry _typeMaps;
    private readonly ConcurrentDictionary<string, ITypeDefinition> _synthetic = new(StringComparer.Ordinal);

    public DomainTypeDefinitionProvider(Domain domain, TypeMappingRegistry typeMaps) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(typeMaps);
        _typeMaps = typeMaps;
        foreach (var type in domain.Types)
            _types.TryAdd(type.Name, type);
    }

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

    private ITypeDefinition? FromClrName(string name) =>
        DomainTypeMapping.TryPrimitiveType(_typeMaps.ToClrTypeName(name), out var primitive)
            ? Clr(primitive.GetClrType()!)
            : null;

    private static ITypeDefinition Clr(Type type) =>
        ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type)
        ?? throw new InvalidOperationException($"No CLR definition for '{type}'.");

    private AstTypeDefinition Synthesize(string name, DomainType declared) {
        var (kind, declaresEquality) = declared switch {
            EnumType => (TypeKind.Enum, true),
            Ontology.ValueType => (TypeKind.Struct, true),
            Entity => (TypeKind.Class, true),
            _ => throw new InvalidOperationException($"Domain type '{name}' is a '{declared.GetType().Name}', which has no definition shape.")
        };
        MethodDefinitionNode[]? methods = declaresEquality
            ? [Equality("op_Equality", name), Equality("op_Inequality", name)]
            : null;
        return new AstTypeDefinition(new TypeDefinitionNode(name, Kind: kind, Methods: methods), this);
    }

    /// <summary>
    /// The one operator an enum or entity declares: <c>static Boolean op(T, T)</c>. Declared members are
    /// how a modeled definition answers <c>GetOperators</c>; ordering and arithmetic stay undeclared.
    /// </summary>
    private static MethodDefinitionNode Equality(string operatorName, string typeName) =>
        new(operatorName,
            new PrimitiveTypeReference(Introspection.PrimitiveType.Boolean),
            [new Parameter("left", new NamedTypeReference(typeName)), new Parameter("right", new NamedTypeReference(typeName))],
            IsStatic: true);

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