using System.Reflection;
using System.Text.Json;

using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

namespace Poly.Data.Modeling.Recipes.Contracts;

internal sealed class ContractImportTypeResolver {
    private readonly Domain _domain;
    private readonly Domain.Mutation _mutation;
    private readonly List<DomainType> _createdTypes = [];
    private readonly Dictionary<Type, DomainType> _clrResolved = [];
    private readonly Dictionary<string, DomainType> _openApiResolved = new(StringComparer.Ordinal);
    private readonly Func<string, string> _typeNameTransform;

    internal ContractImportTypeResolver(Domain domain, Domain.Mutation mutation, Func<string, string>? typeNameTransform = null) {
        _domain = domain;
        _mutation = mutation;
        _typeNameTransform = typeNameTransform ?? (value => value);
    }

    public IReadOnlyList<DomainType> CreatedTypes => _createdTypes;

    public DomainType ResolveClrType(Type clrType) {
        ArgumentNullException.ThrowIfNull(clrType);

        clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (_clrResolved.TryGetValue(clrType, out var resolved)) {
            return resolved;
        }

        if (TryMapClrPrimitive(clrType, out var primitiveName, out var primitiveCategory)) {
            var primitive = EnsurePrimitive(primitiveName, primitiveCategory);
            _clrResolved[clrType] = primitive;
            return primitive;
        }

        if (clrType.IsArray || (clrType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(clrType))) {
            var collectionType = EnsurePrimitive($"{NormalizeName(clrType.Name)}Collection", TypeCategory.Structured | TypeCategory.Collection | TypeCategory.Primitive);
            _clrResolved[clrType] = collectionType;
            return collectionType;
        }

        var existing = _domain.FindType(NormalizeName(clrType.Name));
        if (existing is not null) {
            _clrResolved[clrType] = existing;
            return existing;
        }

        var entity = new Entity(_domain, NormalizeName(clrType.Name));
        _mutation.AddType(entity);
        _createdTypes.Add(entity);
        _clrResolved[clrType] = entity;

        foreach (var propertyInfo in clrType.GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
            if (propertyInfo.GetIndexParameters().Length > 0 || propertyInfo.GetMethod is null) {
                continue;
            }

            var propertyType = ResolveClrType(propertyInfo.PropertyType);
            if (entity.FindProperty(propertyInfo.Name) is not null) {
                continue;
            }

            _mutation.AddProperty(entity, new Property(_domain, propertyInfo.Name, propertyType));
        }

        return entity;
    }

    public DomainType ResolveOpenApiSchema(JsonElement root, JsonElement schema, string fallbackName) {
        if (schema.ValueKind == JsonValueKind.Undefined || schema.ValueKind == JsonValueKind.Null) {
            return EnsurePrimitive("Text", TypeCategory.Text | TypeCategory.Primitive);
        }

        if (schema.TryGetProperty("$ref", out var refProperty) && refProperty.ValueKind == JsonValueKind.String) {
            var refValue = refProperty.GetString();
            if (!string.IsNullOrWhiteSpace(refValue) && refValue.StartsWith("#/components/schemas/", StringComparison.Ordinal)) {
                var schemaName = refValue.Split('/').Last();
                if (_openApiResolved.TryGetValue(schemaName, out var cached)) {
                    return cached;
                }

                if (TryResolveComponentSchema(root, schemaName, out var componentSchema)) {
                    var resolved = ResolveOpenApiSchema(root, componentSchema, schemaName);
                    _openApiResolved[schemaName] = resolved;
                    return resolved;
                }

                throw new InvalidOperationException($"OpenAPI schema reference '{refValue}' could not be resolved.");
            }
        }

        if (schema.TryGetProperty("type", out var typeProperty) && typeProperty.ValueKind == JsonValueKind.String) {
            var typeName = typeProperty.GetString();
            return typeName switch {
                "string" => ResolveStringSchema(schema),
                "boolean" => EnsurePrimitive("Boolean", TypeCategory.Boolean),
                "integer" => EnsurePrimitive("Number", TypeCategory.Numeric | TypeCategory.Integer | TypeCategory.Primitive),
                "number" => EnsurePrimitive("Number", TypeCategory.Numeric | TypeCategory.FloatingPoint | TypeCategory.Primitive),
                "array" => ResolveArraySchema(root, schema, fallbackName),
                "object" => ResolveObjectSchema(root, schema, fallbackName),
                _ => EnsurePrimitive("Text", TypeCategory.Text | TypeCategory.Primitive)
            };
        }

        if (schema.TryGetProperty("properties", out _)) {
            return ResolveObjectSchema(root, schema, fallbackName);
        }

        return EnsurePrimitive("Text", TypeCategory.Text | TypeCategory.Primitive);
    }

