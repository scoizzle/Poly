using System.Globalization;

using Poly.Ast.Nodes;
using Poly.DomainModeling.Meaning;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;

using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Runtime;

public sealed partial class ModuleInstanceExecutor {
    /// <summary>
    /// Tool-layer value → module argument. Three steps: entity-typed parameters take
    /// instance ids; JSON-ish scalars become the declared domain type (dates, guids,
    /// integral Number, Decimal, Boolean); then the module parameter's own CLR shape
    /// (null Text is <c>""</c>, 0/1 Boolean, primitive conversion) so the VM reads
    /// the value the way a bag read would.
    /// </summary>
    private object? CoerceValue(
        Parameter parameter, string? domainTypeName, object? raw, string label, bool namedTypeFailure = false) {
        if (EntityTypeOf(parameter.TypeReference) is { } entityType)
            return ResolveEntityValue(entityType.Name, entityType.IsCollection, raw, label);

        if (raw is not null && domainTypeName is not null)
            raw = CoerceJsonValue(label, domainTypeName, raw);
        if (namedTypeFailure && raw is not null && domainTypeName is not null
            && IsWrongTypedCreateScalar(domainTypeName, raw))
            throw new HostException($"{label} expects {domainTypeName}; got '{raw}'.");

        // An optional parameter (default null) keeps its null: coercing it would turn an
        // absent Date/Time/Duration into a zero of the underlying primitive.
        if (raw is null && parameter.DefaultValue is Constant { Value: null } && !IsTextLike(parameter))
            return null;
        if (raw is null && IsTextLike(parameter))
            raw = "";
        var typeName = ParameterTypeName(parameter);
        if (string.Equals(typeName, "Boolean", StringComparison.Ordinal))
            raw = raw switch {
                long l and (0L or 1L) => l == 1L,
                int i and (0 or 1) => i == 1,
                _ => raw
            };
        if (PrimitiveForParameter(parameter) is not { } primitive)
            return raw;
        try {
            return DictionaryBackedValue.CoerceReadValue(raw, primitive);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Number is integral", StringComparison.Ordinal)) {
            // A non-integral Number is a host failure, not the bag-read throw a wrong type is.
            throw new HostException(ex.Message);
        }
    }

    private (string Name, bool IsCollection)? EntityTypeOf(Node? typeReference) {
        if (typeReference is OptionalTypeReference optional)
            return EntityTypeOf(optional.InnerType);
        if (typeReference is NamedTypeReference { TypeArguments: [NamedTypeReference element] }
            && IsEntityName(element.TypeName))
            return (element.TypeName, true);
        var name = typeReference switch {
            NamedTypeReference named => named.TypeName,
            TypeReference type => type.TypeName,
            _ => null
        };
        return name is not null && IsEntityName(name) ? (name, false) : null;
    }

