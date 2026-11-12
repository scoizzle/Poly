using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Produces Syntax AST <see cref="TypeDefinitionNode"/> trees from a
/// <see cref="Domain"/>, suitable for C# code generation via
/// <see cref="Interpretation.CSharp.CSharpWriter"/>.
///
/// Each entity becomes a class with properties, navigation properties (as
/// <c>IReadOnlyList&lt;T&gt;</c> for collections), lifecycle stages (as enum +
/// <c>CurrentStage</c> field), actions (as void methods), and policies (as
/// bool methods). Constructor parameters are auto-generated for every property.
///
/// Stage subscriptions (<c>when RelName Stage</c>) generate cross-entity notification:
/// the subscriber entity declares a <c>When{Target}{Stage}</c> handler method
/// (peer parameter when <c>when … as name</c>; <c>when all</c> also takes the
/// transitioning record's previous stage), and the target entity emits a
/// subscriber list + notify call after each stage transition
/// (<c>sub.When…(previousStage)</c>, <c>sub.When…(this)</c>, or both).
/// </summary>
public sealed partial class DomainToCSharpExporter {
    private static Node StageEnumMember(string enumName, string stageName) =>
        new Member(new NamedTypeReference(enumName), stageName);

    private static Node EqualToStage(Node currentStage, string enumName, string stageName) =>
        new Equal(currentStage, StageEnumMember(enumName, stageName));

    private static Node NotEqualToStage(Node currentStage, string enumName, string stageName) =>
        new NotEqual(currentStage, StageEnumMember(enumName, stageName));

    private static Node InWatchedStages(Node currentStage, string enumName, IEnumerable<string> stageNames) {
        Node? acc = null;
        foreach (var stageName in stageNames) {
            var eq = EqualToStage(currentStage, enumName, stageName);
            acc = acc is null ? eq : new Syntactic.Or(acc, eq);
        }
        return acc!;
    }

    private static Node NotInWatchedStages(Node currentStage, string enumName, IEnumerable<string> stageNames) {
        Node? acc = null;
        foreach (var stageName in stageNames) {
            var neq = NotEqualToStage(currentStage, enumName, stageName);
            acc = acc is null ? neq : new Syntactic.And(acc, neq);
        }
        return acc!;
    }

    // ── Per-entity builder ──────────────────────────────────────

    private static Node EmptyDefault(DomainTypeReference type) => type.TypeName switch {
        "Text" or "String" => new Constant(""),
        "Boolean" => new Constant(false),
        _ => new Constant(0L),
    };

