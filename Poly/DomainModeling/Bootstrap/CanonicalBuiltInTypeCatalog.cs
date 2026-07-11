using Poly.DomainModeling.Evolution;
using Poly.Introspection;

namespace Poly.DomainModeling.Bootstrap;

/// <summary>
/// Defines the canonical set of built-in primitive types for the domain model.
/// This is the V3 replacement for <c>Poly.Data.Modeling.TypeSystem.CanonicalBuiltInTypeCatalog</c>.
///
/// The catalog produces <see cref="DomainChange"/> objects (specifically <see cref="AddPrimitiveTypeChange"/>)
/// that can be applied through the standard evolution pipeline — no V2 mutation API required.
/// </summary>
public static class CanonicalBuiltInTypeCatalog {
    /// <summary>
    /// The canonical built-in type definitions with their names and type categories.
    /// </summary>
    public static IReadOnlyList<(string Name, TypeCategory Category)> Definitions { get; } =
    [
        ("Boolean",  TypeCategory.Primitive),
        ("Number",   TypeCategory.Primitive | TypeCategory.Numeric),
        ("Text",     TypeCategory.Primitive | TypeCategory.Text),
        ("Date",     TypeCategory.Primitive | TypeCategory.Temporal),
        ("Time",     TypeCategory.Primitive | TypeCategory.Temporal),
        ("DateTime", TypeCategory.Primitive | TypeCategory.Temporal | TypeCategory.Instant),
        ("Duration", TypeCategory.Primitive | TypeCategory.Temporal),
        ("Uuid",     TypeCategory.Primitive | TypeCategory.Identifier),
        ("Binary",   TypeCategory.Primitive | TypeCategory.Binary),
    ];

    /// <summary>
    /// Returns a list of <see cref="DomainChange"/>s that add each canonical built-in
    /// primitive type to a domain. These changes can be passed to
    /// <c>new DomainEvolution(domain).Apply(changes)</c> or composed via
    /// <c>new DomainEvolution(domain).Evolve()</c>.
    /// </summary>
    public static IReadOnlyList<DomainChange> CreateChanges() {
        return Definitions
            .Select(d => new AddPrimitiveTypeChange(d.Name, d.Category, []))
            .ToList();
    }

    /// <summary>
    /// Applies all canonical built-in primitive types to the given domain,
    /// returning a new domain with them included. The original domain is unchanged.
    /// </summary>
    public static Domain ApplyTo(Domain domain) {
        var changes = CreateChanges();
        var result = new DomainEvolution(domain).Apply(changes);
        return result.Succeeded ? result.Root : domain;
    }

    /// <summary>
    /// Applies all canonical built-in primitive types to the given domain
    /// via the evolution builder, for use in chained fluent construction.
    /// </summary>
    public static EvolutionBuilder AddTo(EvolutionBuilder builder) {
        foreach (var (name, category) in Definitions) {
            builder.AddPrimitiveType(name, category);
        }
        return builder;
    }
}