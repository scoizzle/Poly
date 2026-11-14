using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.Interpretation.CSharp;

namespace Poly.Packs.Product;

// MinimalApiGenerator (SchemaId) — the startup check that refuses a SQLite file made by a different schema.
public sealed partial class MinimalApiGenerator {
    /// <summary>
    /// The id of the schema this host's database must have, folded into a positive 31-bit integer (the range of
    /// SQLite's <c>PRAGMA user_version</c>, with 0 left to mean "no id"). Its input is <see cref="SchemaIdInput"/>.
    /// </summary>
    internal int SchemaId() => FoldSchemaId(SchemaIdInput());

    /// <summary>
    /// What the schema id hashes: the storage pack's own printed <c>OnModelCreating</c> (tables, columns, keys, indexes,
    /// relationships, check constraints), the column CLR type and requiredness, the stage column of an entity with
    /// stages, and the navigations (EF derives a foreign key from its navigation): the model, not the host. Routes,
    /// DTOs, action bodies, policies, comments and the EF version are not in it. Line endings are normalised to
    /// <c>\n</c> (the writer uses <see cref="Environment.NewLine"/>) and nothing else is.
    /// </summary>
    internal string SchemaIdInput() {
        var context = new DbContextGenerator(_domain, _storageModel).GenerateCompilationUnit();
        var onModelCreating = context.Types.OfType<TypeDefinitionNode>().Single().Methods!
            .Single(m => string.Equals(m.Name, "OnModelCreating", StringComparison.Ordinal));
        var input = new StringBuilder("poly-schema-id/1\n")
            .Append(new CSharpWriter().Write(onModelCreating.Body!)).Append('\n');
        foreach (var store in _storageModel.Entities) {
            foreach (var column in store.Columns)
                input.Append($"column {store.Name}.{column.ColumnName} {column.ClrTypeName} required={column.IsRequired}\n");
            if (store.HasStages)
                input.Append($"stage {store.Name}.{store.StagePropertyName} {store.StageEnumTypeName} stored=enum\n");
            foreach (var navigation in store.CollectionNavigations.Concat(store.ReferenceNavigations))
                input.Append($"navigation {store.Name}.{navigation.PropertyName} -> {navigation.TargetEntityName} "
                    + $"collection={navigation.IsCollection} owns={navigation.SourceOwnsTarget} "
                    + $"cardinality={navigation.Source.Cardinality} inverse={navigation.InversePropertyName}\n");
        }
        return NormalizeSchemaIdInput(input.ToString());
    }

    /// <summary>Line endings only: the writer joins lines with <see cref="Environment.NewLine"/>.</summary>
    internal static string NormalizeSchemaIdInput(string raw) => raw.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>SHA-256 of the UTF-8 input; the first four bytes, big-endian, masked to 31 bits; 0 becomes 1.</summary>
    internal static int FoldSchemaId(string input) {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var folded = BinaryPrimitives.ReadInt32BigEndian(hash) & int.MaxValue;
        return folded == 0 ? 1 : folded;
    }

    private const string UserVersionQuery = "SELECT user_version AS Value FROM pragma_user_version";

    /// <summary>
    /// SQLite only, in place of a bare <c>EnsureCreatedAsync</c>. A database this call creates is stamped with the
    /// schema id; one that already has tables must carry the same id, or the app exits before it listens. A file with
    /// tables and no id (<c>user_version</c> 0: made before schema ids, or by another tool) cannot be vouched for, so
    /// it is refused as well. <c>EnsureCreatedAsync</c> answers false exactly when the file already has tables.
    /// </summary>
    private List<Node> BuildSqliteSchemaCheck(Variable db, Variable created, int schemaId) {
        var database = new Member(db, "Database");
        var found = new Variable("found");
        var file = new Variable("databaseFile");
        var current = new Variable("schemaId");

        static Node Refuse(Node condition, InterpolatedString message) => new IfStatement(
            condition,
            new Block(
                new Invoke(new Member(new Member(new TypeReference("Console"), "Error"), "WriteLine"), message),
                new Invoke(new Member(new TypeReference("Environment"), "Exit"), new Constant(1))));

        var noId = new InterpolatedString(
            ["The database '", "' has tables but no schema id (it was created before schema ids existed, or by another tool), "
                + "so it is treated as a different version of the model; delete it, or run the app from another directory."],
            [file]);
        var otherId = new InterpolatedString(
            ["The database '", "' was created by a different version of the model (it has schema id ", ", this build has ",
                "); delete it, or run the app from another directory."],
            [file, found, current]);

        var stamp = new Await(new Invoke(new Member(database, "ExecuteSqlRawAsync"),
            new Constant($"PRAGMA user_version = {schemaId.ToString(System.Globalization.CultureInfo.InvariantCulture)}")));
        var read = new Await(new Invoke(new Member(
            new Invoke(new Member(database, "SqlQueryRaw"), new Constant(UserVersionQuery)) {
                TypeArguments = [new PrimitiveTypeReference(Poly.Introspection.PrimitiveType.Int32)]
            },
            "SingleAsync")));

        return [
            new Assignment(created, new Await(new Invoke(new Member(database, "EnsureCreatedAsync")))),
            new IfStatement(
                created,
                new Block(stamp),
                Stmts([
                    new Assignment(found, read),
                    new Assignment(file, new Invoke(
                        new Member(new TypeReference("System.IO.Path"), "GetFullPath"),
                        new Member(new Invoke(new Member(database, "GetDbConnection")), "DataSource"))),
                    Refuse(new Equal(found, new Constant(0)), noId),
                    Refuse(new NotEqual(found, current), otherId),
                ])),
        ];
    }
}