    internal static IReadOnlyList<TypeDefinitionNode> BuildTypeDefsForEntity(
        Entity entity,
        Domain domain,
        IReadOnlyList<Relationship> domainRelationships,
        IReadOnlyDictionary<string, Entity> entityLookup,
        INodeMetadataProvider metadata,
        List<SubscriptionInfo>? targetSubs = null,
        List<SubscriptionInfo>? subscriberSubs = null,
        IReadOnlyDictionary<SubscriptionInfo, string>? handlerNames = null,
        Dictionary<(string Entity, string Policy), Node>? policyBodies = null,
        Dictionary<SubscriptionDispatchPlanEntry, Dictionary<string, Node>>? subscriptionBodies = null,
        ContractSplicePlan? splice = null) {

        ArgumentNullException.ThrowIfNull(metadata);

        var esm = metadata.GetStructure(entity)
            ?? throw new InvalidOperationException(
                $"EntityStructureMetadata is required for entity '{entity.Name}'.");

        var typeDefs = new List<TypeDefinitionNode>();
        var props = new List<PropertyDefinitionNode>();
        var methods = new List<MethodDefinitionNode>();
        var fields = new List<FieldDefinitionNode>();
        var ctorParams = new List<Parameter>();
        var ctorAssignments = new List<Node>();
        // Defaulted props become TRAILING optional ctor params (C# requires optional
        // params after all required ones); collected here, appended after nav params.
        var defaultedCtorParams = new List<Parameter>();

        // Stage enum name — the published fact (EntityStructureMetadata.StageEnumTypeName)
        // is the single source; fall back to the derivation only when the bag is absent.
        var stageEnumTypeName = esm.HasStages
            ? esm.StageEnumTypeName ?? $"{entity.Name}Stage"
            : null;

        // Properties assigned by the initial stage's entry effects are body-initialized
        // (the ctor already runs those effects after setting CurrentStage) — they must
        // NOT also be ctor params, or the value is written twice (param then effect)
        // and the param is dead (e.g. StartedAt: param, then entry `assign StartedAt to now`).
        // The set is a published analysis fact (EntityStructureMetadata) — shared with
        // the constructor-signature computation so both stay in lockstep.
        var entryAssignedProps = esm.EntryAssignedPropertyNames;

        // ── Properties ────────────────────────────────────────────
        AddEntityProperties(entity, domain, metadata, esm, entryAssignedProps,
            props, ctorParams, ctorAssignments, defaultedCtorParams);
        OrderScalarCtorSlots(esm, ctorParams, ctorAssignments);

        // ── Relationships ─────────────────────────────────────────
        AddNavigationMembers(entity, domain, domainRelationships, metadata, subscriberSubs,
            props, fields, methods);
        AddNavigationCtorParameters(esm, domain, metadata, ctorParams, ctorAssignments);

        // Defaulted props trail the required scalar + nav params (optional params must
        // come last). Appended here so the Create/ctor signatures carry them as overridable
        // defaults — `create in { DefaultedProp: value }` flows through construction.
        ctorParams.AddRange(defaultedCtorParams);

        // Watched stages get Notify{Stage}Subscribers; EffectLoweringPass captures
        // the previous stage only when a when-all handler reads it.
        IReadOnlySet<string>? postTransitionNotifyStages = null;
        IReadOnlySet<string>? previousStageNotifyStages = null;
        if (targetSubs is { Count: > 0 }) {
            postTransitionNotifyStages = targetSubs
                .Select(s => s.StageName)
                .ToHashSet(StringComparer.Ordinal);
            previousStageNotifyStages = targetSubs
                .Where(NeedsAllPreviousStage)
                .Select(s => s.StageName)
                .ToHashSet(StringComparer.Ordinal);
        }

        // ── Actions as void methods ───────────────────────────────
        // Same action name on multiple stages is one C# method that dispatches on
        // CurrentStage. Emitting one method per stage produced illegal duplicate members
        // (FieldService WorkOrder.Cancel on Draft/Scheduled/Blocked).
        AddActionMethods(entity, methods, stageEnumTypeName, postTransitionNotifyStages,
            previousStageNotifyStages, domain, metadata, splice);

        // ── Policies as bool methods ──────────────────────────────
        AddPolicyMethods(entity, domain, metadata, methods, policyBodies);

        // ── Notify dispatch and handlers ──────────────────────────
        AddSubscriptionRegistry(targetSubs, stageEnumTypeName, handlerNames, fields, methods);
        AddSubscriptionHandlers(entity, domain, metadata, esm, subscriberSubs, handlerNames,
            postTransitionNotifyStages, previousStageNotifyStages, stageEnumTypeName,
            subscriptionBodies, methods);

        // ── Stage enum + CurrentStage property ────────────────────
        AddStageMembers(entity, esm, stageEnumTypeName, typeDefs, props);

        // ── Private constructor + public static Create factory ─────
        var ctors = AddCreateFactory(entity, domain, metadata, esm, stageEnumTypeName,
            postTransitionNotifyStages, previousStageNotifyStages, subscriberSubs,
            ctorParams, ctorAssignments, methods);

        // Generated C# fans out via Notify{Stage}Subscribers; do not emit a
        // no-op Notify(string) (Fleet dogfood).

        // ── InitializeSubscriptions — for post-load subscription wiring ──
        AddSubscriptionInitializer(subscriberSubs, methods);

        if (entity.Properties.Any(p => p.Constraints.OfType<UniqueConstraint>().Any())) {
            methods.Add(EnsureUniqueAssignMethod(entity));
        }

        AddAutomaticTransitionLoopGuard(entity, fields, methods);
        if (targetSubs is { Count: > 0 })
            AddNotifyCascadeDepthGuard(entity, fields, methods);

        AddActionScopeMembers(props, fields, methods);

        typeDefs.Add(new TypeDefinitionNode(
            entity.Name,
            Constructors: ctors,
            Properties: props.Count > 0 ? props : null,
            Methods: methods.Count > 0 ? methods : null,
            Fields: fields.Count > 0 ? fields : null,
            Interfaces: [new NamedTypeReference("Poly.Runtime.IActionScoped")],
            AccessModifier: DomainAccess.ForEntity(entity),
            Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
        ));

        return typeDefs;
    }

