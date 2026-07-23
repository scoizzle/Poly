using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.Syntax.Analysis;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Syntax.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Produces Syntax AST <see cref="Syntactic.TypeDefinitionNode"/> trees from a
/// <see cref="Domain"/>, suitable for C# code generation via
/// <see cref="Interpretation.CSharp.CSharpGenerator"/>.
///
/// Each entity becomes a class with properties, navigation properties (as
/// <c>IReadOnlyList&lt;T&gt;</c> for collections), lifecycle stages (as enum +
/// <c>CurrentStage</c> field), actions (as void methods), and policies (as
/// bool methods). Constructor parameters are auto-generated for every property.
///
/// Stage subscriptions (<c>when RelName Stage</c>) generate cross-entity notification:
/// the subscriber entity declares a <c>When{Target}{Stage}()</c> handler method, and
/// the target entity emits a subscriber list + notify call after each stage transition.
/// </summary>
public sealed class DomainToCSharpExporter {
    /// <summary>Collected subscription data for cross-entity notification.</summary>
    internal sealed record SubscriptionInfo(
        string StageName,
        StageSubscription Subscription,
        Entity SourceEntity,
        Entity TargetEntity,
        Relationship Relationship
    );

    /// <summary>
    /// Builds Syntax AST type definitions for all entities and their stage enums
    /// in the given domain. The exporter uses <see cref="EffectiveMemberMetadata"/>
    /// from the pre-computed analysis for inheritance-aware member resolution.
    /// </summary>
    /// <param name="domain">The domain model to export.</param>
    /// <param name="analysis">
    /// The analysis result (required). Must include <see cref="EffectiveMemberMetadata"/>
    /// for each entity (produced by <see cref="SemanticDomainAnalyzer"/>).
    /// </param>
    public IReadOnlyList<Syntactic.TypeDefinitionNode> Export(Domain domain,
        INodeMetadataProvider metadata) {
        return DomainProgramProjection.ToSyntax(domain, metadata);
    }

    /// <summary>
    /// Collects subscription info for a single <see cref="StageSubscription"/> and
    /// populates the target/subscriber maps used for code generation.
    /// </summary>
    internal static void CollectSubscriptionInfo(
        StageSubscription sub, Entity entity, string? stageName,
        IReadOnlyList<Relationship> domainRelationships,
        IReadOnlyDictionary<string, Entity> entityLookup,
        List<SubscriptionInfo> subList,
        Dictionary<string, List<SubscriptionInfo>> subscriptionsByTarget) {

        var rel = domainRelationships.FirstOrDefault(r =>
            string.Equals(r.Name, sub.RelationshipName, StringComparison.Ordinal) &&
            string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal));
        if (rel is null) return;

        if (!entityLookup.TryGetValue(rel.Target.TypeName, out var targetEntity))
            return;

