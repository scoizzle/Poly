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
    /// peer it runs on may be an entity the action never touched otherwise. The child's paired to-one is set too
    /// (#572): the collection holding the child and the child's own navigation are the two sides of one pair.
    /// An owned part has one owner (#585): when the child names its owner
    /// (<paramref name="ownerNav"/>), a child already owned by another instance is refused with a
    /// <c>Link</c> failure instead of being held by two owners. The result is returned, so the
    /// caller propagates it.
    /// </summary>
    private static void AddAttachNavMethod(
        Entity entity,
        Relationship rel,
        Domain domain,
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
    /// The collection on <paramref name="peerEntity"/> that pairs with <paramref name="argument"/>: the one
    /// its <c>via</c> names, or the one that declares <c>via</c> back. With neither declaration the pairing is
    /// the only collection of the child's type, and on a self-relationship — where every navigation targets
    /// the child's type — only when nothing else points back: two candidates are ambiguous and nothing pairs.
    /// A target type is not a declaration, so the fallback never guesses past a second candidate (#584).
    /// <c>Unique</c> is set only for a determined answer, and the lowered module this emits is the one body
    /// both hosts run, so the interpreter and the printed C# pair a create argument the same way.
    /// </summary>
    internal static (Relationship? Unique, int Count) FindInverseCollectionInfo(
        Entity peerEntity, string childEntityName, Relationship? argument = null) {
        static bool IsCollection(Relationship nav) =>
            nav.Cardinality is RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany;

        var pointingBack = peerEntity.Navigations
            .Where(nav => string.Equals(nav.Target.TypeName, childEntityName, StringComparison.Ordinal))
            .ToList();
        var candidates = pointingBack
            .Where(IsCollection)
            .Where(nav => argument is null || !ReferenceEquals(nav, argument))
            .ToList();

        if (argument is not null) {
            if (argument.Inverse is { Length: > 0 } declared) {
                var named = candidates.FirstOrDefault(nav => string.Equals(nav.Name, declared, StringComparison.Ordinal));
                return named is null ? (null, candidates.Count) : (named, 1);
            }
            var declaring = candidates
                .Where(nav => string.Equals(nav.Inverse, argument.Name, StringComparison.Ordinal))
                .ToList();
            if (declaring.Count == 1)
                return (declaring[0], 1);
        }

        var self = string.Equals(peerEntity.Name, childEntityName, StringComparison.Ordinal);
        if (self && pointingBack.Count > 1)
            return (null, candidates.Count);
        return candidates.Count == 1 ? (candidates[0], 1) : (null, candidates.Count);
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

                // The getter hands out a copy, never the live List the field holds (#556, #541):
                // a downcast to List<T>/IList<T> would otherwise bypass every generated guard,
                // subscription registration and constraint. A copy is safe to mutate and safe to
                // enumerate while an action mutates the aggregate.
                props.Add(new PropertyDefinitionNode(
                    pascalName, readOnlyType,
                    Getter: new PropertyGetterDefinitionNode(
                        Body: new New(
                            listType,
                            [new Member(new ThisReference(), fieldName)])),
                    IsReadOnly: true,
                    AccessModifier: DomainAccess.NavigationAccess(entity, targetType, domain)
                ));

                // Create{Nav} is the typed create-in. A relationship with no create-in
                // on this entity does not get a factory (Order.CreateCustomer).
                if (EffectHelpers.CreatesIn(entity, rel.Name))
                    AddCreateNavMethod(entity, rel, domain, subscriberSubs, fieldName, methods, metadata);
                var childEntity = domain.Types.OfType<Entity>()
                    .FirstOrDefault(e => string.Equals(e.Name, rel.Target.TypeName, StringComparison.Ordinal));
                AddAttachNavMethod(entity, rel, domain, subscriberSubs, fieldName,
                    childEntity is null ? null : OwnerNavigationOf(childEntity, rel), methods);
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