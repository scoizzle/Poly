using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Introspection;

using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// The one type model over Introspection: resolve a domain name, relate two types, name a numeric family.
/// Bound to one (<see cref="Domain"/>, <see cref="TypeMappingRegistry"/>) pair; a new session needs a new model.
/// <c>ExpressionTypeAnalyzer</c> is its production caller since T-04.
/// </summary>
internal sealed class DomainTypeModel {
    private readonly DomainTypeDefinitionProvider _provider;

    public DomainTypeModel(Domain domain, TypeMappingRegistry typeMaps) {
        _provider = new DomainTypeDefinitionProvider(domain, typeMaps);
    }

    public ExprType Resolve(string domainName) =>
        _provider.GetTypeDefinition(domainName) is { } definition
            ? new ExprType(definition, Optional: false, domainName)
            : ExprType.Unresolved;

    public ExprType OfProperty(Property property) {
        ArgumentNullException.ThrowIfNull(property);
        var type = Resolve(property.Type.TypeName);
        return type.IsResolved
            ? type with { Optional = !EntityStructureAnalyzer.IsRequired(property) }
            : type;
    }

    /// <summary>
    /// How a value of <paramref name="from"/> relates to <paramref name="to"/>'s definition. Optionality is ignored:
    /// the caller checks it (T-05/T-06), so this verdict is not a soundness claim.
    /// </summary>
    public Assignability Assign(ExprType from, ExprType to) {
        if (from.Definition is not { } source || to.Definition is not { } target)
            return Assignability.None;
        if (ReferenceEquals(source, target))
            return Assignability.Identity;

        // ResolveConversionFrom is the door: Float64 to Decimal is explicit there, as the C# specification has it.
        if (target.ResolveConversionFrom(source) is { } conversion) {
            return conversion.Kind is ConversionOperatorKind.Implicit
                ? Assignability.Widening
                : Assignability.RoundingNarrowing;
        }
        return Assignability.None;
    }

    public NumericKind KindOf(ExprType type) => type.Definition?.PrimitiveType switch {
        // Int32 → Integer64 is a judgement V-02 may revisit (T-02 A1: Int32 bounds parse as decimal).
        Prim.Int32 or Prim.Int64 => NumericKind.Integer64,
        Prim.Float64 => NumericKind.Float64,
        Prim.Decimal => NumericKind.Decimal,
        _ => NumericKind.None
    };
}