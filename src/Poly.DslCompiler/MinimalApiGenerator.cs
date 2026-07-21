using System.Text;

using Poly.DomainModeling;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Lowering;

namespace Poly.DslCompiler;

/// <summary>
/// Generates an ASP.NET Minimal API Program.cs from a <see cref="Domain"/>.
///
/// Produces a self-contained <c>Program.cs</c> with:
///   • WebApplication setup + JSON/EF config
///   • CRUD endpoints for every entity
///   • Action endpoints for every entity action
///   • Seed data (from entity Create() factories)
///   • DTO records for POST/action request bodies
///
/// This is the "get it working first" implementation — string-based generation.
/// Next iteration: produce Syntax AST <c>TypeDefinitionNode</c> trees.
/// </summary>
public sealed class MinimalApiGenerator {
    private readonly Domain _domain;
    private readonly List<Entity> _entities;
    private readonly string _domainName;
    private readonly InfrastructureModel _infraModel;
    private readonly Dictionary<string, TransportEntity> _transportLookup;
    private readonly Dictionary<string, StorageEntity> _storageLookup;
    private readonly Dictionary<string, BehaviorEntity> _behaviorLookup;

    public MinimalApiGenerator(Domain domain, InfrastructureModel? infraModel = null) {
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _domainName = domain.Name;
        _infraModel = infraModel ?? new InfrastructureAnalyzer(domain).Analyze();
        _transportLookup = _infraModel.Transport.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _storageLookup = _infraModel.Storage.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _behaviorLookup = _infraModel.Behavior.Entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
    }

    /// <summary>Returns pre-computed actions for an entity from BehaviorModel.</summary>
    private IReadOnlyList<BehaviorAction> GetBehaviorActions(Entity entity) {
        if (_behaviorLookup.TryGetValue(entity.Name, out var beh))
            return beh.Actions;
        return [];
    }

    /// <summary>Returns storage info for an entity from StorageModel.</summary>
    private StorageEntity GetStorageEntity(Entity entity) {
        return _storageLookup.GetValueOrDefault(entity.Name)
            ?? new StorageEntity(entity);
    }


    /// <summary>Generates the complete Program.cs C# source.</summary>
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

    // ── Program.cs preamble ────────────────────────────────────

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

    // ── CRUD endpoints ────────────────────────────────────────

    private void AppendCrudEndpoints(StringBuilder sb, string dbContextName) {
        // CRUD endpoints only for root entities (those that can exist independently).
        foreach (var entity in _entities.Where(e => GetStorageEntity(e).IsRoot)) {
            AppendEntityCrud(sb, entity, dbContextName);
        }
    }

