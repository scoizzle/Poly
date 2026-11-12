using Poly.Analysis;
using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.Interpretation.CSharp;

using ClrPrimitive = Poly.Introspection.PrimitiveType;

namespace Poly.Packs.Product;

// MinimalApiGenerator (Host) — Program.cs host wiring: provider registration, seeding, failure status.
public sealed partial class MinimalApiGenerator {
    /// <summary>
    /// Builds the <c>options =&gt; …</c> body for the EF Core DbContext registration,
    /// driven by the DBMS pack: sqlite → <c>UseSqlite("Data Source=library.db")</c>
    /// (matches the shipped demo), sqlserver → <c>UseSqlServer(...)</c>, generic →
    /// <c>UseInMemoryDatabase(domain)</c> (no provider package required).
    /// </summary>
    private Node BuildProviderRegistration(Parameter optionsParam) {
        var dbName = _domainName.ToLowerInvariant();
        switch (_vendor) {
            case "sqlite":
                return new Invoke(new Member(optionsParam, "UseSqlite"),
                    new Constant($"Data Source={dbName}.db"));
            case "sqlserver":
                return new Invoke(new Member(optionsParam, "UseSqlServer"),
                    new Constant($"Server=(localdb)\\mssqllocaldb;Database={_domainName};Trusted_Connection=True"));
            default:
                return new Invoke(new Member(optionsParam, "UseInMemoryDatabase"),
                    new Constant(_domainName));
        }
    }

    private static MethodDefinitionNode FailureStatusMethod() {
        var message = new Parameter("message",
            new OptionalTypeReference(new PrimitiveTypeReference(ClrPrimitive.String)));
        var messageVar = new Variable("message");
        Node Contains(string text) => new Invoke(new Member(messageVar, "Contains"), new Constant(text));
        var conflict = new Ast.Nodes.And(
            new Ast.Nodes.NotEqual(messageVar, new Constant(null!)),
            new Ast.Nodes.Or(
                Contains("Unique constraint"),
                Contains("already has a target")));
        return new MethodDefinitionNode(
            "FailureStatus",
            new TypeReference("IResult"),
            Parameters: [message],
            Body: new Block(
                new Return(new Ast.Nodes.Conditional(
                    conflict,
                    new Invoke(new Member(new TypeReference("Results"), "Conflict"), messageVar),
                    new Invoke(new Member(new TypeReference("Results"), "BadRequest"), messageVar)))),
            IsStatic: true);
    }

    /// <summary>Appends the SeedAsync local function (Issue 11b).</summary>
    private void AppendSeedMethodStatements(List<Node> statements, string dbContextName) {
        statements.Add(FailureStatusMethod());
        var seedableEntities = _entities.Where(e => GetStorageEntity(e).IsRoot).ToList();
        var bodyNodes = new List<Node>();

        // if (db is null) return;
        bodyNodes.Add(new IfStatement(
            new Equal(new Variable("db"), new Constant(null!)),
            new Block(new Return(null))));

        if (seedableEntities.Count > 0) {
            var firstSet = Pluralize(seedableEntities[0].Name);

            // if (await db.{FirstSet}.AnyAsync()) return;
            bodyNodes.Add(new IfStatement(
                new Await(
                    new Invoke(
                        new Member(
                            new Member(new Variable("db"), firstSet),
                            "AnyAsync"))),
                new Block(new Return(null))));

            foreach (var entity in seedableEntities) {
                var createArgs = BuildCreateCallArgs(entity, GetConstructorOrder(entity), MakeSampleValue);

                var dtoVar = ToCamelCase(entity.Name);
                bodyNodes.Add(Init($"{dtoVar}Result",
                    new Invoke(new Member(new TypeReference(VisibleType(entity.Name)), "Create"), [.. createArgs])));
                bodyNodes.Add(new IfStatement(
                    new Member(new Variable($"{dtoVar}Result"), "IsSuccess"),
                    new Block(
                        new Invoke(new Member(new Variable("db"), "Add"),
                            new Member(new Variable($"{dtoVar}Result"), "Value")))));
            }

            bodyNodes.Add(new Await(
                new Invoke(new Member(new Variable("db"), "SaveChangesAsync"))));
        }

        statements.Add(new MethodDefinitionNode(
            "SeedAsync",
            // Fully qualified: a domain entity named `Task` would otherwise shadow the BCL
            // type in the emitted file and `async Task SeedAsync(…)` stops compiling (S26).
            new TypeReference("System.Threading.Tasks.Task"),
            Parameters: [new Parameter("db", new TypeReference(dbContextName))],
            Body: Stmts(bodyNodes),
            IsStatic: true,
            IsAsync: true
        ));
    }

