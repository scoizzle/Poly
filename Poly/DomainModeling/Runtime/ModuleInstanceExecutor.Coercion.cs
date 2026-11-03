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
        Parameter parameter, string? domainTypeName, object? raw, string label) {
        if (EntityTypeOf(parameter.TypeReference) is { } entityType)
            return ResolveEntityValue(entityType.Name, entityType.IsCollection, raw, label);

        if (raw is not null && domainTypeName is not null)
            raw = CoerceJsonValue(label, domainTypeName, raw);

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
        return PrimitiveForParameter(parameter) is { } primitive
            ? DictionaryBackedValue.CoerceReadValue(raw, primitive)
            : raw;
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
        if (!found || instance is null)
            throw new HostException($"{label}: instance '{raw}' of '{entityName}' not found.");
        if (!string.Equals(instance.EntityName, entityName, StringComparison.Ordinal))
            throw new HostException(
                $"{label}: instance '{instance.Id}' is a '{instance.EntityName}', expected '{entityName}'.");
        return instance.Bag;
    }

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
                _ => throw new HostException(
                    $"Number is integral; {label} got '{raw}', which is not a valid Number. Use Decimal.")
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
                _ => throw new HostException($"{label} expects Boolean; got '{raw}'.")
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