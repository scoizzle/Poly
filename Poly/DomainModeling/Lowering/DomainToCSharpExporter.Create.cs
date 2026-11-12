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
    /// public DomainResult&lt;Loan&gt; CreateLoans(Book book, string status, ...)
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
        var requiredProps = RequireStructure(targetEntity, metadata).RequiredPropertyNames;
        var selfPeerSingular = FindSelfRelPeerSingular(targetEntity, entity.Name);
        var isCollectionRel = rel.Cardinality is RelationshipCardinality.OneToMany
            or RelationshipCardinality.ManyToMany;

        foreach (var parameter in parameterMetadata) {
            if (parameter.IsNavigation && parameter.IsBackReference) {
                // Self-rel slot. A self collection (including `rel` when it is a
                // collection) starts empty; a singular self-nav wires `this` only as
                // the peer of a collection create-in; every other self singular is null.
                if (parameter.IsCollection) {
                    createArgs.Add(new New(
                        new NamedTypeReference("List",
                            TypeArguments: [new NamedTypeReference(parameter.Type.TypeName)])));
                    continue;
                }
                createArgs.Add(isCollectionRel
                    && selfPeerSingular is not null
                    && string.Equals(parameter.Name, selfPeerSingular.Name, StringComparison.Ordinal)
                        ? (Node)new ThisReference()
                        : TypedNull(parameter.Type.TypeName));
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
            // emit nullable to match the property/ctor and avoid CS8625. Scalar ctor
            // params are required by construction; optional scalars are trailing params.
            var propRef = parameter.IsNavigation
                ? (Node)new OptionalTypeReference(mapped)
                : mapped;
            // Same node in the signature and the Create call. A second untyped
            // Parameter does not resolve, so the VM cannot tell scalar from reference.
            // A navigation the caller did not write is omitted. It needs a default
            // so that omission is a legal trailing argument.
            var param = parameter.IsNavigation
                ? new Parameter(paramName, propRef, DefaultValue: new Constant(null))
                : parameter.IsOptional
                    ? new Parameter(paramName, propRef, DefaultValue: EmptyDefault(parameter.Type))
                    : new Parameter(paramName, propRef);
            methodParams.Add(param);
            createArgs.Add(param);
        }

        // NOTE: defaulted and optional properties are NOT included in
        // the Target.Create() parameter list — they are trailing params
        // from TrailingOmittableProperties below.

        // Defaulted and optional props of the target become TRAILING optional method
        // params (C# optional params must be last) and are forwarded to Target.Create —
        // so a `create in { Prop: value }` override flows through construction.
        foreach (var prop in TrailingOmittableProperties(targetEntity, metadata)) {
            var paramName = ToCamelCase(prop.Name);
            var mapped = MapDomainTypeRef(prop.Type, domain, metadata);
            var defaultConstraint = prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault();
            Parameter trailing;
            if (defaultConstraint is null) {
                // Optional with no default: omitted passes null (never ""/0).
                trailing = new Parameter(paramName,
                    new OptionalTypeReference(mapped),
                    DefaultValue: new Constant(null));
            }
            else {
                var runtimeExpr = EffectLoweringPass.LowerDefaultExpression(
                    defaultConstraint.Expression,
                    new NamedTypeReference(prop.Type.TypeName),
                    RuntimeAnalysisCache.MeaningFor(domain),
                    RuntimeAnalysisCache.FormsFor(domain));
                if (runtimeExpr is not null) {
                    trailing = new Parameter(paramName,
                        new OptionalTypeReference(mapped),
                        DefaultValue: new Constant(null));
                }
                else {
                    trailing = new Parameter(paramName,
                        requiredProps.Contains(prop.Name) ? mapped : new OptionalTypeReference(mapped),
                        DefaultValue: LowerDefaultConstantNode(defaultConstraint, prop, domain, metadata));
                }
            }
            methodParams.Add(trailing);
            createArgs.Add(trailing);
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
                new Return(
                    new Invoke(
                        new Member(createResultType, "Failure"),
                        new Syntactic.Coalesce(
                            new Member(localResult, "ErrorMessage"),
                            new Constant(""))))
            ])));

        bodyNodes.Add(new Assignment(local, new Member(localResult, "Value")));

        // Target.Create already Attaches via singular nav args (including auto-wired
        // `this` back-ref): peer collections + WhenEach* registries. Do not duplicate
        // _field.Add / Register* / peer Attach when Create wired this collection.
        var (wiredInverse, _) = FindInverseCollectionInfo(entity, targetTypeName);
        var createWiredThisCollection = (autoWireBackRef is not null || selfPeerSingular is not null)
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

        // A create-in lands the child in its first stage. When a subscription watches
        // that stage, no transition occurs, so no previousStage notify runs. One call
        // here covers both wired and unwired cases.
        if (metadata.GetStructure(targetEntity)?.HasStages == true) {
            var firstStage = targetEntity.InitialStage()!.Name;
            if (subscriberSubs is { Count: > 0 }
                && subscriberSubs.Any(i =>
                    string.Equals(i.Relationship.Name, rel.Name, StringComparison.Ordinal)
                    && string.Equals(i.StageName, firstStage, StringComparison.Ordinal))) {
                var needsPrevious = StageGroupNeedsPrevious(targetEntity, firstStage, domain);
                bodyNodes.Add(new Invoke(
                    new Member(local, $"Notify{firstStage}Subscribers"),
                    needsPrevious ? [new Constant(null)] : []));
            }
        }

        bodyNodes.Add(new Return(
            new Invoke(
                new Member(
                    new NamedTypeReference("DomainResult", TypeArguments: [targetType]),
                    "Success"),
                local)));

        methods.Add(new MethodDefinitionNode(
            methodName,
            new NamedTypeReference("DomainResult", TypeArguments: [targetType]),
            Parameters: methodParams.Count > 0 ? methodParams : null,
            Body: new Block(bodyNodes, [localResult, local]),
            AccessModifier: AccessModifier.Public
        ));
    }

    /// <summary>
    /// Public parameters of <c>Create{Nav}</c>: constructor slots the method
    /// asks the caller for. Back-references, the auto-wired peer, and
    /// collections are filled inside the method.
    /// </summary>
    internal static List<ConstructorParameterOrder> CreateNavParameters(
        Entity source, Entity target, INodeMetadataProvider analysis) {
        var slots = new List<ConstructorParameterOrder>();
        var autoWire = FindAutoWireBackReference(target, source.Name);
        foreach (var parameter in GetConstructorParameters(target, analysis)) {
            if (parameter.IsNavigation && parameter.IsBackReference)
                continue;
            if (autoWire is not null
                && string.Equals(parameter.Name, autoWire.Name, StringComparison.Ordinal))
                continue;
            if (parameter.IsCollection)
                continue;
            slots.Add(parameter);
        }
        return slots;
    }

    internal static IReadOnlyList<ConstructorParameterOrder> GetConstructorParameters(
        Entity targetEntity,
        INodeMetadataProvider analysis) {
        if (analysis.GetStructure(targetEntity) is EntityStructureMetadata esm)
            return esm.ConstructorParameters;

        throw new InvalidOperationException(
            $"EntityStructureMetadata is required for constructor ordering on entity '{targetEntity.Name}'.");
    }

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
    /// Trailing omittable Create/ctor parameters: defaulted or optional properties
    /// that are not entry-assigned, in the order the Create factory appends them.
    /// </summary>
    internal static List<Property> TrailingOmittableProperties(
        Entity entity,
        INodeMetadataProvider metadata) {
        var structure = RequireStructure(entity, metadata);
        var entryAssigned = structure.EntryAssignedPropertyNames;
        var required = structure.RequiredPropertyNames;
        var list = new List<Property>();
        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
            if (required.Contains(prop.Name)
                && !prop.Constraints.OfType<DefaultValueConstraint>().Any())
                continue;
            if (entryAssigned.Contains(prop.Name))
                continue;
            list.Add(prop);
        }
        return list;
    }

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
    /// Builds constraint-validation guard clauses for the <c>Create</c> factory method.
    /// Each constraint on a constructor-parameter property produces an early-return
    /// guard: <c>if (violation) return DomainResult&lt;T&gt;.Failure("'Prop' ...");</c>
    /// Enum-typed properties get a not-any-member guard from the same Member + NotEqual
    /// nodes as assign.
    ///
    /// Only entity properties (not navigation properties) are validated — navs do not
    /// carry constraints in the current domain model. Requiredness guards come from the
    /// published fact; a defaulted property's own default supplies the value. Other
    /// constraint guards still apply to defaulted params. Entry-assigned props are
    /// body-initialized, never ctor params, so they are skipped.
    /// </summary>
    private static List<Node> BuildCreateConstraintChecks(
        Entity entity, Domain domain, IReadOnlySet<string> entryAssignedProps,
        INodeMetadataProvider analysis, IReadOnlyList<Parameter> signature) {

        var checks = new List<Node>();
        var entityTypeRef = new NamedTypeReference(entity.Name);
        var resultType = new NamedTypeReference("DomainResult",
            TypeArguments: [entityTypeRef]);

        Node Failure(Node msg) => new Return(
            new Invoke(new Member(resultType, "Failure"), msg));

        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
            // Entry-assigned props are body-initialized by the ctor's stage-entry effects
            // — not constructor params, so no guard to attach.
            if (entryAssignedProps.Contains(prop.Name)) continue;

            var paramName = ToCamelCase(prop.Name);
            // The signature parameter, not a same-named copy. A copy has no
            // type, so name.Length does not resolve and the VM rejects Create.
            var paramRef = signature.FirstOrDefault(p =>
                string.Equals(p.Name, paramName, StringComparison.Ordinal));
            if (paramRef is null)
                continue;

            // An optional parameter is nullable, so a value constraint only applies
            // when the caller supplied a value. The printed `&&` lets flow analysis see
            // a non-null value inside the comparison (and skips an omitted null).
            var nullableParam = paramRef.TypeReference is OptionalTypeReference;
            // Required-by-default: a defaulted prop's own default supplies the value,
            // so the factory does not ask the caller for one (assign always does).
            var includeRequired = !prop.Constraints.Any(c => c is DefaultValueConstraint);
            var valueHasDefault = paramRef.DefaultValue is not null;

            foreach (var check in BuiltinConstraintCompiler.Checks(
                         entity, prop, domain, analysis,
                         ConstraintCheckWriter.ConstraintCheckStyle.Create,
                         paramRef, nullableParam, valueHasDefault, includeRequired)) {
                checks.Add(BuiltinConstraintCompiler.Claims(check.Kind)
                    ? BuiltinConstraintCompiler.CreateGuard(entity.Name, check)
                    : new IfStatement(
                        check.GuardedCondition,
                        new Block([Failure(check.Message)])));
            }
        }

        return checks;
    }

    /// <summary>
    /// Emits the private constructor (initial stage + entry effects + subscription
    /// wiring) and the public static <c>Create</c> factory with its constraint guard
    /// clauses and unique-inverse attach calls. Returns the constructors to publish.
    /// </summary>
    private static List<ConstructorDefinitionNode>? AddCreateFactory(
        Entity entity, Domain domain, INodeMetadataProvider metadata,
        EntityStructureMetadata esm, string? stageEnumTypeName,
        IReadOnlySet<string>? postTransitionNotifyStages,
        IReadOnlySet<string>? previousStageNotifyStages,
        List<SubscriptionInfo>? subscriberSubs,
        List<Parameter> ctorParams, List<Node> ctorAssignments,
        List<MethodDefinitionNode> methods) {
        // ── Private constructor + public static Create factory ─────
        // Every entity publishes Create, including one with no slots, so
        // a type-name call resolves. No slots and no stages still emit a
        // single private parameterless constructor (CS0111).
        List<ConstructorDefinitionNode>? ctors = null;
        var bodyNodes = new List<Node>();
        bodyNodes.AddRange(ctorAssignments);

        if (esm.HasStages) {
            var firstStage = entity.InitialStage()!;
            bodyNodes.Add(new Assignment(
                new Member(new ThisReference(), "CurrentStage"),
                new Member(
                    new NamedTypeReference(stageEnumTypeName!),
                    firstStage.Name)));

            // Apply the initial stage's entry effects in the constructor.
            // This ensures properties set by entry effects (e.g. "entry { assign
            // CheckedOutAt to now }" on Loan.Active) are initialized during
            // construction, not just during explicit stage transitions.
            if (firstStage.OnEntryEffects.Count > 0) {
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
                    SourceStageName: firstStage.Name);
                var entryPass = new EffectLoweringPass(entity, entryCtx);
                entryPass.AppendInlinedStageEffects(firstStage.OnEntryEffects, bodyNodes);
            }
        }

        // InitializeSubscriptions is called as the last step so that all
        // state (including collections passed from data-store materialization)
        // is set before subscription wiring runs.
        if (subscriberSubs is { Count: > 0 }) {
            bodyNodes.Add(new Invoke(
                new Member(new ThisReference(), "InitializeSubscriptions")));
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

        // Build constraint validation checks before the constructor call.
        // Only entity properties (not navigations) are validated — navs
        // don't carry constraints in the current model.
        var constraintChecks = BuildCreateConstraintChecks(
            entity, domain, esm.EntryAssignedPropertyNames, metadata, ctorParams);

        // Construct, then Success. Create is the unique-inverse attach source
        // (Attach* when count==1); CreateNav/BindCreate defer Add/Register when
        // that attach ran. Ambiguous peer collections (count>1) skip Attach —
        // same as the inverse-link rule — so named create-in can
        // fallback-_field.Add. OneToOne (no collection inverse) leaves the
        // ctor to-one only. Null peer is a no-op.
        var createSuccessNodes = new List<Node>();
        createSuccessNodes.AddRange(constraintChecks);
        var createdLocal = new Variable("created");
        var createLocals = new List<Node> { createdLocal };
        createSuccessNodes.Add(new Assignment(
            createdLocal,
            new New(
                new NamedTypeReference(entity.Name),
                ctorParams.ToArray())));
        var lookup = metadata.GetTypeLookup(domain);
        foreach (var navParam in esm.ConstructorParameters
            .Where(p => p.IsNavigation && !p.IsCollection)) {
            var paramName = ToCamelCase(navParam.Name);
            // The signature parameter, not a same-named copy. A copy has no
            // type, so the VM reads an empty slot and skips Attach.
            var paramRef = ctorParams.FirstOrDefault(p =>
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

        methods.Add(new MethodDefinitionNode(
            "Create",
            createResultType,
            Parameters: ctorParams,
            Body: new Block(createSuccessNodes, createLocals),
            IsStatic: true,
            AccessModifier: AccessModifier.Public
        ));

        return ctors;
    }
}