using Poly.Analysis;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Effects;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;

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
public sealed class DomainToCSharpExporter {
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
            if (defaultValue is not null) {
                var runtimeExpr = EffectLoweringPass.LowerDefaultExpression(
                    defaultValue.Expression, new NamedTypeReference(prop.Type.TypeName),
                    RuntimeAnalysisCache.Meaning(domain));
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

            // Prop assigned by the initial stage's entry effect: the ctor body already
            // runs those effects (after setting CurrentStage), so it is body-initialized
            // — NOT a ctor param (a param would be dead and written twice, e.g. StartedAt).
            if (entryAssignedProps.Contains(prop.Name))
                continue;

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
            }
            else {
                // Singular nav: property with private setter (constructor param).
                // Navs are optional references (set at link/create time, may be null
                // at EF materialization) — emit nullable so the generated code has
                // no non-nullable-uninitialized warnings (CS8618).
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
                ctorParams.Add(new Parameter(paramName, enumerableType));
                ctorAssignments.Add(new Assignment(
                    new Member(new ThisReference(), fieldName),
                    new New(listType, [new Parameter(paramName)])));
            }
            else {
                // Singular nav (incl. back-reference): param + property assign.
                // Nullable to match the property (navs may be unlinked at creation).
                var pascalName = ToPascalCase(navParam.Name);
                var propRef = new OptionalTypeReference(
                    MapDomainTypeRef(navParam.Type, domain, metadata));
                ctorParams.Add(new Parameter(paramName, propRef));
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
        foreach (var action in entity.Actions)
            AddActionMethod(entity, action, methods, stageEnumTypeName, postTransitionNodes, domain: domain, analysis: metadata);
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions)
                AddActionMethod(entity, action, methods, stageEnumTypeName, postTransitionNodes, stage.Name, domain, metadata);

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
                                $"Policy '{policy.Name}' requires store-aware evaluation and cannot be compiled to standalone C#.")))
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
            foreach (var group in targetSubs.GroupBy(i => (i.StageName, i.SourceEntity.Name))) {
                var infos = group.ToList();
                var srcType = new NamedTypeReference(infos[0].SourceEntity.Name);
                var fieldName = $"_{ToCamelCase(infos[0].StageName)}Subscribers";
                var paramName = "subscriber";

                // private List<TA>? _DamagedSubscribers;
                fields.Add(new FieldDefinitionNode(
                    fieldName,
                    new OptionalTypeReference(
                        new NamedTypeReference("List",
                            TypeArguments: [srcType])),
                    AccessModifier: AccessModifier.Private
                ));

                // internal void RegisterDamagedSubscriber(TA sub) { … }
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

                // internal void NotifyDamagedSubscribers() {
                //     if (_damagedSubscribers != null)
                //         foreach (var sub in _damagedSubscribers) {
                //             sub.WhenAnyPaymentCaptured();
                //             sub.WhenAllPaymentCaptured();
                //             sub.WhenEachPaymentCaptured(this);
                //         }
                // }
                var notifyCalls = infos.Select(info => (Node)new Invoke(
                    new Member(new Variable("sub"), handlerNames![info]),
                    info.Subscription.PeerBinding is { Length: > 0 }
                        ? [new ThisReference()]
                        : [])).ToList();
                var foreachBody = new Block(notifyCalls);
                var notifyBody = new IfStatement(
                    new NotEqual(
                        new Member(new ThisReference(), fieldName),
                        new Constant(null)),
                    new ForEachLoop(
                        new Variable("sub"),
                        new Member(new ThisReference(), fieldName),
                        foreachBody));
                methods.Add(new MethodDefinitionNode(
                    $"Notify{infos[0].StageName}Subscribers",
                    new TypeReference("void"),
                    Body: new Block([notifyBody]),
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
                        LowerStageTransitions: true,
                        Domain: domain,
                        EnumPropertyNames: esm.EnumPropertyNames,
                        Meaning: RuntimeAnalysisCache.Meaning(domain)
                    );
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
                    var matchedVar = new Variable("linkedMatched", new Constant(false));
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
                    handlerBody = new Block([matchedVar, gateLoop, emptyCheck, handlerBody]);
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
                        LowerStageTransitions: false,
                        Domain: domain,
                        EnumPropertyNames: esm.EnumPropertyNames,
                        Meaning: RuntimeAnalysisCache.Meaning(domain)
                    );
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

    /// <summary>
    /// Appends subscription registration statements to the constructor body.
    /// For each subscription on this entity's stage, emits code that registers
    /// this instance as a subscriber on each related target entity.
    /// </summary>
    private static void AddSubscriberRegistrationNodes(
        List<SubscriptionInfo>? subscriberSubs,
        List<Node> bodyNodes) {
        if (subscriberSubs is { Count: > 0 }) {
            foreach (var group in subscriberSubs.GroupBy(s => s.Relationship.Name)) {
                var rel = group.First().Relationship;
                var pascalNavName = ToPascalCase(rel.Name);
                var isMany = rel.Cardinality is RelationshipCardinality.OneToMany
                             or RelationshipCardinality.ManyToMany;

                // Multiple subscriptions on the same relation+stage (any/all/Each) share
                // ONE registry list on the target — register once per stage, or the
                // subscriber is added N times and each handler fires N times.
                var perStage = group.GroupBy(s => s.StageName).Select(g => g.First());

                if (isMany) {
                    foreach (var info in perStage) {
                        var subVarName = "target";
                        bodyNodes.Add(new ForEachLoop(
                            new Variable(subVarName),
                            new Member(new ThisReference(), pascalNavName),
                            new Block([
                                new Invoke(
                                    new Member(
                                        new Variable(subVarName),
                                        $"Register{info.SourceEntity.Name}{info.StageName}Subscriber"),
                                    [new ThisReference()])
                            ])
                        ));
                    }
                }
                else {
                    foreach (var info in perStage) {
                        bodyNodes.Add(new Invoke(
                            new Member(
                                new Member(new ThisReference(), pascalNavName),
                                $"Register{info.SourceEntity.Name}{info.StageName}Subscriber"),
                            [new ThisReference()])
                        );
                    }
                }
            }
        }
    }

    /// <summary>
    /// Generates a <c>Create{NavName}(args)</c> method on the source entity
    /// that constructs the target entity, adds it to the collection field,
    /// and wires subscription registration.
    ///
    /// The method parameters correspond to the target entity's constructor
    /// parameters (regular properties + singular nav properties), minus
    /// the back-reference to the source entity which is auto-wired as <c>this</c>.
    ///
    /// For DSL <c>create in loans { book: book }</c>, this produces:
    /// <code>
    /// public Loan CreateLoans(Book book, string status, ...)
    /// {
    ///     var loan = Loan.Create(book: book, borrower: this, status: status, ...);
    ///     _loans.Add(loan);
    ///     loan.RegisterPatronOverdueSubscriber(this);
    ///     return loan;
    /// }
    /// </code>
    /// </summary>
    private static void AddCreateNavMethod(
        Entity entity,
        Relationship rel,
        Domain domain,
        List<SubscriptionInfo>? subscriberSubs,
        string fieldName,
        List<MethodDefinitionNode> methods,
        INodeMetadataProvider metadata) {
        ArgumentNullException.ThrowIfNull(metadata);

        var pascalName = ToPascalCase(rel.Name);
        var targetTypeName = rel.Target.TypeName;
        var targetType = new NamedTypeReference(targetTypeName);
        var lookup = metadata.GetTypeLookup(domain)
            ?? throw new InvalidOperationException(
                "Domain catalog type lookup is required for CreateNav export.");
        if (!lookup.Types.TryGetValue(targetTypeName, out var resolvedType)
            || resolvedType is not Entity targetEntity)
            return;

        var methodName = $"Create{pascalName}";

        // Collect all target entity constructor-level parameters from the COMPLETE
        // signature (EntityStructureMetadata now includes collection navs):
        // regular entity properties (minus defaults) + navs (singular + collection)
        // in relationship order. One source of truth — no per-consumer re-scan of
        // domain.Relationships (the CS7036 class of bugs).
        var methodParams = new List<Parameter>();
        var createArgs = new List<Node>();
        var parameterMetadata = GetConstructorParameters(targetEntity, metadata);

        // Cross-entity auto-wire back-ref: exactly one singular nav on the target
        // pointing back to the source → wired with `this`, excluded from the factory
        // signature. (Self-relationship back-refs are already excluded via IsBackReference.)
        var autoWireBackRef = FindAutoWireBackReference(targetEntity, entity.Name);

        foreach (var parameter in parameterMetadata) {
            if (parameter.IsNavigation && parameter.IsBackReference) {
                createArgs.Add(new ThisReference());
                continue;
            }

            if (autoWireBackRef is not null
                && string.Equals(parameter.Name, autoWireBackRef.Name, StringComparison.Ordinal)) {
                createArgs.Add(new ThisReference());
                continue;
            }

            if (parameter.IsCollection) {
                // Collection nav: ctor param is IEnumerable<T>; the CreateNav factory
                // starts every child with an empty collection.
                createArgs.Add(new New(
                    new NamedTypeReference("List",
                        TypeArguments: [new NamedTypeReference(parameter.Type.TypeName)])));
                continue;
            }

            var paramName = ToCamelCase(parameter.Name);
            var mapped = MapDomainTypeRef(parameter.Type, domain, metadata);
            // Nav params are optional references (unbound initializers pass null) —
            // emit nullable to match the property/ctor and avoid CS8625.
            var propRef = parameter.IsNavigation
                ? (Node)new OptionalTypeReference(mapped)
                : mapped;
            methodParams.Add(new Parameter(paramName, propRef));
            createArgs.Add(new Parameter(paramName));
        }

        // NOTE: properties with DefaultValueConstraint are NOT included in
        // the Target.Create() parameter list — they are set directly in the
        // constructor body from their default expression. Do NOT append them
        // to createArgs here; the Create factory already handles them.

        // Defaulted props of the target become TRAILING optional method params (C#
        // optional params must be last) and are forwarded to Target.Create — so a
        // `create in { DefaultedProp: value }` override flows through construction.
        foreach (var prop in targetEntity.Properties.OrderBy(p => p.Name)) {
            var defaultConstraint = prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault();
            if (defaultConstraint is null) continue;

            var paramName = ToCamelCase(prop.Name);
            var mapped = MapDomainTypeRef(prop.Type, domain, metadata);
            var runtimeExpr = EffectLoweringPass.LowerDefaultExpression(
                defaultConstraint.Expression, new NamedTypeReference(prop.Type.TypeName),
                RuntimeAnalysisCache.Meaning(domain));
            if (runtimeExpr is not null) {
                methodParams.Add(new Parameter(paramName,
                    new OptionalTypeReference(mapped),
                    DefaultValue: new Constant(null)));
            }
            else {
                methodParams.Add(new Parameter(paramName, mapped,
                    DefaultValue: LowerDefaultConstantNode(defaultConstraint, prop, domain, metadata)));
            }
            createArgs.Add(new Parameter(paramName));
        }

        var bodyNodes = new List<Node>();
        var localResultName = $"{ToCamelCase(targetTypeName)}Result";
        var localName = ToCamelCase(targetTypeName);

        // var loanResult = Loan.Create(book: book, borrower: this, ...);
        bodyNodes.Add(new Variable(localResultName,
            new Invoke(
                new Member(targetType, "Create"),
                [.. createArgs])));

        // Unwrap: the Create factory now returns DomainResult<T>, and it may
        // reject invalid inputs via constraint checks. Since CreateNav methods
        // are only called from action bodies with controlled defaults, a
        // failure here is a programmer error — assert fast.
        bodyNodes.Add(new IfStatement(
            new Syntactic.Not(
                new Member(
                    new Variable(localResultName), "IsSuccess")),
            new Block([
                new ThrowStatement(
                    new New(
                        new NamedTypeReference("InvalidOperationException"),
                        new Member(
                            new Variable(localResultName), "ErrorMessage")))
            ])));

        // var loan = loanResult.Value;
        bodyNodes.Add(new Variable(localName,
            new Member(
                new Variable(localResultName), "Value")));

        // _loans.Add(loan);
        bodyNodes.Add(new Invoke(
            new Member(
                new Member(new ThisReference(), fieldName), "Add"),
            [new Variable(localName)]));

        // Subscription registration: loan.RegisterPatronOverdueSubscriber(this)
        // One registration per stage — multiple subscriptions on the same relation+stage
        // (any/all/Each) share the target's single registry list.
        if (subscriberSubs is { Count: > 0 }) {
            foreach (var info in subscriberSubs
                .Where(i => string.Equals(i.Relationship.Name, rel.Name, StringComparison.Ordinal))
                .GroupBy(s => s.StageName)
                .Select(g => g.First())) {
                bodyNodes.Add(new Invoke(
                    new Member(
                        new Variable(localName),
                        $"Register{info.SourceEntity.Name}{info.StageName}Subscriber"),
                    [new ThisReference()]));
            }
        }

        // return loan;
        bodyNodes.Add(new Return(new Variable(localName)));

        methods.Add(new MethodDefinitionNode(
            methodName,
            targetType,
            Parameters: methodParams.Count > 0 ? methodParams : null,
            Body: new Block(bodyNodes),
            AccessModifier: AccessModifier.Private
        ));
    }

    private static IReadOnlyList<ConstructorParameterOrder> GetConstructorParameters(
        Entity targetEntity,
        INodeMetadataProvider analysis) {
        if (analysis.GetStructure(targetEntity) is EntityStructureMetadata esm)
            return esm.ConstructorParameters;

        throw new InvalidOperationException(
            $"EntityStructureMetadata is required for constructor ordering on entity '{targetEntity.Name}'.");
    }

    /// <summary>
    /// Builds constraint-validation guard clauses for the <c>Create</c> factory method.
    /// Each constraint on a constructor-parameter property produces an early-return
    /// guard: <c>if (violation) return DomainResult&lt;T&gt;.Failure("'Prop' ...");</c>
    ///
    /// Only entity properties (not navigation properties) are validated — navs do not
    /// carry constraints in the current domain model. Defaulted props ARE constructor
    /// params (optional overrides), so their constraints are validated too; only
    /// entry-assigned props (body-initialized, never ctor params) are skipped.
    /// </summary>
    private static List<Node> BuildCreateConstraintChecks(
        Entity entity, Domain? domain, IReadOnlySet<string> entryAssignedProps) {

        var checks = new List<Node>();
        var entityTypeRef = new NamedTypeReference(entity.Name);
        var resultType = new NamedTypeReference("DomainResult",
            TypeArguments: [entityTypeRef]);

        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
            // Entry-assigned props are body-initialized by the ctor's stage-entry effects
            // — not constructor params, so no guard to attach.
            if (entryAssignedProps.Contains(prop.Name)) continue;

            var paramName = ToCamelCase(prop.Name);
            var paramRef = new Parameter(paramName);
            var isText = string.Equals(prop.Type.TypeName, "Text", StringComparison.Ordinal)
                      || string.Equals(prop.Type.TypeName, "String", StringComparison.Ordinal);
            var isNumber = string.Equals(prop.Type.TypeName, "Number", StringComparison.Ordinal)
                        || string.Equals(prop.Type.TypeName, "Int", StringComparison.Ordinal);

            Node Failure(string msg) => new Return(
                new Invoke(
                    new Member(resultType, "Failure"),
                    new Constant(msg)));

            foreach (var constraint in prop.Constraints) {
                switch (constraint) {
                    case RequiredConstraint:
                        // Required: skip for value types (Number, Boolean,
                        // DateTime, etc.) — they can never be null at runtime.
                        // Only Text/String and entity reference types benefit
                        // from runtime required checks.
                        if (isText) {
                            checks.Add(new IfStatement(
                                new Invoke(
                                    new Member(
                                        new NamedTypeReference("string"),
                                        "IsNullOrEmpty"),
                                    [paramRef]),
                                new Block([Failure(
                                    $"'{prop.Name}' is required.")])));
                        }
                        else if (IsNullableDomainType(prop.Type.TypeName, domain)) {
                            checks.Add(new IfStatement(
                                new Equal(paramRef, new Constant(null)),
                                new Block([Failure(
                                    $"'{prop.Name}' is required.")])));
                        }
                        break;

                    case RangeConstraint r:
                        if (isNumber && r.Minimum is not null) {
                            var minVal = ConvertToConstant(r.Minimum);
                            if (minVal is not null) {
                                checks.Add(new IfStatement(
                                    new LessThan(paramRef, minVal),
                                    new Block([Failure(
                                        $"'{prop.Name}' must be >= {FormatConstraintValue(r.Minimum)}.")])));
                            }
                        }
                        if (isNumber && r.Maximum is not null) {
                            var maxVal = ConvertToConstant(r.Maximum);
                            if (maxVal is not null) {
                                checks.Add(new IfStatement(
                                    new GreaterThan(paramRef, maxVal),
                                    new Block([Failure(
                                        $"'{prop.Name}' must be <= {FormatConstraintValue(r.Maximum)}.")])));
                            }
                        }
                        break;

                    case LengthConstraint l:
                        if (isText) {
                            var lenAccess = new Member(paramRef, "Length");
                            if (l.MinLength > 0) {
                                checks.Add(new IfStatement(
                                    new LessThan(lenAccess,
                                        new Constant((long)l.MinLength)),
                                    new Block([Failure(
                                        $"'{prop.Name}' must be at least {l.MinLength} characters.")])));
                            }
                            if (l.MaxLength < int.MaxValue) {
                                checks.Add(new IfStatement(
                                    new GreaterThan(lenAccess,
                                        new Constant((long)l.MaxLength)),
                                    new Block([Failure(
                                        $"'{prop.Name}' must be at most {l.MaxLength} characters.")])));
                            }
                        }
                        break;

                    case PatternConstraint p:
                        if (isText) {
                            checks.Add(new IfStatement(
                                new Syntactic.Not(
                                    new Invoke(
                                        new Member(
                                            new NamedTypeReference(
                                                "System.Text.RegularExpressions.Regex"),
                                            "IsMatch"),
                                        [paramRef,
                                         new Constant(p.Pattern)])),
                                new Block([Failure(
                                    $"'{prop.Name}' does not match the required pattern.")])));
                        }
                        break;

                    case EqualityConstraint eq:
                        if (eq.ExpectedValue is not null) {
                            checks.Add(new IfStatement(
                                new NotEqual(paramRef,
                                    new Constant(eq.ExpectedValue)),
                                new Block([Failure(
                                    $"'{prop.Name}' must equal {eq.ExpectedValue}.")])));
                        }
                        break;

                        // DefaultValueConstraint and UniqueConstraint
                        // are not validated at factory time:
                        //   • Default → already handled (only non-default props are params)
                        //   • Unique  → requires store awareness
                }
            }
        }

        return checks;
    }

    /// <summary>Converts a constraint value object to a Syntax Constant.</summary>
    private static Constant? ConvertToConstant(object? value) {
        if (value is null) return null;
        if (value is long l) return new Constant(l);
        if (value is int i) return new Constant((long)i);
        if (value is double d) return d == Math.Floor(d)
            ? new Constant((long)d)
            : new Constant(d);
        if (value is decimal m) return new Constant((double)m);
        if (value is string s) return new Constant(s);
        if (value is bool b) return new Constant(b);
        return new Constant(value?.ToString());
    }

    /// <summary>Formats a constraint boundary value for error messages.</summary>
    private static string FormatConstraintValue(object? value) => value switch {
        null => "?",
        double d => d == Math.Floor(d) ? d.ToString("F0") : d.ToString("G"),
        _ => value.ToString() ?? "?"
    };

    /// <summary>
    /// Returns true if the domain type name maps to a nullable CLR type
    /// (string or reference type), meaning null-check validation applies.
    /// Value types (Number, Boolean, DateTime, etc.) always have a value
    /// and cannot be null at runtime.
    /// </summary>
    private static bool IsNullableDomainType(string typeName, Domain? domain = null) {
        // Enum types are C# value types — never null at runtime.
        if (domain is not null
            && domain.Types.OfType<EnumType>().Any(e => string.Equals(e.Name, typeName, StringComparison.Ordinal)))
            return false;
        return typeName switch {
            "Text" or "String" => true,  // handled separately via IsNullOrEmpty
            "Number" or "Int" or "Int64" or "Int32" => false,
            "Boolean" or "Bool" => false,
            "DateTime" or "Timestamp" => false,
            "Date" or "DateOnly" => false,
            "Time" or "TimeOnly" => false,
            "Duration" or "TimeSpan" => false,
            "Decimal" => false,
            "Float" or "Double" => false,
            "Guid" or "Uuid" => false,
            _ => true, // entity reference types (Book, Patron, etc.) are nullable
        };
    }

    // ── Action method builder ───────────────────────────────────

    private static void AddActionMethod(Entity entity, Action action,
        List<MethodDefinitionNode> methods, string? stageEnumTypeName = null,
        IReadOnlyDictionary<string, IReadOnlyList<Node>>? postTransitionNodes = null,
        string? sourceStageName = null, Domain? domain = null,
        INodeMetadataProvider? analysis = null) {
        var paramNames = new HashSet<string>(
            action.Parameters.Select(p => p.Name), StringComparer.Ordinal);
        var effectsBody = LowerActionToMethodBody(entity, action, paramNames, stageEnumTypeName,
            postTransitionNodes, sourceStageName, domain, analysis);

        // pack-3c-3: a bound action is a call. The exported method invokes the contract
        // adapter (emitted per bound contract), never a bodyless local implementation —
        // the binding must not be dropped by export.
        effectsBody = PrependAdapterInvocation(domain, action, effectsBody);

        // Build the full method body: require guards first, then effects
        var isVoid = action.Result is not { Members.Count: > 0 };
        var body = BuildActionBodyWithGuards(action, entity, effectsBody, domain,
            sourceStageName, stageEnumTypeName, isVoid, analysis);

        // All actions return DomainResult (void) or DomainResult<T> (typed).
        // This lets callers pattern-match on IsSuccess without exceptions.
        Node returnType;
        if (isVoid) {
            returnType = new NamedTypeReference("DomainResult");
        }
        else {
            var innerType = MapDomainTypeRef(action.Result.Members[0].Type, domain, analysis);
            returnType = new NamedTypeReference("DomainResult",
                TypeArguments: [innerType]);
        }

        methods.Add(new MethodDefinitionNode(
            action.Name,
            returnType,
            Parameters: action.Parameters
                .Select(p => new Parameter(p.Name, MapDomainTypeRef(p.Type, domain, analysis)))
                .ToList(),
            Body: body,
            AccessModifier: AccessModifier.Public
        ));
    }

    /// <summary>
    /// Builds a method body with require gate guard clauses prepended before the effects.
    /// Always references entity-level policy methods (<c>bool PolicyName()</c>), not synthetic
    /// action-scoped policy copies. For <c>require not PolicyName</c> (which the parser encodes
    /// as a synthetic <c>not_PolicyName</c> policy on the action), we strip the prefix and
    /// invert the condition so the generated code calls the real entity-level method.
    ///
    /// All guards return <c>DomainResult.Failure("message")</c> (void) or
    /// <c>DomainResult&lt;T&gt;.Failure("message")</c> (typed) instead of throwing
    /// or returning default — enabling clean pattern matching at call sites.
    ///
    /// Generated patterns:
    ///   Stage-scoped:          <c>return DomainResult.Failure("'CheckOut' is not valid for stage 'Active'...");</c>
    ///   <c>require AtLimit</c>     → <c>return DomainResult.Failure("Policy 'AtLimit' not satisfied.");</c>
    ///   <c>require not AtLimit</c> → <c>return DomainResult.Failure("Policy 'AtLimit' not satisfied.");</c>
    ///
    /// Returns a <see cref="Block"/> with at least an empty body — never null —
    /// so the C# generator emits <c>{ }</c> rather than an invalid semicolon.
    /// </summary>
    private static Block BuildActionBodyWithGuards(
        Action action, Entity entity, Node? effectsBody,
        Domain? domain = null, string? sourceStageName = null, string? stageEnumTypeName = null,
        bool isVoid = true, INodeMetadataProvider? analysis = null) {

        // Build the DomainResult type reference for failure/success returns
        Node actionResultType;
        Node resultTypeRef;
        if (isVoid) {
            actionResultType = new NamedTypeReference("DomainResult");
            resultTypeRef = new NamedTypeReference("DomainResult");
        }
        else {
            resultTypeRef = MapDomainTypeRef(action.Result!.Members[0].Type, domain, analysis);
            actionResultType = new NamedTypeReference("DomainResult",
                TypeArguments: [resultTypeRef]);
        }

        // Helper: DomainResult[<T>].Failure("message")
        Node FailureReturn(string message) => new Return(
            new Invoke(
                new Member(actionResultType, "Failure"),
                new Constant(message)));

        // Collect all nodes: require guards first, then effects
        var nodes = new List<Node>();

        // Emit stage guard for stage-scoped actions.
        // Returns DomainResult[<T>].Failure("'CheckOut' requires stage 'Active' on entity 'Patron'.")
        if (sourceStageName is not null && stageEnumTypeName is not null) {
            nodes.Add(new IfStatement(
                new NotEqual(
                    new Member(new ThisReference(), "CurrentStage"),
                    new Member(
                        new NamedTypeReference(stageEnumTypeName),
                        sourceStageName)),
                new Block([
                    FailureReturn(
                        $"'{action.Name}' requires stage '{sourceStageName}' on entity '{entity.Name}'.")
                ])));
        }

        // Emit require guard clauses referencing entity-level policy methods
        // Returns DomainResult[<T>].Failure("'CheckOut' blocked by policy 'AtLimit'.")
        foreach (var policy in action.Policies) {
            if (policy.Name.StartsWith("not_", StringComparison.Ordinal)) {
                var realName = policy.Name.Substring(4);
                var guardCall = new Invoke(
                    new Member(new ThisReference(), realName));
                nodes.Add(new IfStatement(
                    guardCall,
                    new Block([FailureReturn(
                        $"'{action.Name}' blocked by policy '{realName}'.")])));
            }
            else {
                var guardCall = new Invoke(
                    new Member(new ThisReference(), policy.Name));
                nodes.Add(new IfStatement(
                    new Syntactic.Not(guardCall),
                    new Block([FailureReturn(
                        $"'{action.Name}' blocked by policy '{policy.Name}'.")])));
            }
        }

        // Emit entity-level policy guards — the runtime treats every entity policy as an
        // always-on guard on every action invocation (DomainEntityInstance.InvokeAction),
        // skipping any policy the action inverts via `require not PolicyName`. Without
        // this the export silently ran actions the runtime would block (contract parity).
        foreach (var policy in entity.Policies) {
            if (action.Policies.Any(p => string.Equals(p.Name, $"not_{policy.Name}", StringComparison.Ordinal)))
                continue;
            nodes.Add(new IfStatement(
                new Syntactic.Not(new Invoke(new Member(new ThisReference(), policy.Name))),
                new Block([FailureReturn(
                    $"'{action.Name}' blocked by policy '{policy.Name}'.")])));
        }

        // Append the effects body
        if (effectsBody is Block block) {
            nodes.AddRange(block.Nodes);
        }
        else if (effectsBody is not null) {
            nodes.Add(effectsBody);
        }

        if (isVoid) {
            // Void actions end with return DomainResult.Success(); — but not when the
            // body already ended in a throw (quantified-invoke fail-loud), which would
            // make the return unreachable (CS0162).
            if (nodes.Count == 0 || nodes[^1] is not ThrowStatement) {
                nodes.Add(new Return(
                    new Invoke(
                        new Member(
                            new NamedTypeReference("DomainResult"), "Success"))));
            }
        }
        else {
            // Non-void actions: wrap the last effect node in DomainResult<T>.Success(value).
            // If the last effect produces a value (Assignment, Invoke, New, etc.),
            // wrap it: return DomainResult<T>.Success(expr).
            // If the last effect is already a Return, leave it.
            // If there are no effects (body was empty), emit a structural error.
            if (nodes.Count > 0) {
                var lastIdx = nodes.Count - 1;
                var last = nodes[lastIdx];

                if (last is Return) {
                    // Already wrapped — leave as-is.
                }
                else if (last is Syntactic.Assignment or Syntactic.Invoke
                         or Syntactic.Member or Syntactic.Constant
                         or Syntactic.New or Syntactic.UnaryMinus
                         or Syntactic.Not or Syntactic.Add or Syntactic.Subtract
                         or Syntactic.Multiply or Syntactic.Divide) {
                    nodes[lastIdx] = new Return(
                        new Invoke(
                            new Member(actionResultType, "Success"),
                            [last]));
                }
                else if (last is Variable { Value: not null } v) {
                    // var x = expr → return DomainResult<T>.Success(expr)
                    nodes[lastIdx] = new Return(
                        new Invoke(
                            new Member(actionResultType, "Success"),
                            [v.Value]));
                }
                else {
                    // Non-returnable last node — structural error (still throw)
                    nodes.Add(new ThrowStatement(
                        new New(
                            new NamedTypeReference("NotSupportedException"),
                            new Constant(
                                $"Action '{action.Name}' has return type but its last effect " +
                                $"does not produce a value. Use an 'assign' statement as the " +
                                $"final effect, or remove the -> return type declaration."))));
                }
            }
            else {
                // Empty action body with declared return type — structural error
                nodes.Add(new ThrowStatement(
                    new New(
                        new NamedTypeReference("NotSupportedException"),
                        new Constant(
                            $"Action '{action.Name}' has return type but has no effects."))));
            }
        }

        // Block is a sequence; an empty action body is an empty block.
        return new Block(nodes);
    }

    // ── Lowering helpers ────────────────────────────────────────

    /// <summary>
    /// When <paramref name="action"/> is bound to a contract endpoint, prepends the adapter
    /// invocation (<c>{Contract}Adapters.{Endpoint}({param})</c>) to the effects body. The
    /// binding is exported as a call into the emitted adapter (which throws until an
    /// in-process adapter is registered) — never dropped, never a silent no-op. Unknown
    /// contract/endpoint/binding are left untouched: analysis already rejected them, and the
    /// projection never second-guesses a valid model.
    /// </summary>
    private static Node? PrependAdapterInvocation(Domain? domain, Action action, Node? effectsBody) {
        if (domain is null) return effectsBody;
        var binding = domain.ContractBindings.FirstOrDefault(b =>
            string.Equals(b.ActionName, action.Name, StringComparison.Ordinal));
        if (binding is null) return effectsBody;
        var contract = domain.ImportedContracts.FirstOrDefault(c =>
            string.Equals(c.Name, binding.ContractName, StringComparison.Ordinal));
        if (contract is null) return effectsBody;
        var endpoint = contract.Endpoints.FirstOrDefault(e =>
            string.Equals(e.Name, binding.EndpointName, StringComparison.Ordinal));
        if (endpoint is null) return effectsBody;

        var call = new Invoke(
            new Member(new TypeReference($"{contract.Name}Adapters"), endpoint.Name),
            new Variable(binding.LocalParameterName));
        return effectsBody is Block block
            ? new Block([call, .. block.Nodes])
            : new Block([call]);
    }

    /// <summary>
    /// Builds the fail-closed adapter class for a contract with at least one bound endpoint.
    /// One static method per bound endpoint; each throws <c>NotImplementedException</c> at
    /// runtime until an in-process adapter is registered. There is no second parse of the
    /// child domain — the produced contract endpoint has no callable in the exported root,
    /// so an unimplemented binding fails loud instead of silently succeeding.
    /// </summary>
    internal static TypeDefinitionNode BuildContractAdapterTypeDef(
        ImportedContract contract, IReadOnlyList<ContractEndpoint> boundEndpoints) {
        var methods = new List<MethodDefinitionNode>();
        foreach (var endpoint in boundEndpoints) {
            var payload = new NamedTypeReference(
                DomainTypeMapping.ToClrTypeName(endpoint.PayloadType.TypeName));
            methods.Add(new MethodDefinitionNode(
                endpoint.Name,
                new NamedTypeReference("void"),
                Parameters: [new Parameter("request", payload)],
                Body: new Block([
                    new ThrowStatement(
                        new New(
                            new NamedTypeReference("NotImplementedException"),
                            new Constant(
                                $"Contract endpoint '{contract.Name}.{endpoint.Name}' has no implementation. " +
                                "The export emits a fail-closed adapter; register an in-process adapter to serve bound calls."))),
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public
            ));
        }

        return new TypeDefinitionNode(
            $"{contract.Name}Adapters",
            Constructors: [
                new ConstructorDefinitionNode(
                    Parameters: [],
                    Body: new Block([]),
                    AccessModifier: AccessModifier.Private),
            ],
            Methods: methods,
            Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
        );
    }

    internal static Node? LowerActionToMethodBody(
        Entity entity, Action action,
        HashSet<string>? paramNames = null, string? stageEnumTypeName = null,
        IReadOnlyDictionary<string, IReadOnlyList<Node>>? postTransitionNodes = null,
        string? sourceStageName = null, Domain? domain = null,
        INodeMetadataProvider? analysis = null) {
        if (action.Effects.Count == 0) return null;
        var enumProps = GetEnumPropertyNames(entity, domain, analysis);
        var context = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            UseThisReference: true,
            ActionParameterNames: paramNames,
            LowerStageTransitions: true,
            StageEnumTypeName: stageEnumTypeName,
            PostTransitionNodes: postTransitionNodes,
            SourceStageName: sourceStageName,
            Domain: domain,
            EnumPropertyNames: enumProps,
            Meaning: RuntimeAnalysisCache.Meaning(domain)
        );
        var effectPass = new EffectLoweringPass(entity, context);
        var composite = new CompositeEffect(action.Effects);
        return effectPass.TryLowerVmNode(composite);
    }

    internal static Node? LowerExpressionToMethodBody(
        DomainExpression expr, Entity entity, Domain? domain = null,
        INodeMetadataProvider? analysis = null) {
        var enumProps = GetEnumPropertyNames(entity, domain, analysis);
        var context = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            UseThisReference: true,
            EnumPropertyNames: enumProps,
            NavigationNameResolver: EffectLoweringPass.BuildNavigationNameResolver(entity, domain, analysis),
            IsCollectionNavigation: EffectLoweringPass.BuildIsCollectionNavigation(entity, domain, analysis),
            PropertyTypeResolver: EffectLoweringPass.BuildPropertyTypeResolver(entity),
            Meaning: RuntimeAnalysisCache.Meaning(domain)
        );
        var pass = new DomainExpressionLoweringPass(context);
        var lowered = pass.Lower(expr, new Parameter("entity"));
        return lowered is not null
            ? new Block([new Return(lowered)])
            : null;
    }

    internal static bool TryResolveEnumType(Domain? domain, INodeMetadataProvider? analysis, string typeName, out EnumType? enumType) {
        enumType = null;

        // Catalog primary when analysis present.
        if (analysis is not null) {
            var lookup = analysis.GetTypeLookup(domain);
            if (lookup is not null
                && lookup.Types.TryGetValue(typeName, out var resolvedType)
                && resolvedType is EnumType resolvedEnum) {
                enumType = resolvedEnum;
                return true;
            }
            // Analysis present: fail closed (no domain tree rescan).
            return false;
        }

        // Null-analysis residual for non-product/test callers only.
        if (domain is not null) {
            enumType = domain.Types.OfType<EnumType>()
                .FirstOrDefault(e => string.Equals(e.Name, typeName, StringComparison.Ordinal));
            return enumType is not null;
        }

        return false;
    }

    /// <summary>
    /// Builds a map from property name → enum type name for all properties of
    /// <paramref name="entity"/> whose type resolves to an <see cref="EnumType"/>.
    /// Used by the expression lowering pass to emit qualified enum member access.
    /// Catalog-only when <paramref name="analysis"/> is present.
    /// </summary>
    /// <summary>
    /// Returns the entity's enum-typed property map (property name → enum type name),
    /// preferring the published <see cref="EntityStructureMetadata.EnumPropertyNames"/>
    /// bag and falling back to a catalog re-scan only for the null-analysis path.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? GetEnumPropertyNames(
        Entity entity, Domain? domain, INodeMetadataProvider? analysis) {
        if (analysis is not null
            && analysis.GetStructure(entity) is { EnumPropertyNames: not null } esm)
            return esm.EnumPropertyNames;
        return domain is not null ? BuildEnumPropertyNames(entity, domain, analysis) : null;
    }

    internal static Dictionary<string, string>? BuildEnumPropertyNames(
        Entity entity, Domain domain, INodeMetadataProvider? analysis = null) {
        Dictionary<string, string>? map = null;
        IReadOnlyDictionary<string, DomainType> types;
        if (analysis is not null) {
            var lookup = analysis.GetTypeLookup(domain)
                ?? throw new InvalidOperationException(
                    "Domain catalog type lookup is required for enum property mapping when analysis is present.");
            types = lookup.Types;
        }
        else {
            types = domain.Types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        }

        foreach (var prop in entity.Properties) {
            if (types.TryGetValue(prop.Type.TypeName, out var resolved)
                && resolved is EnumType) {
                (map ??= new(StringComparer.Ordinal))[prop.Name] = prop.Type.TypeName;
            }
        }
        return map;
    }

    // ── Type mapping ────────────────────────────────────────────

    internal static Node MapDomainTypeRef(DomainTypeReference domainType,
        Domain? domain = null, INodeMetadataProvider? analysis = null) {
        var typeName = domainType.TypeName;

        // Enum types and entity references both emit NamedTypeReference(typeName),
        // so with analysis present the catalog is the single source of truth and no
        // tree scan is performed (amu-w3-1). Only null-analysis residuals rescan.
        if (analysis is null && domain is not null) {
            var enumType = domain.Types.OfType<EnumType>()
                .FirstOrDefault(e => string.Equals(e.Name, typeName, StringComparison.Ordinal));
            if (enumType is not null)
                return new NamedTypeReference(typeName);
        }

        return typeName switch {
            "Text" => new PrimitiveTypeReference(PrimType.String),
            "Number" or "Int" => new PrimitiveTypeReference(PrimType.Int64),
            "Boolean" or "Bool" => new PrimitiveTypeReference(PrimType.Boolean),
            "DateTime" or "Timestamp" => new PrimitiveTypeReference(PrimType.DateTime),
            "Date" or "DateOnly" => new PrimitiveTypeReference(PrimType.DateOnly),
            "Time" or "TimeOnly" => new PrimitiveTypeReference(PrimType.TimeOnly),
            "Duration" or "TimeSpan" => new PrimitiveTypeReference(PrimType.TimeSpan),
            "Uuid" or "Guid" => new PrimitiveTypeReference(PrimType.Guid),
            "Decimal" => new PrimitiveTypeReference(PrimType.Decimal),
            "Float" or "Double" => new PrimitiveTypeReference(PrimType.Float64),
            _ => new NamedTypeReference(typeName)
        };
    }

    // ── DomainResult infrastructure type builders ───────────────

    /// <summary>
    /// Builds the <c>DomainResult</c> record struct: a discrimated-union-like
    /// result type for void actions. Methods return <c>DomainResult.Success()</c>
    /// or <c>DomainResult.Failure(message)</c> instead of throwing or returning void.
    /// Consumers switch on <see cref="IsSuccess"/> to handle success/failure.
    /// </summary>
    internal static TypeDefinitionNode BuildDomainResultTypeDef() {
        // public readonly record struct DomainResult
        // {
        //     public bool IsSuccess { get; }
        //     public string? ErrorMessage { get; }
        //
        //     private DomainResult(bool isSuccess, string? errorMessage)
        //     {
        //         IsSuccess = isSuccess;
        //         ErrorMessage = errorMessage;
        //     }
        //
        //     public static DomainResult Success() => new(true, null);
        //     public static DomainResult Failure(string message) => new(false, message);
        // }

        var props = new List<PropertyDefinitionNode>
        {
            new("IsSuccess",
                new PrimitiveTypeReference(PrimType.Boolean),
                Getter: new PropertyGetterDefinitionNode()),
            new("ErrorMessage",
                new OptionalTypeReference(
                    new PrimitiveTypeReference(PrimType.String)),
                Getter: new PropertyGetterDefinitionNode()),
        };

        var ctor = new ConstructorDefinitionNode(
            Parameters: [
                new Parameter("isSuccess",
                    new PrimitiveTypeReference(PrimType.Boolean)),
                new Parameter("errorMessage",
                    new OptionalTypeReference(
                        new PrimitiveTypeReference(PrimType.String))),
            ],
            Body: new Block([
                new Assignment(
                    new Member(new ThisReference(), "IsSuccess"),
                    new Parameter("isSuccess")),
                new Assignment(
                    new Member(new ThisReference(), "ErrorMessage"),
                    new Parameter("errorMessage")),
            ]),
            AccessModifier: AccessModifier.Private
        );

        var methods = new List<MethodDefinitionNode>
        {
            new("Success",
                new NamedTypeReference("DomainResult"),
                Body: new Block([
                    new Return(
                        new New(
                            new NamedTypeReference("DomainResult"),
                            new Constant(true),
                            new Constant(null)))
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public),
            new("Failure",
                new NamedTypeReference("DomainResult"),
                Parameters: [
                    new Parameter("message",
                        new PrimitiveTypeReference(PrimType.String))
                ],
                Body: new Block([
                    new Return(
                        new New(
                            new NamedTypeReference("DomainResult"),
                            new Constant(false),
                            new Parameter("message")))
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public),
        };

        return new TypeDefinitionNode(
            "DomainResult",
            Properties: props,
            Constructors: [ctor],
            Methods: methods,
            Semantics: Syntactic.TypeDefinitionSemantics.ImmutableValue
        );
    }

    internal static TypeDefinitionNode BuildValueTypeTypeDef(
        ValueType valueType, Domain domain, INodeMetadataProvider metadata) {
        var props = new List<PropertyDefinitionNode>();
        foreach (var prop in valueType.Properties) {
            var propRef = MapDomainTypeRef(prop.Type, domain, metadata);
            props.Add(new PropertyDefinitionNode(
                prop.Name, propRef,
                Getter: new PropertyGetterDefinitionNode(),
                Setter: new PropertySetterDefinitionNode(),
                Initializer: IsNonNullableReferenceScalar(propRef)
                    ? new Syntactic.PropertyInitializerDefinitionNode(
                        new Syntactic.NullForgiving(new Syntactic.Default()))
                    : null
            ));
        }
        return new TypeDefinitionNode(
            valueType.Name,
            Properties: props,
            Semantics: Syntactic.TypeDefinitionSemantics.ImmutableValue
        );
    }

    /// <summary>
    /// Builds the <c>DomainResult&lt;T&gt;</c> generic record struct: a typed result
    /// for non-void actions. Returns <c>DomainResult&lt;T&gt;.Success(value)</c> or
    /// <c>DomainResult&lt;T&gt;.Failure(message)</c>.
    /// </summary>
    internal static TypeDefinitionNode BuildDomainResultGenericTypeDef() {
        // public readonly record struct DomainResult<T>
        // {
        //     public bool IsSuccess { get; }
        //     public T Value { get; }
        //     public string? ErrorMessage { get; }
        //
        //     private DomainResult(bool isSuccess, T value, string? errorMessage)
        //     {
        //         IsSuccess = isSuccess;
        //         Value = value;
        //         ErrorMessage = errorMessage;
        //     }
        //
        //     public static DomainResult<T> Success(T value) => new(true, value, null);
        //     public static DomainResult<T> Failure(string message) => new(false, default!, message);
        // }

        var tParam = new NamedTypeReference("T");
        var actionResultT = new NamedTypeReference("DomainResult",
            TypeArguments: [tParam]);

        var props = new List<PropertyDefinitionNode>
        {
            new("IsSuccess",
                new PrimitiveTypeReference(PrimType.Boolean),
                Getter: new PropertyGetterDefinitionNode()),
            new("Value", tParam,
                Getter: new PropertyGetterDefinitionNode()),
            new("ErrorMessage",
                new OptionalTypeReference(
                    new PrimitiveTypeReference(PrimType.String)),
                Getter: new PropertyGetterDefinitionNode()),
        };

        var ctor = new ConstructorDefinitionNode(
            Parameters: [
                new Parameter("isSuccess",
                    new PrimitiveTypeReference(PrimType.Boolean)),
                new Parameter("value", tParam),
                new Parameter("errorMessage",
                    new OptionalTypeReference(
                        new PrimitiveTypeReference(PrimType.String))),
            ],
            Body: new Block([
                new Assignment(
                    new Member(new ThisReference(), "IsSuccess"),
                    new Parameter("isSuccess")),
                new Assignment(
                    new Member(new ThisReference(), "Value"),
                    new Parameter("value")),
                new Assignment(
                    new Member(new ThisReference(), "ErrorMessage"),
                    new Parameter("errorMessage")),
            ]),
            AccessModifier: AccessModifier.Private
        );

        var methods = new List<MethodDefinitionNode>
        {
            new("Success", actionResultT,
                Parameters: [
                    new Parameter("value", tParam)
                ],
                Body: new Block([
                    new Return(
                        new New(actionResultT,
                            new Constant(true),
                            new Parameter("value"),
                            new Constant(null)))
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public),
            new("Failure", actionResultT,
                Parameters: [
                    new Parameter("message",
                        new PrimitiveTypeReference(PrimType.String))
                ],
                Body: new Block([
                    new Return(
                        new New(actionResultT,
                            new Constant(false),
                            new NullForgiving(new Default()),
                            new Parameter("message")))
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public),
        };

        return new TypeDefinitionNode(
            "DomainResult",
            GenericParameters: [new Parameter("T")],
            Properties: props,
            Constructors: [ctor],
            Methods: methods,
            Semantics: Syntactic.TypeDefinitionSemantics.ImmutableValue
        );
    }

    /// <summary>
    /// True when the mapped CLR type is a non-nullable reference type (currently the
    /// only one is <c>Text → string</c>), which the parameterless EF-materialization
    /// ctor leaves unset and would otherwise raise CS8618.
    /// </summary>
    private static bool IsNonNullableReferenceScalar(Node propRef) =>
        propRef is PrimitiveTypeReference { PrimitiveId: PrimType.String };

    /// <summary>
    /// Lowers a <see cref="DefaultValueConstraint"/> to its C# constant/enum node,
    /// used as an optional-parameter default on the Create/CreateNav/ctor signatures.
    /// Returns null for runtime defaults (now/today/guid) — those use a
    /// <c>T? = null</c> sentinel and a <c>?? &lt;runtime&gt;</c> coalesce in the ctor
    /// body. Shared with the create-in call site so signature and call stay in lockstep.
    /// </summary>
    internal static Node? LowerDefaultConstantNode(
        DefaultValueConstraint defaultValue,
        Property prop,
        Domain? domain,
        INodeMetadataProvider? metadata) {
        if (defaultValue.Expression is Literal lit)
            return new Constant(lit.Value);
        if (defaultValue.Expression is PropertyAccess pa) {
            if (domain is not null
                && TryResolveEnumType(domain, metadata, prop.Type.TypeName, out var enumType)
                && enumType is not null
                && enumType.MemberNames.Contains(pa.Name, StringComparer.Ordinal)) {
                return new Member(new NamedTypeReference(enumType.Name), pa.Name);
            }
            // Non-keyword, non-enum-member PropertyAccess default cannot be lowered —
            // fail loud instead of silently dropping the constraint (which would turn
            // the property into a required Create parameter).
            if (domain is not null) {
                throw new NotSupportedException(
                    $"default({pa.Name}) on property '{prop.Name}' (type '{prop.Type.TypeName}') cannot be lowered: " +
                    $"'{pa.Name}' is not a member of an enum that '{prop.Name}' is typed with.");
            }
        }
        return null;
    }

    // ── String helpers ──────────────────────────────────────────

    internal static string ToCamelCase(string name) => DomainTypeMapping.ToCamelCase(name);

    internal static string ToPascalCase(string name) => DomainTypeMapping.ToPascalCase(name);

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
}