using System.Text;

using Poly.DomainModeling;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Lowering;

namespace Poly.DslCompiler;

/// <summary>
/// Generates an ASP.NET Minimal API Program.cs from a <see cref="Domain"/>.
///
/// Consumes AggregateModel / StorageModel / BehaviorModel from infrastructure
/// analysis for root detection, parent routing, keys, and action shapes.
/// </summary>
public sealed class MinimalApiGenerator {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly string _domainName;
    private readonly InfrastructureModel _infraModel;
    private readonly Dictionary<string, TransportEntity> _transportLookup;
    private readonly Dictionary<string, StorageEntity> _storageLookup;
    private readonly Dictionary<string, BehaviorEntity> _behaviorLookup;
    private readonly Dictionary<string, AggregateEntity> _aggregateLookup;

    public MinimalApiGenerator(Domain domain, InfrastructureModel? infraModel = null) {
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _domainName = domain.Name;
        _infraModel = infraModel ?? new InfrastructureAnalyzer(domain).Analyze();
        _transportLookup = _infraModel.Transport.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _storageLookup = _infraModel.Storage.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _behaviorLookup = _infraModel.Behavior.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _aggregateLookup = _infraModel.Aggregate.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
    }

    private IReadOnlyList<BehaviorAction> GetBehaviorActions(Entity entity) =>
        _behaviorLookup.TryGetValue(entity.Name, out var beh) ? beh.Actions : [];

    private StorageEntity GetStorageEntity(Entity entity) => _storageLookup[entity.Name];

    private AggregateEntity GetAggregateEntity(Entity entity) => _aggregateLookup[entity.Name];

    /// <summary>
    /// Parent context for a child entity from AggregateModel.
    /// </summary>
    private (Entity Parent, string RelName, string? BackRefName)? GetAggregateParent(Entity child) {
        var agg = GetAggregateEntity(child);
        if (agg.IsRoot || agg.AggregateParentName is null || agg.ParentRelationshipName is null)
            return null;
        var parent = _entities.FirstOrDefault(e =>
            string.Equals(e.Name, agg.AggregateParentName, StringComparison.Ordinal));
        if (parent is null) return null;
        return (parent, agg.ParentRelationshipName, agg.BackReferencePropertyName);
    }

    public string Generate(string dbContextName) {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using Poly.Generated;");
        sb.AppendLine();
        AppendProgramStart(sb, dbContextName);
        AppendCrudEndpoints(sb, dbContextName);
        AppendChildListEndpoints(sb, dbContextName);
        AppendActionEndpoints(sb, dbContextName);
        AppendStart(sb);
        AppendSeedMethod(sb, dbContextName);
        AppendDtos(sb);
        return sb.ToString();
    }

    private readonly string BuilderVar = "builder";
    private readonly string AppVar = "app";

