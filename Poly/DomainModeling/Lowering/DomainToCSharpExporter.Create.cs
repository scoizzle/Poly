using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;

using AccessModifier = Poly.Introspection.AccessModifier;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Create: the static Create factory, create-in factory methods, and create-time constraint guards.
public sealed partial class DomainToCSharpExporter {
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
    /// internal DomainResult&lt;Loan&gt; CreateLoans(Book book, string status, ...)
    /// {
    ///     var loanResult = Loan.Create(book: book, borrower: this, status: status, ...);
    ///     // Loan.Create Attaches via borrower:this (Add + Register*) — no duplicate here.
    ///     if (!loanResult.IsSuccess) return Failure(...);
    ///     return Success(loanResult.Value);
    /// }
    /// </code>
    /// </summary>
    private static void AddCreateSingularNavMethod(
        Entity entity,
        Relationship rel,
        Domain domain,
        List<SubscriptionInfo>? subscriberSubs,
        List<MethodDefinitionNode> methods,
        INodeMetadataProvider metadata) =>
        AddCreateNavMethod(entity, rel, domain, subscriberSubs, fieldName: null, methods, metadata);

    private static void AddCreateNavMethod(
        Entity entity,
        Relationship rel,
        Domain domain,
        List<SubscriptionInfo>? subscriberSubs,
        string? fieldName,
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

        // One argument per target Create slot, in the target's slot order (the
        // published CreateSlots). Every slot the caller supplies is a `T? x = default`
        // parameter of this method, so omitting one passes null and the target's
        // Create decides: Required fails, Defaulted takes its default.
        var methodParams = new List<Parameter>();
        var createArgs = new List<Node>();

        // The navigation `create in` fills with the source (the published
        // CreateInFilledNavigation fact): wired with `this`, excluded from the factory
        // signature. Self-relationship slots are filled here too.
        var filledName = CreateInWiring.FilledNavigationName(metadata, entity.Name, rel.Name);

        foreach (var slot in CreateSlotsOf(targetEntity, metadata)) {
            if (slot is { IsSelfNavigation: true, Shape: not CreateSlotShape.Scalar }) {
                // Self-rel slot. A self collection (including `rel` when it is a
                // collection) starts empty; a singular self-nav wires `this` only as
                // the peer of a collection create-in (the fact); every other self singular is null.
                if (slot.Shape == CreateSlotShape.Collection) {
                    createArgs.Add(new New(
                        new NamedTypeReference("List",
                            TypeArguments: [new NamedTypeReference(slot.Type.TypeName)])));
                    continue;
                }
                createArgs.Add(string.Equals(slot.Name, filledName, StringComparison.Ordinal)
                    ? new ThisReference()
                    : TypedNull(slot.Type.TypeName));
                continue;
            }

            if (string.Equals(slot.Name, filledName, StringComparison.Ordinal)) {
                createArgs.Add(new ThisReference());
                continue;
            }

            if (slot.Shape == CreateSlotShape.Collection) {
                // Collection nav: the CreateNav factory starts every child with an empty collection.
                createArgs.Add(new New(
                    new NamedTypeReference("List",
                        TypeArguments: [new NamedTypeReference(slot.Type.TypeName)])));
                continue;
            }

            // Same node in the signature and the Create call. A second untyped
            // Parameter does not resolve, so the VM cannot tell scalar from reference.
            var param = CreateSlotParameter(slot, domain, metadata);
            methodParams.Add(param);
            createArgs.Add(param);
        }

        var bodyNodes = new List<Node>();
        var localResult = new Variable($"{ToCamelCase(targetTypeName)}Result");
        var local = new Variable(ToCamelCase(targetTypeName));

        bodyNodes.Add(new Assignment(localResult,
            new Invoke(
                new Member(targetType, "Create"),
                [.. createArgs])));

        var createResultType = new NamedTypeReference("DomainResult", TypeArguments: [targetType]);
        bodyNodes.Add(new IfStatement(
            new Syntactic.Not(
                new Member(localResult, "IsSuccess")),
            new Block([
                FailureLowering.Propagate(createResultType, localResult)
            ])));

        bodyNodes.Add(new Assignment(local, new Member(localResult, "Value")));

        // Target.Create already Attaches via singular nav args (including auto-wired
        // `this` back-ref): peer collections + WhenEach* registries. Do not duplicate
        // _field.Add / Register* / peer Attach when Create wired this collection.
        var (wiredInverse, _) = FindInverseCollectionInfo(entity, targetTypeName);
        var createWiredThisCollection = filledName is not null
            && fieldName is not null
            && wiredInverse is not null
            && string.Equals(wiredInverse.Name, rel.Name, StringComparison.Ordinal);

        if (!createWiredThisCollection) {
            if (fieldName is not null) {
                bodyNodes.Add(new Invoke(
                    new Member(
                        new Member(new ThisReference(), fieldName), "Add"),
                    [local]));
            }
            else {
                bodyNodes.Add(new Assignment(
                    new Member(new ThisReference(), pascalName),
                    local));

                // A singular create-in links `this` into the target's unique inverse
                // collection (e.g. local.AttachItems(this)); ambiguous/no inverse is a no-op.
                var inverse = FindInverseCollectionInfo(targetEntity, entity.Name).Unique;
                if (inverse is not null) {
                    bodyNodes.Add(new Invoke(
                        new Member(local, $"Attach{ToPascalCase(inverse.Name)}"),
                        [new ThisReference()]));
                }
            }

            // Subscription registration when Create did not Attach this relation.
            if (subscriberSubs is { Count: > 0 }) {
                foreach (var info in subscriberSubs
                    .Where(i => string.Equals(i.Relationship.Name, rel.Name, StringComparison.Ordinal))
                    .GroupBy(s => s.StageName)
                    .Select(g => g.First())) {
                    bodyNodes.Add(new Invoke(
                        new Member(
                            local,
                            SubscriberRegisterMethodName(info)),
                        [new ThisReference()]));
                }
            }
        }

        // A create-in lands the child in its initial stage. When a subscription watches
        // that stage, no transition occurs, so no previousStage notify runs. One call
        // here covers both wired and unwired cases.
        Variable? notifyResult = null;
        if (metadata.GetStructure(targetEntity)?.HasStages == true) {
            var initialStage = targetEntity.InitialStage()!.Name;
            if (subscriberSubs is { Count: > 0 }
                && subscriberSubs.Any(i =>
                    string.Equals(i.Relationship.Name, rel.Name, StringComparison.Ordinal)
                    && string.Equals(i.StageName, initialStage, StringComparison.Ordinal))) {
                var needsPrevious = StageGroupNeedsPrevious(targetEntity, initialStage, domain);
                // A handler failure fails the create-in: the dispatcher returns it, this method returns it
                // as its own result with no caller prefix (the child's failure above is passed the same way).
                // `__notify` is reserved, so a target property named Notify cannot collide with it.
                notifyResult = new Variable("__notify");
                bodyNodes.Add(new Assignment(notifyResult, new Invoke(
                    new Member(local, $"Notify{initialStage}Subscribers"),
                    needsPrevious ? [new Constant(null)] : [])));
                bodyNodes.Add(new IfStatement(
                    new Syntactic.Not(new Member(notifyResult, "IsSuccess")),
                    new Block([FailureLowering.Propagate(createResultType, notifyResult)])));
            }
        }

        bodyNodes.Add(new Return(
            new Invoke(
                new Member(
                    new NamedTypeReference("DomainResult", TypeArguments: [targetType]),
                    "Success"),
                local)));

        // Scoped like an action (L-04b): a bare call whose child handler fails after the child joined this collection
        // rolls the child back out.
        AddScopedActionMethods(methods, methodName,
            new NamedTypeReference("DomainResult", TypeArguments: [targetType]),
            methodParams,
            new Block(bodyNodes, notifyResult is null ? [localResult, local] : [localResult, local, notifyResult]),
            AccessModifier.Internal);
    }

