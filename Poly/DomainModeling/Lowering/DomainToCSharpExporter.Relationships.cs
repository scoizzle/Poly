using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using AccessModifier = Poly.Introspection.AccessModifier;
using Syntactic = Poly.Ast.Nodes;

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

    /// <summary>
    /// Constructor parameters and assignments for navigation slots, taken from the
    /// COMPLETE published signature (<see cref="EntityStructureMetadata.CreateSlots"/>).
    /// </summary>
    private static void AddNavigationCtorParameters(
        EntityStructureMetadata esm, Domain domain, INodeMetadataProvider metadata,
        List<Parameter> ctorParams, List<Node> ctorAssignments) {
        // ── Constructor params for navs — from the COMPLETE signature (ESM) ──
        // EntityStructureMetadata.CreateSlots includes collection navs
        // (IsCollection). This is the single source of truth for the emitted Create(...)
        // signature — no per-consumer re-derivation of cardinality/order here (the
        // callers already read the same bag). Order matches the nav loop above
        // (relationship order), so output is unchanged.
        foreach (var navParam in LegacyConstructorOrder(esm).Where(p => p.IsNavigation)) {
            var paramName = ToCamelCase(navParam.Name);
            if (navParam.IsCollection) {
                var fieldName = $"_{paramName}";
                var targetType = new NamedTypeReference(navParam.Type.TypeName);
                var listType = new NamedTypeReference("List", TypeArguments: [targetType]);
                var enumerableType = new NamedTypeReference("IEnumerable", TypeArguments: [targetType]);
                ctorParams.Add(new Parameter(
                    paramName,
                    new OptionalTypeReference(enumerableType),
                    DefaultValue: new Constant(null)));
                ctorAssignments.Add(new Assignment(
                    new Member(new ThisReference(), fieldName),
                    new New(listType, [
                        new Syntactic.Coalesce(
                            new Parameter(paramName),
                            new New(listType))])));
            }
            else {
                // Singular nav (incl. back-reference): optional (unlinked at create).
                var pascalName = ToPascalCase(navParam.Name);
                var propRef = new OptionalTypeReference(
                    MapDomainTypeRef(navParam.Type, domain, metadata));
                ctorParams.Add(new Parameter(
                    paramName, propRef, DefaultValue: new Constant(null)));
                ctorAssignments.Add(new Assignment(
                    new Member(new ThisReference(), pascalName),
                    new Parameter(paramName)));
            }
        }
    }
}