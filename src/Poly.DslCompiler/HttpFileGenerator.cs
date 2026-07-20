using System.Text;

using Poly.DomainModeling;
using Poly.DomainModeling.Constraints;

namespace Poly.DslCompiler;

/// <summary>
/// Generates a <c>demo.http</c> file with REST Client requests for every
/// CRUD and action endpoint in the generated API.
/// </summary>
public sealed class HttpFileGenerator {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly string _baseUrl;

    public HttpFileGenerator(Domain domain, string baseUrl = "http://localhost:5201") {
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _baseUrl = baseUrl;
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
            AppendEntitySection(sb, entity);
        }

        return sb.ToString();
    }

    private void AppendEntitySection(StringBuilder sb, Entity entity) {
        var route = Pluralize(ToCamelCase(entity.Name));
        var uniqueProp = entity.Properties.FirstOrDefault(p =>
            p.Constraints.Any(c => c is UniqueConstraint));
        var hasKey = uniqueProp is not null;
        var keyExample = hasKey ? GetExampleValue(uniqueProp!) : "1";

        sb.AppendLine($"### ──────────── {Pluralize(entity.Name)} ────────────");
        sb.AppendLine();

        // GET list
        sb.AppendLine($"### List all {Pluralize(ToCamelCase(entity.Name))}");
        sb.AppendLine($"GET {_baseUrl}/api/{route}");
        sb.AppendLine();

        // GET single
        sb.AppendLine($"### Get {ToCamelCase(entity.Name)} by {(hasKey ? uniqueProp!.Name : "id")}");
        sb.AppendLine($"GET {_baseUrl}/api/{route}/{keyExample}");
        sb.AppendLine();

        // POST create
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

        // Actions on the entity
        foreach (var action in entity.Actions) {
            AppendActionRequest(sb, entity, action, hasKey, keyExample);
        }

        // Actions on stages
        foreach (var stage in entity.Stages) {
            foreach (var action in stage.Actions) {
                AppendActionRequest(sb, entity, action, hasKey, keyExample);
            }
        }
    }

    private void AppendActionRequest(StringBuilder sb, Entity entity,
        DomainModeling.Action action, bool hasKey, string keyExample) {
        var route = Pluralize(ToCamelCase(entity.Name));
        var actionName = ToCamelCase(action.Name);

        sb.AppendLine($"### Action: {action.Name}");
        sb.AppendLine($"POST {_baseUrl}/api/{route}/{keyExample}/{actionName}");
        if (action.Parameters.Count > 0) {
            sb.AppendLine("Content-Type: application/json");
            sb.AppendLine();
            sb.AppendLine("{");
            for (int i = 0; i < action.Parameters.Count; i++) {
                var param = action.Parameters[i];
                var comma = i < action.Parameters.Count - 1 ? "," : "";
                if (_entities.Any(e => string.Equals(e.Name, param.Type.TypeName, StringComparison.Ordinal))) {
                    sb.AppendLine($"    \"{param.Name}Id\": \"example-{ToCamelCase(param.Type.TypeName)}-id\"{comma}");
                }
                else {
                    sb.AppendLine($"    \"{param.Name}\": {GetExampleJsonValue(param)}{comma}");
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
}