using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

using AccessModifier = Poly.Introspection.AccessModifier;
using Action = Poly.DomainModeling.Ontology.Action;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Produces Syntax AST <see cref="TypeDefinitionNode"/> trees from a
/// <see cref="Domain"/>, suitable for C# code generation via
/// <see cref="Interpretation.CSharp.CSharpGenerator"/>.
///
/// Each entity becomes a class with properties, navigation properties (as
/// <c>IReadOnlyList&lt;T&gt;</c> for collections), lifecycle stages (as enum +
/// <c>CurrentStage</c> field), actions (as void methods), and policies (as
/// bool methods). Constructor parameters are auto-generated for every property.
///
/// Stage subscriptions (<c>when RelName Stage</c>) generate cross-entity notification:
/// the subscriber entity declares a <c>When{Target}{Stage}</c> handler method
/// (zero-arg when notification-only; one peer parameter of the target entity type named
/// <c>PeerBinding</c> when <c>when … as name</c>), and the target entity emits a
/// subscriber list + notify call after each stage transition
/// (<c>sub.When…()</c> or <c>sub.When…(this)</c>).
/// </summary>
public sealed partial class DomainToCSharpExporter {
    /// <summary>Collected subscription data for cross-entity notification — populated from the
    /// analysis-published <see cref="SubscriptionDispatchPlanMetadata"/> (the SAME dispatch
    /// plan the runtime consumes), one info per watched target stage.</summary>
    internal sealed record SubscriptionInfo(
        string StageName,
        SubscriptionDispatchPlanEntry Subscription,
        Entity SourceEntity,
        Entity TargetEntity,
        Relationship Relationship,
        string? SubscriberStageName = null
    );

    private static string SubscriberRegistryFieldName(SubscriptionInfo info) =>
        $"_{ToCamelCase(info.SourceEntity.Name)}{info.StageName}Subscribers";

    /// <summary>
    /// Builds Syntax AST type definitions for all entities and their stage enums
    /// in the given domain. Reads entity members directly from the domain model.
    /// </summary>
    /// <param name="domain">The domain model to export.</param>
    /// <param name="analysis">
    /// The analysis result (required).
    /// </param>
    public IReadOnlyList<TypeDefinitionNode> Export(Domain domain,
        AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(analysis);
        return DomainProgramProjection.ToSyntax(domain, analysis);
    }

    /// <summary>
    /// Collects the subscription facts for one node's dispatch plan (an entity for
    /// entity-level subscriptions, or a stage for stage-scoped ones) into the target/
    /// subscriber maps used for code generation. The exporter consumes the analysis-
    /// published <see cref="SubscriptionDispatchPlanMetadata"/> rather than re-walking
    /// <see cref="StageSubscription"/> — one description of what fires, shared with the
    /// runtime (no per-site re-derivation drift).
    /// </summary>
    internal static void CollectSubscriptionInfo(
        SubscriptionDispatchPlanMetadata plan,
        Entity subscriber,
        string? subscriberStageName,
        IReadOnlyDictionary<string, Entity> entityLookup,
        List<SubscriptionInfo> subList,
        Dictionary<string, List<SubscriptionInfo>> subscriptionsByTarget) {
        foreach (var entry in plan.ByRelationshipName.Values.SelectMany(e => e)) {
            var rel = subscriber.Navigations.FirstOrDefault(n =>
                string.Equals(n.Name, entry.RelationshipName, StringComparison.Ordinal));
            if (rel is null) continue;
            if (!entityLookup.TryGetValue(entry.TargetEntityName, out var targetEntity))
                continue;

            foreach (var sName in entry.StageNames) {
                var info = new SubscriptionInfo(sName, entry, subscriber, targetEntity, rel, subscriberStageName);
                subList.Add(info);

                if (!subscriptionsByTarget.TryGetValue(targetEntity.Name, out var targetList))
                    subscriptionsByTarget[targetEntity.Name] = targetList = new();
                targetList.Add(info);
            }
        }
    }