    /// <summary>
    /// Adds GET list/GET by key endpoints for child entities nested under parent routes.
    /// E.g. /api/patrons/{email}/loans, /api/patrons/{email}/loans/{id}
    /// </summary>
    private void AppendChildListEndpoints(StringBuilder sb, string dbContextName) {
        foreach (var entity in _entities.Where(e => !GetStorageEntity(e).IsRoot)) {
            var parents = GetParentRelationships(entity).ToList();
            if (parents.Count == 0) continue;

            foreach (var (parentEntity, rel) in parents) {
                var relName = ToCamelCase(rel.Name);
                var parentProp = parentEntity.Properties.FirstOrDefault(p =>
                    p.Constraints.Any(c => c is UniqueConstraint));
                var parentKey = parentProp is not null ? ToCamelCase(parentProp.Name) : "id";
                var parentKeyType = parentProp is not null ? "string" : "int";
                var childUnique = entity.Properties.FirstOrDefault(p =>
                    p.Constraints.Any(c => c is UniqueConstraint));
                var childKey = childUnique is not null ? ToCamelCase(childUnique.Name) : "id";

                var listRoute = $"/api/{Pluralize(parentEntity.Name).ToLowerInvariant()}/{{{parentKey}}}/{relName.ToLowerInvariant()}";
                var detailRoute = $"{listRoute}/{{{childKey}}}";

                // GET list: /api/parents/{parentKey}/{childPlural}
                sb.AppendLine($"// ── {parentEntity.Name} → {entity.Name} ──");
                sb.AppendLine($"app.MapGet(\"{listRoute}\", async ({parentKeyType} {parentKey}, {dbContextName} db) =>");
                sb.AppendLine("{");
                sb.AppendLine($"    var parent = await db.{Pluralize(parentEntity.Name)}.FindAsync({parentKey});");
                sb.AppendLine($"    if (parent is null) return Results.NotFound(new {{ error = \"{parentEntity.Name} not found\" }});");
                sb.AppendLine($"    await db.Entry(parent).Collection(e => e.{Pascalize(rel.Name)}).LoadAsync();");
                sb.AppendLine($"    return Results.Ok(parent.{Pascalize(rel.Name)});");
                sb.AppendLine("});");
                sb.AppendLine();

                // GET by key: /api/parents/{parentKey}/{childPlural}/{childKey}
                // Filter by both parent (via back-reference) and child's key.
                var childKeyType = childUnique is not null ? "string" : "int";
                var backRefRel = GetBackReferenceRelationship(entity, parentEntity);
                var backRefName = backRefRel is not null ? Pascalize(backRefRel.Name) : null;
                sb.AppendLine($"app.MapGet(\"{detailRoute}\", async ({parentKeyType} {parentKey}, {childKeyType} {childKey}, {dbContextName} db) =>");
                sb.AppendLine("{");
                if (childUnique is not null && backRefRel is not null && parentProp is not null) {
                    // Natural-key child — query with Where + FirstOrDefaultAsync
                    sb.AppendLine($"    var child = await db.{Pluralize(entity.Name)}");
                    sb.AppendLine($"        .Where(e => e.{backRefName}.{parentProp.Name} == {parentKey})");
                    sb.AppendLine($"        .FirstOrDefaultAsync(e => e.{childUnique.Name} == {childKey});");
                }
                else if (backRefRel is not null && parentProp is not null) {
                    // Shadow-key child — can't use FirstOrDefault on shadow key in LINQ
                    // Load parent collection and check membership via back-ref
                    sb.AppendLine($"    var parent = await db.{Pluralize(parentEntity.Name)}.FindAsync({parentKey});");
                    sb.AppendLine($"    if (parent is null) return Results.NotFound(new {{ error = \"{parentEntity.Name} not found\" }});");
                    sb.AppendLine($"    await db.Entry(parent).Collection(e => e.{Pascalize(rel.Name)}).LoadAsync();");
                    sb.AppendLine($"    var child = parent.{Pascalize(rel.Name)}.FirstOrDefault();");
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
    }

    /// <summary>
    /// Finds the singular navigation from a child entity back to its parent
    /// (e.g. Loan.borrower → Patron). Returns null if no back-reference exists.
    /// </summary>
    private Relationship? GetBackReferenceRelationship(Entity child, Entity parent) {
        return _domain.Relationships.FirstOrDefault(r =>
            string.Equals(r.Source.TypeName, child.Name, StringComparison.Ordinal) &&
            string.Equals(r.Target.TypeName, parent.Name, StringComparison.Ordinal) &&
            r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany));
    }

    private void AppendEntityCrud(StringBuilder sb, Entity entity, string dbContextName) {
        // Only called for root entities — see AppendCrudEndpoints filter.
        var route = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";
        var uniqueProp = entity.Properties.FirstOrDefault(p =>
            p.Constraints.Any(c => c is UniqueConstraint));
        var hasKey = uniqueProp is not null;
        var hasEntityRef = !GetStorageEntity(entity).IsRoot; // safety check

        var keyRoute = hasKey
            ? $"{route}/{{{ToCamelCase(uniqueProp!.Name)}}}"
            : $"{route}/{{id}}";
        var keyParam = hasKey
            ? $"string {ToCamelCase(uniqueProp!.Name)}"
            : "int id";
        var dbSet = $"db.{Pluralize(entity.Name)}";
        var dbLookup = hasKey
            ? $"await {dbSet}.FindAsync({ToCamelCase(uniqueProp!.Name)})"
            : $"await {dbSet}.FindAsync(id)";

        var dtoName = $"{entity.Name}Dto";

        // GET list
        sb.AppendLine($"// ── {entity.Name} ──");
        sb.AppendLine($"app.MapGet(\"{route}\", async ({dbContextName} db) =>");
        sb.AppendLine($"    await db.{Pluralize(entity.Name)}.ToListAsync());");
        sb.AppendLine();

        // GET single
        sb.AppendLine($"app.MapGet(\"{keyRoute}\", async ({keyParam}, {dbContextName} db) =>");
        sb.AppendLine($"    {dbLookup} is {entity.Name} {ToCamelCase(entity.Name)}");
        sb.AppendLine($"        ? Results.Ok({ToCamelCase(entity.Name)})");
        sb.AppendLine($"        : Results.NotFound());");
        sb.AppendLine();

        // POST create (only for seedable entities — no required entity refs)
        if (!hasEntityRef) {
            sb.AppendLine($"app.MapPost(\"{route}\", async ({dtoName} dto, {dbContextName} db) =>");
            sb.AppendLine("{");
            AppendCreateCall(sb, entity, dtoName, "dto", route, uniqueProp);
            sb.AppendLine("});");
        }
        sb.AppendLine();
    }

    private void AppendCreateCall(StringBuilder sb, Entity entity, string dtoName, string dtoVar, string route, Property? uniqueProp) {
        // Build Create() argument list from constructor-level properties.
        // There are three categories:
        //   1. Scalar entity properties  → come from DTO fields
        //   2. Collection navs (IEnumerable<T>) → default to Enumerable.Empty<T>()
        //   3. Entity reference navs (Book, Patron, etc.) → not supported for POST CRUD;
        //      entities with required entity-ref params skip POST generation.
        var skipPost = false;
        var args = new List<string>();
        foreach (var prop in entity.Properties.Where(p => !p.Constraints.Any(c => c is DefaultValueConstraint)).OrderBy(p => p.Name)) {
            if (_entities.Any(e => string.Equals(e.Name, prop.Type.TypeName, StringComparison.Ordinal))) {
                // Entity reference navigation — this entity can't be created standalone via POST
                skipPost = true;
                break;
            }
            args.Add($"{dtoVar}.{prop.Name}");
        }
        // Also handle collection navigations which appear as constructor params via relationships
        foreach (var rel in _domain.Relationships) {
            if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal)) continue;
            var isMany = rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;
            if (isMany) {
                // Collection nav — add Enumerable.Empty<T>() as default
                args.Add($"Enumerable.Empty<{rel.Target.TypeName}>()");
            }
        }

        if (skipPost) {
            // Entity requires reference navigations — cannot be created standalone
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

        // Created response with location header
        if (uniqueProp is not null) {
            sb.AppendLine($"    return Results.Created($\"{route}/{{{resultVar}.Value.{uniqueProp.Name}}}\", {resultVar}.Value);");
        }
        else {
            // Shadow key entities: no CLR property for the key, so omit location
            sb.AppendLine($"    return Results.Ok({resultVar}.Value);");
        }
    }

    // ── Action endpoints ──────────────────────────────────────

    private void AppendActionEndpoints(StringBuilder sb, string dbContextName) {
        foreach (var entity in _entities) {
            var isChild = !GetStorageEntity(entity).IsRoot;
            var parents = isChild ? GetParentRelationships(entity).ToList() : [];
            var transportActions = GetBehaviorActions(entity);

            // Action endpoints for each entity, using pre-computed BehaviorAction records
            foreach (var ia in transportActions) {
                AppendActionEndpoint(sb, entity, ia, dbContextName, parents);
            }
        }
    }

    /// <summary>
    /// Returns parent relationships for a child entity — one-to-many rels from
    /// a root entity to this child. For a child entity with no such relationship
    /// (unusual but possible), returns empty.
    /// </summary>
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

    /// <summary>
    /// Gets the route prefix and key info for an entity.
    /// Root entities: /api/books, /api/patrons
    /// Child entities: /api/parents/{parentKey}/{childPlural}
    /// Returns (routePrefix, keyName, keyType, isChild, parentKeyParam).
    /// </summary>
    private (string Route, string KeyName, string KeyType, string DbSet,
            string? ParentRoute, string? ParentKeyName, string? ParentKeyType)
        GetEntityRouteInfo(Entity entity, List<(Entity Parent, Relationship Rel)>? parents = null) {
        var uniqueProp = entity.Properties.FirstOrDefault(p =>
            p.Constraints.Any(c => c is UniqueConstraint));
        var keyName = uniqueProp is not null ? ToCamelCase(uniqueProp.Name) : "id";
        var keyType = uniqueProp is not null ? "string" : "int";
        var dbSet = $"db.{Pluralize(entity.Name)}";

        if (parents is { Count: > 0 }) {
            // Use the first parent for routing
            var (parent, rel) = parents[0];
            var parentProp = parent.Properties.FirstOrDefault(p =>
                p.Constraints.Any(c => c is UniqueConstraint));
            var parentKeyName = parentProp is not null ? ToCamelCase(parentProp.Name) : "id";
            var parentKeyType = parentProp is not null ? "string" : "int";
            var parentRoute = $"/api/{Pluralize(parent.Name).ToLowerInvariant()}/{{{parentKeyName}}}/{ToCamelCase(rel.Name).ToLowerInvariant()}";
            return (parentRoute, keyName, keyType, dbSet, parentRoute, parentKeyName, parentKeyType);
        }

        var route = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";
        return (route, keyName, keyType, dbSet, null, null, null);
    }

    private void AppendActionEndpoint(StringBuilder sb, Entity entity,
        BehaviorAction ia, string dbContextName,
        List<(Entity Parent, Relationship Rel)>? parents = null) {

        var (route, keyName, keyType, dbSet, parentRoute, parentKeyName, parentKeyType) =
            GetEntityRouteInfo(entity, parents);

        if (parents is { Count: > 0 }) {
            // Child entity: route is under parent: /api/parents/{parentKey}/{childPlural}/{childKey}/action
            var (parentEntity, rel) = parents[0];
            var childRoute = $"{parentRoute}/{{{keyName}}}";

            sb.AppendLine($"// ── {parentEntity.Name} → {entity.Name}: {ia.Name} ──");
            sb.AppendLine($"app.MapPost(\"{childRoute}/{ToCamelCase(ia.Name).ToLowerInvariant()}\", async ({parentKeyType} {parentKeyName}, {keyType} {keyName}, {dbContextName} db) =>");
            sb.AppendLine("{");

            var parentSet = $"db.{Pluralize(parentEntity.Name)}";
            sb.AppendLine($"    var parentEntity = await {parentSet}.FindAsync({parentKeyName});");
            sb.AppendLine($"    if (parentEntity is null) return Results.NotFound(new {{ error = \"{parentEntity.Name} not found\" }});");

            var childLookup = keyType == "int"
                ? $"await {dbSet}.FindAsync({keyName})"
                : $"await {dbSet}.FindAsync({keyName})";
            sb.AppendLine($"    var entity = {childLookup};");
            sb.AppendLine($"    if (entity is null) return Results.NotFound(new {{ error = \"{entity.Name} not found\" }});");
            sb.AppendLine();
            sb.AppendLine($"    // Verify child belongs to parent");
            sb.AppendLine($"    await db.Entry(parentEntity).Collection(e => e.{Pascalize(rel.Name)}).LoadAsync();");
            sb.AppendLine($"    if (!parentEntity.{Pascalize(rel.Name)}.Any(e => e == entity))");
            sb.AppendLine($"        return Results.NotFound(new {{ error = \"{entity.Name} not found for this {parentEntity.Name}\" }});");
        }
        else {
            // Root entity: direct action route
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

        // Include navigations for actions that need them
        foreach (var rel in _domain.Relationships) {
            if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal)) continue;
            if (rel.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany) {
                sb.AppendLine($"    await db.Entry(entity).Collection(e => e.{Pascalize(rel.Name)}).LoadAsync();");
            }
        }

        // Build invoke — entity-typed params are looked up from DB
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
        foreach (var lookup in entityLookups) {
            sb.AppendLine($"        {lookup}");
        }
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

        if (ia.IsVoid) {
            sb.AppendLine("            { IsSuccess: true } => Results.Ok(new { status = \"ok\" }),");
        }
        else {
            sb.AppendLine("            { IsSuccess: true, Value: var resultValue } => Results.Ok(resultValue),");
        }
        sb.AppendLine("            { ErrorMessage: [..] msg } => Results.Conflict(new { error = msg }),");
        sb.AppendLine("            _ => Results.StatusCode(500)");
        sb.AppendLine("        };");
    }

