using System.ComponentModel.DataAnnotations;
using System.Reflection;

using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.DomainModeling.Ontology.Bootstrap;

/// <summary>
/// Maps CLR types to domain entity definitions, and verifies domain entities
/// against CLR types for round-trip mutation testing.
///
/// <list type="bullet">
///   <item><b>CLR → Domain:</b> Reflect a C# record/class into <see cref="Property"/> arrays
///   for <c>AddPropertyToEntity</c>, mapping well-known types to domain primitives
///   and DataAnnotations attributes to domain <see cref="Constraint"/>s.</item>
///   <item><b>Domain → CLR verification:</b> Check that a domain entity's properties
///   and constraints match a CLR type — useful for asserting mutation results.</item>
/// </list>
///
/// Unmapped CLR types throw <see cref="NotSupportedException"/> so gaps are surfaced
/// immediately rather than silently producing wrong domain definitions.
/// </summary>
public static class ClrTypeEntityMapping {
    /// <summary>
    /// Maps CLR types to domain primitive type names defined in
    /// <see cref="CanonicalBuiltInTypeCatalog"/>.
    /// Returns <c>null</c> for unmapped types.
    /// </summary>
    public static string? ClrTypeToDomainName(Type type) {
        if (type == typeof(string)) return "Text";
        if (type == typeof(int)) return "Number";
        if (type == typeof(long)) return "Number";
        if (type == typeof(decimal)) return "Number";
        if (type == typeof(double)) return "Number";
        if (type == typeof(float)) return "Number";
        if (type == typeof(short)) return "Number";
        if (type == typeof(byte)) return "Number";
        if (type == typeof(bool)) return "Boolean";
        if (type == typeof(DateTime)) return "DateTime";
        if (type == typeof(DateTimeOffset)) return "DateTime";
        if (type == typeof(DateOnly)) return "Date";
        if (type == typeof(TimeOnly)) return "Time";
        if (type == typeof(TimeSpan)) return "Duration";
        if (type == typeof(Guid)) return "Uuid";

        var inner = Nullable.GetUnderlyingType(type);
        if (inner is not null)
            return ClrTypeToDomainName(inner);

        return null;
    }

    /// <summary>
    /// Maps DataAnnotations validation attributes to domain <see cref="Constraint"/> objects.
    /// Returns <c>null</c> for attributes with no domain analogue (silently skipped).
    /// </summary>
    public static Constraint? ClrAttributeToConstraint(Attribute attr) => attr switch {
        RequiredAttribute => new RequiredConstraint(),
        RangeAttribute r => new RangeConstraint(r.Minimum, r.Maximum),
        StringLengthAttribute sl => new LengthConstraint(0, sl.MaximumLength),
        MinLengthAttribute min => new LengthConstraint(min.Length, int.MaxValue),
        MaxLengthAttribute max => new LengthConstraint(0, max.Length),
        RegularExpressionAttribute re => new PatternConstraint(re.Pattern),
        _ => null
    };

    /// <summary>
    /// Reflects <typeparamref name="T"/>'s public instance properties into
    /// domain <see cref="Property"/> definitions.
    /// Throws <see cref="NotSupportedException"/> if a CLR type has no domain mapping.
    /// </summary>
    public static Property[] ToProperties<T>() => ToProperties(typeof(T));

    /// <summary>
    /// Reflects on <paramref name="type"/>'s public instance properties.
    /// </summary>
    public static Property[] ToProperties(Type type) {
        ArgumentNullException.ThrowIfNull(type);

        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(pi => pi.CanRead && pi.GetIndexParameters().Length == 0)
            .Select(ToDomainProperty)
            .ToArray();
    }

    /// <summary>
    /// Verifies that all properties on <paramref name="entity"/> have a matching
    /// property on <typeparamref name="T"/> with a consistent domain type mapping.
    /// Throws if a property is missing or types don't align.
    ///
    /// Useful for round-trip mutation testing:
    /// <code>
    /// // Start from a CLR type
    /// var domain = DomainFactory.Create("Test", b => b.AddEntityFrom&lt;Person&gt;());
    /// // Apply mutations
    /// var result = new DomainEvolution(domain).Apply(changes);
    /// // Assert the result matches expected CLR shape
    /// result.Root.Types.OfType&lt;Entity&gt;().Single().EnsureMatchesType&lt;ExpectedPerson&gt;();
    /// </code>
    /// </summary>
    public static void EnsureMatchesType<T>(this Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);

        var clrType = typeof(T);
        var clrProps = clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(pi => pi.CanRead && pi.GetIndexParameters().Length == 0)
            .ToDictionary(pi => pi.Name, StringComparer.Ordinal);