    /// <summary>
    /// <c>Poly.Runtime.IActionScoped</c> (L-03, D3: implicit, so the two names sit in the entity's
    /// declaration space under the reserved <c>__</c> prefix). The snapshot is one slot per piece of
    /// instance state, in declaration order: auto properties, then fields. A list is captured as an
    /// array and restored by clear and refill so its identity survives; a nullable list or set is
    /// captured as null or a copy. Neither member calls the storage port.
    /// </summary>
    private static void AddActionScopeMembers(
        List<PropertyDefinitionNode> props, List<FieldDefinitionNode> fields, List<MethodDefinitionNode> methods) {
        var objectType = new ClrTypeReference(typeof(object));
        var optionalObject = new OptionalTypeReference(objectType);
        var slots = new List<ScopeSlot>();
        foreach (var prop in props.Where(static p => p.Setter is not null && p.Getter?.Body is null && !p.IsStatic))
            slots.Add(new ScopeSlot(prop.Name, prop.MemberType));
        foreach (var field in fields.Where(static f => !f.IsStatic && !f.IsConst))
            slots.Add(new ScopeSlot(field.Name, field.MemberType));

        static Node Field(string name) => new Member(new ThisReference(), name);

        var snapshotArray = new Variable("__state");
        var snapshot = new List<Node> {
            new Assignment(snapshotArray, new NewArray(optionalObject, new Constant(slots.Count)))
        };
        var stateParameter = new Parameter("state", objectType);
        var savedArray = new Variable("__saved");
        var restore = new List<Node> {
            new Assignment(savedArray, new TypeCast(stateParameter, new CollectionTypeReference(optionalObject, CollectionKind.Array)))
        };

        for (var i = 0; i < slots.Count; i++) {
            var (name, type) = slots[i];
            Node Slot(Variable array) => new IndexAccess(array, new Constant(i));
            Node SavedValue() => new NullForgiving(Slot(savedArray));

            switch (ListElement(type, out var optionalList)) {
                case { } element when optionalList: {
                        var listType = new NamedTypeReference("List", TypeArguments: [element]);
                        var item = new Variable($"__item{i}");
                        snapshot.Add(new Assignment(Slot(snapshotArray), new Syntactic.Conditional(
                            new Equal(Field(name), new Constant(null)),
                            new Constant(null),
                            new New(listType, Field(name)))));
                        restore.Add(new IfStatement(
                            new Equal(Slot(savedArray), new Constant(null)),
                            new Assignment(Field(name), new Constant(null)),
                            new Block(
                                new IfStatement(
                                    new Equal(Field(name), new Constant(null)),
                                    new Assignment(Field(name), new New(listType))),
                                ClearList(Field(name)),
                                new ForEachLoop(item, new TypeCast(SavedValue(), listType),
                                    new Invoke(new Member(Field(name), "Add"), item)))));
                        break;
                    }
                case { } element: {
                        var listType = new NamedTypeReference("List", TypeArguments: [element]);
                        var item = new Variable($"__item{i}");
                        snapshot.Add(new Assignment(Slot(snapshotArray), new New(listType, Field(name))));
                        restore.Add(ClearList(Field(name)));
                        restore.Add(new ForEachLoop(item, new TypeCast(SavedValue(), listType),
                            new Invoke(new Member(Field(name), "Add"), item)));
                        break;
                    }
                default: {
                        if (IsOptionalSet(type, out var setType)) {
                            var enumerable = new NamedTypeReference(
                                "IEnumerable", TypeArguments: [new PrimitiveTypeReference(PrimType.String)]);
                            snapshot.Add(new Assignment(Slot(snapshotArray), new Syntactic.Conditional(
                                new Equal(Field(name), new Constant(null)),
                                new Constant(null),
                                new New(setType, Field(name)))));
                            restore.Add(new Assignment(Field(name), new Syntactic.Conditional(
                                new Equal(Slot(savedArray), new Constant(null)),
                                new Constant(null),
                                new New(setType, new TypeCast(SavedValue(), enumerable)))));
                            break;
                        }

                        snapshot.Add(new Assignment(Slot(snapshotArray), Field(name)));
                        restore.Add(new Assignment(Field(name), new TypeCast(SavedValue(), type)));
                        break;
                    }
            }
        }

        snapshot.Add(new Return(snapshotArray));
        methods.Add(new MethodDefinitionNode(
            "__Snapshot", objectType, Body: new Block(snapshot, [snapshotArray])));
        methods.Add(new MethodDefinitionNode(
            "__Restore", new TypeReference("void"), [stateParameter], new Block(restore, [savedArray])));
    }

