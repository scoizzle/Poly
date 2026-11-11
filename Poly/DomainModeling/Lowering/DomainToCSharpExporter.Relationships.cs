using Poly.DomainModeling.Ontology;

using AccessModifier = Poly.Introspection.AccessModifier;

namespace Poly.DomainModeling.Lowering;

// Relationships: navigation shape, inverse resolution, and attach (link) methods.
public sealed partial class DomainToCSharpExporter {
    /// <summary>
    /// Inverse of a create-in to-one initializer: add an already-constructed child
    /// to this collection and register this entity's subscriptions on it.
    /// </summary>
    private static void AddAttachNavMethod(
        Entity entity,
        Relationship rel,
        List<SubscriptionInfo>? subscriberSubs,
        string fieldName,
        List<MethodDefinitionNode> methods) {
        var targetType = new NamedTypeReference(rel.Target.TypeName);
        var child = new Parameter("child", targetType);
        var body = new List<Node> {
            new Invoke(
                new Member(new Member(new ThisReference(), fieldName), "Add"),
                [child])
        };
        if (subscriberSubs is { Count: > 0 }) {
            foreach (var info in subscriberSubs
                .Where(i => string.Equals(i.Relationship.Name, rel.Name, StringComparison.Ordinal))
                .GroupBy(s => s.StageName)
                .Select(g => g.First())) {
                body.Add(new Invoke(
                    new Member(child, SubscriberRegisterMethodName(info)),
                    [new ThisReference()]));
            }
        }
        methods.Add(new MethodDefinitionNode(
            $"Attach{ToPascalCase(rel.Name)}",
            new TypeReference("void"),
            Parameters: [child],
            Body: new Block(body),
            AccessModifier: AccessModifier.Internal
        ));
    }

    /// <summary>
    /// For a self-relationship <c>create in rel</c>, the unique singular navigation on
    /// <paramref name="targetEntity"/> whose target is <paramref name="sourceEntityName"/>.
    /// This is the peer the new instance wires to <c>this</c> (e.g. <c>Account.parent</c>
    /// for a <c>create in children</c>); null when there is none or it is ambiguous.
    /// Unlike <see cref="FindAutoWireBackReference"/>, it does not skip self-relationships.
    /// </summary>
    private static Relationship? FindSelfRelPeerSingular(Entity targetEntity, string sourceEntityName) {
        Relationship? found = null;
        var count = 0;
        foreach (var nav in targetEntity.Navigations) {
            if (nav.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
                continue;
            if (string.Equals(nav.Target.TypeName, sourceEntityName, StringComparison.Ordinal)) {
                found = nav;
                count++;
            }
        }
        return count == 1 ? found : null;
    }

    /// <summary>
    /// The auto-wire back-reference for a <c>create in Rel</c> export: the single
    /// singular navigation on <paramref name="targetEntity"/> whose target is
    /// <paramref name="sourceEntityName"/>. Returns null when ambiguous (multiple
    /// singular back-refs), when the back-ref is a collection, or for
    /// self-relationships (which use the existing <c>IsBackReference</c> path).
    /// Single source of truth for both <see cref="AddCreateNavMethod"/> (factory
    /// signature + <c>this</c> wiring) and the call-site arg list
    /// (<c>EffectLoweringPass.CreateEntityInRelationship</c>).
    /// </summary>
    internal static Relationship? FindAutoWireBackReference(Entity targetEntity, string sourceEntityName) {
        if (string.Equals(sourceEntityName, targetEntity.Name, StringComparison.Ordinal))
            return null; // self-relationships use the existing IsBackReference path

        Relationship? found = null;
        var count = 0;
        foreach (var nav in targetEntity.Navigations) {
            if (nav.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany)
                continue;
            if (string.Equals(nav.Target.TypeName, sourceEntityName, StringComparison.Ordinal)) {
                found = nav;
                count++;
            }
        }
        return count == 1 ? found : null;
    }

    /// <summary>
    /// Collection inverses on <paramref name="peerEntity"/> targeting
    /// <paramref name="childEntityName"/> — same rule as runtime
    /// <c>TryLinkInverseCollection</c>. <c>Unique</c> is set only when
    /// <c>Count == 1</c>; otherwise null (skip Attach / no fail-closed).
    /// </summary>
    internal static (Relationship? Unique, int Count) FindInverseCollectionInfo(
        Entity peerEntity, string childEntityName) {
        Relationship? found = null;
        var count = 0;
        foreach (var nav in peerEntity.Navigations) {
            if (nav.Cardinality is not (RelationshipCardinality.OneToMany
                or RelationshipCardinality.ManyToMany))
                continue;
            if (string.Equals(nav.Target.TypeName, childEntityName, StringComparison.Ordinal)) {
                found = nav;
                count++;
            }
        }
        return count == 1 ? (found, count) : (null, count);
    }

    private static string? ResolveNavTargetEntityName(
        string sourceEntityName, string relationshipName, Domain domain) {
        var source = domain.Types.OfType<Entity>().FirstOrDefault(e =>
            string.Equals(e.Name, sourceEntityName, StringComparison.Ordinal));
        var nav = source?.Navigations.FirstOrDefault(n =>
            string.Equals(n.Name, relationshipName, StringComparison.Ordinal));
        return nav?.Target.TypeName;
    }
}