using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.Introspection;
using Poly.Runtime;

using ClrPrimitive = Poly.Introspection.PrimitiveType;

namespace Poly.Packs.Product;

// DbContextGenerator (Keys) — the IStoragePort host half: pending-key set and
// Exists/TryReserve/Record/Release. Every key is KeyCodec.Canonical, so the host and the
// simulator cannot drift on the encoding.
public sealed partial class DbContextGenerator {
    /// <summary>
    /// The context is the host half of <see cref="IStoragePort"/>: <c>Exists</c>
    /// queries the mapped <c>DbSet</c>; <c>TryReserve</c> atomically adds the pending
    /// key a nested create must see before the host saves; <c>Record</c> is the shim
    /// that does the same add; <c>Release</c> drops a pending key this invocation
    /// replaced. The persisted unique index is the authority, so a saved row is left
    /// for the host to reject at commit.
    /// </summary>
    private static FieldDefinitionNode PendingKeysField() =>
        new("_pendingKeys",
            new NamedTypeReference("HashSet", TypeArguments: [new PrimitiveTypeReference(ClrPrimitive.String)]),
            DefaultValue: new New(new NamedTypeReference("HashSet", TypeArguments: [new PrimitiveTypeReference(ClrPrimitive.String)])),
            IsReadOnly: true,
            AccessModifier: AccessModifier.Private);

    private MethodDefinitionNode ExistsMethod() {
        var entityParam = new Parameter("entity", new PrimitiveTypeReference(ClrPrimitive.String));
        var propertyParam = new Parameter("property", new PrimitiveTypeReference(ClrPrimitive.String));
        var valueParam = new Parameter("value", new PrimitiveTypeReference(ClrPrimitive.String));
        var nodes = new List<Node> {
            new IfStatement(
                new Invoke(
                    new Member(new Member(new ThisReference(), "_pendingKeys"), "Contains"),
                    PendingKey(entityParam, propertyParam, valueParam)),
                new Block([new Return(new Constant(true))]))
        };

        foreach (var store in _storageModel.Entities.OrderBy(e => e.Name, StringComparer.Ordinal)) {
            var unique = store.Columns
                .Where(c => c.IsUnique)
                .OrderBy(c => c.Name, StringComparer.Ordinal)
                .ToList();
            if (unique.Count == 0)
                continue;
            var body = new List<Node>();
            foreach (var column in unique) {
                var row = new Parameter("e");
                Node comparison = string.Equals(column.ClrTypeName, "string", StringComparison.Ordinal)
                    ? new Equal(new Member(row, column.Name), valueParam)
                    : new Equal(
                        new Invoke(new Member(new Member(row, column.Name), "ToString")),
                        valueParam);
                var any = new Invoke(
                    new Member(new Member(new ThisReference(), Pluralize(store.Name)), "Any"),
                    new Lambda([row], comparison));
                body.Add(new IfStatement(
                    new Equal(propertyParam, new Constant(column.Name)),
                    new Block([new Return(any)])));
            }
            body.Add(new Return(new Constant(false)));
            nodes.Add(new IfStatement(
                new Equal(entityParam, new Constant(store.Name)),
                new Block(body)));
        }

        nodes.Add(new Return(new Constant(false)));
        return new MethodDefinitionNode(
            "Exists",
            new PrimitiveTypeReference(ClrPrimitive.Boolean),
            Parameters: [entityParam, propertyParam, valueParam],
            Body: new Block(nodes));
    }

    private static MethodDefinitionNode TryReserveMethod() {
        var entityParam = new Parameter("entity", new PrimitiveTypeReference(ClrPrimitive.String));
        var propertyParam = new Parameter("property", new PrimitiveTypeReference(ClrPrimitive.String));
        var valueParam = new Parameter("value", new PrimitiveTypeReference(ClrPrimitive.String));
        return new MethodDefinitionNode(
            "TryReserve",
            new PrimitiveTypeReference(ClrPrimitive.Boolean),
            Parameters: [entityParam, propertyParam, valueParam],
            Body: new Block([
                new Return(new Invoke(
                    new Member(new Member(new ThisReference(), "_pendingKeys"), "Add"),
                    PendingKey(entityParam, propertyParam, valueParam)))
            ]));
    }

    private static MethodDefinitionNode RecordMethod() {
        var entityParam = new Parameter("entity", new PrimitiveTypeReference(ClrPrimitive.String));
        var propertyParam = new Parameter("property", new PrimitiveTypeReference(ClrPrimitive.String));
        var valueParam = new Parameter("value", new PrimitiveTypeReference(ClrPrimitive.String));
        return new MethodDefinitionNode(
            "Record",
            new TypeReference("void"),
            Parameters: [entityParam, propertyParam, valueParam],
            Body: new Block([
                new Invoke(
                    new Member(new Member(new ThisReference(), "_pendingKeys"), "Add"),
                    PendingKey(entityParam, propertyParam, valueParam))
            ]));
    }

    private static MethodDefinitionNode ReleaseMethod() {
        var entityParam = new Parameter("entity", new PrimitiveTypeReference(ClrPrimitive.String));
        var propertyParam = new Parameter("property", new PrimitiveTypeReference(ClrPrimitive.String));
        var valueParam = new Parameter("value", new PrimitiveTypeReference(ClrPrimitive.String));
        return new MethodDefinitionNode(
            "Release",
            new TypeReference("void"),
            Parameters: [entityParam, propertyParam, valueParam],
            Body: new Block([
                new Invoke(
                    new Member(new Member(new ThisReference(), "_pendingKeys"), "Remove"),
                    PendingKey(entityParam, propertyParam, valueParam))
            ]));
    }

    // One encoding for the host and the simulator: the length-prefixed KeyCodec key.
    private static Node PendingKey(Node entity, Node property, Node value) =>
        new Invoke(
            new Member(new ClrTypeReference(typeof(KeyCodec)), "Canonical"),
            entity, property, value);
}