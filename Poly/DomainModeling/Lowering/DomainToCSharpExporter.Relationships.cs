using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Runtime;

using AccessModifier = Poly.Introspection.AccessModifier;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Relationships: navigation shape, inverse resolution, and attach (link) methods.
public sealed partial class DomainToCSharpExporter {
    /// <summary>
    /// Inverse of a create-in to-one initializer: add an already-constructed child
    /// to this collection and register this entity's subscriptions on it. It touches <c>this</c> first (L-03c): the
    /// peer it runs on may be an entity the action never touched otherwise.
    /// An owned part has one owner (#585): when the child names its owner
    /// (<paramref name="ownerNav"/>), a child already owned by another instance is refused with a
    /// <c>Link</c> failure instead of being held by two owners. The result is returned, so the
    /// caller propagates it.
    /// </summary>
    private static void AddAttachNavMethod(
        Entity entity,
        Relationship rel,
        List<SubscriptionInfo>? subscriberSubs,
        string fieldName,
        Relationship? ownerNav,
        List<MethodDefinitionNode> methods) {
        var targetType = new NamedTypeReference(rel.Target.TypeName);
        var child = new Parameter("child", targetType);
        var resultType = new NamedTypeReference("DomainResult");
        var body = new List<Node> { TouchThis() };
        if (rel.SourceOwnsTarget && ownerNav is not null) {
            var owner = new Member(child, ToPascalCase(ownerNav.Name));
            body.Add(new IfStatement(
                new Syntactic.And(
                    new NotEqual(owner, new Constant(null)),
                    new NotEqual(owner, new ThisReference())),
                new Block([FailureLowering.Return(resultType, new FailureLowering.Spec(
                    DomainFailureKind.Link,
                    new Constant(Relationship.OwnerViolationMessage(
                        rel.Name, rel.Target.TypeName, entity.Name)),
                    $"{entity.Name}.{rel.Name}"))])));
        }
        body.Add(new Invoke(
            new Member(new Member(new ThisReference(), fieldName), "Add"),
            [child]));
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
        body.Add(DomainResultSuccess());
        methods.Add(new MethodDefinitionNode(
            $"Attach{ToPascalCase(rel.Name)}",
            resultType,
            Parameters: [child],
            Body: new Block(body),
            AccessModifier: AccessModifier.Internal
        ));
    }

    /// <summary>
    /// The singular navigation on <paramref name="childEntity"/> that names the owner of the part
    /// <paramref name="owned"/>'s target: the one <c>via</c> declares, the one that declares
    /// <c>via</c> back, or the only singular navigation pointing back. Null when the part names no
    /// owner at all — then nothing records who owns it and neither host can refuse a second one.
    /// The emitted <c>Attach{Nav}</c> and the host's link door read this one answer (#585).
    /// </summary>
    internal static Relationship? OwnerNavigationOf(Entity childEntity, Relationship owned) {
        static bool IsSingular(Relationship nav) =>
            nav.Cardinality is RelationshipCardinality.OneToOne or RelationshipCardinality.ManyToOne;

        var pointingBack = childEntity.Navigations
            .Where(nav => IsSingular(nav)
                && string.Equals(nav.Target.TypeName, owned.Source.TypeName, StringComparison.Ordinal)
                && !ReferenceEquals(nav, owned))
            .ToList();
        if (owned.Inverse is { Length: > 0 } declared)
            return pointingBack.FirstOrDefault(nav => string.Equals(nav.Name, declared, StringComparison.Ordinal));
        var declaring = pointingBack
            .Where(nav => string.Equals(nav.Inverse, owned.Name, StringComparison.Ordinal))
            .ToList();
        if (declaring.Count == 1)
            return declaring[0];
        return pointingBack.Count == 1 ? pointingBack[0] : null;
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
                var childEntity = domain.Types.OfType<Entity>()
                    .FirstOrDefault(e => string.Equals(e.Name, rel.Target.TypeName, StringComparison.Ordinal));
                AddAttachNavMethod(entity, rel, subscriberSubs, fieldName,
                    childEntity is null ? null : OwnerNavigationOf(childEntity, rel), methods);
            }
            else {
                // Singular nav: property with private setter (constructor param).
                // Navs are optional references (set at link/create time, may be null
                // at EF materialization) — emit nullable so the generated code has
                // no non-nullable-uninitialized warnings (CS8618).
                if (EffectHelpers.CreatesIn(entity, rel.Name))
                    AddCreateSingularNavMethod(entity, rel, domain, subscriberSubs, methods, metadata);
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