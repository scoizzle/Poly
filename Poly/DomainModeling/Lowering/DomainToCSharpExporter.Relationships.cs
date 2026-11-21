using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using AccessModifier = Poly.Introspection.AccessModifier;

namespace Poly.DomainModeling.Lowering;

// Relationships: navigation shape, inverse resolution, and attach (link) methods.
public sealed partial class DomainToCSharpExporter {
    /// <summary>
    /// Inverse of a create-in to-one initializer: add an already-constructed child
    /// to this collection and register this entity's subscriptions on it. It touches <c>this</c> first (L-03c): the
    /// peer it runs on may be an entity the action never touched otherwise. The child's paired to-one is set too
    /// (#572): the collection holding the child and the child's own navigation are the two sides of one pair.
    /// </summary>
    private static void AddAttachNavMethod(
        Entity entity,
        Relationship rel,
        Domain domain,
        List<SubscriptionInfo>? subscriberSubs,
        string fieldName,
        List<MethodDefinitionNode> methods) {
        var targetType = new NamedTypeReference(rel.Target.TypeName);
        var child = new Parameter("child", targetType);
        var body = new List<Node> {
            TouchThis(),
            new Invoke(
                new Member(new Member(new ThisReference(), fieldName), "Add"),
                [child])
        };
        var targetEntity = domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, rel.Target.TypeName, StringComparison.Ordinal));
        var paired = targetEntity is null ? null : CreateInWiring.Inverse(entity, rel, targetEntity);
        if (paired is not null && !CreateInWiring.IsCollection(paired)) {
            // Through the child's own attach: the to-one's setter is private to the child's class,
            // and Attach also registers the child's subscriptions (L-03c).
            body.Add(new Invoke(
                new Member(child, $"Attach{ToPascalCase(paired.Name)}"),
                [new ThisReference()]));
        }
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
    /// The singular inverse of a collection navigation: <c>Attach{Nav}(child)</c> assigns the peer's
    /// to-one navigation and registers this entity's subscriptions on the child. The collection form
    /// above adds to the field; both are the wiring a factory calls for a supplied create argument
    /// (#572, #587).
    /// </summary>
    private static void AddAttachSingularNavMethod(
        Entity entity,
        Relationship rel,
        List<SubscriptionInfo>? subscriberSubs,
        List<MethodDefinitionNode> methods) {
        var targetType = new NamedTypeReference(rel.Target.TypeName);
        var child = new Parameter("child", targetType);
        var body = new List<Node> {
            TouchThis(),
            new Assignment(new Member(new ThisReference(), ToPascalCase(rel.Name)), child)
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

    /// <summary>
    /// Emits the navigation properties (PascalCase; <c>IReadOnlyList</c> for collections)
    /// and the create-in / attach methods that go with a collection navigation.
    /// </summary>
    private static void AddNavigationMembers(
        Entity entity, Domain domain, IReadOnlyList<Relationship> domainRelationships,
        INodeMetadataProvider metadata, List<SubscriptionInfo>? subscriberSubs,
        List<PropertyDefinitionNode> props, List<FieldDefinitionNode> fields,
        List<MethodDefinitionNode> methods) {
        // ── Navigation properties (PascalCase; IReadOnlyList for collections) ──
        foreach (var rel in domainRelationships) {
            if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal))
                continue;

            var isMany = rel.Cardinality is RelationshipCardinality.OneToMany
                         or RelationshipCardinality.ManyToMany;
            var targetType = new NamedTypeReference(rel.Target.TypeName);
            var pascalName = ToPascalCase(rel.Name);
            var paramName = ToCamelCase(pascalName);

            if (isMany) {
                // Collection nav: private field, IEnumerable<T> constructor param,
                // getter-only property. EF passes loaded items in via the constructor.
                var fieldName = $"_{paramName}";
                var listType = new NamedTypeReference("List",
                    TypeArguments: [targetType]);
                var readOnlyType = new NamedTypeReference("IReadOnlyList",
                    TypeArguments: [targetType]);
                var enumerableType = new NamedTypeReference("IEnumerable",
                    TypeArguments: [targetType]);

                fields.Add(new FieldDefinitionNode(
                    fieldName,
                    listType,
                    DefaultValue: new New(listType),
                    AccessModifier: AccessModifier.Private
                ));

                props.Add(new PropertyDefinitionNode(
                    pascalName, readOnlyType,
                    Getter: new PropertyGetterDefinitionNode(
                        Body: new Member(new ThisReference(), fieldName)),
                    IsReadOnly: true,
                    AccessModifier: DomainAccess.NavigationAccess(entity, targetType, domain)
                ));

                // Create{Nav} is the typed create-in. A relationship with no create-in
                // on this entity does not get a factory (Order.CreateCustomer).
                if (EffectHelpers.CreatesIn(entity, rel.Name))
                    AddCreateNavMethod(entity, rel, domain, subscriberSubs, fieldName, methods, metadata);
                AddAttachNavMethod(entity, rel, domain, subscriberSubs, fieldName, methods);
            }
            else {
                // Singular nav: property with private setter (constructor param).
                // Navs are optional references (set at link/create time, may be null
                // at EF materialization) — emit nullable so the generated code has
                // no non-nullable-uninitialized warnings (CS8618).
                if (EffectHelpers.CreatesIn(entity, rel.Name))
                    AddCreateSingularNavMethod(entity, rel, domain, subscriberSubs, methods, metadata);
                AddAttachSingularNavMethod(entity, rel, subscriberSubs, methods);
                props.Add(new PropertyDefinitionNode(
                    pascalName, new OptionalTypeReference(targetType),
                    Getter: new PropertyGetterDefinitionNode(),
                    Setter: new PropertySetterDefinitionNode(
                        AccessModifier: AccessModifier.Private),
                    AccessModifier: DomainAccess.NavigationAccess(entity, targetType, domain)
                ));
            }
        }
    }
}