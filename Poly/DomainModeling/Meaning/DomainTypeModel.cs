using Poly.Analysis;
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

    /// <summary>
    /// Fork A (lock #679 6841065713), structural: two types are the same type when they share a definition, so a
    /// declared alias (<c>Cents: decimal</c>) is the same type as <c>Decimal</c>. A nominal ruling changes this
    /// predicate only.
    /// </summary>
    public static bool SameType(ExprType a, ExprType b) =>
        a.Definition is { } definition && ReferenceEquals(definition, b.Definition);

    /// <summary>
    /// The numeric primitives <c>sum</c> accumulates, each with the primitive its accumulator is: an Int32 property
    /// sums into Number, as the lowered seed <c>0L</c> does.
    /// </summary>
    private static readonly (string Property, string Result)[] SumPrimitives = [
        ("Number", "Number"), ("Int32", "Number"), ("Float", "Float"), ("Decimal", "Decimal"),
    ];

    /// <summary>
    /// The type of <c>sum Rel P</c> for a property of type <paramref name="property"/>: the numeric primitive the
    /// property is the same type as (<see cref="SameType"/>), with no domain name; <see cref="ExprType.Unresolved"/>
    /// when the property is not numeric. Analysis and lowering's typed seed both read it, through this model's
    /// type maps, so a pack override applies to both (fork B).
    /// </summary>
    public ExprType SumResult(ExprType property) {
        foreach (var (name, result) in SumPrimitives) {
            if (SameType(property, Resolve(name)))
                return Resolve(result) with { DomainName = null };
        }
        return ExprType.Unresolved;
    }

    /// <summary>
    /// The model of this analysis run: the one stamped on <paramref name="domain"/>, or a new one over the
    /// session's type maps, stamped so every pass in the run reads the same model.
    /// </summary>
    internal static DomainTypeModel Of(AnalysisContext context, Domain domain) {
        if (context.GetMetadata<DomainTypeModelMetadata>(domain) is { } stamped) return stamped.Model;
        var model = new DomainTypeModel(domain, RuntimeAnalysisCache.Session(domain).TypeMaps);
        context.SetMetadata(domain, new DomainTypeModelMetadata(model));
        return model;
    }

    public NumericKind KindOf(ExprType type) => type.Definition?.PrimitiveType switch {
        // Int32 → Integer64 is a judgement V-02 may revisit (T-02 A1: Int32 bounds parse as decimal).
        Prim.Int32 or Prim.Int64 => NumericKind.Integer64,
        Prim.Float64 => NumericKind.Float64,
        Prim.Decimal => NumericKind.Decimal,
        _ => NumericKind.None
    };
}