    // ── Program start ─────────────────────────────────────────

    private void AppendStart(StringBuilder sb) {
        sb.AppendLine("// ═══════════════════════════════════════════");
        sb.AppendLine($"//  {_domainName} API");
        sb.AppendLine("//  Generated from Poly DSL");
        sb.AppendLine("// ═══════════════════════════════════════════");
        sb.AppendLine();
        foreach (var entity in _entities) {
            var isRoot = GetStorageEntity(entity).IsRoot;

            if (isRoot) {
                var uniqueProp = entity.Properties.FirstOrDefault(p =>
                    p.Constraints.Any(c => c is UniqueConstraint));
                var keyName = uniqueProp is not null ? ToCamelCase(uniqueProp.Name) : "id";
                var route = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";

                sb.AppendLine($"//   GET  {route}");
                sb.AppendLine($"//   GET  {route}/{{{keyName}}}");
                sb.AppendLine($"//   POST {route}");

                // Actions on this root entity (from pre-computed BehaviorAction)
                var transportActions = GetBehaviorActions(entity);
                foreach (var ia in transportActions) {
                    sb.AppendLine($"//   POST {route}/{{{keyName}}}/{ToCamelCase(ia.Name)}");
                }

                // Child entities nested under this parent
                foreach (var child in _entities.Where(e => !GetStorageEntity(e).IsRoot)) {
                    var parents = GetParentRelationships(child).ToList();
                    foreach (var (parentEntity, rel) in parents) {
                        if (!string.Equals(parentEntity.Name, entity.Name, StringComparison.Ordinal)) continue;
                        var childUnique = child.Properties.FirstOrDefault(p =>
                            p.Constraints.Any(c => c is UniqueConstraint));
                        var childKey = childUnique is not null ? ToCamelCase(childUnique.Name) : "id";
                        var relRoute = $"{route}/{{{keyName}}}/{ToCamelCase(rel.Name).ToLowerInvariant()}";

                        sb.AppendLine($"//   GET  {relRoute}");
                        sb.AppendLine($"//   GET  {relRoute}/{{{childKey}}}");

                        var childTransportActions = GetBehaviorActions(child);
                        foreach (var ia in childTransportActions) {
                            sb.AppendLine($"//   POST {relRoute}/{{{childKey}}}/{ToCamelCase(ia.Name)}");
                        }
                    }
                }
            }
        }
        sb.AppendLine();
        sb.AppendLine($"{AppVar}.Run();");
        sb.AppendLine();
    }