    /// <summary>
    /// Public parameters of <c>Create{Nav}</c>: the target's create slots the method
    /// asks the caller for, in slot order. Self-navigations, the auto-wired peer and
    /// collections are filled inside the method.
    /// </summary>
    internal static List<CreateSlot> CreateNavParameters(
        Entity source, string relationshipName, Entity target, INodeMetadataProvider analysis) {
        var slots = new List<CreateSlot>();
        var filledName = CreateInWiring.FilledNavigationName(analysis, source.Name, relationshipName);
        foreach (var slot in CreateSlotsOf(target, analysis)) {
            if (slot is { IsSelfNavigation: true, Shape: not CreateSlotShape.Scalar })
                continue;
            if (string.Equals(slot.Name, filledName, StringComparison.Ordinal))
                continue;
            if (slot.Shape == CreateSlotShape.Collection)
                continue;
            slots.Add(slot);
        }
        return slots;
    }

    /// <summary>
    /// The entity's published <see cref="EntityStructureMetadata.CreateSlots"/>. A missing
    /// bag is a lowering fault: it throws rather than recomputing the signature.
    /// </summary>
    internal static IReadOnlyList<CreateSlot> CreateSlotsOf(Entity entity, INodeMetadataProvider analysis) =>
        RequireStructure(entity, analysis).CreateSlots;