    /// <summary>
    /// Assigns each subscription its generated handler method name. The name is
    /// quantifier-aware (<c>WhenAny/WhenAll/WhenEach{Target}{Stage}</c>) and
    /// disambiguated with a <c>_{n}</c> suffix when multiple subscriptions share the
    /// same method signature (same quantifier + peer-binding shape) on the same
    /// relationship+stage — two <c>when Rel Stage</c> (Each) blocks must each fire.
    /// Keyed by reference so even structurally-identical subscriptions get distinct names.
    /// </summary>
    internal static Dictionary<SubscriptionInfo, string> BuildHandlerNames(
        IEnumerable<KeyValuePair<string, List<SubscriptionInfo>>> subscriptionsBySubscriber) {
        var map = new Dictionary<SubscriptionInfo, string>(ReferenceEqualityComparer.Instance);
        foreach (var (_, subList) in subscriptionsBySubscriber) {
            var counts = new Dictionary<(string Stage, string Target, string Quantifier, bool HasPeer), int>();
            foreach (var info in subList) {
                var hasPeer = info.Subscription.PeerBinding is { Length: > 0 };
                var quantifier = QuantifierName(info.Subscription.Quantifier);
                var key = (info.StageName, info.TargetEntity.Name, quantifier, hasPeer);
                counts.TryGetValue(key, out var occurrence);
                counts[key] = occurrence + 1;
                var suffix = occurrence == 0 ? "" : $"_{occurrence + 1}";
                map[info] = $"When{quantifier}{info.TargetEntity.Name}{info.StageName}{suffix}";
            }
        }
        return map;
    }

    private static string QuantifierName(StageSubscriptionQuantifier quantifier) => quantifier switch {
        StageSubscriptionQuantifier.Any => "Any",
        StageSubscriptionQuantifier.All => "All",
        _ => "Each",
    };

    // ── Per-entity builder ──────────────────────────────────────