    private bool IsEntityName(string name) =>
        _module.Any(t => string.Equals(t.Name, name, StringComparison.Ordinal))
        && _domain.Types.OfType<Ontology.Entity>().Any(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    private object? ResolveEntityValue(string entityName, bool isCollection, object? raw, string label) {
        if (raw is null)
            return isCollection ? new List<object>() : null;
        if (!isCollection)
            return ResolveInstanceBag(entityName, raw, label);
        if (raw is string or not System.Collections.IEnumerable)
            throw new HostException($"{label} expects a list of '{entityName}' instance ids.");
        var bags = new List<object>();
        foreach (var item in (System.Collections.IEnumerable)raw)
            bags.Add(ResolveInstanceBag(entityName, item, label));
        return bags;
    }

    private object ResolveInstanceBag(string entityName, object? raw, string label) {
        ModuleInstance? instance = null;
        var found = raw switch {
            string id => _byId.TryGetValue(id, out instance),
            ModuleInstance given => _byId.TryGetValue(given.Id, out instance),
            not null => _byBag.TryGetValue(raw, out instance),
            _ => false
        };
        if (!found || instance is null) {
            // A failed Create registers nothing. create-in used to adopt that
            // unregistered bag and fail unique against the port. Reject it here
            // so the action fails instead of linking a null.
            if (BagOf(raw) is { } bag && UniqueCollision(entityName, bag) is { } collision)
                throw new HostException(collision);
            throw new HostException($"{label}: instance '{raw}' of '{entityName}' not found.");
        }
        if (!string.Equals(instance.EntityName, entityName, StringComparison.Ordinal))
            throw new HostException(
                $"{label}: instance '{instance.Id}' is a '{instance.EntityName}', expected '{entityName}'.");
        return instance.Bag;
    }

    private static IDictionary<string, object?>? BagOf(object? raw) => raw switch {
        ModuleInstance instance => instance.Bag,
        IDictionary<string, object?> bag => bag,
        _ => null
    };

    private string? UniqueCollision(string entityName, IDictionary<string, object?> bag) {
        var entity = _domain.Types.OfType<Poly.DomainModeling.Ontology.Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, entityName, StringComparison.Ordinal));
        if (entity is null)
            return null;
        foreach (var property in entity.Properties) {
            if (!property.Constraints.OfType<Poly.DomainModeling.Ontology.Constraints.UniqueConstraint>().Any())
                continue;
            if (!bag.TryGetValue(property.Name, out var value) || value is null)
                continue;
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (_port.Exists(entityName, property.Name, text))
                return $"Unique constraint violated: '{property.Name}' value is already used on another '{entityName}'.";
        }
        return null;
    }

    private static bool IsWrongTypedCreateScalar(string typeName, object raw) => typeName switch {
        "Number" or "Int" or "Int64" or "Int32" => raw is not (long or int or decimal or double or float),
        "Boolean" or "Bool" => raw is not (bool or long or int),
        _ => false
    };

    private static object CoerceJsonValue(string label, string typeName, object raw) =>
        typeName switch {
            "DateTime" or "Timestamp" => raw switch {
                DateTime => raw,
                string s when DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var parsed) => parsed,
                _ => throw new HostException($"{label} expects DateTime; got '{raw}'.")
            },
            "Date" or "DateOnly" => raw switch {
                DateOnly => raw,
                DateTime dateTime => DateOnly.FromDateTime(dateTime),
                string s when DateOnly.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed) => parsed,
                _ => throw new HostException($"{label} expects Date; got '{raw}'.")
            },
            "Time" or "TimeOnly" => raw switch {
                TimeOnly => raw,
                string s when TimeOnly.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed) => parsed,
                _ => throw new HostException($"{label} expects Time; got '{raw}'.")
            },
            "Duration" or "TimeSpan" => raw switch {
                TimeSpan => raw,
                string s when TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => throw new HostException($"{label} expects Duration; got '{raw}'.")
            },
            "Guid" or "Uuid" => raw switch {
                Guid => raw,
                string s when Guid.TryParse(s, out var parsed) => parsed,
                _ => throw new HostException($"{label} expects Guid; got '{raw}'.")
            },
            "Number" or "Int" or "Int64" or "Int32" => raw switch {
                long or int => raw,
                decimal m when m == decimal.Truncate(m) && m >= long.MinValue && m <= long.MaxValue => (long)m,
                double d when d == Math.Truncate(d) && !double.IsNaN(d) && !double.IsInfinity(d)
                    && d >= long.MinValue && d <= long.MaxValue => (long)d,
                float f when f == Math.Truncate(f) && !float.IsNaN(f) && !float.IsInfinity(f)
                    && f >= long.MinValue && f <= long.MaxValue => (long)f,
                // Wrong-typed values fail loud in GuardCompatible, the same message a bag read throws.
                _ => raw
            },
            "Decimal" => raw switch {
                decimal => raw,
                long or int => Convert.ToDecimal(raw, CultureInfo.InvariantCulture),
                double d => Convert.ToDecimal(d, CultureInfo.InvariantCulture),
                _ => throw new HostException($"{label} expects Decimal; got '{raw}'.")
            },
            "Boolean" or "Bool" => raw switch {
                bool => raw,
                0L => false,
                1L => true,
                // A non-boolean fails in GuardCompatible, not as a swallowed host failure.
                _ => raw
            },
            _ => raw
        };

    private static bool IsTextLike(Parameter parameter) =>
        parameter.TypeReference switch {
            ClrTypeReference { RuntimeType: var rt } when rt == typeof(string) => true,
            PrimitiveTypeReference { PrimitiveId: Prim.String } => true,
            NamedTypeReference { TypeName: "Text" or "String" or "string" } => true,
            TypeReference { TypeName: "Text" or "String" or "string" } => true,
            _ => false
        };

    private static string? ParameterTypeName(Parameter parameter) =>
        parameter.TypeReference switch {
            ClrTypeReference { RuntimeType: var rt } when rt == typeof(string) => "Text",
            ClrTypeReference { RuntimeType: var rt } when rt == typeof(bool) => "Boolean",
            PrimitiveTypeReference { PrimitiveId: Prim.String } => "Text",
            PrimitiveTypeReference { PrimitiveId: Prim.Boolean } => "Boolean",
            NamedTypeReference n => n.TypeName,
            TypeReference t => t.TypeName,
            _ => null
        };

    private static Prim? PrimitiveForParameter(Parameter parameter) =>
        parameter.TypeReference switch {
            PrimitiveTypeReference p => p.PrimitiveId,
            ClrTypeReference { RuntimeType: var rt } => rt.GetPrimitiveType(),
            NamedTypeReference n => DomainPrimitive(n.TypeName),
            TypeReference t => DomainPrimitive(t.TypeName),
            _ => null
        };

    private static Prim? DomainPrimitive(string typeName) =>
        DomainTypeMapping.TryPrimitiveType(
            DomainTypeMapping.ToClrTypeName(typeName), out var prim)
            ? prim : null;
}