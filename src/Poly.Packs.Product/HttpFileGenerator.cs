using System.Text;

using Poly.Analysis;
using Poly.DomainModeling.Analysis;

namespace Poly.Packs.Product;

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
    /// The entity's scalar create slots — every member of the create DTO, so the sample body
    /// sends what the transport contract accepts (#588).
    /// </summary>
    private IReadOnlyList<CreateSlot> GetCreateScalarParams(Entity entity) {
        if (_analysis.GetStructure(entity) is { } esm)
            return [.. esm.CreateSlots.Where(s => s.Shape == CreateSlotShape.Scalar)];
        return [];
    }

    // The same published create slots the generated host reads for its POST route.
    private IReadOnlyList<CreateSlot> GetCreateSlots(Entity entity) =>
        _analysis.GetStructure(entity)?.CreateSlots
        ?? throw new InvalidOperationException(
            $"EntityStructureMetadata is required for the create slots of entity '{entity.Name}'.");

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
        sb.AppendLine($"###  The database starts empty. Add \"--SeedDemoData true\" to insert one");
        sb.AppendLine($"###  sample row per entity.");
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
        var keyExample = GetKeyExample(entity);

        sb.AppendLine($"### ──────────── {Pluralize(entity.Name)} ────────────");
        sb.AppendLine();

        if (isRoot) {
            sb.AppendLine($"### List all {Pluralize(ToCamelCase(entity.Name))}");
            sb.AppendLine($"GET {_baseUrl}/api/{route}");
            sb.AppendLine();
            sb.AppendLine($"### Get {ToCamelCase(entity.Name)} by {(MinimalApiGenerator.RouteKeyFor(store).Property?.Name ?? "id")}");
            sb.AppendLine($"GET {_baseUrl}/api/{route}/{keyExample}");
            sb.AppendLine();
            if (MinimalApiGenerator.CreatesDirectly(_entities, entity, GetCreateSlots(entity), _storageLookup)) {
                sb.AppendLine($"### Create a new {ToCamelCase(entity.Name)}");
                sb.AppendLine($"POST {_baseUrl}/api/{route}");
                var scalarProps = GetCreateScalarParams(entity)
                    .Select(p => entity.Properties.First(x => string.Equals(x.Name, p.Name, StringComparison.Ordinal)))
                    .ToList();
                // A Required link is sent as the target's key, the value the target's own create sample posts for it, so
                // the target's sample and this one succeed in file order. CreatesDirectly guarantees a natural key.
                var notes = new List<string>();
                var members = scalarProps.Select(p => $"\"{p.Name}\": {GetExampleJsonValue(p, notes)}")
                    .Concat(GetCreateSlots(entity).Where(MinimalApiGenerator.IsRequiredLink).Select(slot =>
                        $"\"{MinimalApiGenerator.CreateLinkMemberName(slot.Name)}\": "
                        + GetExampleJsonValue(MinimalApiGenerator.RouteKeyFor(_storageLookup[slot.Type.TypeName]).Property!)))
                    .ToList();
                // No member means no body: the route takes none, so the sample sends none.
                if (members.Count > 0) {
                    sb.AppendLine("Content-Type: application/json");
                    sb.AppendLine();
                    sb.AppendLine("{");
                    foreach (var note in notes)
                        sb.AppendLine(note);
                    for (int i = 0; i < members.Count; i++)
                        sb.AppendLine($"    {members[i]}{(i < members.Count - 1 ? "," : "")}");
                    sb.AppendLine("}");
                }
                sb.AppendLine();
            }
        }

        if (!isRoot) {
            var parentCtx = GetAggregateParent(entity);
            if (parentCtx is { } ctx) {
                var (parentEntity, relName) = ctx;
                var parentKeyEx = GetKeyExample(parentEntity);
                var relRoute = $"{Pluralize(ToCamelCase(parentEntity.Name))}/{parentKeyEx}/{ToCamelCase(relName).ToLowerInvariant()}";
                sb.AppendLine($"### List {Pluralize(ToCamelCase(entity.Name))} for {ToCamelCase(parentEntity.Name)}");
                sb.AppendLine($"GET {_baseUrl}/api/{relRoute}");
                sb.AppendLine();
                sb.AppendLine($"### Get {ToCamelCase(entity.Name)} by id for {ToCamelCase(parentEntity.Name)}");
                sb.AppendLine($"GET {_baseUrl}/api/{relRoute}/{keyExample}");
                sb.AppendLine();
            }
        }

        var emittedActions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ia in GetBehaviorActions(entity)) {
            if (!emittedActions.Add(ia.Name))
                continue;
            AppendActionRequest(sb, entity, ia, keyExample);
        }
    }

    private void AppendActionRequest(StringBuilder sb, Entity entity, BehaviorAction ia, string keyExample) {
        var actionName = ToCamelCase(ia.Name);
        var isChild = !GetStorageEntity(entity).IsRoot;
        var parentRoute = "";
        if (isChild) {
            var parentCtx = GetAggregateParent(entity);
            if (parentCtx is { } ctx) {
                var (parentEntity, relName) = ctx;
                var parentKeyExample = GetKeyExample(parentEntity);
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
                var property = ActionDtoPropertyName(param);
                if (param.IsEntityRef)
                    // The target's route key, with the JSON kind its DTO member binds: the same value the target's
                    // create sample posts for it, so a Number key is a number and not a quoted string (#940).
                    sb.AppendLine($"    \"{property}\": {EntityKeyExampleJson(_storageLookup[param.DomainType])}{comma}");
                else
                    sb.AppendLine($"    \"{property}\": {GetExampleJsonValueForTransportParam(param)}{comma}");
            }
            sb.AppendLine("}");
        }
        sb.AppendLine();
    }

    private static string Pluralize(string name) => Pluralizer.Pluralize(name);
    private static string ToCamelCase(string name) => DomainTypeMapping.ToCamelCase(name);
    private static string Pascalize(string name) => DomainTypeMapping.ToPascalCase(name);

    /// <summary>JSON key for an action body: the PascalCase DTO property.</summary>
    private static string ActionDtoPropertyName(BehaviorParameter param) =>
        param.IsEntityRef ? Pascalize(param.Name) + "Id" : Pascalize(param.Name);

    /// <summary>An entity argument's body value: the target's route key as <see cref="GetExampleJsonValue(Property, List{string}?)"/>
    /// prints it (a JSON number, boolean, or quoted literal), or <c>1</c> for a shadow key — which has no value a client
    /// could know (P-12), the same id <see cref="GetKeyExample"/> uses.</summary>
    private string EntityKeyExampleJson(StorageEntity store) =>
        MinimalApiGenerator.RouteKeyFor(store).Property is { } key ? GetExampleJsonValue(key) : "1";

    /// <summary>
    /// The path value a sample request uses for the entity's route key: the value its own create sample posts
    /// for that key, so the requests in this file succeed in the order they appear (#596). A shadow key has no
    /// such value, so the file keeps <c>1</c> — the row the opt-in seed (#595) writes, or the id a POST returns
    /// once #589 publishes it.
    /// </summary>
    private string GetKeyExample(Entity entity) {
        var key = MinimalApiGenerator.RouteKeyFor(GetStorageEntity(entity));
        if (key.Property is null) return "1";
        var json = GetExampleJsonValue(key.Property);
        return json.StartsWith('"') && json.EndsWith('"') ? json[1..^1] : json;
    }

    private string GetExampleJsonValue(Property prop, List<string>? notes = null) =>
        GetExampleJsonValue(prop, new HashSet<string>(StringComparer.Ordinal), notes);

    private string GetExampleJsonValue(Property prop, HashSet<string> stack, List<string>? notes = null) {
        if (TryValueType(prop.Type.TypeName, out var valueType))
            return FormatValueType(valueType, stack);

        var json = GetExampleJsonValueForType(prop);
        return ApplyConstraints(prop, json, notes);
    }

    private string GetExampleJsonValueForType(Property prop) {
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
            "Time" or "TimeOnly" => "\"10:30:00\"",
            "Duration" or "TimeSpan" => "\"01:30:00\"",
            "Decimal" => "0.0",
            "Float" or "Double" => "0.0",
            "Guid" or "Uuid" => "\"550e8400-e29b-41d4-a716-446655440000\"",
            // Binary over JSON is base64 (System.Text.Json's byte[] encoding): the two bytes 0x00 0x01.
            "Binary" => "\"AAE=\"",
            _ => "0",
        };
    }

    /// <summary>
    /// #596: the value a create sample posts must satisfy the property's own constraints, or the first request
    /// a reader sends answers 400. A range clamps the number; a length pads or trims the text; a pattern is met
    /// by trying the canonical candidates in order. When no candidate matches, the value is kept and
    /// <paramref name="notes"/> says so — the file never claims a sample the domain would reject.
    /// </summary>
    private static string ApplyConstraints(Property prop, string json, List<string>? notes) {
        var range = prop.Constraints.OfType<RangeConstraint>().FirstOrDefault();
        if (range is not null)
            json = ClampToRange(json, range);

        if (prop.Type.TypeName is not ("Text" or "String"))
            return json;

        var length = prop.Constraints.OfType<LengthConstraint>().FirstOrDefault();
        var pattern = prop.Constraints.OfType<PatternConstraint>().FirstOrDefault();
        var seed = json.Trim('"');
        if (pattern is null)
            return Quote(FitLength(seed, length));

        string? unmatched = null;
        foreach (var candidate in PatternCandidates(seed)) {
            var fitted = FitLength(candidate, length);
            unmatched ??= fitted;
            if (System.Text.RegularExpressions.Regex.IsMatch(fitted, pattern.Pattern))
                return Quote(fitted);
        }
        notes?.Add($"### NOTE: the '{prop.Name}' sample does not satisfy pattern(\"{pattern.Pattern}\");"
            + " edit it before sending.");
        return Quote(unmatched!);
    }

    /// <summary>The values a constrained text sample may be: the natural one first, then shapes that meet a
    /// common pattern (an uppercase code, an email, a number).</summary>
    private static IEnumerable<string> PatternCandidates(string seed) => new[] {
        seed, "sample", "example", "XXXXXXXX", "ABC123", "X1", "A1", "1", "user@test.com", "a@b.io",
    }.Distinct(StringComparer.Ordinal);

    private static string FitLength(string text, LengthConstraint? length) {
        if (length is null) return text;
        if (text.Length < length.MinLength)
            text += new string('X', length.MinLength - text.Length);
        return text.Length > length.MaxLength ? text[..length.MaxLength] : text;
    }

    private static string Quote(string text) => $"\"{text}\"";

    private static string ClampToRange(string json, RangeConstraint range) {
        if (!decimal.TryParse(json, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return json;
        var clamped = value;
        if (TryDecimal(range.Minimum, out var min) && clamped < min) clamped = min;
        if (TryDecimal(range.Maximum, out var max) && clamped > max) clamped = max;
        return clamped == value
            ? json
            : clamped.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool TryDecimal(object? bound, out decimal value) {
        value = 0;
        return bound is not null && decimal.TryParse(
            Convert.ToString(bound, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }

    private string GetExampleJsonValueForTransportParam(BehaviorParameter param) {
        if (TryValueType(param.DomainType, out var valueType))
            return FormatValueType(valueType, new HashSet<string>(StringComparer.Ordinal));

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
            "Time" or "TimeOnly" => "\"10:30:00\"",
            "Duration" or "TimeSpan" => "\"01:30:00\"",
            "Decimal" => "0.0",
            "Float" or "Double" => "0.0",
            "Guid" or "Uuid" => "\"550e8400-e29b-41d4-a716-446655440000\"",
            // Binary over JSON is base64 (System.Text.Json's byte[] encoding): the two bytes 0x00 0x01.
            "Binary" => "\"AAE=\"",
            _ => "0",
        };
    }

    private bool TryValueType(string name, out Poly.DomainModeling.Ontology.ValueType valueType) {
        var found = _domain.Types.OfType<Poly.DomainModeling.Ontology.ValueType>()
            .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
        if (found is null) {
            foreach (var contract in _domain.ImportedContracts) {
                found = contract.Types.FirstOrDefault(t =>
                    string.Equals(t.Name, name, StringComparison.Ordinal));
                if (found is not null)
                    break;
            }
        }
        valueType = found!;
        return found is not null;
    }

    /// <summary>One JSON object. Keys are camelCase, matching action routes in this file.</summary>
    private string FormatValueType(Poly.DomainModeling.Ontology.ValueType valueType, HashSet<string> stack) {
        if (!stack.Add(valueType.Name))
            return "{}";
        if (valueType.Properties.Count == 0) {
            stack.Remove(valueType.Name);
            return "{}";
        }
        var parts = new string[valueType.Properties.Count];
        for (var i = 0; i < valueType.Properties.Count; i++) {
            var field = valueType.Properties[i];
            parts[i] = $"\"{ToCamelCase(field.Name)}\": {GetExampleJsonValue(field, stack)}";
        }
        stack.Remove(valueType.Name);
        return "{ " + string.Join(", ", parts) + " }";
    }
}