using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.Interpretation.CSharp;
using Poly.Introspection;

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
        // out-of-range value, so the DB constraints are sound. A declared length is its own CHECK
        // (#563): Create and assign guard every write, so the DB can state it too.
        var checks = store.Columns
            .Where(c => c.IsRangeVerified && c.VerifiedRange is { } vr && (vr.Min is not null || vr.Max is not null))
            .Select(c => (Name: $"CK_{c.ColumnName}", Sql: CheckSql(c)))
            .Concat(store.Columns
                .Select(c => (Name: $"CK_{c.ColumnName}_length", Sql: LengthCheckSql(c)))
                .Where(c => c.Sql is not null)
                .Select(c => (c.Name, Sql: c.Sql!)))
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
            TypeArguments = [TypeReference.To(typeof(int))]
        });
        bodyNodes.Add(new Invoke(
            new Member(bParam, "HasKey"),
            new Constant(shadowKey)
        ));

        // Optimistic concurrency (#626, #653): every stored entity carries an integer token the host bumps before a
        // modifying save, so a read-modify-write that lost the race updates no row and answers 409 instead of
        // overwriting one. A shadow column, never a CLR member: the entity and its DTO keep their shape. The default
        // lets a row written outside this context (raw SQL, another writer) start at the same token as one inserted here.
        var versionColumn = new Invoke(
            new Member(bParam, "Property"),
            new Constant(VersionPropertyName())) {
            TypeArguments = [TypeReference.To(typeof(long))]
        };
        var concurrencyToken = new Invoke(new Member(versionColumn, "IsConcurrencyToken"));
        bodyNodes.Add(new Invoke(new Member(concurrencyToken, "HasDefaultValue"), new Constant(0L)));

        // Column configurations
        // A unique text column whose mapped type is unbounded cannot be an index key on the provider that
        // declares it (SQL Server: nvarchar(max) → error 1919). Refuse the schema at generation, with what to
        // write, rather than emit DDL the database rejects — and never invent a length the model does not
        // state (#627).
        foreach (var unbounded in store.Columns.Where(IsUnindexableUniqueText))
            throw new InvalidOperationException(
                $"Column '{entity.Name}.{unbounded.Name}' is unique but has no length: a {unbounded.ColumnType} column "
                + "cannot be a unique index key on this provider. Give the property a length, for example length(1, 255).");

        foreach (var col in store.Columns)
            bodyNodes.Add(BuildColumnConfigNode(bParam, col));

        foreach (var col in store.Columns.Where(c => c.IsUnique)) {
            var xParam = new Parameter("x");
            var index = new Invoke(
                new Member(bParam, "HasIndex"),
                new Lambda([xParam], new Member(xParam, col.Name)));
            bodyNodes.Add(new Invoke(new Member(index, "IsUnique")));
        }

        // A navigation the exporter prints internal (public owner, internal target) is invisible to EF's
        // public-member discovery, so it is mapped explicitly, before its access mode is set below.
        foreach (var nav in store.CollectionNavigations.Concat(store.ReferenceNavigations).Where(n => IsPrintedInternal(entity, n)))
            bodyNodes.Add(BuildExplicitNavigationMapping(bParam, nav));

        // Collection navigations: set backing field access mode
        foreach (var nav in store.CollectionNavigations) {
            // b.Metadata.FindNavigation("NavProp")!.SetPropertyAccessMode(PropertyAccessMode.Field)
            bodyNodes.Add(BuildNavigationConfigNode(bParam, nav));
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

    /// <summary>
    /// One statement per decided relationship, declaring what the language says (L-15b): the foreign key is required when
    /// the dependent end's navigation is (<see cref="StorageNavigation.IsRequired"/>), and the delete behaviour is
    /// <see cref="StorageNavigation.OnDelete"/>. Both are explicit either way: EF otherwise infers requiredness from the
    /// CLR nullability of the navigation (today <c>T?</c>, so optional) and would move the schema the day that changes.
    /// A shape the analysis does not decide (<see cref="StorageNavigation.OnDelete"/> null) gets no statement.
    /// </summary>
    private IEnumerable<Node> BuildRelationshipConfigs() {
        foreach (var entity in _entities) {
            var store = GetStorageEntity(entity);
            foreach (var nav in store.ReferenceNavigations.Concat(store.CollectionNavigations)) {
                if (nav.ConfiguresForeignKey && nav.OnDelete is { } onDelete)
                    yield return BuildRelationshipConfig(entity, nav, onDelete);
            }
        }
    }

    private static Node BuildRelationshipConfig(Entity entity, StorageNavigation nav, StorageDeleteBehavior onDelete) {
        var entityBuilder = new Invoke(new Member(new TypeReference("modelBuilder"), "Entity")) {
            TypeArguments = [new TypeReference(entity.Name)]
        };
        var eParam = new Parameter("e");
        Node chain = new Invoke(
            new Member(entityBuilder, nav.IsCollection ? "HasMany" : "HasOne"),
            new Lambda([eParam], new Member(eParam, nav.PropertyName)));

        var withMany = nav.IsCollection ? nav.InverseIsCollection : nav.InversePropertyName is null || nav.InverseIsCollection;
        var withArgs = new List<Node>();
        if (nav.InversePropertyName is { } inversePropertyName) {
            var iParam = new Parameter("i");
            withArgs.Add(new Lambda([iParam], new Member(iParam, inversePropertyName)));
        }
        chain = new Invoke(new Member(chain, withMany ? "WithMany" : "WithOne"), [.. withArgs]);

        // A one-to-one pair names its dependent end; EF cannot pick one (the model does not build without it).
        if (!nav.IsCollection && nav.InversePropertyName is not null && !nav.InverseIsCollection) {
            chain = new Invoke(new Member(chain, "HasForeignKey"), new Constant(nav.PropertyName + "Id")) {
                TypeArguments = [new TypeReference(entity.Name)]
            };
        }

        Node[] requiredArgs = nav.IsRequired ? [] : [new Constant(false)];
        chain = new Invoke(new Member(chain, "IsRequired"), requiredArgs);
        return new Invoke(
            new Member(chain, "OnDelete"),
            new Member(new TypeReference("DeleteBehavior"), onDelete.ToString()));
    }

    /// <summary>The exporter's own rule (<see cref="DomainAccess.NavigationAccess"/>), not a copy of it.</summary>
    private bool IsPrintedInternal(Entity entity, StorageNavigation nav) =>
        DomainAccess.NavigationAccess(entity, new NamedTypeReference(nav.TargetEntityName), _domain)
            == AccessModifier.Internal;

    /// <summary>
    /// <c>b.HasMany(e =&gt; e.Nav).WithOne(i =&gt; i.Inverse)</c> or <c>b.HasOne(e =&gt; e.Nav).WithMany()</c>: the
    /// inverse is the one analysis resolved (<see cref="StorageNavigation.InversePropertyName"/>); none means
    /// a one-directional navigation (a bare <c>WithOne()</c> / <c>WithMany()</c>).
    /// </summary>
    private Node BuildExplicitNavigationMapping(Parameter bParam, StorageNavigation nav) {
        var eParam = new Parameter("e");
        var navigation = new Invoke(
            new Member(bParam, nav.IsCollection ? "HasMany" : "HasOne"),
            new Lambda([eParam], new Member(eParam, nav.PropertyName)));

        var inverseIsCollection = nav.InverseIsCollection;
        // The many side of a one-to-many is entered with WithOne; the one side of a many-to-one with WithMany.
        var withMany = nav.IsCollection ? inverseIsCollection : nav.InversePropertyName is null || inverseIsCollection;

        var withArgs = new List<Node>();
        if (nav.InversePropertyName is { } inversePropertyName) {
            var iParam = new Parameter("i");
            withArgs.Add(new Lambda([iParam], new Member(iParam, inversePropertyName)));
        }

        return new Invoke(new Member(navigation, withMany ? "WithMany" : "WithOne"), [.. withArgs]);
    }

    private static Node BuildNavigationConfigNode(Parameter bParam, StorageNavigation nav) {
        // b.Metadata.FindNavigation("NavProp")!.SetPropertyAccessMode(PropertyAccessMode.Field)
        // The name is a string, not `nameof(Entity.NavProp)`: a nameof argument is an expression, where a bare
        // domain type binds to an inherited DbContext member first (an entity named `Entry` to
        // DbContext.Entry<TEntity>, CS0119, #1012) and a namespace-qualified one binds to the entity when it is
        // named like its domain (`Library.Library`). A string names the CLR property and cannot be shadowed.
        var findNavigation = new Invoke(
            new Member(new Member(bParam, "Metadata"), "FindNavigation"),
            new Constant(nav.PropertyName));

        return new Invoke(
            new Member(
                new NullForgiving(findNavigation),
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

    /// <summary>The shadow key's EF property name, shared with the create route's <c>Location</c> (#589).</summary>
    internal static string ShadowKeyName(Entity entity, StorageEntity store) {
        var taken = entity.Properties.Any(p => p.Name == "Id")
            || store.Columns.Any(c => c.Name == "Id");
        return taken ? "PolyId" : "Id";
    }

    /// <summary>A <c>unique</c> Text column whose provider type is unbounded (<c>nvarchar(max)</c>): no such
    /// provider can use it as an index key, so the schema is refused at generation (#627).</summary>
    private static bool IsUnindexableUniqueText(StorageColumn col) =>
        col.IsUnique
        && col.MaxLength is null
        && col.DomainType is "Text" or "String"
        && col.ColumnType.EndsWith("(max)", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The shadow column the optimistic-concurrency token lives in: <c>Version</c>, or the first <c>PolyVersion</c>,
    /// <c>PolyVersion2</c>, … that neither a property of any entity nor any mapped column already uses. One name for
    /// the whole context, so the save-time bump can name one property; a domain property that happened to be called
    /// <c>Version</c> keeps its own value instead of silently becoming the token (#626, #653).
    /// </summary>
    private string VersionPropertyName() {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in _entities) {
            foreach (var property in entity.Properties)
                taken.Add(property.Name);
            foreach (var column in GetStorageEntity(entity).Columns)
                taken.Add(column.Name);
        }
        if (!taken.Contains(VersionProperty))
            return VersionProperty;
        var name = "Poly" + VersionProperty;
        for (var n = 2; taken.Contains(name); n++)
            name = "Poly" + VersionProperty + n;
        return name;
    }
}