    /// <summary>
    /// The published structure bag for <paramref name="entity"/>. A missing bag is a
    /// lowering fault: it throws rather than recomputing requiredness or entry assignment.
    /// </summary>
    internal static EntityStructureMetadata RequireStructure(
        Entity entity,
        INodeMetadataProvider analysis) =>
        analysis.GetStructure(entity)
        ?? throw new InvalidOperationException(
            $"EntityStructureMetadata is required for lowering entity '{entity.Name}'; analysis published none.");

    /// <summary>
    /// The value type a slot holds once its guard and default have run: the property's
    /// mapped type, a navigation's target type, or <c>IEnumerable&lt;T&gt;</c> for a collection.
    /// </summary>
    private static Node SlotValueType(CreateSlot slot, Domain domain, INodeMetadataProvider metadata) =>
        slot.Shape == CreateSlotShape.Collection
            ? new NamedTypeReference("IEnumerable", TypeArguments: [new NamedTypeReference(slot.Type.TypeName)])
            : MapDomainTypeRef(slot.Type, domain, metadata);

    /// <summary>A <c>Create</c> (or <c>Create{Nav}</c>) parameter: <c>T? x = default</c>.</summary>
    internal static Parameter CreateSlotParameter(CreateSlot slot, Domain domain, INodeMetadataProvider metadata) =>
        new(ToCamelCase(slot.Name), new OptionalTypeReference(SlotValueType(slot, domain, metadata)),
            DefaultValue: new Syntactic.Default());

    /// <summary>
    /// The reserved local a Required or Defaulted slot's value lives in once its guard
    /// and default have run. <c>__</c> is a reserved prefix, so it cannot shadow a parameter.
    /// </summary>
    internal static string EffectiveLocalName(string slotName) => "__" + ToCamelCase(slotName);

    /// <summary>
    /// The node a create-time check reads for <paramref name="property"/> in the lowered
    /// <c>Create</c>: the effective-value local for a Required or Defaulted property, the
    /// nullable parameter itself for an Optional one.
    /// </summary>
    internal static Node CreateValueOf(Property property, Parameter parameter) =>
        EntityStructureAnalyzer.SlotKindFor(property) is CreateSlotKind.Optional
            ? parameter
            : new Variable(EffectiveLocalName(property.Name));

    /// <summary>
    /// True when the slot's C# type is a value type (a number, Boolean, a temporal type,
    /// a Guid or an enum), whose <c>T?</c> is <c>Nullable&lt;T&gt;</c> and hands off by cast.
    /// </summary>
    private static bool HoldsClrValueType(CreateSlot slot, Domain domain) =>
        slot.Shape == CreateSlotShape.Scalar
        && (domain.Types.OfType<EnumType>().Any(e => string.Equals(e.Name, slot.Type.TypeName, StringComparison.Ordinal))
            || DomainTypeMapping.IsNonNullableClrValueType(RuntimeAnalysisCache.ClrTypeName(domain, slot.Type.TypeName)));

