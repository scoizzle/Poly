using System.Text;

using Poly.DomainModeling;
using Poly.DomainModeling.Constraints;

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

    public MinimalApiGenerator(Domain domain) {
        _domain = domain;
        _entities = domain.Types.OfType<Entity>().ToList();
        _domainName = domain.Name;
    }

    /// <summary>Generates the complete Program.cs C# source.</summary>
    public string Generate(string dbContextName) {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine();
        AppendProgramStart(sb, dbContextName);
        AppendCrudEndpoints(sb, dbContextName);
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
        foreach (var entity in _entities) {
            AppendEntityCrud(sb, entity, dbContextName);
        }
    }

    private void AppendEntityCrud(StringBuilder sb, Entity entity, string dbContextName) {
        var route = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";
        var uniqueProp = entity.Properties.FirstOrDefault(p =>
            p.Constraints.Any(c => c is UniqueConstraint));
        var hasKey = uniqueProp is not null;

        // Determine if this entity can be created standalone via POST
        // (all non-default constructor params are scalar or collection navs)
        var hasEntityRef = HasRequiredEntityRef(entity);

        var keyRoute = hasKey
            ? $"{route}/{{{ToCamelCase(uniqueProp!.Name)}}}"
            : $"{route}/{{id}}";
        var keyParam = hasKey
            ? $"string {ToCamelCase(uniqueProp!.Name)}"
            : "int id";
        var dbLookup = hasKey
            ? $"await db.Set<{entity.Name}>().FindAsync({ToCamelCase(uniqueProp!.Name)})"
            : $"await db.Set<{entity.Name}>().FindAsync(id)";

        var dtoName = $"{entity.Name}Dto";

        // GET list
        sb.AppendLine($"// ── {entity.Name} ──");
        sb.AppendLine($"app.MapGet(\"{route}\", async ({dbContextName} db) =>");
        sb.AppendLine($"    await db.Set<{entity.Name}>().ToListAsync());");
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
        sb.AppendLine($"    db.Set<{entity.Name}>().Add({resultVar}.Value);");
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
            // Actions declared on the entity itself
            foreach (var action in entity.Actions) {
                AppendActionEndpoint(sb, entity, action, stageName: null, dbContextName);
            }
            // Actions declared on stages
            foreach (var stage in entity.Stages) {
                foreach (var action in stage.Actions) {
                    AppendActionEndpoint(sb, entity, action, stage.Name, dbContextName);
                }
            }
        }
    }

    private void AppendActionEndpoint(StringBuilder sb, Entity entity,
        DomainModeling.Action action, string? stageName, string dbContextName) {
        var route = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";
        var uniqueProp = entity.Properties.FirstOrDefault(p =>
            p.Constraints.Any(c => c is UniqueConstraint));
        var keyName = uniqueProp is not null
            ? ToCamelCase(uniqueProp.Name)
            : "id";

        var actionRoute = $"{route}/{{{keyName}}}/{ToCamelCase(action.Name).ToLowerInvariant()}";
        var dtoName = action.Parameters.Count > 0 ? $"{Pascalize(action.Name)}Dto" : null;

        var paramSignature = dtoName is not null
            ? $"string {keyName}, {dtoName} dto, {dbContextName} db"
            : $"string {keyName}, {dbContextName} db";

        var dbLookup = uniqueProp is not null
            ? $"await db.Set<{entity.Name}>().FindAsync({keyName})"
            : $"await db.Set<{entity.Name}>().FindAsync(int.Parse({keyName}))";

        sb.AppendLine($"// ── Action: {action.Name} ──");
        sb.AppendLine($"app.MapPost(\"{actionRoute}\", async ({paramSignature}) =>");
        sb.AppendLine("{");

        // Load entity
        sb.AppendLine($"    var entity = {dbLookup};");
        sb.AppendLine($"    if (entity is null) return Results.NotFound(new {{ error = \"{entity.Name} not found\" }});");

        // Include navigations for actions that need them (CheckOut needs .Loans)
        foreach (var rel in _domain.Relationships) {
            if (string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal)) {
                var isMany = rel.Cardinality is RelationshipCardinality.OneToMany
                             or RelationshipCardinality.ManyToMany;
                if (isMany) {
                    sb.AppendLine($"    await db.Entry(entity).Collection(e => e.{Pascalize(rel.Name)}).LoadAsync();");
                }
            }
        }

        // Build invoke — entity-typed params are looked up from DB
        var entityLookups = new List<string>();
        var invokeArgs = new List<string>();
        foreach (var param in action.Parameters) {
            if (_entities.Any(e => string.Equals(e.Name, param.Type.TypeName, StringComparison.Ordinal))) {
                // Entity-typed param: look up from DB
                var lookupKey = $"dto.{param.Name}Id";
                var lookupVar = ToCamelCase(param.Type.TypeName);
                entityLookups.Add($"var {lookupVar} = await db.Set<{param.Type.TypeName}>().FindAsync({lookupKey});");
                entityLookups.Add($"if ({lookupVar} is null) return Results.NotFound(new {{ error = \"{param.Type.TypeName} not found\" }});");
                invokeArgs.Add(lookupVar);
            }
            else {
                invokeArgs.Add($"dto.{param.Name}");
            }
        }

        var invokeCall = action.Parameters.Count > 0
            ? $"entity.{action.Name}({string.Join(", ", invokeArgs)})"
            : $"entity.{action.Name}()";

        sb.AppendLine();
        sb.AppendLine("    try");
        sb.AppendLine("    {");
        foreach (var lookup in entityLookups) {
            sb.AppendLine($"        {lookup}");
        }
        sb.AppendLine($"        var result = {invokeCall};");
        sb.AppendLine("        await db.SaveChangesAsync();");
        sb.AppendLine();
        AppendResultSwitch(sb, action);
        sb.AppendLine("    }");
        sb.AppendLine("    catch (Exception ex)");
        sb.AppendLine("    {");
        sb.AppendLine("        return Results.Problem(detail: ex.Message, statusCode: 500);");
        sb.AppendLine("    }");
        sb.AppendLine("});");
        sb.AppendLine();
    }

    private void AppendResultSwitch(StringBuilder sb, DomainModeling.Action action) {
        var isVoid = action.Result is not { Members.Count: > 0 };
        sb.AppendLine("        return result switch");
        sb.AppendLine("        {");

        if (isVoid) {
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
            var route = $"/api/{Pluralize(entity.Name).ToLowerInvariant()}";
            sb.AppendLine($"//   GET  {route}");
            sb.AppendLine($"//   GET  {route}/{{id}}");
            sb.AppendLine($"//   POST {route}");
            foreach (var action in entity.Actions) {
                sb.AppendLine($"//   POST {route}/{{id}}/{ToCamelCase(action.Name)}");
            }
            foreach (var stage in entity.Stages) {
                foreach (var action in stage.Actions) {
                    sb.AppendLine($"//   POST {route}/{{id}}/{ToCamelCase(action.Name)}");
                }
            }
        }
        sb.AppendLine();
        sb.AppendLine($"{AppVar}.Run();");
        sb.AppendLine();
    }

    // ── Seed data ─────────────────────────────────────────────

    private void AppendSeedMethod(StringBuilder sb, string dbContextName) {
        sb.AppendLine("static async Task SeedAsync(DbContext db)");
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
            sb.AppendLine($"    if (await db.Set<{seedableEntities[0].Name}>().AnyAsync()) return;");
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
            if (HasRequiredEntityRef(entity)) continue;

            var fields = scalarProps.Select(prop => $"{GetClrTypeName(prop.Type.TypeName)} {prop.Name}");
            sb.AppendLine($"record {entity.Name}Dto({string.Join(", ", fields)});");
        }

        // Action DTOs — entity-typed parameters become lookup keys (string ID)
        foreach (var entity in _entities) {
            foreach (var action in entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions))) {
                if (action.Parameters.Count == 0) continue;
                var fields = new List<string>();
                foreach (var param in action.Parameters) {
                    // Entity-typed params become string lookups
                    if (_entities.Any(e => string.Equals(e.Name, param.Type.TypeName, StringComparison.Ordinal)))
                        fields.Add($"string {param.Name}Id");
                    else
                        fields.Add($"{GetClrTypeName(param.Type.TypeName)} {param.Name}");
                }
                sb.AppendLine($"record {Pascalize(action.Name)}Dto({string.Join(", ", fields)});");
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

    /// <summary>Returns true if the entity has a required entity-reference
    /// in its constructor-level params (either as a property or a singular nav).</summary>
    private bool HasRequiredEntityRef(Entity entity) {
        // Check entity properties whose type is another entity (e.g. book: Book)
        if (entity.Properties.Any(p => !p.Constraints.Any(c => c is DefaultValueConstraint)
            && _entities.Any(e => string.Equals(e.Name, p.Type.TypeName, StringComparison.Ordinal))))
            return true;
        // Check singular navigations that aren't self-references
        if (_domain.Relationships.Any(r => string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal)
            && r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
            && !string.Equals(r.Target.TypeName, entity.Name, StringComparison.Ordinal)))
            return true;
        return false;
    }

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

    private static string GetSampleValue(Entity entity, Property prop) {
        if (prop.Constraints.Any(c => c is DefaultValueConstraint))
            return "default"; // shouldn't happen — filtered above
        return prop.Type.TypeName switch {
            "Text" or "String" => $"\"Sample {prop.Name}\"",
            "Number" or "Int" or "Int64" => "0",
            "Int32" => "0",
            "Boolean" or "Bool" => "false",
            "DateTime" or "Timestamp" => "DateTime.UtcNow",
            "Date" or "DateOnly" => "DateOnly.FromDateTime(DateTime.UtcNow)",
            "Decimal" => "0m",
            "Float" or "Double" => "0.0",
            "Guid" or "Uuid" => "Guid.NewGuid()",
            _ => $"default", // enum or entity reference
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