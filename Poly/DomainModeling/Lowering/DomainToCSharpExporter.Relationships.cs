using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using AccessModifier = Poly.Introspection.AccessModifier;

namespace Poly.DomainModeling.Lowering;

// Relationships: navigation shape, inverse resolution, and attach (link) methods.
public sealed partial class DomainToCSharpExporter {
    /// <summary>
    /// Inverse of a create-in to-one initializer: add an already-constructed child
    /// to this collection and register this entity's subscriptions on it. It touches <c>this</c> first (L-03c): the
    /// peer it runs on may be an entity the action never touched otherwise.
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
            TouchThis(),
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
                AddAttachNavMethod(entity, rel, subscriberSubs, fieldName, methods);
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