    private void AppendProgramStart(StringBuilder sb, string dbContextName) {
        sb.AppendLine("var builder = WebApplication.CreateBuilder(args);");
        sb.AppendLine();
        sb.AppendLine("// ── JSON: handle navigation cycles ──");
        sb.AppendLine($"{BuilderVar}.Services.ConfigureHttpJsonOptions(options =>");
        sb.AppendLine("{");
        sb.AppendLine("    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;");
        sb.AppendLine("    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());");
        sb.AppendLine("});");
        sb.AppendLine();
        sb.AppendLine("// ── EF Core InMemory ──");
        sb.AppendLine($"{BuilderVar}.Services.AddDbContext<{dbContextName}>(options =>");
        sb.AppendLine($"    options.UseInMemoryDatabase(\"{_domainName}\"));");
        sb.AppendLine();
        sb.AppendLine($"var {AppVar} = {BuilderVar}.Build();");
        sb.AppendLine();
        sb.AppendLine("// ── Seed data ──");
        sb.AppendLine($"using (var scope = {AppVar}.Services.CreateScope())");
        sb.AppendLine("{");
        sb.AppendLine($"    var db = scope.ServiceProvider.GetRequiredService<{dbContextName}>();");
        sb.AppendLine("    await SeedAsync(db);");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private void AppendCrudEndpoints(StringBuilder sb, string dbContextName) {
        foreach (var entity in _entities.Where(e => GetStorageEntity(e).IsRoot))
            AppendEntityCrud(sb, entity, dbContextName);
    }

    private void AppendChildListEndpoints(StringBuilder sb, string dbContextName) {
        foreach (var entity in _entities.Where(e => !GetStorageEntity(e).IsRoot)) {
            var parentCtx = GetAggregateParent(entity);
            if (parentCtx is null) continue;

            var (parentEntity, relNameRaw, backRefRaw) = parentCtx.Value;
            var parentStore = GetStorageEntity(parentEntity);
            var childStore = GetStorageEntity(entity);
            var relName = ToCamelCase(relNameRaw);
            var parentKey = parentStore.KeyName;
            var parentKeyType = parentStore.KeyClrType;
            var childKey = childStore.KeyName;
            var childKeyType = childStore.KeyClrType;
            var parentKeyPropName = parentStore.KeyProperty?.Name;
            var childKeyPropName = childStore.KeyProperty?.Name;
            var pascalRel = Pascalize(relNameRaw);
            var backRefName = backRefRaw is not null ? Pascalize(backRefRaw) : null;

            var listRoute = $"/api/{Pluralize(parentEntity.Name).ToLowerInvariant()}/{{{parentKey}}}/{relName.ToLowerInvariant()}";
            var detailRoute = $"{listRoute}/{{{childKey}}}";

            sb.AppendLine($"// ── {parentEntity.Name} → {entity.Name} ──");
            sb.AppendLine($"app.MapGet(\"{listRoute}\", async ({parentKeyType} {parentKey}, {dbContextName} db) =>");
            sb.AppendLine("{");
            sb.AppendLine($"    var parent = await db.{Pluralize(parentEntity.Name)}.FindAsync({parentKey});");
            sb.AppendLine($"    if (parent is null) return Results.NotFound(new {{ error = \"{parentEntity.Name} not found\" }});");
            sb.AppendLine($"    await db.Entry(parent).Collection(e => e.{pascalRel}).LoadAsync();");
            sb.AppendLine($"    return Results.Ok(parent.{pascalRel});");
            sb.AppendLine("});");
            sb.AppendLine();

            sb.AppendLine($"app.MapGet(\"{detailRoute}\", async ({parentKeyType} {parentKey}, {childKeyType} {childKey}, {dbContextName} db) =>");
            sb.AppendLine("{");
            if (childKeyPropName is not null && backRefName is not null && parentKeyPropName is not null) {
                sb.AppendLine($"    var child = await db.{Pluralize(entity.Name)}");
                sb.AppendLine($"        .Where(e => e.{backRefName}.{parentKeyPropName} == {parentKey})");
                sb.AppendLine($"        .FirstOrDefaultAsync(e => e.{childKeyPropName} == {childKey});");
            }
            else if (backRefName is not null && parentKeyPropName is not null) {
                sb.AppendLine($"    var parent = await db.{Pluralize(parentEntity.Name)}.FindAsync({parentKey});");
                sb.AppendLine($"    if (parent is null) return Results.NotFound(new {{ error = \"{parentEntity.Name} not found\" }});");
                sb.AppendLine($"    await db.Entry(parent).Collection(e => e.{pascalRel}).LoadAsync();");
                sb.AppendLine($"    var child = parent.{pascalRel}.FirstOrDefault();");
            }
            else {
                sb.AppendLine($"    var child = await db.{Pluralize(entity.Name)}.FindAsync({childKey});");
            }
            sb.AppendLine($"    if (child is null) return Results.NotFound(new {{ error = \"{entity.Name} not found\" }});");
            sb.AppendLine($"    return Results.Ok(child);");
            sb.AppendLine("});");
            sb.AppendLine();
        }
    }

    private void AppendEntityCrud(StringBuilder sb, Entity entity, string dbContextName) {
        var store = GetStorageEntity(entity);
        var route = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";
        var keyName = store.KeyName;
        var keyType = store.KeyClrType;
        var keyProp = store.KeyProperty;
        var keyRoute = $"{route}/{{{keyName}}}";
        var keyParam = $"{keyType} {keyName}";
        var dbSet = $"db.{Pluralize(entity.Name)}";
        var dbLookup = $"await {dbSet}.FindAsync({keyName})";
        var dtoName = $"{entity.Name}Dto";

        sb.AppendLine($"// ── {entity.Name} ──");
        sb.AppendLine($"app.MapGet(\"{route}\", async ({dbContextName} db) =>");
        sb.AppendLine($"    await db.{Pluralize(entity.Name)}.ToListAsync());");
        sb.AppendLine();

        sb.AppendLine($"app.MapGet(\"{keyRoute}\", async ({keyParam}, {dbContextName} db) =>");
        sb.AppendLine($"    {dbLookup} is {entity.Name} {ToCamelCase(entity.Name)}");
        sb.AppendLine($"        ? Results.Ok({ToCamelCase(entity.Name)})");
        sb.AppendLine($"        : Results.NotFound());");
        sb.AppendLine();

        if (store.IsRoot) {
            sb.AppendLine($"app.MapPost(\"{route}\", async ({dtoName} dto, {dbContextName} db) =>");
            sb.AppendLine("{");
            AppendCreateCall(sb, entity, dtoName, "dto", route, keyProp);
            sb.AppendLine("});");
        }
        sb.AppendLine();
    }

    private void AppendCreateCall(StringBuilder sb, Entity entity, string dtoName, string dtoVar, string route, Property? uniqueProp) {
        var skipPost = false;
        var args = new List<string>();
        foreach (var prop in entity.Properties.Where(p => !p.Constraints.Any(c => c is DefaultValueConstraint)).OrderBy(p => p.Name)) {
            if (_entities.Any(e => string.Equals(e.Name, prop.Type.TypeName, StringComparison.Ordinal))) {
                skipPost = true;
                break;
            }
            args.Add($"{dtoVar}.{prop.Name}");
        }
        foreach (var rel in _domain.Relationships) {
            if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal)) continue;
            var isMany = rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
            if (isMany)
                args.Add($"Enumerable.Empty<{rel.Target.TypeName}>()");
        }

