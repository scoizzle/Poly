using Poly.DomainModeling.Evolution;

namespace Poly.DomainModeling.Ontology.Bootstrap;

/// <summary>
/// Defines the canonical set of built-in primitive types for the domain model.
///
/// The catalog produces <see cref="DomainChange"/> objects (specifically <see cref="AddPrimitiveTypeChange"/>)
/// that can be applied through the standard evolution pipeline — no V2 mutation API required.
/// </summary>
public static class CanonicalBuiltInTypeCatalog {
    /// <summary>
    /// The canonical built-in type definitions with their names and type categories.
    /// </summary>
    public static IReadOnlyList<(string Name, PrimitiveSemantics Semantics)> Definitions { get; } =
    [
        ("Boolean",  PrimitiveSemantics.Boolean),
        ("Number",   PrimitiveSemantics.Number),
        ("Decimal",  PrimitiveSemantics.Decimal),
        ("Text",     PrimitiveSemantics.Text),
        ("Uuid",     PrimitiveSemantics.Uuid),
        ("Binary",   PrimitiveSemantics.Binary),
    ];

    /// <summary>
    /// Returns a list of <see cref="DomainChange"/>s that add each canonical built-in
    /// primitive type to a domain. These changes can be passed to
    /// <c>new DomainEvolution(domain).ApplyAndAnalyze(changes)</c> or composed via
    /// <c>new DomainEvolution(domain).Evolve()</c>.
    /// </summary>
    public static IReadOnlyList<DomainChange> CreateChanges() {
        return Definitions
            .Select(d => new AddPrimitiveTypeChange(d.Name, d.Semantics, []))
            .ToList();
    }

    /// <summary>
    /// Applies all canonical built-in primitive types to the given domain,
    /// returning a new domain with them included. The original domain is unchanged.
    /// </summary>
    public static Domain ApplyTo(Domain domain) {
        var changes = CreateChanges();
        var result = new DomainEvolution(domain).ApplyAndAnalyze(changes);
        return result.Succeeded ? result.Root : domain;
    }

    /// <summary>
    /// Applies all canonical built-in primitive types to the given domain
    /// via the evolution builder, for use in chained fluent construction.
    /// </summary>
    public static EvolutionBuilder AddTo(EvolutionBuilder builder) {
        foreach (var (name, semantics) in Definitions) {
            builder.AddPrimitiveType(name, semantics);
        }
        return builder;
    }
}