    // ── Seed data ─────────────────────────────────────────────

    private void AppendSeedMethod(StringBuilder sb, string dbContextName) {
        sb.AppendLine($"static async Task SeedAsync({dbContextName} db)");
        sb.AppendLine("{");
        sb.AppendLine("    if (db is null) return;");

        // Only seed entities that have no required entity reference navs
        var seedableEntities = _entities
            .Where(e => !_domain.Relationships
                .Any(r => string.Equals(r.Source.TypeName, e.Name, StringComparison.Ordinal)
                    && r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
                    && !string.Equals(r.Target.TypeName, e.Name, StringComparison.Ordinal)))
            .ToList();

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
            foreach (var prop in scalarProps) {
                sampleArgs.Add(GetSampleValue(entity, prop));
            }

            // Collection navs: default to empty
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

    // ── DTOs ──────────────────────────────────────────────────

    private void AppendDtos(StringBuilder sb) {
        sb.AppendLine("// ── DTOs ──");

        // CRUD DTOs — only for entities with all-scalar Create() params
        foreach (var entity in _entities) {
            var scalarProps = entity.Properties
                .Where(p => !p.Constraints.Any(c => c is DefaultValueConstraint))
                .Where(p => !_entities.Any(e => string.Equals(e.Name, p.Type.TypeName, StringComparison.Ordinal)))
                .OrderBy(p => p.Name)
                .ToList();

            if (scalarProps.Count == 0)
                continue;

            // Only emit DTO if all non-default params are scalar (no entity refs)
            if (!GetStorageEntity(entity).IsRoot) continue;

            var fields = scalarProps.Select(prop => $"{GetClrTypeName(prop.Type.TypeName)} {prop.Name}");
            sb.AppendLine($"record {entity.Name}Dto({string.Join(", ", fields)});");
        }

        // Action DTOs — entity-typed parameters become lookup keys (string ID)
        foreach (var entity in _entities) {
            var behaviorActions = GetBehaviorActions(entity);
            foreach (var ia in behaviorActions) {
                if (ia.Parameters.Count == 0) continue;
                var fields = new List<string>();
                foreach (var param in ia.Parameters) {
                    // Entity-typed params become string lookups
                    if (param.IsEntityRef)
                        fields.Add($"string {param.Name}Id");
                    else
                        fields.Add($"{param.ClrTypeName} {param.Name}");
                }
                sb.AppendLine($"record {Pascalize(ia.Name)}Dto({string.Join(", ", fields)});");
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    private List<Relationship> GetEntityNavProperties(Entity entity) {
        return _domain.Relationships
            .Where(r => string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal)
                     && r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany))
            .ToList();
    }

    private static string Pluralize(string name) => name + "s";

    private static string ToCamelCase(string name) {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
            return name;
        // Handle acronyms
        int upperCount = 0;
        for (int i = 0; i < name.Length && char.IsUpper(name[i]); i++)
            upperCount++;
        if (upperCount <= 1)
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        return name.Substring(0, upperCount).ToLowerInvariant() + name.Substring(upperCount);
    }

    private static string Pascalize(string name) {
        if (string.IsNullOrEmpty(name))
            return name;
        if (char.IsUpper(name[0]))
            return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    private static string FirstLetterToLower(string name) {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string GetClrTypeName(string domainType) => domainType switch {
        "Text" or "String" => "string",
        "Number" or "Int" or "Int64" => "long",
        "Int32" => "int",
        "Boolean" or "Bool" => "bool",
        "DateTime" or "Timestamp" => "DateTime",
        "Date" or "DateOnly" => "DateOnly",
        "Time" or "TimeOnly" => "TimeOnly",
        "Duration" or "TimeSpan" => "TimeSpan",
        "Decimal" => "decimal",
        "Float" or "Double" => "double",
        "Guid" or "Uuid" => "Guid",
        _ => domainType, // enum or entity reference — use the type name as-is
    };

    private string GetSampleValue(Entity entity, Property prop) {
        if (prop.Constraints.Any(c => c is DefaultValueConstraint))
            return "default";

        // Common property name conventions for better seed data
        var propNameLower = prop.Name.ToLowerInvariant();
        if (propNameLower == "email" || propNameLower == "emailaddress") {
            var length = prop.Constraints.OfType<LengthConstraint>().FirstOrDefault();
            if (length is not null && length.MinLength > 0) {
                var localPart = length.MinLength > 5 ? new string('x', length.MinLength - 4) : "user";
                return $"\"{localPart}@test.com\"";
            }
            return "\"user@test.com\"";
        }

        // Check for RangeConstraint to generate valid seed values
        var range = prop.Constraints.OfType<RangeConstraint>().FirstOrDefault();
        if (range is not null) {
            if (range.Minimum is not null && range.Minimum is long l && l > 0) {
                return l.ToString();
            }
            if (range.Maximum is not null && range.Maximum is long l2 && l2 > 0) {
                return (l2 / 2).ToString();
            }
            return "1";
        }

        // Check for LengthConstraint on Text — generate a string that fits
        if (prop.Type.TypeName is "Text" or "String") {
            var length = prop.Constraints.OfType<LengthConstraint>().FirstOrDefault();
            if (length is not null && length.MinLength > 0) {
                return "\"" + new string('X', Math.Max(length.MinLength, 8)) + "\"";
            }
        }

        // Check for enum types
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

    private static bool IsValueDomainType(string typeName) => typeName switch {
        "Number" or "Int" or "Int64" or "Int32" => true,
        "Boolean" or "Bool" => true,
        "DateTime" or "Timestamp" => true,
        "Date" or "DateOnly" => true,
        "Time" or "TimeOnly" => true,
        "Duration" or "TimeSpan" => true,
        "Decimal" => true,
        "Float" or "Double" => true,
        "Guid" or "Uuid" => true,
        _ => false,
    };
}