    /// <summary>Creates a Syntax node representing a sample value for a property.</summary>
    private Node MakeSampleValue(Property prop) {
        var typeName = prop.Type.TypeName;
        var propNameLower = prop.Name.ToLowerInvariant();

        // Email pattern
        if (propNameLower is "email" or "emailaddress")
            return new Constant("user@test.com");

        // Range-constrained properties
        var range = prop.Constraints.OfType<RangeConstraint>().FirstOrDefault();
        if (range is not null) {
            if (range.Minimum is long l && l > 0) return new Constant((int)l);
            if (range.Maximum is long l2 && l2 > 0) return new Constant((int)(l2 / 2));
            return new Constant(1);
        }

        var clr = RuntimeAnalysisCache.ClrTypeName(_domain, typeName);
        if (clr is "string") {
            var length = prop.Constraints.OfType<LengthConstraint>().FirstOrDefault();
            if (length is not null && length.MinLength > 0)
                return new Constant(new string('X', Math.Max((int)length.MinLength, 8)));
            return new Constant("Sample");
        }

        return clr switch {
            "long" or "int" => new Constant(1),
            "bool" => new Constant(false),
            "DateTime" => new Member(new TypeReference("DateTime"), "UtcNow"),
            "DateOnly" => new Invoke(
                new Member(new TypeReference("DateOnly"), "FromDateTime"),
                new Member(new TypeReference("DateTime"), "UtcNow")),
            "decimal" => new Constant(1m),
            "double" or "float" => new Constant(1.0),
            "Guid" => new Invoke(new Member(new TypeReference("Guid"), "NewGuid")),
            _ when _enumLookup.TryGetValue(typeName, out var enumType) && enumType.MemberNames.Count > 0
                => new Member(new TypeReference(typeName), enumType.MemberNames[0]),
            _ => new Constant("Sample"),
        };
    }
}

/// <summary>
/// Emits the composition-root host files (<c>Program.cs</c> + <c>demo.http</c>) from the
/// analyzed domain via the artifact-contributor hook. Surfaces only the root domain's own
/// entities — produced internal contracts contribute value types and operation endpoints,
/// never child-entity routes.
/// </summary>
public sealed class MinimalApiHostArtifactContributor : IArtifactContributor {
    private readonly IStorageSyntaxEmitter? _emitter;
    private readonly string? _vendorOverride;

    /// <summary>
    /// A non-null <paramref name="vendor"/> (a loaded library id such as <c>sqlite</c> or
    /// <c>sqlserver</c>; anything else means the generic in-memory provider) always wins for
    /// Program.cs provider selection. When null, the vendor is the first of those ids the
    /// domain lists, resolved at contribute time.
    /// </summary>
    public MinimalApiHostArtifactContributor(
        IStorageSyntaxEmitter? emitter = null,
        string? vendor = null) {
        _emitter = emitter;
        _vendorOverride = vendor;
    }

    public IReadOnlyList<Artifact> Contribute(Domain domain, AnalysisResult analysis, ArtifactCatalog catalog) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(catalog);

        var http = analysis.GetMetadata<HttpSurfaceMetadata>(domain);
        var storage = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage;
        var aggregate = analysis.GetMetadata<OwnershipAggregateMetadata>(domain)?.Aggregate;

        // NoOp only when both http and storage are null (Load gating + both-null harness).
        // http-null + storage-present still emits (intentional harness path).
        if (http is null && storage is null)
            return [];

        if (storage is null || aggregate is null) {
            throw new InvalidOperationException(
                "HTTP artifacts require storage and aggregate analysis metadata.");
        }

        var behavior = BehaviorMetadata.From(domain, analysis);
        var vendor = _vendorOverride ?? ResolveVendor(domain);
        RequireHttpActionsInModule(domain, analysis, catalog);

        var dbContextName = $"{domain.Name}DbContext";
        var apiGen = new MinimalApiGenerator(domain, analysis, storage, behavior, aggregate, _emitter, vendor,
            new CatalogIndex(catalog));
        var httpGen = new HttpFileGenerator(domain, analysis, storage, behavior, aggregate);
        var programUnit = apiGen.GenerateCompilationUnit(dbContextName);
        var programTree = GeneratedTree.Create(
            domain, "Program.cs", programUnit, nameof(MinimalApiHostArtifactContributor));
        return [
            programTree,
            ContributedFile.Create(
                domain,
                "Program.cs",
                new CSharpWriter().Write(programUnit),
                nameof(MinimalApiHostArtifactContributor),
                [programTree.Descriptor.Id]),
            ContributedFile.Create(domain, "demo.http", httpGen.Generate(), nameof(MinimalApiHostArtifactContributor)),
        ];
    }

    private static string? ResolveVendor(Domain domain) {
        foreach (var id in domain.Extensions) {
            if (string.Equals(id, "sqlite", StringComparison.Ordinal)
                || string.Equals(id, "sqlserver", StringComparison.Ordinal))
                return id;
        }
        return null;
    }

    /// <summary>
    /// HTTP names catalog actions; those operations must already exist on the
    /// entity and scaffolding trees Lower registered. Program.cs calls them —
    /// it does not copy effect walks.
    /// </summary>
    private static void RequireHttpActionsInModule(
        Domain domain, AnalysisResult analysis, ArtifactCatalog catalog) {
        var module = catalog.Artifacts
            .Where(a => a.Descriptor.Id.Type is "entity" or "scaffolding")
            .SelectMany(a => (IReadOnlyList<TypeDefinitionNode>)a.Payload!)
            .ToList();
        var behavior = BehaviorMetadata.From(domain, analysis);
        foreach (var entity in behavior.Entities) {
            var type = module.FirstOrDefault(t =>
                string.Equals(t.Name, entity.Name, StringComparison.Ordinal));
            if (type is null)
                throw new InvalidOperationException(
                    $"HTTP names entity '{entity.Name}' that is not in the operation module.");
            foreach (var action in entity.Actions) {
                if (type.Methods?.Any(m =>
                    string.Equals(m.Name, action.Name, StringComparison.Ordinal)) != true) {
                    throw new InvalidOperationException(
                        $"HTTP names action '{entity.Name}.{action.Name}' that is not in the operation module.");
                }
            }
        }
    }
}