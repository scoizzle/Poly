using System.Text;

using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
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
    private readonly Dictionary<string, StorageEntity> _storageLookup;
    private readonly Dictionary<string, BehaviorEntity> _behaviorLookup;
    private readonly Dictionary<string, AggregateEntity> _aggregateLookup;
    private readonly Dictionary<string, EnumType> _enumLookup;
    private readonly AnalysisResult _analysis;

    public HttpFileGenerator(Domain domain,
        AnalysisResult analysis,
        StorageModel storageModel,
        BehaviorModel behaviorModel,
        AggregateModel aggregateModel,
        string baseUrl = "http://localhost:5201") {
        ArgumentNullException.ThrowIfNull(analysis);
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _baseUrl = baseUrl;
        _analysis = analysis;
        _storageLookup = storageModel.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _behaviorLookup = behaviorModel.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _aggregateLookup = aggregateModel.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _enumLookup = domain.Types.OfType<EnumType>().ToDictionary(e => e.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// The entity's CREATE-signature scalar params (the complete constructor
    /// metadata, non-nav) — the DTO/JSON body mirrors what Entity.Create(...) takes.
    /// </summary>
    private IReadOnlyList<ConstructorParameterOrder> GetCreateScalarParams(Entity entity) {
        if (_analysis.GetMetadata<EntityStructureMetadata>(entity) is { } esm)
            return esm.ConstructorParameters.Where(p => !p.IsNavigation).ToList();
        return [];
    }

    private StorageEntity GetStorageEntity(Entity entity) => _storageLookup[entity.Name];

    private IReadOnlyList<BehaviorAction> GetBehaviorActions(Entity entity) =>
        _behaviorLookup.TryGetValue(entity.Name, out var beh) ? beh.Actions : [];

    private (Entity Parent, string RelName)? GetAggregateParent(Entity child) {
        var agg = _aggregateLookup[child.Name];
        if (agg.IsRoot || agg.AggregateParentName is null || agg.ParentRelationshipName is null)
            return null;
        var parent = _entities.FirstOrDefault(e =>
            string.Equals(e.Name, agg.AggregateParentName, StringComparison.Ordinal));
        if (parent is null) return null;
        return (parent, agg.ParentRelationshipName);
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

    private void AppendEntitySection(StringBuilder sb, Entity entity, bool isRoot) {
        var store = GetStorageEntity(entity);
        var route = Pluralize(ToCamelCase(entity.Name));
        var keyExample = GetKeyExample(store);

        sb.AppendLine($"### ──────────── {Pluralize(entity.Name)} ────────────");
        sb.AppendLine();

        if (isRoot) {
            sb.AppendLine($"### List all {Pluralize(ToCamelCase(entity.Name))}");
            sb.AppendLine($"GET {_baseUrl}/api/{route}");
            sb.AppendLine();
            sb.AppendLine($"### Get {ToCamelCase(entity.Name)} by {(store.KeyProperty?.Name ?? "id")}");
            sb.AppendLine($"GET {_baseUrl}/api/{route}/{keyExample}");
            sb.AppendLine();
            sb.AppendLine($"### Create a new {ToCamelCase(entity.Name)}");
            sb.AppendLine($"POST {_baseUrl}/api/{route}");
            sb.AppendLine("Content-Type: application/json");
            sb.AppendLine();
            sb.AppendLine("{");
            var scalarProps = GetCreateScalarParams(entity)
                .Select(p => entity.Properties.First(x => string.Equals(x.Name, p.Name, StringComparison.Ordinal)))
                .ToList();
            for (int i = 0; i < scalarProps.Count; i++) {
                var comma = i < scalarProps.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{scalarProps[i].Name}\": {GetExampleJsonValue(scalarProps[i])}{comma}");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }

        if (!isRoot) {
            var parentCtx = GetAggregateParent(entity);
            if (parentCtx is { } ctx) {
                var (parentEntity, relName) = ctx;
                var parentStore = GetStorageEntity(parentEntity);
                var parentKeyEx = GetKeyExample(parentStore);
                var relRoute = $"{Pluralize(ToCamelCase(parentEntity.Name))}/{parentKeyEx}/{ToCamelCase(relName).ToLowerInvariant()}";
                sb.AppendLine($"### List {Pluralize(ToCamelCase(entity.Name))} for {ToCamelCase(parentEntity.Name)}");
                sb.AppendLine($"GET {_baseUrl}/api/{relRoute}");
                sb.AppendLine();
                sb.AppendLine($"### Get {ToCamelCase(entity.Name)} by id for {ToCamelCase(parentEntity.Name)}");
                sb.AppendLine($"GET {_baseUrl}/api/{relRoute}/{keyExample}");
                sb.AppendLine();
            }
        }

        foreach (var ia in GetBehaviorActions(entity))
            AppendActionRequest(sb, entity, ia, keyExample);
    }

    private void AppendActionRequest(StringBuilder sb, Entity entity, BehaviorAction ia, string keyExample) {
        var actionName = ToCamelCase(ia.Name);
        var isChild = !GetStorageEntity(entity).IsRoot;
        var parentRoute = "";
        if (isChild) {
            var parentCtx = GetAggregateParent(entity);
            if (parentCtx is { } ctx) {
                var (parentEntity, relName) = ctx;
                var parentStore = GetStorageEntity(parentEntity);
                var parentKeyExample = GetKeyExample(parentStore);
                parentRoute = $"{Pluralize(ToCamelCase(parentEntity.Name))}/{parentKeyExample}/{ToCamelCase(relName).ToLowerInvariant()}";
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
                if (param.IsEntityRef)
                    sb.AppendLine($"    \"{param.Name}Id\": \"example-{ToCamelCase(param.DomainType)}-id\"{comma}");
                else
                    sb.AppendLine($"    \"{param.Name}\": {GetExampleJsonValueForTransportParam(param)}{comma}");
            }
            sb.AppendLine("}");
        }
        sb.AppendLine();
    }

    private static string Pluralize(string name) => name + "s";
    private static string ToCamelCase(string name) => DomainTypeMapping.ToCamelCase(name);

    private static string GetKeyExample(StorageEntity store) {
        if (store.KeyProperty is null) return "1";
        return store.KeyClrType switch {
            "string" => "example-value",
            "long" or "int" => "42",
            "Guid" => "550e8400-e29b-41d4-a716-446655440000",
            _ => "example",
        };
    }

    private string GetExampleJsonValue(Property prop) {
        if (prop.Constraints.Any(c => c is UniqueConstraint) &&
            (prop.Type.TypeName is "Text" or "String")) {
            var baseVal = ToCamelCase(prop.Name);
            return $"\"example-{baseVal}\"";
        }
        _enumLookup.TryGetValue(prop.Type.TypeName, out var enumType);
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

    private string GetExampleJsonValueForTransportParam(BehaviorParameter param) {
        _enumLookup.TryGetValue(param.DomainType, out var enumType);
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