    /// <summary>
    /// The slot's value once it is known non-null: <c>(T)x</c> for a value type, <c>x</c>
    /// itself for a reference (Text, a domain value type, a navigation, a collection).
    /// No <c>??</c> on a value type: the VM's coalesce cannot produce a <c>bool</c>.
    /// </summary>
    private static Node HandOff(CreateSlot slot, Parameter parameter, Node valueType, Domain domain) =>
        HoldsClrValueType(slot, domain) ? new TypeCast(parameter, valueType) : parameter;

    /// <summary>
    /// A Defaulted slot's authored default, lowered: a runtime value (<c>now</c>, <c>today</c>,
    /// <c>guid</c>) or a constant (a literal or an enum member).
    /// </summary>
    private static Node LoweredDefault(CreateSlot slot, Property prop, Domain domain, INodeMetadataProvider metadata) =>
        EffectLoweringPass.LowerDefaultExpression(
            slot.Default!.Expression,
            new NamedTypeReference(prop.Type.TypeName),
            RuntimeAnalysisCache.MeaningFor(domain),
            RuntimeAnalysisCache.FormsFor(domain))
        ?? LowerDefaultConstantNode(slot.Default, prop, domain, metadata)
        ?? throw new NotSupportedException(
            $"default(...) on property '{prop.Name}' cannot be lowered.");

    /// <summary>
    /// Null <c>IEnumerable&lt;T&gt;?</c> for an omitted collection create argument.
    /// The printed constructor already coalesces that null into an empty list.
    /// A synthetic <c>new List&lt;T&gt;()</c> is a different collection instance the
    /// VM cannot assign or construct.
    /// </summary>
    internal static Node NullCollectionNode(string elementTypeName) =>
        new TypeCast(
            new Constant(null),
            new OptionalTypeReference(
                new NamedTypeReference("IEnumerable",
                    TypeArguments: [new NamedTypeReference(elementTypeName)])));

    /// <summary>
    /// One slot of the lowered <c>Create</c>, in declaration order: a Required slot's
    /// null guard (<see cref="ConstraintCheckWriter.RequiredSlotCheck"/>), the effective
    /// value local of a Required or Defaulted slot (<c>(T)x</c> or <c>x</c> for Required,
    /// <c>x == null ? d : (T)x</c> or <c>x == null ? d : x</c> for Defaulted), then that slot's
    /// constraint checks on the effective value. Per slot, so the first declared violation,
    /// missing or invalid, is the one the caller sees. Returns the value the constructor takes.
    /// </summary>
    private static Node AppendCreateSlot(
        Entity entity, Domain domain, INodeMetadataProvider analysis,
        CreateSlot slot, Parameter parameter, List<Node> checks, List<Node> locals) {
        var resultType = new NamedTypeReference("DomainResult",
            TypeArguments: [new NamedTypeReference(entity.Name)]);
        Node Guard(ConstraintCheckWriter.Check check) =>
            BuiltinConstraintCompiler.Claims(check.Kind)
                ? BuiltinConstraintCompiler.CreateGuard(entity.Name, slot.Name, check)
                : new IfStatement(
                    check.GuardedCondition,
                    new Block([FailureLowering.Return(
                        resultType, ConstraintCheckWriter.FailureSpec(check, entity.Name, slot.Name))]));

        var prop = slot.Shape == CreateSlotShape.Scalar
            ? entity.Properties.First(p => string.Equals(p.Name, slot.Name, StringComparison.Ordinal))
            : null;
        Node value = parameter;
        if (slot.Kind == CreateSlotKind.Required)
            checks.Add(Guard(ConstraintCheckWriter.RequiredSlotCheck(slot, parameter)));
        if (slot.Kind is CreateSlotKind.Required or CreateSlotKind.Defaulted) {
            var local = new Variable(EffectiveLocalName(slot.Name));
            locals.Add(local);
            var handOff = HandOff(slot, parameter, SlotValueType(slot, domain, analysis), domain);
            checks.Add(new Assignment(local, slot.Kind == CreateSlotKind.Required
                ? handOff
                : new Conditional(
                    new Equal(parameter, new Constant(null)),
                    LoweredDefault(slot, prop!, domain, analysis),
                    handOff)));
            value = local;
        }
        if (prop is null)
            return value;

        foreach (var check in BuiltinConstraintCompiler.Checks(
                     entity, prop, domain, analysis,
                     ConstraintCheckWriter.ConstraintCheckStyle.Create,
                     value, nullableValue: slot.Kind == CreateSlotKind.Optional, includeRequired: false))
            checks.Add(Guard(check));
        return value;
    }

