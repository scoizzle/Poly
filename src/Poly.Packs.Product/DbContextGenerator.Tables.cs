using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Introspection;

using ClrPrimitive = Poly.Introspection.PrimitiveType;

namespace Poly.Packs.Product;

// DbContextGenerator (Tables) — entity config block: ToTable, shadow key, columns, indexes, navigations, table names.
public sealed partial class DbContextGenerator {
    private StorageEntity GetStorageEntity(Entity entity) =>
        _storageLookup[entity.Name];

    /// <summary>Builds Syntax IR for a single entity's OnModelCreating config block.</summary>
    private Node BuildEntityConfig(Entity entity) {
        var store = GetStorageEntity(entity);
        var tableName = EmitTableName(entity, store);

        // Lambda parameter: b
        var bParam = new Parameter("b");

        // Build statements inside the lambda body
        var bodyNodes = new List<Node>();

        // b.ToTable("TableName") — or with verified-range CHECK constraints via the modern
        // ToTable(tableBuilder => …) API (HasCheckConstraint on EntityTypeBuilder is obsolete).
        // Checks are emitted only when the invariant analysis PROVED no effect can produce an
        // out-of-range value, so the DB constraints are sound.
        var checks = store.Columns
            .Where(c => c.IsRangeVerified && c.VerifiedRange is { } vr && (vr.Min is not null || vr.Max is not null))
            .Select(c => (Name: $"CK_{c.ColumnName}", Sql: CheckSql(c.ColumnName, c.VerifiedRange!)))
            .ToList();
        if (checks.Count == 0) {
            bodyNodes.Add(new Invoke(
                new Member(bParam, "ToTable"),
                new Constant(tableName)
            ));
        }
        else {
            var tParam = new Parameter("t");
            var tBody = checks.Select(check => (Node)new Invoke(
                new Member(tParam, "HasCheckConstraint"),
                new Constant(check.Name), new Constant(check.Sql))).ToList();
            bodyNodes.Add(new Invoke(
                new Member(bParam, "ToTable"),
                new Constant(tableName),
                new Lambda([tParam], new Block(tBody))
            ));
        }

        // Synthetic int identity. A `unique` property is a unique index, not the primary key,
        // so a second unique on the same entity is stored instead of dropped.
        // PolyId when the entity already has a CLR property named Id.
        var shadowKey = ShadowKeyName(entity, store);
        bodyNodes.Add(new Invoke(
            new Member(bParam, "Property"),
            new Constant(shadowKey)
        ) {
            TypeArguments = [new PrimitiveTypeReference(Poly.Introspection.PrimitiveType.Int32)]
        });
        bodyNodes.Add(new Invoke(
            new Member(bParam, "HasKey"),
            new Constant(shadowKey)
        ));

        // Column configurations
        foreach (var col in store.Columns)
            bodyNodes.Add(BuildColumnConfigNode(bParam, col));

        foreach (var col in store.Columns.Where(c => c.IsUnique)) {
            var xParam = new Parameter("x");
            var index = new Invoke(
                new Member(bParam, "HasIndex"),
                new Lambda([xParam], new Member(xParam, col.Name)));
            bodyNodes.Add(new Invoke(new Member(index, "IsUnique")));
        }

        // Collection navigations: set backing field access mode
        foreach (var nav in store.CollectionNavigations) {
            // b.Metadata.FindNavigation(nameof(Entity.NavProp))!.SetPropertyAccessMode(PropertyAccessMode.Field)
            bodyNodes.Add(BuildNavigationConfigNode(bParam, nav, entity.Name));
        }

        // modelBuilder.Entity<EntityName>(b => { ... })
        var lambdaBody = new Block(expressions: bodyNodes, variables: []);

        return new Invoke(
            new Member(new TypeReference("modelBuilder"), "Entity"),
            new Lambda([bParam], lambdaBody)
        ) {
            TypeArguments = [new TypeReference(entity.Name)]
        };
    }

    private static Node BuildNavigationConfigNode(Parameter bParam, StorageNavigation nav, string entityName) {
        // b.Metadata.FindNavigation(nameof(Entity.NavProp))!.SetPropertyAccessMode(PropertyAccessMode.Field)
        // Issue 8: nameof must receive a Member expression, not a string Constant
        var nameofCall = new Invoke(
            new TypeReference("nameof"),
            new Member(new TypeReference(entityName), nav.PropertyName));

        return new Invoke(
            new Member(
                new NullForgiving(
                    new Invoke(
                        new Member(new Member(bParam, "Metadata"), "FindNavigation"),
                        nameofCall
                    )
                ),
                "SetPropertyAccessMode"
            ),
            new Member(new TypeReference("PropertyAccessMode"), "Field")
        );
    }

    /// <summary>
    /// Default storage table is <c>Name + "s"</c> (<c>Opportunitys</c>). The host
    /// emits the English plural unless <c>table("…")</c> overrode the name.
    /// </summary>
    private static string EmitTableName(Entity entity, StorageEntity store) =>
        string.Equals(store.TableName, entity.Name + "s", StringComparison.Ordinal)
            ? Pluralize(entity.Name)
            : store.TableName;

    private static string ShadowKeyName(Entity entity, StorageEntity store) {
        var taken = entity.Properties.Any(p => p.Name == "Id")
            || store.Columns.Any(c => c.Name == "Id");
        return taken ? "PolyId" : "Id";
    }
}