    /// <summary>The synthetic entity-list type has no <c>Clear</c>; the cast to <c>IList</c> reaches the real one.</summary>
    private static Node ClearList(Node list) =>
        new Invoke(new Member(new TypeCast(list, new ClrTypeReference(typeof(System.Collections.IList))), "Clear"));

    private readonly record struct ScopeSlot(string Name, Node Type);

    /// <summary>The element type when <paramref name="type"/> is <c>List&lt;T&gt;</c> or <c>List&lt;T&gt;?</c>.</summary>
    private static Node? ListElement(Node type, out bool optional) {
        optional = type is OptionalTypeReference;
        var inner = type is OptionalTypeReference opt ? opt.InnerType : type;
        return inner is NamedTypeReference { TypeName: "List", TypeArguments: [var element] } ? element : null;
    }

    private static bool IsOptionalSet(Node type, out NamedTypeReference setType) {
        if (type is OptionalTypeReference { InnerType: NamedTypeReference { TypeName: "HashSet" } set }) {
            setType = set;
            return true;
        }
        setType = null!;
        return false;
    }

    // ── Type mapping ────────────────────────────────────────────

    internal static Node MapDomainTypeRef(DomainTypeReference domainType,
        Domain domain, INodeMetadataProvider analysis) {
        var typeName = domainType.TypeName;

        // Enum types and entity references both emit NamedTypeReference(typeName):
        // the catalog is the single source of truth and no tree scan is performed (amu-w3-1).
        var clr = RuntimeAnalysisCache.ClrTypeName(domain, typeName);
        if (DomainTypeMapping.TryPrimitiveType(clr, out var prim))
            return new PrimitiveTypeReference(prim);
        return new NamedTypeReference(typeName);
    }

    internal static bool TryResolveEnumType(Domain domain, INodeMetadataProvider analysis, string typeName, out EnumType? enumType) {
        enumType = null;

        // Catalog only: fail closed (no domain tree rescan).
        var lookup = analysis.GetTypeLookup(domain);
        if (lookup is not null
            && lookup.Types.TryGetValue(typeName, out var resolvedType)
            && resolvedType is EnumType resolvedEnum) {
            enumType = resolvedEnum;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Builds a map from property name → enum type name for all properties of
    /// <paramref name="entity"/> whose type resolves to an <see cref="EnumType"/>.
    /// Used by the expression lowering pass to emit qualified enum member access.
    /// Catalog-only.
    /// </summary>
    /// <summary>
    /// Returns the entity's enum-typed property map (property name → enum type name),
    /// preferring the published <see cref="EntityStructureMetadata.EnumPropertyNames"/>
    /// bag and falling back to the catalog type lookup when the bag is absent.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? GetEnumPropertyNames(
        Entity entity, Domain domain, INodeMetadataProvider analysis) {
        if (analysis.GetStructure(entity) is { EnumPropertyNames: not null } esm)
            return esm.EnumPropertyNames;
        return BuildEnumPropertyNames(entity, domain, analysis);
    }

    internal static Dictionary<string, string>? BuildEnumPropertyNames(
        Entity entity, Domain domain, INodeMetadataProvider analysis) {
        Dictionary<string, string>? map = null;
        var types = (analysis.GetTypeLookup(domain)
            ?? throw new InvalidOperationException(
                "Domain catalog type lookup is required for enum property mapping.")).Types;

        foreach (var prop in entity.Properties) {
            if (types.TryGetValue(prop.Type.TypeName, out var resolved)
                && resolved is EnumType) {
                (map ??= new(StringComparer.Ordinal))[prop.Name] = prop.Type.TypeName;
            }
        }
        return map;
    }

    // ── String helpers ──────────────────────────────────────────

    internal static string ToCamelCase(string name) => DomainTypeMapping.ToCamelCase(name);

    internal static string ToPascalCase(string name) => DomainTypeMapping.ToPascalCase(name);
}