    /// <summary>
    /// The private constructor's parameter for a slot: the value type for a Required or
    /// Defaulted slot, which <c>Create</c> has made non-null, and <c>T?</c> otherwise.
    /// </summary>
    private static Parameter ConstructorParameter(CreateSlot slot, Domain domain, INodeMetadataProvider metadata) {
        var type = SlotValueType(slot, domain, metadata);
        return new Parameter(ToCamelCase(slot.Name),
            slot.Kind == CreateSlotKind.Optional || slot.Shape != CreateSlotShape.Scalar
                ? new OptionalTypeReference(type)
                : type);
    }

    /// <summary>The constructor's assignment of one slot: a property, a singular navigation, or a collection field.</summary>
    private static Node ConstructorAssignment(CreateSlot slot, Parameter parameter) {
        switch (slot.Shape) {
            case CreateSlotShape.Collection: {
                    var listType = new NamedTypeReference("List",
                        TypeArguments: [new NamedTypeReference(slot.Type.TypeName)]);
                    return new Assignment(
                        new Member(new ThisReference(), $"_{parameter.Name}"),
                        new New(listType, [new Syntactic.Coalesce(parameter, new New(listType))]));
                }
            case CreateSlotShape.Reference:
                return new Assignment(new Member(new ThisReference(), ToPascalCase(slot.Name)), parameter);
            default:
                return new Assignment(new Member(new ThisReference(), slot.Name), parameter);
        }
    }

    /// <summary>
    /// The private result-returning method <c>Create</c> calls after <c>new</c> to run the initial stage's entry
    /// effects. <c>__</c> is a reserved prefix, so no member can collide with it.
    /// </summary>
    internal const string EnterInitialStageMethodName = "__EnterInitialStage";

