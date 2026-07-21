using System.Text;

using Poly.DomainModeling;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Lowering;

namespace Poly.DslCompiler;

/// <summary>
/// Generates a <c>demo.http</c> file with REST Client requests for every
/// CRUD and action endpoint in the generated API.
/// </summary>
public sealed class HttpFileGenerator {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly string _baseUrl;
    private readonly InfrastructureModel _infraModel;
    private readonly Dictionary<string, TransportEntity> _transportLookup;
    private readonly Dictionary<string, StorageEntity> _storageLookup;
    private readonly Dictionary<string, BehaviorEntity> _behaviorLookup;

    public HttpFileGenerator(Domain domain, string baseUrl = "http://localhost:5201",
        InfrastructureModel? infraModel = null) {
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _baseUrl = baseUrl;
        _infraModel = infraModel ?? new InfrastructureAnalyzer(domain).Analyze();
        _transportLookup = _infraModel.Transport.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _storageLookup = _infraModel.Storage.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _behaviorLookup = _infraModel.Behavior.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
    }

    private StorageEntity GetStorageEntity(Entity entity) =>
        _storageLookup.GetValueOrDefault(entity.Name) ?? new StorageEntity(entity);

    private IReadOnlyList<BehaviorAction> GetBehaviorActions(Entity entity) {
        if (_behaviorLookup.TryGetValue(entity.Name, out var beh))
            return beh.Actions;
        return [];
    }

    public string Generate() {
        var sb = new StringBuilder();
        sb.AppendLine($"### ═══════════════════════════════════════════════════════════");
        sb.AppendLine($"###  {_domain.Name} REST API — Generated");
        sb.AppendLine($"###");
        sb.AppendLine($"###  Start the server:");
        sb.AppendLine($"###    dotnet run --project path/to/project --urls \"{_baseUrl}\"");
        sb.AppendLine($"###");
        sb.AppendLine($"###  Then click \"Send Request\" above any request below.");
        sb.AppendLine($"### ═══════════════════════════════════════════════════════════");
        sb.AppendLine();

        foreach (var entity in _entities) {
            var isRoot = GetStorageEntity(entity).IsRoot;
            AppendEntitySection(sb, entity, isRoot);
        }

        return sb.ToString();
    }

    /// <param name="isRoot">True if the entity can exist independently (has CRUD endpoints).</param>
    private void AppendEntitySection(StringBuilder sb, Entity entity, bool isRoot) {
        var route = Pluralize(ToCamelCase(entity.Name));
        var uniqueProp = entity.Properties.FirstOrDefault(p =>
            p.Constraints.Any(c => c is UniqueConstraint));
        var hasKey = uniqueProp is not null;
        var keyExample = hasKey ? GetExampleValue(uniqueProp!) : "1";

        sb.AppendLine($"### ──────────── {Pluralize(entity.Name)} ────────────");
        sb.AppendLine();

        if (isRoot) {
            // CRUD for root entities
            sb.AppendLine($"### List all {Pluralize(ToCamelCase(entity.Name))}");
            sb.AppendLine($"GET {_baseUrl}/api/{route}");
            sb.AppendLine();
            sb.AppendLine($"### Get {ToCamelCase(entity.Name)} by {(hasKey ? uniqueProp!.Name : "id")}");
            sb.AppendLine($"GET {_baseUrl}/api/{route}/{keyExample}");
            sb.AppendLine();
            sb.AppendLine($"### Create a new {ToCamelCase(entity.Name)}");
            sb.AppendLine($"POST {_baseUrl}/api/{route}");
            sb.AppendLine("Content-Type: application/json");
            sb.AppendLine();
            sb.AppendLine("{");
            var scalarProps = entity.Properties
                .Where(p => !p.Constraints.Any(c => c is DefaultValueConstraint))
                .Where(p => !_entities.Any(e => string.Equals(e.Name, p.Type.TypeName, StringComparison.Ordinal)))
                .OrderBy(p => p.Name)
                .ToList();
            for (int i = 0; i < scalarProps.Count; i++) {
                var comma = i < scalarProps.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{scalarProps[i].Name}\": {GetExampleJsonValue(scalarProps[i])}{comma}");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Child entity list/detail under parent
        if (!isRoot) {
            var parents = GetParentRelationships(entity).ToList();
            foreach (var (parentEntity, rel) in parents) {
                var parentUnique = parentEntity.Properties.FirstOrDefault(p =>
                    p.Constraints.Any(c => c is UniqueConstraint));
                var parentKeyEx = parentUnique is not null ? GetExampleValue(parentUnique) : "1";
                var relRoute = $"{Pluralize(ToCamelCase(parentEntity.Name))}/{parentKeyEx}/{ToCamelCase(rel.Name).ToLowerInvariant()}";
                sb.AppendLine($"### List {Pluralize(ToCamelCase(entity.Name))} for {ToCamelCase(parentEntity.Name)}");
                sb.AppendLine($"GET {_baseUrl}/api/{relRoute}");
                sb.AppendLine();
                sb.AppendLine($"### Get {ToCamelCase(entity.Name)} by id for {ToCamelCase(parentEntity.Name)}");
                sb.AppendLine($"GET {_baseUrl}/api/{relRoute}/{keyExample}");
                sb.AppendLine();
            }
        }

        // Actions on the entity (for both root and child)
        var transportActions = GetBehaviorActions(entity);
        foreach (var ia in transportActions) {
            AppendActionRequest(sb, entity, ia, hasKey, keyExample);
        }
    }

    /// <summary>Returns parent relationships for a child entity.</summary>
    private IEnumerable<(Entity Parent, Relationship Rel)> GetParentRelationships(Entity child) {
        foreach (var rel in _domain.Relationships) {
            if (!string.Equals(rel.Target.TypeName, child.Name, StringComparison.Ordinal))
                continue;
            if (rel.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany))
                continue;
            var parent = _entities.FirstOrDefault(e =>
                string.Equals(e.Name, rel.Source.TypeName, StringComparison.Ordinal));
            if (parent is null) continue;
            yield return (parent, rel);
        }
    }