    internal static IReadOnlyList<TypeDefinitionNode> BuildTypeDefsForEntity(
        Entity entity,
        Domain domain,
        IReadOnlyList<Relationship> domainRelationships,
        IReadOnlyDictionary<string, Entity> entityLookup,
        INodeMetadataProvider metadata,
        List<SubscriptionInfo>? targetSubs = null,
        List<SubscriptionInfo>? subscriberSubs = null,
        IReadOnlyDictionary<SubscriptionInfo, string>? handlerNames = null) {

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
        // is the single source; fall back to the derivation only for the null-analysis path.
        var stageEnumTypeName = esm.StageEnumTypeName ?? $"{entity.Name}Stage";

        // Properties assigned by the initial stage's entry effects are body-initialized
        // (the ctor already runs those effects after setting CurrentStage) — they must
        // NOT also be ctor params, or the value is written twice (param then effect)
        // and the param is dead (e.g. StartedAt: param, then entry `assign StartedAt to now`).
        // The set is a published analysis fact (EntityStructureMetadata) — shared with
        // the constructor-signature computation so both stay in lockstep.
        var entryAssignedProps = esm.EntryAssignedPropertyNames;

        // ── Entity properties (sorted for deterministic output) ──
        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
            var propRef = MapDomainTypeRef(prop.Type, domain, metadata);
            var isRequired = prop.Constraints.Any(c => c is RequiredConstraint);
            List<Node>? constraints = null;
            if (isRequired)
                constraints = [new Constant("required")];

            props.Add(new PropertyDefinitionNode(
                prop.Name, propRef,
                Getter: new PropertyGetterDefinitionNode(),
                // private set — domain state changes only through the Create factory /
                // action effects (no same-assembly backdoor mutability).
                Setter: new PropertySetterDefinitionNode(
                    AccessModifier: AccessModifier.Private),
                // CS8618 hygiene: non-nullable reference-typed scalars (Text → string)
                // are left unset by the EF-materialization parameterless ctor — emit
                // `= default!;` so the generated code compiles warning-free.
                Initializer: IsNonNullableReferenceScalar(propRef)
                    ? new Syntactic.PropertyInitializerDefinitionNode(
                        new Syntactic.NullForgiving(new Syntactic.Default()))
                    : null,
                Constraints: constraints
            ));

            // Defaulted props become OPTIONAL ctor params (the DSL default is the C#
            // default) so `create in { DefaultedProp: value }` overrides flow through
            // construction — no setters, no post-create assignment.
            var defaultValue = prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault();
            var paramName = ToCamelCase(prop.Name);
            // Gym dogfood: Visits default(0) AND Active entry { assign Visits to 0 }
            // made a dead ctor param (assign param, then overwrite with entry).
            if (entryAssignedProps.Contains(prop.Name))
                continue;
            if (defaultValue is not null) {
                var runtimeExpr = EffectLoweringPass.LowerDefaultExpression(
                    defaultValue.Expression, new NamedTypeReference(prop.Type.TypeName));
                if (runtimeExpr is not null) {
                    // Runtime default (now/today/guid) can't be a compile-time default —
                    // T? = null sentinel; body applies the runtime default when null.
                    defaultedCtorParams.Add(new Parameter(paramName,
                        new OptionalTypeReference(propRef),
                        DefaultValue: new Constant(null)));
                    ctorAssignments.Add(new Assignment(
                        new Member(new ThisReference(), prop.Name),
                        new Syntactic.Coalesce(new Parameter(paramName), runtimeExpr)));
                }
                else {
                    var defaultNode = LowerDefaultConstantNode(defaultValue, prop, domain, metadata);
                    defaultedCtorParams.Add(new Parameter(paramName, propRef, DefaultValue: defaultNode));
                    ctorAssignments.Add(new Assignment(
                        new Member(new ThisReference(), prop.Name),
                        new Parameter(paramName)));
                }
                continue; // skip ctor param — handled as a trailing optional param
            }

            // No default expression — full constructor param + assignment
            ctorParams.Add(new Parameter(paramName, propRef));
            ctorAssignments.Add(new Assignment(
                new Member(new ThisReference(), prop.Name),
                new Parameter(paramName)));
        }

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
                    AccessModifier: AccessModifier.Private
                ));

                props.Add(new PropertyDefinitionNode(
                    pascalName, readOnlyType,
                    Getter: new PropertyGetterDefinitionNode(
                        Body: new Member(new ThisReference(), fieldName)),
                    IsReadOnly: true
                ));

                // Generate CreateNavName(args) factory method on the source entity
                AddCreateNavMethod(entity, rel, domain!, subscriberSubs, fieldName, methods, metadata);
                AddAttachNavMethod(entity, rel, subscriberSubs, fieldName, methods);
            }
            else {
                // Singular nav: property with private setter (constructor param).
                // Navs are optional references (set at link/create time, may be null
                // at EF materialization) — emit nullable so the generated code has
                // no non-nullable-uninitialized warnings (CS8618).
                AddCreateSingularNavMethod(entity, rel, domain!, methods, metadata);
                props.Add(new PropertyDefinitionNode(
                    pascalName, new OptionalTypeReference(targetType),
                    Getter: new PropertyGetterDefinitionNode(),
                    Setter: new PropertySetterDefinitionNode(
                        AccessModifier: AccessModifier.Private)
                ));
            }
        }

        // ── Constructor params for navs — from the COMPLETE signature (ESM) ──
        // EntityStructureMetadata.ConstructorParameters now includes collection navs
        // (IsCollection). This is the single source of truth for the emitted Create(...)
        // signature — no per-consumer re-derivation of cardinality/order here (the
        // callers already read the same bag). Order matches the nav loop above
        // (relationship order), so output is unchanged.
        foreach (var navParam in esm.ConstructorParameters.Where(p => p.IsNavigation)) {
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

        // Defaulted props trail the required scalar + nav params (optional params must
        // come last). Appended here so the Create/ctor signatures carry them as overridable
        // defaults — `create in { DefaultedProp: value }` flows through construction.
        ctorParams.AddRange(defaultedCtorParams);

        // ── Build post-transition notification nodes ──────────────
        Dictionary<string, IReadOnlyList<Node>>? postTransitionNodes = null;
        if (targetSubs is { Count: > 0 }) {
            postTransitionNodes = new Dictionary<string, IReadOnlyList<Node>>(
                StringComparer.Ordinal);
            foreach (var stageGroup in targetSubs.GroupBy(s => s.StageName)) {
                var nodes = new List<Node> {
                    new Invoke(
                        new Member(new ThisReference(),
                            $"Notify{stageGroup.Key}Subscribers"))
                };
                postTransitionNodes[stageGroup.Key] = nodes;
            }
        }

        // ── Actions as void methods ───────────────────────────────
        // Same action name on multiple stages is one C# method that dispatches on
        // CurrentStage. Emitting one method per stage produced illegal duplicate members
        // (FieldService WorkOrder.Cancel on Draft/Scheduled/Blocked).
        AddActionMethods(entity, methods, stageEnumTypeName, postTransitionNodes, domain, metadata);

        // ── Policies as bool methods ──────────────────────────────
        foreach (var policy in entity.Policies) {
            Node? body;
            try {
                body = LowerExpressionToMethodBody(policy.Expression, entity, domain, analysis: metadata);
            }
            catch (NotSupportedException) {
                // Collection quantifiers (any/all/none/count) and other store-dependent
                // expressions cannot be lowered to standalone C# methods yet.
                // Generate a runtime exception so calling code fails loud.
                body = new Block([
                    new ThrowStatement(
                        new New(
                            new NamedTypeReference("NotSupportedException"),
                            new Constant(
                                $"Policy '{policy.Name}' requires store-aware evaluation and cannot be compiled to standalone C.")))
                ]);
            }
            methods.Add(new MethodDefinitionNode(
                policy.Name,
                new PrimitiveTypeReference(PrimType.Boolean),
                Body: body,
                AccessModifier: AccessModifier.Public
            ));
        }

        // ── Target entity: subscription registry ──────────────────
        // One registry field + register method per (stage, subscriber) pair; the notify
        // method calls EVERY subscription's handler for that pair — a subscriber may
        // declare any/all/Each (or multiple Each) reactions on the same relation+stage,
        // each with a quantifier-disambiguated handler name.
        if (targetSubs is { Count: > 0 }) {
            var groups = targetSubs
                .GroupBy(i => (i.StageName, i.SourceEntity.Name))
                .Select(g => g.ToList())
                .ToList();
            foreach (var infos in groups) {
                var srcType = new NamedTypeReference(infos[0].SourceEntity.Name);
                var fieldName = SubscriberRegistryFieldName(infos[0]);
                var paramName = "subscriber";

                fields.Add(new FieldDefinitionNode(
                    fieldName,
                    new OptionalTypeReference(
                        new NamedTypeReference("List",
                            TypeArguments: [srcType])),
                    AccessModifier: AccessModifier.Private
                ));

                var fieldAcc = new Member(new ThisReference(), fieldName);
                var registerBody = new Block([
                    new IfStatement(
                        new Equal(fieldAcc, new Constant(null)),
                        new Block([
                            new Assignment(fieldAcc,
                                new New(
                                    new NamedTypeReference("List",
                                        TypeArguments: [srcType])))
                        ])),
                    new Invoke(
                        new Member(fieldAcc, "Add"),
                        [new Parameter(paramName)])
                ]);
                methods.Add(new MethodDefinitionNode(
                    $"Register{infos[0].SourceEntity.Name}{infos[0].StageName}Subscriber",
                    new TypeReference("void"),
                    Parameters: [new Parameter(paramName, srcType)],
                    Body: registerBody,
                    AccessModifier: AccessModifier.Internal
                ));
            }

            // One Notify{Stage}Subscribers per watched stage, fanning out every
            // (source, stage) registry. Two subscribers on the same target stage
            // (Student + Section on Enrollment.Dropped) used to emit duplicate
            // _droppedSubscribers / NotifyDroppedSubscribers members.
            foreach (var stageGroup in groups.GroupBy(infos => infos[0].StageName)) {
                var notifyNodes = new List<Node>();
                foreach (var infos in stageGroup) {
                    var fieldName = SubscriberRegistryFieldName(infos[0]);
                    var notifyCalls = infos.Select(info => (Node)new Invoke(
                        new Member(new Variable("sub"), handlerNames![info]),
                        info.Subscription.PeerBinding is { Length: > 0 }
                            ? [new ThisReference()]
                            : [])).ToList();
                    notifyNodes.Add(new IfStatement(
                        new NotEqual(
                            new Member(new ThisReference(), fieldName),
                            new Constant(null)),
                        new ForEachLoop(
                            new Variable("sub"),
                            new Member(new ThisReference(), fieldName),
                            new Block(notifyCalls))));
                }
                methods.Add(new MethodDefinitionNode(
                    $"Notify{stageGroup.Key}Subscribers",
                    new TypeReference("void"),
                    Body: new Block(notifyNodes),
                    AccessModifier: AccessModifier.Internal
                ));
            }
        }

        // ── Subscriber entity: subscription handler methods ──────
        if (subscriberSubs is { Count: > 0 }) {
            foreach (var info in subscriberSubs) {
                var handlerName = handlerNames![info];
                var peerBinding = info.Subscription.PeerBinding;
                IReadOnlyList<Parameter>? handlerParams = peerBinding is { Length: > 0 }
                    ? [new Parameter(peerBinding, new NamedTypeReference(info.TargetEntity.Name))]
                    : null;

                // Lower subscription effects into the handler body.
                // Peer path-prefix roots (binder name) resolve via Parameters to the
                // peer method parameter; bare props / this stay on the subscriber.
                var subscriptionEffects = info.Subscription.Effects;
                Node handlerBody;
                if (subscriptionEffects.Count > 0) {
                    IReadOnlyDictionary<string, Node>? peerParams = null;
                    if (peerBinding is { Length: > 0 }) {
                        peerParams = new Dictionary<string, Node>(StringComparer.Ordinal) {
                            [peerBinding] = new Parameter(peerBinding)
                        };
                    }
                    var context = new LoweringContext(
                        new Parameter("entity",
                            new TypeReference(entity.Name)),
                        Parameters: peerParams,
                        Analysis: metadata,
                        UseThisReference: true,
                        Domain: domain,
                        EnumPropertyNames: esm.EnumPropertyNames);
                    var effectPass = new EffectLoweringPass(entity, context);
                    var composite = new CompositeEffect(subscriptionEffects);
                    handlerBody = effectPass.TryLowerVmNode(composite)
                        ?? throw new InvalidOperationException(
                            "Subscription effects could not be lowered to a Syntax AST node.");
                }
                else {
                    handlerBody = new Block([]);
                }

                // Stage-scoped subscriptions (`when` inside a subscriber stage) fire only
                // while the subscriber is in that stage — gate the handler to match the
                // runtime store (NotifyTransition resolves the plan from CurrentStage).
                // Entity-level subscriptions (SubscriberStageName == null) are always active.
                if (info.SubscriberStageName is { Length: > 0 }) {
                    var stageGate = new IfStatement(
                        new NotEqual(
                            new Member(new ThisReference(), "CurrentStage"),
                            new Member(new NamedTypeReference(stageEnumTypeName), info.SubscriberStageName)),
                        new Block([new Return()]));
                    handlerBody = new Block([stageGate, handlerBody]);
                }

                // `when all Rel Stage` fires only when EVERY linked target is in the
                // watched stage (and at least one exists) — the notify call fires per
                // transition, so the set condition must gate the handler body. Mirrors
                // the runtime dispatch (matchedCount == allLinkedTargets.Count; the
                // empty set never fires). Discovery round5 F10. The gate references the
                // target's CurrentStage / stage enum, so it is only emitted when the
                // target actually has stages (a stageless target is rejected at analysis;
                // the guard is defense-in-depth).
                if (info.Subscription.Quantifier == StageSubscriptionQuantifier.All
                    && info.TargetEntity.Stages.Count > 0) {
                    var targetStageEnumName = metadata.GetStructure(info.TargetEntity)
                        ?.StageEnumTypeName ?? $"{info.TargetEntity.Name}Stage";
                    var linkedVar = new Variable("linkedTarget");
                    var matchedVar = new Variable("linkedMatched");
                    var gateLoop = new ForEachLoop(
                        linkedVar,
                        new Member(new ThisReference(), ToPascalCase(info.Relationship.Name)),
                        new Block([
                            new Assignment(matchedVar, new Constant(true)),
                            new IfStatement(
                                new NotEqual(
                                    new Member(linkedVar, "CurrentStage"),
                                    new Member(new NamedTypeReference(targetStageEnumName), info.StageName)),
                                new Block([new Return()]))
                        ]));
                    var emptyCheck = new IfStatement(
                        new Poly.Ast.Nodes.Not(matchedVar),
                        new Block([new Return()]));
                    handlerBody = new Block(
                        [new Assignment(matchedVar, new Constant(false)), gateLoop, emptyCheck, handlerBody],
                        [matchedVar]);
                }

                // `when any Rel Stage` fires once when the linked set becomes non-empty
                // in the watched stage (rising edge). Notify still runs per transition.
                if (info.Subscription.Quantifier == StageSubscriptionQuantifier.Any
                    && info.TargetEntity.Stages.Count > 0) {
                    var targetStageEnumName = metadata.GetStructure(info.TargetEntity)
                        ?.StageEnumTypeName ?? $"{info.TargetEntity.Name}Stage";
                    var linkedVar = new Variable("linkedTarget");
                    var matchedVar = new Variable("linkedMatched");
                    var gateLoop = new ForEachLoop(
                        linkedVar,
                        new Member(new ThisReference(), ToPascalCase(info.Relationship.Name)),
                        new Block([
                            new IfStatement(
                                new Equal(
                                    new Member(linkedVar, "CurrentStage"),
                                    new Member(new NamedTypeReference(targetStageEnumName), info.StageName)),
                                new Block([
                                    new Assignment(matchedVar,
                                        new Syntactic.Add(matchedVar, new Constant(1L)))
                                ]))
                        ]));
                    var notRisingEdge = new IfStatement(
                        new NotEqual(matchedVar, new Constant(1L)),
                        new Block([new Return()]));
                    handlerBody = new Block(
                        [new Assignment(matchedVar, new Constant(0L)), gateLoop, notRisingEdge, handlerBody],
                        [matchedVar]);
                }

                methods.Add(new MethodDefinitionNode(
                    handlerName,
                    new TypeReference("void"),
                    Parameters: handlerParams,
                    Body: handlerBody,
                    AccessModifier: AccessModifier.Internal
                ));
            }
        }

        // ── Stage enum + CurrentStage property ────────────────────
        if (entity.Stages.Count > 0) {
            var enumTypeName = stageEnumTypeName;
            var stageEnumFields = new List<FieldDefinitionNode>();
            for (int si = 0; si < entity.Stages.Count; si++) {
                stageEnumFields.Add(new FieldDefinitionNode(
                    entity.Stages[si].Name,
                    new PrimitiveTypeReference(PrimType.Int32),
                    DefaultValue: new Constant((int)si),
                    AccessModifier: AccessModifier.Public
                ));
            }
            typeDefs.Add(new TypeDefinitionNode(
                enumTypeName,
                Fields: stageEnumFields,
                Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
            ));

            props.Add(new PropertyDefinitionNode(
                "CurrentStage",
                new NamedTypeReference(enumTypeName),
                Getter: new PropertyGetterDefinitionNode(),
                Setter: new PropertySetterDefinitionNode(
                    AccessModifier: AccessModifier.Private)
            ));
        }

        // ── Private constructor + public static Create factory ─────
        List<ConstructorDefinitionNode>? ctors = null;
        if (ctorParams.Count > 0 || entity.Stages.Count > 0) {
            var bodyNodes = new List<Node>();
            bodyNodes.AddRange(ctorAssignments);

            if (entity.Stages.Count > 0) {
                var firstStage = entity.Stages[0];
                bodyNodes.Add(new Assignment(
                    new Member(new ThisReference(), "CurrentStage"),
                    new Member(
                        new NamedTypeReference(stageEnumTypeName),
                        firstStage.Name)));

                // Apply the initial stage's entry effects in the constructor.
                // This ensures properties set by entry effects (e.g. "entry { assign
                // CheckedOutAt to now }" on Loan.Active) are initialized during
                // construction, not just during explicit stage transitions.
                if (firstStage.OnEntryEffects.Count > 0) {
                    var entryCtx = new LoweringContext(
                        new Parameter("entity",
                            new TypeReference(entity.Name)),
                        Analysis: metadata,
                        UseThisReference: true,
                        Domain: domain,
                        EnumPropertyNames: esm.EnumPropertyNames);
                    var entryPass = new EffectLoweringPass(entity, entryCtx);
                    foreach (var entryEffect in firstStage.OnEntryEffects) {
                        var lowered = entryPass.Route(entryEffect);
                        if (lowered is not null)
                            bodyNodes.Add(lowered);
                    }
                }
            }

            // InitializeSubscriptions is called as the last step so that all
            // state (including collections passed from data-store materialization)
            // is set before subscription wiring runs.
            if (subscriberSubs is { Count: > 0 }) {
                bodyNodes.Add(new Invoke(
                    new Member(new ThisReference(), "InitializeSubscriptions")));
            }

            // Private parameterless constructor for EF Core materialization.
            var paramlessBody = new List<Node>();
            foreach (var f in fields) {
                var isOptional = f.FieldType is OptionalTypeReference;
                if (isOptional) continue;
                paramlessBody.Add(new Assignment(
                    new Member(new ThisReference(), f.Name),
                    new New(f.FieldType)));
            }
            ctors = [new ConstructorDefinitionNode(
                Parameters: null,
                Body: new Block(paramlessBody),
                AccessModifier: AccessModifier.Private
            )];

            // Full constructor — only the static Create factory can construct
            // instances with data. EntityFramework uses the parameterless ctor.
            ctors = [.. ctors, new ConstructorDefinitionNode(
                Parameters: ctorParams,
                Body: bodyNodes.Count > 0 ? new Block(bodyNodes) : null,
                AccessModifier: AccessModifier.Private
            )];

            // public static DomainResult<EntityName> Create(args...)
            var createResultType = new NamedTypeReference("DomainResult",
                TypeArguments: [new NamedTypeReference(entity.Name)]);

            // Build constraint validation checks before the constructor call.
            // Only entity properties (not navigations) are validated — navs
            // don't carry constraints in the current model.
            var constraintChecks = BuildCreateConstraintChecks(entity, domain, esm.EntryAssignedPropertyNames);

            // return DomainResult<EntityName>.Success(new EntityName(args...));
            var createSuccessNodes = new List<Node>();
            createSuccessNodes.AddRange(constraintChecks);
            createSuccessNodes.Add(new Return(
                new Invoke(
                    new Member(createResultType, "Success"),
                    [new New(
                        new NamedTypeReference(entity.Name),
                        ctorParams.Select(p => new Parameter(p.Name)).ToArray())])));

            methods.Add(new MethodDefinitionNode(
                "Create",
                createResultType,
                Parameters: ctorParams,
                Body: new Block(createSuccessNodes),
                IsStatic: true,
                AccessModifier: AccessModifier.Public
            ));
        }

        // Store fan-out is DomainEntityInstance.Notify on the runtime path.
        // Generated C# fans out via Notify{Stage}Subscribers; do not emit a
        // no-op Notify(string) (Fleet dogfood).

        // ── InitializeSubscriptions — for post-load subscription wiring ──
        if (subscriberSubs is { Count: > 0 }) {
            var initBody = new List<Node>();
            AddSubscriberRegistrationNodes(subscriberSubs, initBody);
            if (initBody.Count > 0) {
                methods.Add(new MethodDefinitionNode(
                    "InitializeSubscriptions",
                    new TypeReference("void"),
                    Body: new Block(initBody),
                    AccessModifier: AccessModifier.Private
                ));
            }
        }

        AddStoreBindMethods(entity, domain, metadata, methods);

        typeDefs.Add(new TypeDefinitionNode(
            entity.Name,
            Constructors: ctors,
            Properties: props.Count > 0 ? props : null,
            Methods: methods.Count > 0 ? methods : null,
            Fields: fields.Count > 0 ? fields : null,
            Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
        ));

        return typeDefs;
    }
}