    private DomainType ResolveObjectSchema(JsonElement root, JsonElement schema, string fallbackName) {
        var rawName = schema.TryGetProperty("title", out var titleProperty) && titleProperty.ValueKind == JsonValueKind.String
            ? titleProperty.GetString() ?? fallbackName
            : fallbackName;

        var typeName = NormalizeName(rawName);
        var existing = _domain.FindType(typeName);
        if (existing is Entity existingEntity) {
            return existingEntity;
        }

        var entity = new Entity(_domain, typeName);
        _mutation.AddType(entity);
        _createdTypes.Add(entity);

        if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object) {
            foreach (var property in properties.EnumerateObject()) {
                if (entity.FindProperty(property.Name) is not null) {
                    continue;
                }

                var propertyType = ResolveOpenApiSchema(root, property.Value, $"{typeName}{NormalizeName(property.Name)}");
                _mutation.AddProperty(entity, new Property(_domain, property.Name, propertyType));
            }
        }

        return entity;
    }

    private DomainType ResolveArraySchema(JsonElement root, JsonElement schema, string fallbackName) {
        if (schema.TryGetProperty("items", out var items) && items.ValueKind is JsonValueKind.Object or JsonValueKind.Null) {
            _ = ResolveOpenApiSchema(root, items, $"{NormalizeName(fallbackName)}Item");
        }

        return EnsurePrimitive($"{NormalizeName(fallbackName)}Collection", TypeCategory.Structured | TypeCategory.Primitive);
    }

    private DomainType ResolveStringSchema(JsonElement schema) {
        if (schema.TryGetProperty("format", out var formatProperty) && formatProperty.ValueKind == JsonValueKind.String) {
            return formatProperty.GetString() switch {
                "uuid" => EnsurePrimitive("Uuid", TypeCategory.Identifier | TypeCategory.Primitive),
                "date" => EnsurePrimitive("Date", TypeCategory.DateOnly | TypeCategory.Primitive),
                "date-time" => EnsurePrimitive("DateTime", TypeCategory.DateTime | TypeCategory.Primitive),
                "binary" => EnsurePrimitive("Binary", TypeCategory.Binary | TypeCategory.Primitive),
                _ => EnsurePrimitive("Text", TypeCategory.Text | TypeCategory.Primitive)
            };
        }

        return EnsurePrimitive("Text", TypeCategory.Text | TypeCategory.Primitive);
    }

    private Primitive EnsurePrimitive(string name, TypeCategory category) {
        var normalizedName = NormalizeName(name);
        var existing = _domain.FindPrimitive(normalizedName);
        if (existing is not null) {
            return existing;
        }

        var primitive = new Primitive(_domain, normalizedName, category);
        _mutation.AddType(primitive);
        _createdTypes.Add(primitive);
        return primitive;
    }

    private string NormalizeName(string raw) {
        ArgumentNullException.ThrowIfNull(raw);
        var normalized = _typeNameTransform(raw).Trim();
        if (normalized.Length == 0) {
            normalized = "ContractType";
        }

        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized) {
            if (char.IsLetterOrDigit(character)) {
                builder.Append(character);
            }
        }

        if (builder.Length == 0) {
            return "ContractType";
        }

        if (!char.IsLetter(builder[0])) {
            builder.Insert(0, 'T');
        }

        return builder.ToString();
    }

    private static bool TryMapClrPrimitive(Type clrType, out string primitiveName, out TypeCategory category) {
        primitiveName = string.Empty;
        category = TypeCategory.None;

        if (clrType == typeof(bool)) {
            primitiveName = "Boolean";
            category = TypeCategory.Boolean;
            return true;
        }

        if (clrType == typeof(string) || clrType == typeof(char)) {
            primitiveName = "Text";
            category = TypeCategory.Text | TypeCategory.Primitive;
            return true;
        }

        if (clrType == typeof(Guid)) {
            primitiveName = "Uuid";
            category = TypeCategory.Identifier | TypeCategory.Primitive;
            return true;
        }

        if (clrType == typeof(DateOnly)) {
            primitiveName = "Date";
            category = TypeCategory.DateOnly | TypeCategory.Primitive;
            return true;
        }

        if (clrType == typeof(TimeOnly)) {
            primitiveName = "Time";
            category = TypeCategory.TimeOfDay | TypeCategory.Primitive;
            return true;
        }

        if (clrType == typeof(DateTime) || clrType == typeof(DateTimeOffset)) {
            primitiveName = "DateTime";
            category = TypeCategory.DateTime | TypeCategory.Primitive;
            return true;
        }

        if (clrType == typeof(TimeSpan)) {
            primitiveName = "Duration";
            category = TypeCategory.Duration | TypeCategory.Primitive;
            return true;
        }

        if (clrType == typeof(byte[])) {
            primitiveName = "Binary";
            category = TypeCategory.Binary | TypeCategory.Primitive;
            return true;
        }

        if (clrType.IsPrimitive || clrType == typeof(decimal)) {
            primitiveName = "Number";
            category = TypeCategory.Numeric | TypeCategory.Primitive;
            return true;
        }

        return false;
    }

    private static bool TryResolveComponentSchema(JsonElement root, string schemaName, out JsonElement schema) {
        schema = default;
        if (!root.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (!components.TryGetProperty("schemas", out var schemas) || schemas.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (!schemas.TryGetProperty(schemaName, out schema)) {
            return false;
        }

        return true;
    }
}