        var missing = new List<string>();
        var typeMismatch = new List<string>();

        foreach (var domainProp in entity.Properties) {
            if (!clrProps.TryGetValue(domainProp.Name, out var clrProp)) {
                missing.Add(domainProp.Name);
                continue;
            }

            var expectedDomainType = ClrTypeToDomainName(clrProp.PropertyType);
            if (expectedDomainType is null) {
                // CLR type has no mapping — it's not a primitive, which is OK
                // for entity references; skip type checking for unmapped types
                continue;
            }

            if (domainProp.Type.TypeName != expectedDomainType) {
                typeMismatch.Add(
                    $"'{domainProp.Name}': expected domain type '{expectedDomainType}' " +
                    $"(from CLR '{clrProp.PropertyType.Name}') but found '{domainProp.Type.TypeName}'");
            }
        }

        if (missing.Count > 0 || typeMismatch.Count > 0) {
            var msg = $"Entity '{entity.Name}' does not match CLR type '{clrType.Name}':\n";
            if (missing.Count > 0)
                msg += $"  Missing properties: {string.Join(", ", missing)}\n";
            if (typeMismatch.Count > 0)
                msg += $"  Type mismatches:\n    {string.Join("\n    ", typeMismatch)}\n";
            throw new InvalidOperationException(msg.TrimEnd());
        }
    }

    // ── Internal ─────────────────────────────────────────────

    /// <summary>
    /// Converts a single <see cref="PropertyInfo"/> to a domain <see cref="Property"/>.
    /// Exposed for testing and advanced scenarios.
    ///
    /// Attributes are read from the property via <see cref="GetCustomAttributes()"/>,
    /// which handles both:
    /// <list type="bullet">
    ///   <item><c>[property: Required]</c> — explicit property-targeting syntax
    ///   (attribute placed directly on the synthesized property).</item>
    ///   <item>Implicit record constructor parameters — the fallback
    ///   <see cref="GetConstructorParameterAttributes"/> catches these.</item>
    /// </list>
    /// </summary>
    public static Property ToDomainProperty(PropertyInfo pi) {
        ArgumentNullException.ThrowIfNull(pi);

        var domainTypeName = ClrTypeToDomainName(pi.PropertyType)
            ?? throw new NotSupportedException(
                $"CLR type '{pi.PropertyType}' on property '{pi.Name}' has no domain mapping. " +
                $"Add a mapping in {nameof(ClrTypeEntityMapping)}.{nameof(ClrTypeToDomainName)}.");

        var constraints = pi.GetCustomAttributes()
            .Concat(GetConstructorParameterAttributes(pi))
            .Select(ClrAttributeToConstraint)
            .Where(c => c is not null)
            .Cast<Constraint>()
            .ToArray();

        return new Property(pi.Name, new DomainTypeReference(domainTypeName), constraints);
    }

    /// <summary>
    /// For record types, attributes on primary constructor parameters may not
    /// always propagate to the synthesized property. Checks the declaring type's
    /// constructor for a parameter matching the property name.
    /// </summary>
    private static IEnumerable<Attribute> GetConstructorParameterAttributes(PropertyInfo pi) {
        var declaringType = pi.DeclaringType;
        if (declaringType is null)
            yield break;

        var ctor = declaringType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(c => c.GetParameters().Any(p =>
                string.Equals(p.Name, pi.Name, StringComparison.Ordinal)));

        if (ctor is null)
            yield break;

        var param = ctor.GetParameters().FirstOrDefault(p =>
            string.Equals(p.Name, pi.Name, StringComparison.Ordinal));

        if (param is null)
            yield break;

        foreach (var attr in param.GetCustomAttributes())
            yield return attr;
    }
}

public static class EvolutionBuilderClrExtensions {
    /// <summary>
    /// Adds an entity whose properties are derived from the public instance
    /// properties of <typeparamref name="T"/>. Uses <see cref="ClrTypeEntityMapping"/>
    /// to map CLR types and DataAnnotations to domain types and constraints.
    /// </summary>
    /// <param name="entityName">Optional custom entity name (defaults to <c>T.Name</c>).</param>
    public static EvolutionBuilder AddEntityFrom<T>(this EvolutionBuilder builder, string? entityName = null) {
        ArgumentNullException.ThrowIfNull(builder);

        var name = entityName ?? typeof(T).Name;
        var properties = ClrTypeEntityMapping.ToProperties<T>();

        var result = builder.AddEntity(name);
        foreach (var prop in properties)
            result = result.AddPropertyToEntity(name, prop);
        return result;
    }
}