        foreach (var sName in sub.StageNames) {
            var info = new SubscriptionInfo(sName, sub, entity, targetEntity, rel);
            subList.Add(info);

            if (!subscriptionsByTarget.TryGetValue(targetEntity.Name, out var targetList))
                subscriptionsByTarget[targetEntity.Name] = targetList = new();
            targetList.Add(info);
        }
    }

    // ── Per-entity builder ──────────────────────────────────────

    internal static IReadOnlyList<Syntactic.TypeDefinitionNode> BuildTypeDefsForEntity(
        Entity entity,
        Domain domain,
        IReadOnlyList<Relationship> domainRelationships,
        IReadOnlyDictionary<string, Entity> entityLookup,
        INodeMetadataProvider metadata,
        List<SubscriptionInfo>? targetSubs = null,
        List<SubscriptionInfo>? subscriberSubs = null) {

        var typeDefs = new List<Syntactic.TypeDefinitionNode>();
        var props = new List<Syntactic.PropertyDefinitionNode>();
        var methods = new List<Syntactic.MethodDefinitionNode>();
        var fields = new List<Syntactic.FieldDefinitionNode>();
        var ctorParams = new List<Syntactic.Parameter>();
        var ctorAssignments = new List<Poly.Syntax.Node>();

        var stageEnumTypeName = $"{entity.Name}Stage";

        // ── Common property: IsDeleted (always emitted) ────────────
        props.Add(new Syntactic.PropertyDefinitionNode(
            "IsDeleted",
            new Syntactic.PrimitiveTypeReference(PrimType.Boolean),
            Getter: new Syntactic.PropertyGetterDefinitionNode(),
            Setter: new Syntactic.PropertySetterDefinitionNode(
                AccessModifier: AccessModifier.Private)
        ));

        // ── Entity properties (sorted for deterministic output) ──
        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
            var propRef = MapDomainTypeRef(prop.Type, domain);
            var isRequired = prop.Constraints.Any(c => c is RequiredConstraint);
            List<Poly.Syntax.Node>? constraints = null;
            if (isRequired)
                constraints = [new Syntactic.Constant("required")];

            props.Add(new Syntactic.PropertyDefinitionNode(
                prop.Name, propRef,
                Getter: new Syntactic.PropertyGetterDefinitionNode(),
                Setter: new Syntactic.PropertySetterDefinitionNode(
                    AccessModifier: AccessModifier.Private),
                Constraints: constraints
            ));

            // Check for default value expression (runtime default or constant)
            var defaultValue = prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault();
            if (defaultValue is not null) {
                // Try to lower as a runtime default (now, today, guid)
                var runtimeExpr = EffectLoweringPass.LowerDefaultExpression(defaultValue.Expression);
                if (runtimeExpr is not null) {
                    ctorAssignments.Add(new Syntactic.Assignment(
                        new Syntactic.Member(new Syntactic.ThisReference(), prop.Name),
                        runtimeExpr));
                    continue; // skip ctor param — runtime value set in body
                }
                // If it's a literal, emit directly in body as default
                if (defaultValue.Expression is Poly.DomainModeling.Literal lit) {
                    ctorAssignments.Add(new Syntactic.Assignment(
                        new Syntactic.Member(new Syntactic.ThisReference(), prop.Name),
                        new Syntactic.Constant(lit.Value)));
                    continue; // skip ctor param — constant default in body
                }
                // If it's an enum member (PropertyAccess), also emit directly
                if (defaultValue.Expression is Poly.DomainModeling.PropertyAccess pa && domain is not null) {
                    // Try to resolve as enum member: EnumType.MemberName
                    var enumProp = entity.Properties.FirstOrDefault(p =>
                        string.Equals(p.Name, prop.Name, StringComparison.Ordinal));
                    if (enumProp is not null) {
                        var enumTypes = domain.Types.OfType<EnumType>()
                            .ToDictionary(e => e.Name, StringComparer.Ordinal);
                        if (enumTypes.TryGetValue(enumProp.Type.TypeName, out var enumType)) {
                            ctorAssignments.Add(new Syntactic.Assignment(
                                new Syntactic.Member(new Syntactic.ThisReference(), prop.Name),
                                new Syntactic.Member(
                                    new Syntactic.NamedTypeReference(enumType.Name), pa.Name)));
                            continue; // skip ctor param
                        }
                    }
                }
            }

            // No default expression — full constructor param + assignment
            var paramName = ToCamelCase(prop.Name);
            ctorParams.Add(new Syntactic.Parameter(paramName, propRef));
            ctorAssignments.Add(new Syntactic.Assignment(
                new Syntactic.Member(new Syntactic.ThisReference(), prop.Name),
                new Syntactic.Parameter(paramName)));
        }

        // ── Navigation properties (PascalCase; IReadOnlyList for collections) ──
        foreach (var rel in domainRelationships) {
            if (!string.Equals(rel.Source.TypeName, entity.Name, StringComparison.Ordinal))
                continue;

            var isMany = rel.Cardinality is RelationshipCardinality.OneToMany
                         or RelationshipCardinality.ManyToMany;
            var targetType = new Syntactic.NamedTypeReference(rel.Target.TypeName);
            var pascalName = ToPascalCase(rel.Name);
            var paramName = ToCamelCase(pascalName);

            if (isMany) {
                // Collection nav: private field, IEnumerable<T> constructor param,
                // getter-only property. EF passes loaded items in via the constructor.
                var fieldName = $"_{paramName}";
                var listType = new Syntactic.NamedTypeReference("List",
                    TypeArguments: [targetType]);
                var readOnlyType = new Syntactic.NamedTypeReference("IReadOnlyList",
                    TypeArguments: [targetType]);
                var enumerableType = new Syntactic.NamedTypeReference("IEnumerable",
                    TypeArguments: [targetType]);

                fields.Add(new Syntactic.FieldDefinitionNode(
                    fieldName,
                    listType,
                    AccessModifier: AccessModifier.Private
                ));

                props.Add(new Syntactic.PropertyDefinitionNode(
                    pascalName, readOnlyType,
                    Getter: new Syntactic.PropertyGetterDefinitionNode(
                        Body: new Syntactic.Member(new Syntactic.ThisReference(), fieldName)),
                    IsReadOnly: true
                ));

                // Constructor param: IEnumerable<T> items → _field = new List<T>(items)
                var param = new Syntactic.Parameter(paramName, enumerableType);
                ctorParams.Add(param);
                ctorAssignments.Add(new Syntactic.Assignment(
                    new Syntactic.Member(new Syntactic.ThisReference(), fieldName),
                    new Syntactic.New(listType, [new Syntactic.Parameter(paramName)])));

                // Generate CreateNavName(args) factory method on the source entity
                AddCreateNavMethod(entity, rel, domain!, subscriberSubs, fieldName, methods);
            }
            else {
                // Singular nav: property with private setter (constructor param)
                props.Add(new Syntactic.PropertyDefinitionNode(
                    pascalName, targetType,
                    Getter: new Syntactic.PropertyGetterDefinitionNode(),
                    Setter: new Syntactic.PropertySetterDefinitionNode(
                        AccessModifier: AccessModifier.Private)
                ));

                var param = new Syntactic.Parameter(paramName, targetType);
                ctorParams.Add(param);
                ctorAssignments.Add(new Syntactic.Assignment(
                    new Syntactic.Member(new Syntactic.ThisReference(), pascalName),
                    new Syntactic.Parameter(paramName)));
            }
        }

        // ── Build post-transition notification nodes ──────────────
        Dictionary<string, IReadOnlyList<Node>>? postTransitionNodes = null;
        if (targetSubs is { Count: > 0 }) {
            postTransitionNodes = new Dictionary<string, IReadOnlyList<Node>>(
                StringComparer.Ordinal);
            foreach (var group in targetSubs.GroupBy(s => s.StageName)) {
                var nodes = new List<Node>();
                foreach (var info in group)
                    nodes.Add(new Syntactic.Invoke(
                        new Syntactic.Member(new Syntactic.ThisReference(),
                            $"Notify{info.StageName}Subscribers")));
                postTransitionNodes[group.Key] = nodes;
            }
        }

        // ── Actions as void methods ───────────────────────────────
        foreach (var action in entity.Actions)
            AddActionMethod(entity, action, methods, stageEnumTypeName, postTransitionNodes, domain: domain);
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions)
                AddActionMethod(entity, action, methods, stageEnumTypeName, postTransitionNodes, stage.Name, domain);

        // ── Policies as bool methods ──────────────────────────────
        foreach (var policy in entity.Policies) {
            Poly.Syntax.Node? body;
            try {
                body = LowerExpressionToMethodBody(policy.Expression, entity, domain);
            }
            catch (NotSupportedException) {
                // Q3′ quantifiers (any/all/none/count) and other store-dependent
                // expressions cannot be lowered to standalone C# methods yet.
                // Generate a runtime exception so calling code fails loud.
                body = new Syntactic.Block([
                    new Syntactic.ThrowStatement(
                        new Syntactic.New(
                            new Syntactic.NamedTypeReference("NotSupportedException"),
                            new Syntactic.Constant(
                                $"Policy '{policy.Name}' requires store-aware evaluation and cannot be compiled to standalone C#.")))
                ]);
            }
            methods.Add(new Syntactic.MethodDefinitionNode(
                policy.Name,
                new Syntactic.PrimitiveTypeReference(PrimType.Boolean),
                Body: body,
                AccessModifier: AccessModifier.Public
            ));
        }

        // ── Target entity: subscription registry ──────────────────
        // Fields, register methods, and notify methods for each stage/subscriber pair.
        if (targetSubs is { Count: > 0 }) {
            var emitted = new HashSet<(string Stage, string SourceType)>();
            foreach (var info in targetSubs) {
                var key = (info.StageName, info.SourceEntity.Name);
                if (!emitted.Add(key)) continue;

                var srcType = new Syntactic.NamedTypeReference(info.SourceEntity.Name);
                var fieldName = $"_{ToCamelCase(info.StageName)}Subscribers";
                var paramName = "subscriber";

                // private List<TA>? _DamagedSubscribers;
                fields.Add(new Syntactic.FieldDefinitionNode(
                    fieldName,
                    new Syntactic.OptionalTypeReference(
                        new Syntactic.NamedTypeReference("List",
                            TypeArguments: [srcType])),
                    AccessModifier: AccessModifier.Private
                ));

                // internal void RegisterDamagedSubscriber(TA sub) {
                //     if (_damagedSubscribers == null)
                //         _damagedSubscribers = new List<TA>();
                //     _damagedSubscribers.Add(sub);
                // }
                var fieldAcc = new Syntactic.Member(new Syntactic.ThisReference(), fieldName);
                var registerBody = new Syntactic.Block([
                    new Syntactic.IfStatement(
                        new Syntactic.Equal(fieldAcc, new Syntactic.Constant(null)),
                        new Syntactic.Block([
                            new Syntactic.Assignment(fieldAcc,
                                new Syntactic.New(
                                    new Syntactic.NamedTypeReference("List",
                                        TypeArguments: [srcType])))
                        ])),
                    new Syntactic.Invoke(
                        new Syntactic.Member(fieldAcc, "Add"),
                        [new Syntactic.Parameter(paramName)])
                ]);
                methods.Add(new Syntactic.MethodDefinitionNode(
                    $"Register{info.SourceEntity.Name}{info.StageName}Subscriber",
                    new Syntactic.TypeReference("void"),
                    Parameters: [new Syntactic.Parameter(paramName, srcType)],
                    Body: registerBody,
                    AccessModifier: AccessModifier.Internal
                ));

                // internal void NotifyDamagedSubscribers() {
                //     if (_damagedSubscribers != null)
                //         foreach (var sub in _damagedSubscribers)
                //             sub.WhenBookDamaged();
                // }
                var handlerName = $"When{info.TargetEntity.Name}{info.StageName}";
                var subVar = "sub";
                var foreachBody = new Syntactic.Block([
                    new Syntactic.Invoke(
                        new Syntactic.Member(
                            new Syntactic.Variable(subVar), handlerName))
                ]);
                var notifyBody = new Syntactic.IfStatement(
                    new Syntactic.NotEqual(
                        new Syntactic.Member(new Syntactic.ThisReference(), fieldName),
                        new Syntactic.Constant(null)),
                    new Syntactic.ForEachLoop(
                        new Syntactic.Variable(subVar),
                        new Syntactic.Member(new Syntactic.ThisReference(), fieldName),
                        foreachBody));
                methods.Add(new Syntactic.MethodDefinitionNode(
                    $"Notify{info.StageName}Subscribers",
                    new Syntactic.TypeReference("void"),
                    Body: new Syntactic.Block([notifyBody]),
                    AccessModifier: AccessModifier.Internal
                ));
            }
        }

        // ── Subscriber entity: subscription handler methods ──────
        if (subscriberSubs is { Count: > 0 }) {
            foreach (var info in subscriberSubs) {
                var handlerName = $"When{info.TargetEntity.Name}{info.StageName}";

                // Lower the subscription effects into the handler body
                var subscriptionEffects = info.Subscription.Effects;
                Poly.Syntax.Node handlerBody;
                if (subscriptionEffects.Count > 0) {
                    var context = new LoweringContext(
                        new Syntactic.Parameter("entity",
                            new Syntactic.TypeReference(entity.Name)),
                        UseThisReference: true,
                        LowerStageTransitions: true,
                        Domain: domain,
                        EnumPropertyNames: domain is not null
                            ? BuildEnumPropertyNames(entity, domain)
                            : null
                    );
                    var effectPass = new EffectLoweringPass(entity, context);
                    var composite = new CompositeEffect(subscriptionEffects);
                    handlerBody = effectPass.TryLowerVmNode(composite)
                        ?? new Syntactic.Block([new Syntactic.Comment("no-op")]);
                }
                else {
                    handlerBody = new Syntactic.Block([new Syntactic.Comment("no-op")]);
                }

                methods.Add(new Syntactic.MethodDefinitionNode(
                    handlerName,
                    new Syntactic.TypeReference("void"),
                    Body: handlerBody,
                    AccessModifier: AccessModifier.Internal
                ));
            }
        }

        // ── Stage enum + CurrentStage property ────────────────────
        if (entity.Stages.Count > 0) {
            var enumTypeName = $"{entity.Name}Stage";
            var stageEnumFields = new List<Syntactic.FieldDefinitionNode>();
            for (int si = 0; si < entity.Stages.Count; si++) {
                stageEnumFields.Add(new Syntactic.FieldDefinitionNode(
                    entity.Stages[si].Name,
                    new Syntactic.PrimitiveTypeReference(PrimType.Int32),
                    DefaultValue: new Syntactic.Constant((int)si),
                    AccessModifier: AccessModifier.Public
                ));
            }
            typeDefs.Add(new Syntactic.TypeDefinitionNode(
                enumTypeName,
                Fields: stageEnumFields,
                Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
            ));

            props.Add(new Syntactic.PropertyDefinitionNode(
                "CurrentStage",
                new Syntactic.NamedTypeReference(enumTypeName),
                Getter: new Syntactic.PropertyGetterDefinitionNode(),
                Setter: new Syntactic.PropertySetterDefinitionNode(
                    AccessModifier: AccessModifier.Private)
            ));
        }

        // ── Private constructor + public static Create factory ─────
        List<Syntactic.ConstructorDefinitionNode>? ctors = null;
        if (ctorParams.Count > 0 || entity.Stages.Count > 0) {
            var bodyNodes = new List<Poly.Syntax.Node>();
            bodyNodes.AddRange(ctorAssignments);

            if (entity.Stages.Count > 0) {
                var firstStage = entity.Stages[0];
                bodyNodes.Add(new Syntactic.Assignment(
                    new Syntactic.Member(new Syntactic.ThisReference(), "CurrentStage"),
                    new Syntactic.Member(
                        new Syntactic.NamedTypeReference($"{entity.Name}Stage"),
                        firstStage.Name)));

                // Apply the initial stage's entry effects in the constructor.
                // This ensures properties set by entry effects (e.g. "entry { assign
                // CheckedOutAt to now }" on Loan.Active) are initialized during
                // construction, not just during explicit stage transitions.
                if (firstStage.OnEntryEffects.Count > 0) {
                    var entryCtx = new LoweringContext(
                        new Syntactic.Parameter("entity",
                            new Syntactic.TypeReference(entity.Name)),
                        UseThisReference: true,
                        LowerStageTransitions: false,
                        Domain: domain,
                        EnumPropertyNames: domain is not null
                            ? BuildEnumPropertyNames(entity, domain)
                            : null
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
                bodyNodes.Add(new Syntactic.Invoke(
                    new Syntactic.Member(new Syntactic.ThisReference(), "InitializeSubscriptions")));
            }

            // Private parameterless constructor for EF Core materialization.
            var paramlessBody = new List<Poly.Syntax.Node>();
            foreach (var f in fields) {
                var isOptional = f.FieldType is Syntactic.OptionalTypeReference;
                if (isOptional) continue;
                paramlessBody.Add(new Syntactic.Assignment(
                    new Syntactic.Member(new Syntactic.ThisReference(), f.Name),
                    new Syntactic.New(f.FieldType)));
            }
            if (paramlessBody.Count == 0)
                paramlessBody.Add(new Syntactic.Comment("EF materialization"));
            ctors = [new Syntactic.ConstructorDefinitionNode(
                Parameters: null,
                Body: new Syntactic.Block(paramlessBody),
                AccessModifier: AccessModifier.Private
            )];

            // Full constructor — only the static Create factory can construct
            // instances with data. EntityFramework uses the parameterless ctor.
            ctors = [.. ctors, new Syntactic.ConstructorDefinitionNode(
                Parameters: ctorParams,
                Body: bodyNodes.Count > 0 ? new Syntactic.Block(bodyNodes) : null,
                AccessModifier: AccessModifier.Private
            )];

            // public static DomainResult<EntityName> Create(args...)
            var createResultType = new Syntactic.NamedTypeReference("DomainResult",
                TypeArguments: [new Syntactic.NamedTypeReference(entity.Name)]);

            // Build constraint validation checks before the constructor call.
            // Only entity properties (not navigations) are validated — navs
            // don't carry constraints in the current model.
            var constraintChecks = BuildCreateConstraintChecks(entity, domain);

            // return DomainResult<EntityName>.Success(new EntityName(args...));
            var createSuccessNodes = new List<Poly.Syntax.Node>();
            createSuccessNodes.AddRange(constraintChecks);
            createSuccessNodes.Add(new Syntactic.Return(
                new Syntactic.Invoke(
                    new Syntactic.Member(createResultType, "Success"),
                    [new Syntactic.New(
                        new Syntactic.NamedTypeReference(entity.Name),
                        ctorParams.Select(p => new Syntactic.Parameter(p.Name)).ToArray())])));

            methods.Add(new Syntactic.MethodDefinitionNode(
                "Create",
                createResultType,
                Parameters: ctorParams,
                Body: new Syntactic.Block(createSuccessNodes),
                IsStatic: true,
                AccessModifier: AccessModifier.Public
            ));
        }

        // ── InitializeSubscriptions — for post-load subscription wiring ──
        if (subscriberSubs is { Count: > 0 }) {
            var initBody = new List<Poly.Syntax.Node>();
            AddSubscriberRegistrationNodes(subscriberSubs, initBody);
            if (initBody.Count > 0) {
                methods.Add(new Syntactic.MethodDefinitionNode(
                    "InitializeSubscriptions",
                    new Syntactic.TypeReference("void"),
                    Body: new Syntactic.Block(initBody),
                    AccessModifier: AccessModifier.Private
                ));
            }
        }

        typeDefs.Add(new Syntactic.TypeDefinitionNode(
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
        List<Poly.Syntax.Node> bodyNodes) {
        if (subscriberSubs is { Count: > 0 }) {
            foreach (var group in subscriberSubs.GroupBy(s => s.Relationship.Name)) {
                var rel = group.First().Relationship;
                var pascalNavName = ToPascalCase(rel.Name);
                var isMany = rel.Cardinality is RelationshipCardinality.OneToMany
                             or RelationshipCardinality.ManyToMany;

                if (isMany) {
                    foreach (var info in group) {
                        var subVarName = "target";
                        bodyNodes.Add(new Syntactic.ForEachLoop(
                            new Syntactic.Variable(subVarName),
                            new Syntactic.Member(new Syntactic.ThisReference(), pascalNavName),
                            new Syntactic.Block([
                                new Syntactic.Invoke(
                                    new Syntactic.Member(
                                        new Syntactic.Variable(subVarName),
                                        $"Register{info.SourceEntity.Name}{info.StageName}Subscriber"),
                                    [new Syntactic.ThisReference()])
                            ])
                        ));
                    }
                }
                else {
                    foreach (var info in group) {
                        bodyNodes.Add(new Syntactic.Invoke(
                            new Syntactic.Member(
                                new Syntactic.Member(new Syntactic.ThisReference(), pascalNavName),
                                $"Register{info.SourceEntity.Name}{info.StageName}Subscriber"),
                            [new Syntactic.ThisReference()])
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
        List<Syntactic.MethodDefinitionNode> methods) {

        var pascalName = ToPascalCase(rel.Name);
        var targetTypeName = rel.Target.TypeName;
        var targetType = new Syntactic.NamedTypeReference(targetTypeName);
        var targetEntity = domain.Types.OfType<Entity>()
            .FirstOrDefault(e => string.Equals(e.Name, targetTypeName, StringComparison.Ordinal));
        if (targetEntity is null) return;

        var methodName = $"Create{pascalName}";

        // Collect all target entity constructor-level properties:
        // regular entity properties (minus those with DefaultValueConstraint)
        // plus singular nav properties (one-to-one relationships to other entities)
        var targetRelationships = domain.Relationships
            .Where(r => string.Equals(r.Source.TypeName, targetTypeName, StringComparison.Ordinal)
                     && r.Cardinality is not (RelationshipCardinality.OneToMany or RelationshipCardinality.ManyToMany))
            .ToList();

        var methodParams = new List<Syntactic.Parameter>();
        var createArgs = new List<Poly.Syntax.Node>();

        // Add regular properties without defaults (sorted to match Create factory)
        foreach (var prop in targetEntity.Properties.OrderBy(p => p.Name)) {
            if (prop.Constraints.Any(c => c is DefaultValueConstraint)) continue;
            var paramName = ToCamelCase(prop.Name);
            var propRef = MapDomainTypeRef(prop.Type, domain);
            methodParams.Add(new Syntactic.Parameter(paramName, propRef));
            createArgs.Add(new Syntactic.Parameter(paramName));
        }

        // Add singular navigation properties (e.g. book: Book, borrower: Patron)
        foreach (var trel in targetRelationships) {
            // Skip the back-reference to the source entity (auto-wired as 'this')
            if (string.Equals(trel.Target.TypeName, entity.Name, StringComparison.Ordinal)) continue;

            var paramName = ToCamelCase(trel.Name);
            var trgType = new Syntactic.NamedTypeReference(trel.Target.TypeName);
            methodParams.Add(new Syntactic.Parameter(paramName, trgType));
            createArgs.Add(new Syntactic.Parameter(paramName));
        }

        // Back-ref check — find the relationship where target points back to source
        var backRefRel = targetRelationships
            .FirstOrDefault(r => string.Equals(r.Target.TypeName, entity.Name, StringComparison.Ordinal));
        // Auto-wire back-reference (borrower: Patron → this)
        if (backRefRel is not null) {
            createArgs.Add(new Syntactic.ThisReference());
        }

        // NOTE: properties with DefaultValueConstraint are NOT included in
        // the Target.Create() parameter list — they are set directly in the
        // constructor body from their default expression. Do NOT append them
        // to createArgs here; the Create factory already handles them.

        var bodyNodes = new List<Poly.Syntax.Node>();
        var localResultName = $"{ToCamelCase(targetTypeName)}Result";
        var localName = ToCamelCase(targetTypeName);

        // var loanResult = Loan.Create(book: book, borrower: this, ...);
        bodyNodes.Add(new Syntactic.Variable(localResultName,
            new Syntactic.Invoke(
                new Syntactic.Member(targetType, "Create"),
                [.. createArgs])));

        // Unwrap: the Create factory now returns DomainResult<T>, and it may
        // reject invalid inputs via constraint checks. Since CreateNav methods
        // are only called from action bodies with controlled defaults, a
        // failure here is a programmer error — assert fast.
        bodyNodes.Add(new Syntactic.IfStatement(
            new Syntactic.Not(
                new Syntactic.Member(
                    new Syntactic.Variable(localResultName), "IsSuccess")),
            new Syntactic.Block([
                new Syntactic.ThrowStatement(
                    new Syntactic.New(
                        new Syntactic.NamedTypeReference("InvalidOperationException"),
                        new Syntactic.Member(
                            new Syntactic.Variable(localResultName), "ErrorMessage")))
            ])));

        // var loan = loanResult.Value;
        bodyNodes.Add(new Syntactic.Variable(localName,
            new Syntactic.Member(
                new Syntactic.Variable(localResultName), "Value")));

        // _loans.Add(loan);
        bodyNodes.Add(new Syntactic.Invoke(
            new Syntactic.Member(
                new Syntactic.Member(new Syntactic.ThisReference(), fieldName), "Add"),
            [new Syntactic.Variable(localName)]));

        // Subscription registration: loan.RegisterPatronOverdueSubscriber(this)
        if (subscriberSubs is { Count: > 0 }) {
            foreach (var info in subscriberSubs) {
                if (string.Equals(info.Relationship.Name, rel.Name, StringComparison.Ordinal)) {
                    bodyNodes.Add(new Syntactic.Invoke(
                        new Syntactic.Member(
                            new Syntactic.Variable(localName),
                            $"Register{info.SourceEntity.Name}{info.StageName}Subscriber"),
                        [new Syntactic.ThisReference()]));
                }
            }
        }

        // return loan;
        bodyNodes.Add(new Syntactic.Return(new Syntactic.Variable(localName)));

        methods.Add(new Syntactic.MethodDefinitionNode(
            methodName,
            targetType,
            Parameters: methodParams.Count > 0 ? methodParams : null,
            Body: new Syntactic.Block(bodyNodes),
            AccessModifier: AccessModifier.Private
        ));
    }

    /// <summary>Returns a default-value Syntax node for a property (null, 0, false, etc.).</summary>
    private static Poly.Syntax.Node DefaultValueForProp(Property prop, Domain domain) {
        var defaultValue = prop.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault();
        if (defaultValue is not null) {
            var runtimeExpr = EffectLoweringPass.LowerDefaultExpression(defaultValue.Expression);
            if (runtimeExpr is not null) return runtimeExpr;
            if (defaultValue.Expression is Poly.DomainModeling.Literal lit)
                return new Syntactic.Constant(lit.Value);
            if (defaultValue.Expression is Poly.DomainModeling.PropertyAccess pa) {
                var enumTypes = domain.Types.OfType<EnumType>()
                    .ToDictionary(e => e.Name, StringComparer.Ordinal);
                if (enumTypes.TryGetValue(prop.Type.TypeName, out var enumType))
                    return new Syntactic.Member(new Syntactic.NamedTypeReference(enumType.Name), pa.Name);
            }
        }
        return new Syntactic.Constant(null);
    }

    /// <summary>
    /// Builds constraint-validation guard clauses for the <c>Create</c> factory method.
    /// Each constraint on a constructor-parameter property produces an early-return
    /// guard: <c>if (violation) return DomainResult&lt;T&gt;.Failure("'Prop' ...");</c>
    ///
    /// Only entity properties (not navigation properties) are validated — navs do not
    /// carry constraints in the current domain model.
    /// </summary>
    private static List<Poly.Syntax.Node> BuildCreateConstraintChecks(
        Entity entity, Domain? domain) {

        var checks = new List<Poly.Syntax.Node>();
        var entityTypeRef = new Syntactic.NamedTypeReference(entity.Name);
        var resultType = new Syntactic.NamedTypeReference("DomainResult",
            TypeArguments: [entityTypeRef]);

        foreach (var prop in entity.Properties.OrderBy(p => p.Name)) {
            // Only properties without DefaultValueConstraint are constructor params
            if (prop.Constraints.Any(c => c is DefaultValueConstraint)) continue;

            var paramName = ToCamelCase(prop.Name);
            var paramRef = new Syntactic.Parameter(paramName);
            var isText = string.Equals(prop.Type.TypeName, "Text", StringComparison.Ordinal)
                      || string.Equals(prop.Type.TypeName, "String", StringComparison.Ordinal);
            var isNumber = string.Equals(prop.Type.TypeName, "Number", StringComparison.Ordinal)
                        || string.Equals(prop.Type.TypeName, "Int", StringComparison.Ordinal);

            Poly.Syntax.Node Failure(string msg) => new Syntactic.Return(
                new Syntactic.Invoke(
                    new Syntactic.Member(resultType, "Failure"),
                    new Syntactic.Constant(msg)));

            foreach (var constraint in prop.Constraints) {
                switch (constraint) {
                    case RequiredConstraint:
                        // Required: skip for value types (Number, Boolean,
                        // DateTime, etc.) — they can never be null at runtime.
                        // Only Text/String and entity reference types benefit
                        // from runtime required checks.
                        if (isText) {
                            checks.Add(new Syntactic.IfStatement(
                                new Syntactic.Invoke(
                                    new Syntactic.Member(
                                        new Syntactic.NamedTypeReference("string"),
                                        "IsNullOrEmpty"),
                                    [paramRef]),
                                new Syntactic.Block([Failure(
                                    $"'{prop.Name}' is required.")])));
                        }
                        else if (IsNullableDomainType(prop.Type.TypeName)) {
                            checks.Add(new Syntactic.IfStatement(
                                new Syntactic.Equal(paramRef, new Syntactic.Constant(null)),
                                new Syntactic.Block([Failure(
                                    $"'{prop.Name}' is required.")])));
                        }
                        break;

                    case RangeConstraint r:
                        if (isNumber && r.Minimum is not null) {
                            var minVal = ConvertToConstant(r.Minimum);
                            if (minVal is not null) {
                                checks.Add(new Syntactic.IfStatement(
                                    new Syntactic.LessThan(paramRef, minVal),
                                    new Syntactic.Block([Failure(
                                        $"'{prop.Name}' must be >= {FormatConstraintValue(r.Minimum)}.")])));
                            }
                        }
                        if (isNumber && r.Maximum is not null) {
                            var maxVal = ConvertToConstant(r.Maximum);
                            if (maxVal is not null) {
                                checks.Add(new Syntactic.IfStatement(
                                    new Syntactic.GreaterThan(paramRef, maxVal),
                                    new Syntactic.Block([Failure(
                                        $"'{prop.Name}' must be <= {FormatConstraintValue(r.Maximum)}.")])));
                            }
                        }
                        break;

                    case LengthConstraint l:
                        if (isText) {
                            var lenAccess = new Syntactic.Member(paramRef, "Length");
                            if (l.MinLength > 0) {
                                checks.Add(new Syntactic.IfStatement(
                                    new Syntactic.LessThan(lenAccess,
                                        new Syntactic.Constant((long)l.MinLength)),
                                    new Syntactic.Block([Failure(
                                        $"'{prop.Name}' must be at least {l.MinLength} characters.")])));
                            }
                            if (l.MaxLength < int.MaxValue) {
                                checks.Add(new Syntactic.IfStatement(
                                    new Syntactic.GreaterThan(lenAccess,
                                        new Syntactic.Constant((long)l.MaxLength)),
                                    new Syntactic.Block([Failure(
                                        $"'{prop.Name}' must be at most {l.MaxLength} characters.")])));
                            }
                        }
                        break;

                    case PatternConstraint p:
                        if (isText) {
                            checks.Add(new Syntactic.IfStatement(
                                new Syntactic.Not(
                                    new Syntactic.Invoke(
                                        new Syntactic.Member(
                                            new Syntactic.NamedTypeReference(
                                                "System.Text.RegularExpressions.Regex"),
                                            "IsMatch"),
                                        [paramRef,
                                         new Syntactic.Constant(p.Pattern)])),
                                new Syntactic.Block([Failure(
                                    $"'{prop.Name}' does not match the required pattern.")])));
                        }
                        break;

                    case EqualityConstraint eq:
                        if (eq.ExpectedValue is not null) {
                            checks.Add(new Syntactic.IfStatement(
                                new Syntactic.NotEqual(paramRef,
                                    new Syntactic.Constant(eq.ExpectedValue)),
                                new Syntactic.Block([Failure(
                                    $"'{prop.Name}' must equal {eq.ExpectedValue}.")])));
                        }
                        break;

                        // DefaultValueConstraint, UniqueConstraint, EnumConstraint
                        // are not validated at factory time:
                        //   • Default → already handled (only non-default props are params)
                        //   • Unique  → requires store awareness
                        //   • Enum    → enforced by the type system at the compiler level
                }
            }
        }

        return checks;
    }

    /// <summary>Converts a constraint value object to a Syntax Constant.</summary>
    private static Syntactic.Constant? ConvertToConstant(object? value) {
        if (value is null) return null;
        if (value is long l) return new Syntactic.Constant(l);
        if (value is int i) return new Syntactic.Constant((long)i);
        if (value is double d) return d == Math.Floor(d)
            ? new Syntactic.Constant((long)d)
            : new Syntactic.Constant(d);
        if (value is decimal m) return new Syntactic.Constant((double)m);
        if (value is string s) return new Syntactic.Constant(s);
        if (value is bool b) return new Syntactic.Constant(b);
        return new Syntactic.Constant(value?.ToString());
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
    private static bool IsNullableDomainType(string typeName) => typeName switch {
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

    /// <summary>
    /// Returns a CLR-appropriate default value Syntax node for a domain type
    /// reference (e.g., <c>false</c> for <c>Boolean</c>, <c>0</c> for <c>Number</c>,
    /// <c>null</c> for <c>Text</c>). For enum types, returns the first member.
    /// Used by <see cref="BuildActionBodyWithGuards"/> for guard-clause default returns.
    /// </summary>
    private static Poly.Syntax.Node DefaultValueForTypeRef(DomainTypeReference typeRef, Domain? domain) {
        if (domain is not null) {
            var enumType = domain.Types.OfType<EnumType>()
                .FirstOrDefault(e => string.Equals(e.Name, typeRef.TypeName, StringComparison.Ordinal));
            if (enumType is not null && enumType.MemberNames.Count > 0)
                return new Syntactic.Member(
                    new Syntactic.NamedTypeReference(enumType.Name), enumType.MemberNames[0]);
        }
        return typeRef.TypeName switch {
            "Text" or "String" => new Syntactic.Constant(""),
            "Number" or "Int" or "Int64" => new Syntactic.Constant(0L),
            "Int32" => new Syntactic.Constant(0),
            "Boolean" or "Bool" => new Syntactic.Constant(false),
            "DateTime" or "Timestamp" => new Syntactic.Member(
                new Syntactic.NamedTypeReference("DateTime"), "MinValue"),
            "Date" or "DateOnly" => new Syntactic.Member(
                new Syntactic.NamedTypeReference("DateOnly"), "MinValue"),
            "Guid" or "Uuid" => new Syntactic.Member(
                new Syntactic.NamedTypeReference("Guid"), "Empty"),
            _ => new Syntactic.Constant(null),
        };
    }

    // ── Action method builder ───────────────────────────────────

    private static void AddActionMethod(Entity entity, Poly.DomainModeling.Action action,
        List<Syntactic.MethodDefinitionNode> methods, string? stageEnumTypeName = null,
        IReadOnlyDictionary<string, IReadOnlyList<Node>>? postTransitionNodes = null,
        string? sourceStageName = null, Domain? domain = null) {
        var paramNames = new HashSet<string>(
            action.Parameters.Select(p => p.Name), StringComparer.Ordinal);
        var effectsBody = LowerActionToMethodBody(entity, action, paramNames, stageEnumTypeName,
            postTransitionNodes, sourceStageName, domain);

        // Build the full method body: require guards first, then effects
        var isVoid = action.Result is not { Members.Count: > 0 };
        var body = BuildActionBodyWithGuards(action, entity, effectsBody, domain,
            sourceStageName, stageEnumTypeName, isVoid);

        // All actions return DomainResult (void) or DomainResult<T> (typed).
        // This lets callers pattern-match on IsSuccess without exceptions.
        Poly.Syntax.Node returnType;
        if (isVoid) {
            returnType = new Syntactic.NamedTypeReference("DomainResult");
        }
        else {
            var innerType = MapDomainTypeRef(action.Result.Members[0].Type, domain);
            returnType = new Syntactic.NamedTypeReference("DomainResult",
                TypeArguments: [innerType]);
        }

        methods.Add(new Syntactic.MethodDefinitionNode(
            action.Name,
            returnType,
            Parameters: action.Parameters
                .Select(p => new Syntactic.Parameter(p.Name, MapDomainTypeRef(p.Type, domain)))
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
    /// Returns a <see cref="Syntactic.Block"/> with at least an empty body — never null —
    /// so the C# generator emits <c>{ }</c> rather than an invalid semicolon.
    /// </summary>
    private static Syntactic.Block BuildActionBodyWithGuards(
        Poly.DomainModeling.Action action, Entity entity, Poly.Syntax.Node? effectsBody,
        Domain? domain = null, string? sourceStageName = null, string? stageEnumTypeName = null,
        bool isVoid = true) {

        // Build the DomainResult type reference for failure/success returns
        Poly.Syntax.Node actionResultType;
        Poly.Syntax.Node resultTypeRef;
        if (isVoid) {
            actionResultType = new Syntactic.NamedTypeReference("DomainResult");
            resultTypeRef = new Syntactic.NamedTypeReference("DomainResult");
        }
        else {
            resultTypeRef = MapDomainTypeRef(action.Result!.Members[0].Type, domain);
            actionResultType = new Syntactic.NamedTypeReference("DomainResult",
                TypeArguments: [resultTypeRef]);
        }

        // Helper: DomainResult[<T>].Failure("message")
        Poly.Syntax.Node FailureReturn(string message) => new Syntactic.Return(
            new Syntactic.Invoke(
                new Syntactic.Member(actionResultType, "Failure"),
                new Syntactic.Constant(message)));

        // Collect all nodes: require guards first, then effects
        var nodes = new List<Poly.Syntax.Node>();

        // Emit stage guard for stage-scoped actions.
        // Returns DomainResult[<T>].Failure("'CheckOut' requires stage 'Active' on entity 'Patron'.")
        if (sourceStageName is not null && stageEnumTypeName is not null) {
            nodes.Add(new Syntactic.IfStatement(
                new Syntactic.NotEqual(
                    new Syntactic.Member(new Syntactic.ThisReference(), "CurrentStage"),
                    new Syntactic.Member(
                        new Syntactic.NamedTypeReference(stageEnumTypeName),
                        sourceStageName)),
                new Syntactic.Block([
                    FailureReturn(
                        $"'{action.Name}' requires stage '{sourceStageName}' on entity '{entity.Name}'.")
                ])));
        }

        // Emit require guard clauses referencing entity-level policy methods
        // Returns DomainResult[<T>].Failure("'CheckOut' blocked by policy 'AtLimit'.")
        foreach (var policy in action.Policies) {
            if (policy.Name.StartsWith("not_", StringComparison.Ordinal)) {
                var realName = policy.Name.Substring(4);
                var guardCall = new Syntactic.Invoke(
                    new Syntactic.Member(new Syntactic.ThisReference(), realName));
                nodes.Add(new Syntactic.IfStatement(
                    guardCall,
                    new Syntactic.Block([FailureReturn(
                        $"'{action.Name}' blocked by policy '{realName}'.")])));
            }
            else {
                var guardCall = new Syntactic.Invoke(
                    new Syntactic.Member(new Syntactic.ThisReference(), policy.Name));
                nodes.Add(new Syntactic.IfStatement(
                    new Syntactic.Not(guardCall),
                    new Syntactic.Block([FailureReturn(
                        $"'{action.Name}' blocked by policy '{policy.Name}'.")])));
            }
        }

        // Append the effects body
        if (effectsBody is Syntactic.Block block) {
            nodes.AddRange(block.Nodes);
        }
        else if (effectsBody is not null) {
            nodes.Add(effectsBody);
        }

        if (isVoid) {
            // Void actions end with return DomainResult.Success();
            nodes.Add(new Syntactic.Return(
                new Syntactic.Invoke(
                    new Syntactic.Member(
                        new Syntactic.NamedTypeReference("DomainResult"), "Success"))));
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

                if (last is Syntactic.Return) {
                    // Already wrapped — leave as-is.
                }
                else if (last is Syntactic.Assignment or Syntactic.Invoke
                         or Syntactic.Member or Syntactic.Constant
                         or Syntactic.New or Syntactic.UnaryMinus
                         or Syntactic.Not or Syntactic.Add or Syntactic.Subtract
                         or Syntactic.Multiply or Syntactic.Divide) {
                    nodes[lastIdx] = new Syntactic.Return(
                        new Syntactic.Invoke(
                            new Syntactic.Member(actionResultType, "Success"),
                            [last]));
                }
                else if (last is Syntactic.Variable { Value: not null } v) {
                    // var x = expr → return DomainResult<T>.Success(expr)
                    nodes[lastIdx] = new Syntactic.Return(
                        new Syntactic.Invoke(
                            new Syntactic.Member(actionResultType, "Success"),
                            [v.Value]));
                }
                else {
                    // Non-returnable last node — structural error (still throw)
                    nodes.Add(new Syntactic.ThrowStatement(
                        new Syntactic.New(
                            new Syntactic.NamedTypeReference("NotSupportedException"),
                            new Syntactic.Constant(
                                $"Action '{action.Name}' has return type but its last effect " +
                                $"does not produce a value. Use an 'assign' statement as the " +
                                $"final effect, or remove the -> return type declaration."))));
                }
            }
            else {
                // Empty action body with declared return type — structural error
                nodes.Add(new Syntactic.ThrowStatement(
                    new Syntactic.New(
                        new Syntactic.NamedTypeReference("NotSupportedException"),
                        new Syntactic.Constant(
                            $"Action '{action.Name}' has return type but has no effects."))));
            }
        }

        // Block requires ≥1 node; use Comment for empty method bodies
        if (nodes.Count == 0)
            nodes.Add(new Syntactic.Comment("no-op"));

        return new Syntactic.Block(nodes);
    }

    // ── Lowering helpers ────────────────────────────────────────

    internal static Poly.Syntax.Node? LowerActionToMethodBody(
        Entity entity, Poly.DomainModeling.Action action,
        HashSet<string>? paramNames = null, string? stageEnumTypeName = null,
        IReadOnlyDictionary<string, IReadOnlyList<Node>>? postTransitionNodes = null,
        string? sourceStageName = null, Domain? domain = null) {
        if (action.Effects.Count == 0) return null;
        var enumProps = domain is not null ? BuildEnumPropertyNames(entity, domain) : null;
        var context = new LoweringContext(
            new Syntactic.Parameter("entity", new Syntactic.TypeReference(entity.Name)),
            UseThisReference: true,
            ActionParameterNames: paramNames,
            LowerStageTransitions: true,
            StageEnumTypeName: stageEnumTypeName,
            PostTransitionNodes: postTransitionNodes,
            SourceStageName: sourceStageName,
            Domain: domain,
            EnumPropertyNames: enumProps
        );
        var effectPass = new EffectLoweringPass(entity, context);
        var composite = new CompositeEffect(action.Effects);
        return effectPass.TryLowerVmNode(composite);
    }

    internal static Poly.Syntax.Node? LowerExpressionToMethodBody(
        DomainExpression expr, Entity entity, Domain? domain = null) {
        var enumProps = domain is not null ? BuildEnumPropertyNames(entity, domain) : null;
        var context = new LoweringContext(
            new Syntactic.Parameter("entity", new Syntactic.TypeReference(entity.Name)),
            UseThisReference: true,
            EnumPropertyNames: enumProps
        );
        var pass = new DomainExpressionLoweringPass(context);
        var lowered = pass.Lower(expr, new Syntactic.Parameter("entity"));
        return lowered is not null
            ? new Syntactic.Block([new Syntactic.Return(lowered)])
            : null;
    }

    /// <summary>
    /// Builds a map from property name → enum type name for all properties of
    /// <paramref name="entity"/> whose type resolves to an <see cref="EnumType"/>.
    /// Used by the expression lowering pass to emit qualified enum member access.
    /// </summary>
    internal static Dictionary<string, string>? BuildEnumPropertyNames(
        Entity entity, Domain domain) {
        Dictionary<string, string>? map = null;
        var enumTypes = domain.Types.OfType<EnumType>()
            .ToDictionary(e => e.Name, StringComparer.Ordinal);
        if (enumTypes.Count == 0) return null;

        foreach (var prop in entity.Properties) {
            if (enumTypes.TryGetValue(prop.Type.TypeName, out _)) {
                (map ??= new(StringComparer.Ordinal))[prop.Name] = prop.Type.TypeName;
            }
        }
        return map;
    }

    // ── Type mapping ────────────────────────────────────────────

    internal static Poly.Syntax.Node MapDomainTypeRef(DomainTypeReference domainType,
        Domain? domain = null) {
        var typeName = domainType.TypeName;

        // Check for enum types in the domain
        if (domain is not null) {
            var enumType = domain.Types.OfType<EnumType>()
                .FirstOrDefault(e => string.Equals(e.Name, typeName, StringComparison.Ordinal));
            if (enumType is not null)
                return new Syntactic.NamedTypeReference(typeName);
        }

        return typeName switch {
            "Text" => new Syntactic.PrimitiveTypeReference(PrimType.String),
            "Number" or "Int" => new Syntactic.PrimitiveTypeReference(PrimType.Int64),
            "Boolean" or "Bool" => new Syntactic.PrimitiveTypeReference(PrimType.Boolean),
            "DateTime" or "Timestamp" => new Syntactic.PrimitiveTypeReference(PrimType.DateTime),
            "Date" or "DateOnly" => new Syntactic.PrimitiveTypeReference(PrimType.DateOnly),
            "Time" or "TimeOnly" => new Syntactic.PrimitiveTypeReference(PrimType.TimeOnly),
            "Duration" or "TimeSpan" => new Syntactic.PrimitiveTypeReference(PrimType.TimeSpan),
            "Uuid" or "Guid" => new Syntactic.PrimitiveTypeReference(PrimType.Guid),
            "Decimal" => new Syntactic.PrimitiveTypeReference(PrimType.Decimal),
            "Float" or "Double" => new Syntactic.PrimitiveTypeReference(PrimType.Float64),
            _ => new Syntactic.NamedTypeReference(typeName)
        };
    }

    // ── DomainResult infrastructure type builders ───────────────

    /// <summary>
    /// Builds the <c>DomainResult</c> record struct: a discrimated-union-like
    /// result type for void actions. Methods return <c>DomainResult.Success()</c>
    /// or <c>DomainResult.Failure(message)</c> instead of throwing or returning void.
    /// Consumers switch on <see cref="IsSuccess"/> to handle success/failure.
    /// </summary>
    internal static Syntactic.TypeDefinitionNode BuildDomainResultTypeDef() {
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

        var props = new List<Syntactic.PropertyDefinitionNode>
        {
            new("IsSuccess",
                new Syntactic.PrimitiveTypeReference(PrimType.Boolean),
                Getter: new Syntactic.PropertyGetterDefinitionNode()),
            new("ErrorMessage",
                new Syntactic.OptionalTypeReference(
                    new Syntactic.PrimitiveTypeReference(PrimType.String)),
                Getter: new Syntactic.PropertyGetterDefinitionNode()),
        };

        var ctor = new Syntactic.ConstructorDefinitionNode(
            Parameters: [
                new Syntactic.Parameter("isSuccess",
                    new Syntactic.PrimitiveTypeReference(PrimType.Boolean)),
                new Syntactic.Parameter("errorMessage",
                    new Syntactic.OptionalTypeReference(
                        new Syntactic.PrimitiveTypeReference(PrimType.String))),
            ],
            Body: new Syntactic.Block([
                new Syntactic.Assignment(
                    new Syntactic.Member(new Syntactic.ThisReference(), "IsSuccess"),
                    new Syntactic.Parameter("isSuccess")),
                new Syntactic.Assignment(
                    new Syntactic.Member(new Syntactic.ThisReference(), "ErrorMessage"),
                    new Syntactic.Parameter("errorMessage")),
            ]),
            AccessModifier: AccessModifier.Private
        );

        var methods = new List<Syntactic.MethodDefinitionNode>
        {
            new("Success",
                new Syntactic.NamedTypeReference("DomainResult"),
                Body: new Syntactic.Block([
                    new Syntactic.Return(
                        new Syntactic.New(
                            new Syntactic.NamedTypeReference("DomainResult"),
                            new Syntactic.Constant(true),
                            new Syntactic.Constant(null)))
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public),
            new("Failure",
                new Syntactic.NamedTypeReference("DomainResult"),
                Parameters: [
                    new Syntactic.Parameter("message",
                        new Syntactic.PrimitiveTypeReference(PrimType.String))
                ],
                Body: new Syntactic.Block([
                    new Syntactic.Return(
                        new Syntactic.New(
                            new Syntactic.NamedTypeReference("DomainResult"),
                            new Syntactic.Constant(false),
                            new Syntactic.Parameter("message")))
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public),
        };

        return new Syntactic.TypeDefinitionNode(
            "DomainResult",
            Properties: props,
            Constructors: [ctor],
            Methods: methods,
            Semantics: Syntactic.TypeDefinitionSemantics.ImmutableValue
        );
    }

    /// <summary>
    /// Builds the <c>DomainResult&lt;T&gt;</c> generic record struct: a typed result
    /// for non-void actions. Returns <c>DomainResult&lt;T&gt;.Success(value)</c> or
    /// <c>DomainResult&lt;T&gt;.Failure(message)</c>.
    /// </summary>
    internal static Syntactic.TypeDefinitionNode BuildDomainResultGenericTypeDef() {
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

        var tParam = new Syntactic.NamedTypeReference("T");
        var actionResultT = new Syntactic.NamedTypeReference("DomainResult",
            TypeArguments: [tParam]);

        var props = new List<Syntactic.PropertyDefinitionNode>
        {
            new("IsSuccess",
                new Syntactic.PrimitiveTypeReference(PrimType.Boolean),
                Getter: new Syntactic.PropertyGetterDefinitionNode()),
            new("Value", tParam,
                Getter: new Syntactic.PropertyGetterDefinitionNode()),
            new("ErrorMessage",
                new Syntactic.OptionalTypeReference(
                    new Syntactic.PrimitiveTypeReference(PrimType.String)),
                Getter: new Syntactic.PropertyGetterDefinitionNode()),
        };

        var ctor = new Syntactic.ConstructorDefinitionNode(
            Parameters: [
                new Syntactic.Parameter("isSuccess",
                    new Syntactic.PrimitiveTypeReference(PrimType.Boolean)),
                new Syntactic.Parameter("value", tParam),
                new Syntactic.Parameter("errorMessage",
                    new Syntactic.OptionalTypeReference(
                        new Syntactic.PrimitiveTypeReference(PrimType.String))),
            ],
            Body: new Syntactic.Block([
                new Syntactic.Assignment(
                    new Syntactic.Member(new Syntactic.ThisReference(), "IsSuccess"),
                    new Syntactic.Parameter("isSuccess")),
                new Syntactic.Assignment(
                    new Syntactic.Member(new Syntactic.ThisReference(), "Value"),
                    new Syntactic.Parameter("value")),
                new Syntactic.Assignment(
                    new Syntactic.Member(new Syntactic.ThisReference(), "ErrorMessage"),
                    new Syntactic.Parameter("errorMessage")),
            ]),
            AccessModifier: AccessModifier.Private
        );

        var methods = new List<Syntactic.MethodDefinitionNode>
        {
            new("Success", actionResultT,
                Parameters: [
                    new Syntactic.Parameter("value", tParam)
                ],
                Body: new Syntactic.Block([
                    new Syntactic.Return(
                        new Syntactic.New(actionResultT,
                            new Syntactic.Constant(true),
                            new Syntactic.Parameter("value"),
                            new Syntactic.Constant(null)))
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public),
            new("Failure", actionResultT,
                Parameters: [
                    new Syntactic.Parameter("message",
                        new Syntactic.PrimitiveTypeReference(PrimType.String))
                ],
                Body: new Syntactic.Block([
                    new Syntactic.Return(
                        new Syntactic.New(actionResultT,
                            new Syntactic.Constant(false),
                            new Syntactic.NullForgiving(new Syntactic.Default()),
                            new Syntactic.Parameter("message")))
                ]),
                IsStatic: true,
                AccessModifier: AccessModifier.Public),
        };

        return new Syntactic.TypeDefinitionNode(
            "DomainResult",
            GenericParameters: [new Syntactic.Parameter("T")],
            Properties: props,
            Constructors: [ctor],
            Methods: methods,
            Semantics: Syntactic.TypeDefinitionSemantics.ImmutableValue
        );
    }

    // ── String helpers ──────────────────────────────────────────

    internal static string ToCamelCase(string name) => DomainTypeMapping.ToCamelCase(name);

    internal static string ToPascalCase(string name) => DomainTypeMapping.ToPascalCase(name);
}