    /// <summary>
    /// Emits the private constructor (assignments and the initial stage only) and the public static <c>Create</c>
    /// factory: constraint guards, <c>new</c>, the initial entry (<see cref="EnterInitialStageMethodName"/>),
    /// subscription registration and the unique-inverse attach calls. Returns the constructors to publish.
    /// </summary>
    private static List<ConstructorDefinitionNode>? AddCreateFactory(
        Entity entity, Domain domain, INodeMetadataProvider metadata,
        EntityStructureMetadata esm, string? stageEnumTypeName,
        IReadOnlySet<string>? postTransitionNotifyStages,
        IReadOnlySet<string>? previousStageNotifyStages,
        List<SubscriptionInfo>? subscriberSubs,
        List<MethodDefinitionNode> methods) {
        // ── Private constructor + public static Create factory ─────
        // Every entity publishes Create, including one with no slots, so
        // a type-name call resolves. No slots and no stages still emit a
        // single private parameterless constructor (CS0111).
        List<ConstructorDefinitionNode>? ctors = null;
        // One loop over the published slots, in declaration order: the Create
        // parameter (`T? x = default`), the constructor parameter and assignment,
        // and the slot's guard, effective value and checks.
        var createParams = new List<Parameter>();
        var ctorParams = new List<Parameter>();
        var ctorArgs = new List<Node>();
        var constraintChecks = new List<Node>();
        var slotLocals = new List<Node>();
        // The constructor is pure assignment: the parameters and the initial stage. The initial stage's entry effects
        // can fail, so they run in the result-returning __EnterInitialStage, which Create calls (L-04a, #825).
        var bodyNodes = new List<Node>();
        foreach (var slot in esm.CreateSlots) {
            var parameter = CreateSlotParameter(slot, domain, metadata);
            createParams.Add(parameter);
            var ctorParam = ConstructorParameter(slot, domain, metadata);
            ctorParams.Add(ctorParam);
            bodyNodes.Add(ConstructorAssignment(slot, ctorParam));
            ctorArgs.Add(AppendCreateSlot(entity, domain, metadata, slot, parameter, constraintChecks, slotLocals));
        }

        MethodDefinitionNode? entryMethod = null;
        if (esm.HasStages) {
            var initialStage = entity.InitialStage()!;
            bodyNodes.Add(new Assignment(
                new Member(new ThisReference(), "CurrentStage"),
                new Member(
                    new NamedTypeReference(stageEnumTypeName!),
                    initialStage.Name)));

            // Entry effects (e.g. "entry { assign CheckedOutAt to now }" on Loan.Active) initialize the new
            // instance, so they run after `new` and before the instance is attached anywhere.
            if (initialStage.OnEntryEffects.Count > 0) {
                var entryCtx = new LoweringContext(
                    new ThisReference(),
                    Analysis: metadata,
                    Domain: domain,
                    StageEnumTypeName: stageEnumTypeName,
                    EnumPropertyNames: esm.EnumPropertyNames,
                    LifecyclePropertyName: esm.LifecyclePropertyName,
                    LifecycleEnumTypeName: esm.LifecycleEnumTypeName,
                    PostTransitionNotifyStages: postTransitionNotifyStages,
                    PreviousStageNotifyStages: previousStageNotifyStages,
                    SourceStageName: initialStage.Name,
                    ActionResultType: new NamedTypeReference("DomainResult"));
                var entryPass = new EffectLoweringPass(entity, entryCtx);
                var entryNodes = new List<Node>();
                entryPass.AppendInlinedStageEffects(initialStage.OnEntryEffects, entryNodes);
                if (entryNodes.Count > 0) {
                    entryMethod = new MethodDefinitionNode(
                        EnterInitialStageMethodName,
                        new NamedTypeReference("DomainResult"),
                        Body: WithSuccessTail(new Block([TouchThis(), .. entryNodes])),
                        AccessModifier: AccessModifier.Private);
                    methods.Add(entryMethod);
                }
            }
        }

        // Private parameterless constructor only when a persistence pack is
        // loaded (EF materialization). Collection fields carry their list
        // initializer, so this ctor is always empty (D9).
        var hasPersistence = metadata.GetMetadata<PersistenceSurfaceMetadata>(domain) is not null;
        if (ctorParams.Count > 0 && hasPersistence) {
            ctors = [new ConstructorDefinitionNode(
                Parameters: null,
                Body: null,
                AccessModifier: AccessModifier.Private
            )];
        }
        else {
            ctors = [];
        }
        ctors = [.. ctors, new ConstructorDefinitionNode(
            Parameters: ctorParams,
            Body: bodyNodes.Count > 0 ? new Block(bodyNodes) : null,
            AccessModifier: AccessModifier.Private
        )];

        // public static DomainResult<EntityName> Create(args...)
        var createResultType = new NamedTypeReference("DomainResult",
            TypeArguments: [new NamedTypeReference(entity.Name)]);

        // Construct, then Success. Create is the unique-inverse attach source
        // (Attach* when count==1); CreateNav/BindCreate defer Add/Register when
        // that attach ran. Ambiguous peer collections (count>1) skip Attach —
        // same as the inverse-link rule — so named create-in can
        // fallback-_field.Add. OneToOne (no collection inverse) leaves the
        // ctor to-one only. Null peer is a no-op.
        var createSuccessNodes = new List<Node>();
        createSuccessNodes.AddRange(constraintChecks);
        var createdLocal = new Variable("created");
        var createLocals = new List<Node>(slotLocals) { createdLocal };
        createSuccessNodes.Add(new Assignment(
            createdLocal,
            new New(
                new NamedTypeReference(entity.Name),
                ctorArgs.ToArray())));
        // The open action scope records what it created, after `new` and before any peer attach (L-03c F3; L-04's
        // initial-stage entry follows this line). A no-op with no scope open.
        createSuccessNodes.Add(ActionScopeCall("Create", createdLocal, new Constant(entity.Name)));

        // The initial entry runs before the instance is attached or registered anywhere, and its failure is returned
        // as-is: a failed create leaves the instance in no collection and no registry. `__entry` cannot collide with a
        // parameter (a property named Entry prints as `entry`; `__` is a reserved prefix).
        if (entryMethod is not null) {
            var entryResult = new Variable("__entry");
            createLocals.Add(entryResult);
            createSuccessNodes.Add(new Assignment(entryResult, new Invoke(new Member(createdLocal, EnterInitialStageMethodName))));
            createSuccessNodes.Add(new IfStatement(
                new Syntactic.Not(new Member(entryResult, "IsSuccess")),
                new Block([FailureLowering.Propagate(createResultType, entryResult)])));
        }

        // InitializeSubscriptions registers the new instance in its peers' registries, so it runs after the entry
        // (a failed entry must leave no registry entry in a peer) and after all state, including collections passed
        // from data-store materialization, is set.
        if (subscriberSubs is { Count: > 0 }) {
            createSuccessNodes.Add(new Invoke(new Member(createdLocal, "InitializeSubscriptions")));
        }
        var lookup = metadata.GetTypeLookup(domain);
        foreach (var navParam in esm.CreateSlots.Where(s => s.Shape == CreateSlotShape.Reference)) {
            var paramName = ToCamelCase(navParam.Name);
            // The signature parameter, not a same-named copy. A copy has no
            // type, so the VM reads an empty slot and skips Attach.
            var paramRef = createParams.FirstOrDefault(p =>
                string.Equals(p.Name, paramName, StringComparison.Ordinal));
            if (paramRef is null)
                continue;
            var peerTypeName = navParam.Type.TypeName;
            Entity? peerEntity = null;
            if (lookup is not null
                && lookup.Types.TryGetValue(peerTypeName, out var resolvedPeer)
                && resolvedPeer is Entity pe)
                peerEntity = pe;
            else
                peerEntity = domain.Types.OfType<Entity>()
                    .FirstOrDefault(e => string.Equals(e.Name, peerTypeName, StringComparison.Ordinal));

            // Unique collection inverse on the peer (FindInverseCollectionInfo).
            // count == 0: OneToOne / no
            // collection — ctor already set the to-one. count > 1: skip Attach
            // (named create-in CreateNav fallback Add). count == 1: Attach*.
            var inverse = peerEntity is not null
                ? FindInverseCollectionInfo(peerEntity, entity.Name).Unique
                : null;

            if (inverse is null)
                continue;

            createSuccessNodes.Add(new IfStatement(
                new NotEqual(paramRef, new Constant(null)),
                new Block([
                    new Invoke(
                        new Member(paramRef, $"Attach{ToPascalCase(inverse.Name)}"),
                        [createdLocal])
                ])));
        }

        createSuccessNodes.Add(new Return(
            new Invoke(
                new Member(createResultType, "Success"),
                [createdLocal])));

        // A bare `Create` (no action running) opens a root scope, so a failure after another entity changed rolls
        // everything back (L-04b); inside an action it joins that action's scope.
        AddScopedActionMethods(methods, "Create", createResultType, createParams,
            new Block(createSuccessNodes, createLocals),
            DomainAccess.Demote(
                AccessModifier.Public, domain,
                [.. createParams.Select(p => p.TypeReference), createResultType]),
            staticReceiver: new NamedTypeReference(entity.Name));

        return ctors;
    }
}