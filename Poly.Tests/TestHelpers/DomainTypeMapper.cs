using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// Thin wrapper around <see cref="ClrTypeEntityMapping"/> and <see cref="EvolutionBuilderClrExtensions"/>
/// for test convenience. Prefer the production API directly in tests:
///
/// <code>
/// DomainFactory.Create("Demo", b => b.AddEntityFrom&lt;Person&gt;())
/// </code>
/// </summary>
public static class DomainTypeMapper {
    public static string? ClrTypeToDomainName(Type type) =>
        ClrTypeEntityMapping.ClrTypeToDomainName(type);

    public static Constraint? ClrAttributeToConstraint(Attribute attr) =>
        ClrTypeEntityMapping.ClrAttributeToConstraint(attr);

    public static Property[] ToProperties<T>() =>
        ClrTypeEntityMapping.ToProperties<T>();

    public static Property[] ToProperties(Type type) =>
        ClrTypeEntityMapping.ToProperties(type);

    public static Func<EvolutionBuilder, EvolutionBuilder> EntityFrom<T>(string? entityName = null) {
        var name = entityName ?? typeof(T).Name;
        return b => b.AddEntityFrom<T>(name);
    }

    public static Domain CreateDomainWithEntity<T>(string domainName, string? entityName = null) =>
        DomainFactory.Create(domainName, b => b.AddEntityFrom<T>(entityName));
}