    private void AppendActionRequest(StringBuilder sb, Entity entity,
        BehaviorAction ia, bool hasKey, string keyExample) {
        var actionName = ToCamelCase(ia.Name);

        var isChild = !GetStorageEntity(entity).IsRoot;
        var parentRoute = "";
        if (isChild) {
            var parents = GetParentRelationships(entity).ToList();
            if (parents.Count > 0) {
                var (parentEntity, rel) = parents[0];
                var parentUnique = parentEntity.Properties.FirstOrDefault(p =>
                    p.Constraints.Any(c => c is UniqueConstraint));
                var parentKeyExample = parentUnique is not null ? GetExampleValue(parentUnique) : "1";
                parentRoute = $"{Pluralize(ToCamelCase(parentEntity.Name))}/{parentKeyExample}/{ToCamelCase(rel.Name).ToLowerInvariant()}";
            }
        }

        var route = isChild && parentRoute.Length > 0
            ? parentRoute
            : $"{Pluralize(ToCamelCase(entity.Name))}";

        sb.AppendLine($"### Action: {ia.Name}");
        sb.AppendLine($"POST {_baseUrl}/api/{route}/{keyExample}/{actionName}");
        if (ia.Parameters.Count > 0) {
            sb.AppendLine("Content-Type: application/json");
            sb.AppendLine();
            sb.AppendLine("{");
            for (int i = 0; i < ia.Parameters.Count; i++) {
                var param = ia.Parameters[i];
                var comma = i < ia.Parameters.Count - 1 ? "," : "";
                if (param.IsEntityRef) {
                    sb.AppendLine($"    \"{param.Name}Id\": \"example-{ToCamelCase(param.DomainType)}-id\"{comma}");
                }
                else {
                    sb.AppendLine($"    \"{param.Name}\": {GetExampleJsonValueForTransportParam(param, entity)}{comma}");
                }
            }
            sb.AppendLine("}");
        }
        sb.AppendLine();
    }

    // ── Helpers ────────────────────────────────────────────────

    private static string Pluralize(string name) => name + "s";

    private static string ToCamelCase(string name) {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
            return name;
        int upperCount = 0;
        for (int i = 0; i < name.Length && char.IsUpper(name[i]); i++)
            upperCount++;
        if (upperCount <= 1)
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        return name.Substring(0, upperCount).ToLowerInvariant() + name.Substring(upperCount);
    }

    private static string GetExampleValue(Property prop) {
        if (prop.Constraints.Any(c => c is UniqueConstraint)) {
            return prop.Type.TypeName switch {
                "Text" or "String" => "example-value",
                "Number" or "Int" or "Int64" => "42",
                "Guid" or "Uuid" => "550e8400-e29b-41d4-a716-446655440000",
                _ => "example",
            };
        }
        return "example";
    }

    private string GetExampleJsonValue(Property prop) {
        if (prop.Constraints.Any(c => c is UniqueConstraint) &&
            (prop.Type.TypeName is "Text" or "String")) {
            var baseVal = ToCamelCase(prop.Name);
            return $"\"example-{baseVal}\"";
        }
        // If the type is an enum in the domain, use the first member
        var enumType = _domain.Types.OfType<EnumType>()
            .FirstOrDefault(e => string.Equals(e.Name, prop.Type.TypeName, StringComparison.Ordinal));
        if (enumType is not null && enumType.MemberNames.Count > 0)
            return $"\"{enumType.MemberNames[0]}\"";
        return prop.Type.TypeName switch {
            "Text" or "String" => "\"sample\"",
            "Number" or "Int" or "Int64" => "0",
            "Int32" => "0",
            "Boolean" or "Bool" => "false",
            "DateTime" or "Timestamp" => "\"2026-07-20T00:00:00Z\"",
            "Date" or "DateOnly" => "\"2026-07-20\"",
            "Decimal" => "0.0",
            "Float" or "Double" => "0.0",
            "Guid" or "Uuid" => "\"550e8400-e29b-41d4-a716-446655440000\"",
            _ => "0",
        };
    }

    private string GetExampleJsonValueForTransportParam(BehaviorParameter param, Entity entity) {
        // If the type is an enum in the domain, use the first member
        var enumType = _domain.Types.OfType<EnumType>()
            .FirstOrDefault(e => string.Equals(e.Name, param.DomainType, StringComparison.Ordinal));
        if (enumType is not null && enumType.MemberNames.Count > 0)
            return $"\"{enumType.MemberNames[0]}\"";

        return param.DomainType switch {
            "Text" or "String" => "\"sample\"",
            "Number" or "Int" or "Int64" => "0",
            "Int32" => "0",
            "Boolean" or "Bool" => "false",
            "DateTime" or "Timestamp" => "\"2026-07-20T00:00:00Z\"",
            "Date" or "DateOnly" => "\"2026-07-20\"",
            "Decimal" => "0.0",
            "Float" or "Double" => "0.0",
            "Guid" or "Uuid" => "\"550e8400-e29b-41d4-a716-446655440000\"",
            _ => "0",
        };
    }
}