        if (skipPost) {
            sb.AppendLine($"    return Results.BadRequest(new {{ error = \"{entity.Name} requires related entities and cannot be created directly.\" }});");
            return;
        }

        var createCall = $"{entity.Name}.Create({string.Join(", ", args)})";
        var resultVar = $"{ToCamelCase(entity.Name)}Result";

        sb.AppendLine($"    var {resultVar} = {createCall};");
        sb.AppendLine($"    if (!{resultVar}.IsSuccess)");
        sb.AppendLine($"        return Results.Conflict(new {{ error = {resultVar}.ErrorMessage }});");
        sb.AppendLine($"    db.{Pluralize(entity.Name)}.Add({resultVar}.Value);");
        sb.AppendLine($"    await db.SaveChangesAsync();");

        if (uniqueProp is not null)
            sb.AppendLine($"    return Results.Created($\"{route}/{{{resultVar}.Value.{uniqueProp.Name}}}\", {resultVar}.Value);");
        else
            sb.AppendLine($"    return Results.Ok({resultVar}.Value);");
    }

    private void AppendActionEndpoints(StringBuilder sb, string dbContextName) {
        foreach (var entity in _entities) {
            var parentCtx = GetStorageEntity(entity).IsRoot ? null : GetAggregateParent(entity);
            foreach (var ia in GetBehaviorActions(entity))
                AppendActionEndpoint(sb, entity, ia, dbContextName, parentCtx);
        }
    }

    private void AppendActionEndpoint(
        StringBuilder sb,
        Entity entity,
        BehaviorAction ia,
        string dbContextName,
        (Entity Parent, string RelName, string? BackRefName)? parentCtx) {
        var store = GetStorageEntity(entity);
        var keyName = store.KeyName;
        var keyType = store.KeyClrType;
        var dbSet = $"db.{Pluralize(entity.Name)}";

        if (parentCtx is { } ctx) {
            var (parentEntity, relNameRaw, _) = ctx;
            var parentStore = GetStorageEntity(parentEntity);
            var parentKeyName = parentStore.KeyName;
            var parentKeyType = parentStore.KeyClrType;
            var parentRoute = $"/api/{Pluralize(parentEntity.Name).ToLowerInvariant()}/{{{parentKeyName}}}/{ToCamelCase(relNameRaw).ToLowerInvariant()}";
            var childRoute = $"{parentRoute}/{{{keyName}}}";
            var pascalRel = Pascalize(relNameRaw);

            sb.AppendLine($"// ── {parentEntity.Name} → {entity.Name}: {ia.Name} ──");
            sb.AppendLine($"app.MapPost(\"{childRoute}/{ToCamelCase(ia.Name).ToLowerInvariant()}\", async ({parentKeyType} {parentKeyName}, {keyType} {keyName}, {dbContextName} db) =>");
            sb.AppendLine("{");
            sb.AppendLine($"    var parentEntity = await db.{Pluralize(parentEntity.Name)}.FindAsync({parentKeyName});");
            sb.AppendLine($"    if (parentEntity is null) return Results.NotFound(new {{ error = \"{parentEntity.Name} not found\" }});");
            sb.AppendLine($"    var entity = await {dbSet}.FindAsync({keyName});");
            sb.AppendLine($"    if (entity is null) return Results.NotFound(new {{ error = \"{entity.Name} not found\" }});");
            sb.AppendLine();
            sb.AppendLine($"    // Verify child belongs to parent");
            sb.AppendLine($"    await db.Entry(parentEntity).Collection(e => e.{pascalRel}).LoadAsync();");
            sb.AppendLine($"    if (!parentEntity.{pascalRel}.Any(e => e == entity))");
            sb.AppendLine($"        return Results.NotFound(new {{ error = \"{entity.Name} not found for this {parentEntity.Name}\" }});");
        }
        else {
            var route = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";
            var actionRoute = $"{route}/{{{keyName}}}/{ToCamelCase(ia.Name).ToLowerInvariant()}";
            var dtoName = ia.Parameters.Count > 0 ? $"{Pascalize(ia.Name)}Dto" : null;
            var paramSignature = dtoName is not null
                ? $"{keyType} {keyName}, {dtoName} dto, {dbContextName} db"
                : $"{keyType} {keyName}, {dbContextName} db";

            sb.AppendLine($"// ── Action: {ia.Name} ──");
            sb.AppendLine($"app.MapPost(\"{actionRoute}\", async ({paramSignature}) =>");
            sb.AppendLine("{");
            sb.AppendLine($"    var entity = await {dbSet}.FindAsync({keyName});");
            sb.AppendLine($"    if (entity is null) return Results.NotFound(new {{ error = \"{entity.Name} not found\" }});");
        }

        foreach (var rel in _domain.Relationships) {
            if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal)) continue;
            if (rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
                sb.AppendLine($"    await db.Entry(entity).Collection(e => e.{Pascalize(rel.Name)}).LoadAsync();");
        }

        var entityLookups = new List<string>();
        var invokeArgs = new List<string>();
        foreach (var param in ia.Parameters) {
            if (param.IsEntityRef) {
                var lookupKey = $"dto.{param.Name}Id";
                var lookupVar = ToCamelCase(param.DomainType);
                entityLookups.Add($"var {lookupVar} = await db.{Pluralize(param.DomainType)}.FindAsync({lookupKey});");
                entityLookups.Add($"if ({lookupVar} is null) return Results.NotFound(new {{ error = \"{param.DomainType} not found\" }});");
                invokeArgs.Add(lookupVar);
            }
            else {
                invokeArgs.Add($"dto.{param.Name}");
            }
        }

        var invokeCall = ia.Parameters.Count > 0
            ? $"entity.{ia.Name}({string.Join(", ", invokeArgs)})"
            : $"entity.{ia.Name}()";

        sb.AppendLine();
        sb.AppendLine("    try");
        sb.AppendLine("    {");
        foreach (var lookup in entityLookups)
            sb.AppendLine($"        {lookup}");
        sb.AppendLine($"        var result = {invokeCall};");
        sb.AppendLine("        await db.SaveChangesAsync();");
        sb.AppendLine();
        AppendResultSwitch(sb, ia);
        sb.AppendLine("    }");
        sb.AppendLine("    catch (Exception ex)");
        sb.AppendLine("    {");
        sb.AppendLine("        return Results.Problem(detail: ex.Message, statusCode: 500);");
        sb.AppendLine("    }");
        sb.AppendLine("});");
        sb.AppendLine();
    }

    private void AppendResultSwitch(StringBuilder sb, BehaviorAction ia) {
        sb.AppendLine("        return result switch");
        sb.AppendLine("        {");
        if (ia.IsVoid)
            sb.AppendLine("            { IsSuccess: true } => Results.Ok(new { status = \"ok\" }),");
        else
            sb.AppendLine("            { IsSuccess: true, Value: var resultValue } => Results.Ok(resultValue),");
        sb.AppendLine("            { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),");
        sb.AppendLine("            _ => Results.StatusCode(500)");
        sb.AppendLine("        };");
    }

    private void AppendStart(StringBuilder sb) {
        sb.AppendLine("// ═══════════════════════════════════════════");
        sb.AppendLine($"//  {_domainName} API");
        sb.AppendLine("//  Generated from Poly DSL");
        sb.AppendLine("// ═══════════════════════════════════════════");
        sb.AppendLine();
        foreach (var entity in _entities.Where(e => GetStorageEntity(e).IsRoot)) {
            var store = GetStorageEntity(entity);
            var keyName = store.KeyName;
            var route = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";

            sb.AppendLine($"//   GET  {route}");
            sb.AppendLine($"//   GET  {route}/{{{keyName}}}");
            sb.AppendLine($"//   POST {route}");

            foreach (var ia in GetBehaviorActions(entity))
                sb.AppendLine($"//   POST {route}/{{{keyName}}}/{ToCamelCase(ia.Name)}");

            foreach (var child in _entities.Where(e => !GetStorageEntity(e).IsRoot)) {
                var parentCtx = GetAggregateParent(child);
                if (parentCtx is null) continue;
                var (parentEntity, relName, _) = parentCtx.Value;
                if (!string.Equals(parentEntity.Name, entity.Name, StringComparison.Ordinal)) continue;

                var childStore = GetStorageEntity(child);
                var childKey = childStore.KeyName;
                var relRoute = $"{route}/{{{keyName}}}/{ToCamelCase(relName).ToLowerInvariant()}";

                sb.AppendLine($"//   GET  {relRoute}");
                sb.AppendLine($"//   GET  {relRoute}/{{{childKey}}}");
                foreach (var ia in GetBehaviorActions(child))
                    sb.AppendLine($"//   POST {relRoute}/{{{childKey}}}/{ToCamelCase(ia.Name)}");
            }
        }
        sb.AppendLine();
        sb.AppendLine($"{AppVar}.Run();");
        sb.AppendLine();
    }

    private void AppendSeedMethod(StringBuilder sb, string dbContextName) {
        sb.AppendLine($"static async Task SeedAsync({dbContextName} db)");
        sb.AppendLine("{");
        sb.AppendLine("    if (db is null) return;");

        var seedableEntities = _entities.Where(e => GetStorageEntity(e).IsRoot).ToList();
        if (seedableEntities.Count > 0) {
            sb.AppendLine($"    if (await db.{Pluralize(seedableEntities[0].Name)}.AnyAsync()) return;");
            sb.AppendLine();
        }

        foreach (var entity in seedableEntities) {
            var scalarProps = entity.Properties
                .Where(p => !p.Constraints.Any(c => c is DefaultValueConstraint))
                .Where(p => !_entities.Any(e => string.Equals(e.Name, p.Type.TypeName, StringComparison.Ordinal)))
                .OrderBy(p => p.Name)
                .ToList();

            var sampleArgs = new List<string>();
            foreach (var prop in scalarProps)
                sampleArgs.Add(GetSampleValue(entity, prop));

            foreach (var rel in _domain.Relationships) {
                if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal)) continue;
                if (rel.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)) continue;
                sampleArgs.Add($"Enumerable.Empty<{rel.Target.TypeName}>()");
            }

            var dtoVar = ToCamelCase(entity.Name);
            sb.AppendLine($"    var {dtoVar}Result = {entity.Name}.Create({string.Join(", ", sampleArgs)});");
            sb.AppendLine($"    if ({dtoVar}Result.IsSuccess)");
            sb.AppendLine($"        db.Add({dtoVar}Result.Value);");
            sb.AppendLine();
        }

        sb.AppendLine("    await db.SaveChangesAsync();");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private void AppendDtos(StringBuilder sb) {
        sb.AppendLine("// ── DTOs ──");

        foreach (var entity in _entities) {
            var scalarProps = entity.Properties
                .Where(p => !p.Constraints.Any(c => c is DefaultValueConstraint))
                .Where(p => !_entities.Any(e => string.Equals(e.Name, p.Type.TypeName, StringComparison.Ordinal)))
                .OrderBy(p => p.Name)
                .ToList();

            if (scalarProps.Count == 0) continue;
            if (!GetStorageEntity(entity).IsRoot) continue;

            var fields = scalarProps.Select(prop => $"{GetClrTypeName(prop.Type.TypeName)} {prop.Name}");
            sb.AppendLine($"record {entity.Name}Dto({string.Join(", ", fields)});");
        }

        foreach (var entity in _entities) {
            foreach (var ia in GetBehaviorActions(entity)) {
                if (ia.Parameters.Count == 0) continue;
                var fields = new List<string>();
                foreach (var param in ia.Parameters) {
                    if (param.IsEntityRef)
                        fields.Add($"string {param.Name}Id");
                    else
                        fields.Add($"{GetClrTypeName(param.DomainType)} {param.Name}");
                }
                sb.AppendLine($"record {Pascalize(ia.Name)}Dto({string.Join(", ", fields)});");
            }
        }
    }

    private static string Pluralize(string name) => name + "s";
    private static string ToCamelCase(string name) => DomainTypeMapping.ToCamelCase(name);
    private static string Pascalize(string name) => DomainTypeMapping.ToPascalCase(name);
    private static string GetClrTypeName(string domainType) => DomainTypeMapping.ToClrTypeName(domainType);

    private string GetSampleValue(Entity entity, Property prop) {
        if (prop.Constraints.Any(c => c is DefaultValueConstraint))
            return "default";

        var propNameLower = prop.Name.ToLowerInvariant();
        if (propNameLower is "email" or "emailaddress") {
            var length = prop.Constraints.OfType<LengthConstraint>().FirstOrDefault();
            if (length is not null && length.MinLength > 0) {
                var localPart = length.MinLength > 5 ? new string('x', length.MinLength - 4) : "user";
                return $"\"{localPart}@test.com\"";
            }
            return "\"user@test.com\"";
        }

        var range = prop.Constraints.OfType<RangeConstraint>().FirstOrDefault();
        if (range is not null) {
            if (range.Minimum is long l && l > 0) return l.ToString();
            if (range.Maximum is long l2 && l2 > 0) return (l2 / 2).ToString();
            return "1";
        }

        if (prop.Type.TypeName is "Text" or "String") {
            var length = prop.Constraints.OfType<LengthConstraint>().FirstOrDefault();
            if (length is not null && length.MinLength > 0)
                return "\"" + new string('X', Math.Max(length.MinLength, 8)) + "\"";
        }

        var enumType = _domain.Types.OfType<EnumType>()
            .FirstOrDefault(e => string.Equals(e.Name, prop.Type.TypeName, StringComparison.Ordinal));
        if (enumType is not null && enumType.MemberNames.Count > 0)
            return $"{enumType.Name}.{enumType.MemberNames[0]}";

        return prop.Type.TypeName switch {
            "Text" or "String" => "\"Sample\"",
            "Number" or "Int" or "Int64" => "1",
            "Int32" => "1",
            "Boolean" or "Bool" => "false",
            "DateTime" or "Timestamp" => "DateTime.UtcNow",
            "Date" or "DateOnly" => "DateOnly.FromDateTime(DateTime.UtcNow)",
            "Decimal" => "1m",
            "Float" or "Double" => "1.0",
            "Guid" or "Uuid" => "Guid.NewGuid()",
            _ => "default",
        };
    }
}