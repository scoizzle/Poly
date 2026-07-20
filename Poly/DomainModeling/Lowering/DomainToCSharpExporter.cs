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
        AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(analysis);
        var domainRelationships = domain.Relationships.ToList();
        var entities = domain.Types.OfType<Entity>().ToList();
        var entityLookup = entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var result = new List<Syntactic.TypeDefinitionNode>();

        // ── Collect all subscriptions ─────────────────────────────
        var subscriptionsByTarget = new Dictionary<string, List<SubscriptionInfo>>(
            StringComparer.Ordinal);
        var subscriptionsBySubscriber = new Dictionary<string, List<SubscriptionInfo>>(
            StringComparer.Ordinal);

        foreach (var entity in entities) {
            var subList = new List<SubscriptionInfo>();
            foreach (var stage in entity.Stages) {
                foreach (var sub in stage.Subscriptions) {
                    // Resolve the target entity via the relationship
                    var rel = domainRelationships.FirstOrDefault(r =>
                        string.Equals(r.Name, sub.RelationshipName, StringComparison.Ordinal) &&
                        string.Equals(r.Source.TypeName, entity.Name, StringComparison.Ordinal));
                    if (rel is null) continue;

                    if (!entityLookup.TryGetValue(rel.Target.TypeName, out var targetEntity))
                        continue;

                    foreach (var stageName in sub.StageNames) {
                        var info = new SubscriptionInfo(stageName, sub, entity, targetEntity, rel);
                        subList.Add(info);

                        if (!subscriptionsByTarget.TryGetValue(targetEntity.Name, out var targetList))
                            subscriptionsByTarget[targetEntity.Name] = targetList = new();
                        targetList.Add(info);
                    }
                }
            }
            if (subList.Count > 0)
                subscriptionsBySubscriber[entity.Name] = subList;
        }

        // ── Build type defs with subscription context ──────────────
        foreach (var entity in entities) {
            var targetSubs = subscriptionsByTarget.GetValueOrDefault(entity.Name);
            var subscriberSubs = subscriptionsBySubscriber.GetValueOrDefault(entity.Name);

            result.AddRange(BuildTypeDefsForEntity(
                entity, domainRelationships, entityLookup, analysis,
                targetSubs, subscriberSubs));
        }

        return result;
    }

    // ── Per-entity builder ──────────────────────────────────────

    internal IReadOnlyList<Syntactic.TypeDefinitionNode> BuildTypeDefsForEntity(
        Entity entity,
        IReadOnlyList<Relationship> domainRelationships,
        IReadOnlyDictionary<string, Entity> entityLookup,
        AnalysisResult analysis,
        List<SubscriptionInfo>? targetSubs = null,
        List<SubscriptionInfo>? subscriberSubs = null) {

        var typeDefs = new List<Syntactic.TypeDefinitionNode>();
        var props = new List<Syntactic.PropertyDefinitionNode>();
        var methods = new List<Syntactic.MethodDefinitionNode>();
        var fields = new List<Syntactic.FieldDefinitionNode>();
        var ctorParams = new List<Syntactic.Parameter>();
        var ctorAssignments = new List<Poly.Syntax.Node>();

        // ── Resolve inheritance from analysis metadata ─────────────
        var effective = analysis.GetMetadata<EffectiveMemberMetadata>(entity)
            ?? throw new InvalidOperationException(
                $"Missing EffectiveMemberMetadata for entity '{entity.Name}'. " +
                "Ensure SemanticDomainAnalyzer ran on the domain.");

        var effectiveProperties = effective.EffectiveProperties;
        var effectiveActions = effective.EffectiveActions;
        var effectivePolicies = effective.EffectivePolicies;
        var effectiveStages = effective.EffectiveStages;

        string? baseTypeName = null;
        if (entity.ParentEntityName is not null
            && entityLookup.TryGetValue(entity.ParentEntityName, out var parentEntity)) {
            baseTypeName = parentEntity.Name;
        }

        var ownPropertyNames = new HashSet<string>(
            entity.Properties.Select(p => p.Name), StringComparer.Ordinal);

        var stageEnumOwner = GetStageEnumOwner(entity, entityLookup);

        // Determine stage enum owner (root ancestor that defines stages)
        stageEnumOwner = GetStageEnumOwner(entity, entityLookup);

        // ── Common property: IsDeleted (always emitted) ────────────
        props.Add(new Syntactic.PropertyDefinitionNode(
            "IsDeleted",
            new Syntactic.PrimitiveTypeReference(PrimType.Boolean),
            Getter: new Syntactic.PropertyGetterDefinitionNode(),
            Setter: new Syntactic.PropertySetterDefinitionNode(
                AccessModifier: AccessModifier.Private)
        ));

        // ── Entity properties (own only for declaration, effective for ctor) ──
        foreach (var prop in (baseTypeName is not null
            ? entity.Properties.OrderBy(p => p.Name)
            : effectiveProperties.OrderBy(p => p.Name))) {
            var propRef = MapDomainTypeRef(prop.Type);
            List<Poly.Syntax.Node>? constraints = null;
            if (prop.Constraints.Any(c => c is RequiredConstraint))
                constraints = [new Syntactic.Constant("required")];

            props.Add(new Syntactic.PropertyDefinitionNode(
                prop.Name, propRef,
                Getter: new Syntactic.PropertyGetterDefinitionNode(),
                Setter: new Syntactic.PropertySetterDefinitionNode(
                    AccessModifier: AccessModifier.Protected),
                Constraints: constraints
            ));
        }

        // ── Constructor params and assignments (ALL effective properties) ──
        foreach (var prop in effectiveProperties.OrderBy(p => p.Name)) {
            var propRef = MapDomainTypeRef(prop.Type);
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

            Poly.Syntax.Node propType;
            Poly.Syntax.Node ctorParamType;
            if (isMany) {
                propType = new Syntactic.NamedTypeReference("IReadOnlyList",
                    TypeArguments: [targetType]);
                ctorParamType = new Syntactic.CollectionTypeReference(targetType);
            }
            else {
                propType = targetType;
                ctorParamType = targetType;
            }

            var paramName = ToCamelCase(pascalName);
            ctorParams.Add(new Syntactic.Parameter(paramName, ctorParamType));
            ctorAssignments.Add(new Syntactic.Assignment(
                new Syntactic.Member(new Syntactic.ThisReference(), pascalName),
                new Syntactic.Parameter(paramName)));

            props.Add(new Syntactic.PropertyDefinitionNode(
                pascalName, propType,
                Getter: new Syntactic.PropertyGetterDefinitionNode(),
                Setter: new Syntactic.PropertySetterDefinitionNode(
                    AccessModifier: AccessModifier.Protected)
            ));
        }

        // ── Actions as void methods (own only for inheritance) ────
        var stageEnumTypeName = $"{stageEnumOwner}Stage";

        // Build post-transition notification nodes (subscription fan-out on target entity)
        Dictionary<string, IReadOnlyList<Node>>? postTransitionNodes = null;
        if (targetSubs is { Count: > 0 }) {
            postTransitionNodes = new Dictionary<string, IReadOnlyList<Node>>(
                StringComparer.Ordinal);
            foreach (var group in targetSubs.GroupBy(s => s.StageName)) {
                var nodes = new List<Node>();
                foreach (var info in group) {
                    // this.Notify{Stage}Subscribers();
                    nodes.Add(new Syntactic.Invoke(
                        new Syntactic.Member(new Syntactic.ThisReference(),
                            $"Notify{info.StageName}Subscribers")));
                }
                postTransitionNodes[group.Key] = nodes;
            }
        }

        var actionsToEmit = baseTypeName is not null ? entity.Actions : effectiveActions;
        foreach (var action in actionsToEmit)
            AddActionMethod(entity, action, methods, stageEnumTypeName, postTransitionNodes);
        foreach (var stage in (baseTypeName is not null ? entity.Stages : effectiveStages))
            foreach (var action in stage.Actions)
                AddActionMethod(entity, action, methods, stageEnumTypeName, postTransitionNodes);

        // ── Policies as bool methods (own only for inheritance) ───
        var policiesToEmit = baseTypeName is not null ? entity.Policies : effectivePolicies;
        foreach (var policy in policiesToEmit) {
            var body = LowerExpressionToMethodBody(policy.Expression, entity);
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
                var fieldName = $"_{info.StageName}Subscribers";
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
                Poly.Syntax.Node? handlerBody = null;
                if (subscriptionEffects.Count > 0) {
                    var context = new LoweringContext(
                        new Syntactic.Parameter("entity",
                            new Syntactic.TypeReference(entity.Name)),
                        UseThisReference: true,
                        LowerStageTransitions: true
                    );
                    var effectPass = new EffectLoweringPass(entity, context);
                    var composite = new CompositeEffect(subscriptionEffects);
                    handlerBody = effectPass.TryLowerVmNode(composite);
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
        if (effectiveStages.Count > 0) {
            var enumOwner = GetStageEnumOwner(entity, entityLookup);
            var enumTypeName = $"{enumOwner}Stage";

            // Only emit the stage enum if this entity owns it
            if (string.Equals(enumOwner, entity.Name, StringComparison.Ordinal)) {
                var stageEnumFields = new List<Syntactic.FieldDefinitionNode>();
                for (int si = 0; si < effectiveStages.Count; si++) {
                    stageEnumFields.Add(new Syntactic.FieldDefinitionNode(
                        effectiveStages[si].Name,
                        new Syntactic.PrimitiveTypeReference(PrimType.Int32),
                        DefaultValue: new Syntactic.Constant((long)si),
                        AccessModifier: AccessModifier.Public
                    ));
                }
                typeDefs.Add(new Syntactic.TypeDefinitionNode(
                    enumTypeName,
                    Fields: stageEnumFields,
                    Semantics: Syntactic.TypeDefinitionSemantics.MutableReference
                ));
            }

            props.Add(new Syntactic.PropertyDefinitionNode(
                "CurrentStage",
                new Syntactic.NamedTypeReference(enumTypeName),
                Getter: new Syntactic.PropertyGetterDefinitionNode(),
                Setter: new Syntactic.PropertySetterDefinitionNode(
                    AccessModifier: AccessModifier.Private)
            ));
        }

        // ── Constructor (with base() call for inheritance) ────────
        List<Syntactic.ConstructorDefinitionNode>? ctors = null;
        if (ctorParams.Count > 0 || effectiveStages.Count > 0) {
            List<Node>? baseCallArgs = null;
            List<Poly.Syntax.Node> bodyNodes;

            if (baseTypeName is not null) {
                baseCallArgs = new List<Node>();
                var ownAssignments = new List<Poly.Syntax.Node>();

                foreach (var assign in ctorAssignments) {
                    if (assign is Syntactic.Assignment a
                        && a.Destination is Syntactic.Member m
                        && ownPropertyNames.Contains(m.MemberName)) {
                        ownAssignments.Add(assign);
                    }
                    else if (assign is Syntactic.Assignment a2) {
                        baseCallArgs.Add(a2.Value ?? new Syntactic.Constant(null));
                    }
                }

                bodyNodes = new List<Poly.Syntax.Node>();
                bodyNodes.AddRange(ownAssignments);

                if (effectiveStages.Count > 0) {
                    bodyNodes.Add(new Syntactic.Assignment(
                        new Syntactic.Member(new Syntactic.ThisReference(), "CurrentStage"),
                        new Syntactic.Member(
                            new Syntactic.NamedTypeReference($"{stageEnumOwner}Stage"),
                            effectiveStages[0].Name)));
                }
            }
            else {
                bodyNodes = new List<Poly.Syntax.Node>();
                bodyNodes.AddRange(ctorAssignments);

                if (effectiveStages.Count > 0) {
                    bodyNodes.Add(new Syntactic.Assignment(
                        new Syntactic.Member(new Syntactic.ThisReference(), "CurrentStage"),
                        new Syntactic.Member(
                            new Syntactic.NamedTypeReference($"{stageEnumOwner}Stage"),
                            effectiveStages[0].Name)));
                }
            }

            // Append subscription registrations for subscriber entities after navigation assignments
            AddSubscriberRegistrationNodes(subscriberSubs, bodyNodes);

            ctors = [new Syntactic.ConstructorDefinitionNode(
                Parameters: ctorParams,
                Body: bodyNodes.Count > 0 ? new Syntactic.Block(bodyNodes) : null,
                BaseCall: baseCallArgs?.Count > 0 ? baseCallArgs : null,
                AccessModifier: AccessModifier.Public
            )];
        }

        typeDefs.Add(new Syntactic.TypeDefinitionNode(
            entity.Name,
            Constructors: ctors,
            Properties: props.Count > 0 ? props : null,
            Methods: methods.Count > 0 ? methods : null,
            Fields: fields.Count > 0 ? fields : null,
            BaseType: baseTypeName is not null
                ? new Syntactic.NamedTypeReference(baseTypeName)
                : null,
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
    /// Determines which entity "owns" the stage enum. For inherited entities,
    /// the root ancestor's stages are canonical; the child reuses that enum.
    /// </summary>
    public static string GetStageEnumOwnerName(
        Entity entity, IReadOnlyDictionary<string, Entity> entityLookup) {
        var current = entity;
        while (current.ParentEntityName is not null
               && entityLookup.TryGetValue(current.ParentEntityName, out var parent)) {
            current = parent;
        }
        return current.Name;
    }

    private static string GetStageEnumOwner(
        Entity entity, IReadOnlyDictionary<string, Entity> entityLookup)
        => GetStageEnumOwnerName(entity, entityLookup);

    // ── Action method builder ───────────────────────────────────

    private static void AddActionMethod(Entity entity, Poly.DomainModeling.Action action,
        List<Syntactic.MethodDefinitionNode> methods, string? stageEnumTypeName = null,
        IReadOnlyDictionary<string, IReadOnlyList<Node>>? postTransitionNodes = null) {
        var paramNames = new HashSet<string>(
            action.Parameters.Select(p => p.Name), StringComparer.Ordinal);
        var effectsBody = LowerActionToMethodBody(entity, action, paramNames, stageEnumTypeName,
            postTransitionNodes);

        // Build the full method body: require guards first, then effects
        var body = BuildActionBodyWithGuards(action, entity, effectsBody);

        methods.Add(new Syntactic.MethodDefinitionNode(
            action.Name,
            new Syntactic.TypeReference("void"),
            Parameters: action.Parameters
                .Select(p => new Syntactic.Parameter(p.Name, MapDomainTypeRef(p.Type)))
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
    /// Generated patterns:
    ///   <c>require AtLimit</c>     → <c>if (!this.AtLimit()) { return; }</c>
    ///   <c>require not AtLimit</c> → <c>if (this.AtLimit()) { return; }</c>
    /// </summary>
    private static Syntactic.Block? BuildActionBodyWithGuards(
        Poly.DomainModeling.Action action, Entity entity, Poly.Syntax.Node? effectsBody) {

        // Collect all nodes: require guards first, then effects
        var nodes = new List<Poly.Syntax.Node>();

        // Emit require guard clauses referencing entity-level policy methods
        foreach (var policy in action.Policies) {
            if (policy.Name.StartsWith("not_", StringComparison.Ordinal)) {
                // require not PolicyName → if (this.PolicyName()) { return; }
                var realName = policy.Name.Substring(4);
                var guardCall = new Syntactic.Invoke(
                    new Syntactic.Member(new Syntactic.ThisReference(), realName));
                nodes.Add(new Syntactic.IfStatement(
                    guardCall, // block when policy IS true (no negation)
                    new Syntactic.Block([new Syntactic.Return()])));
            }
            else {
                // require PolicyName → if (!this.PolicyName()) { return; }
                var guardCall = new Syntactic.Invoke(
                    new Syntactic.Member(new Syntactic.ThisReference(), policy.Name));
                nodes.Add(new Syntactic.IfStatement(
                    new Syntactic.Not(guardCall),
                    new Syntactic.Block([new Syntactic.Return()])));
            }
        }

        // Append the effects body
        if (effectsBody is Syntactic.Block block) {
            nodes.AddRange(block.Nodes);
        }
        else if (effectsBody is not null) {
            nodes.Add(effectsBody);
        }

        return nodes.Count > 0 ? new Syntactic.Block(nodes) : null;
    }

    // ── Lowering helpers ────────────────────────────────────────

    internal static Poly.Syntax.Node? LowerActionToMethodBody(
        Entity entity, Poly.DomainModeling.Action action,
        HashSet<string>? paramNames = null, string? stageEnumTypeName = null,
        IReadOnlyDictionary<string, IReadOnlyList<Node>>? postTransitionNodes = null) {
        if (action.Effects.Count == 0) return null;
        var context = new LoweringContext(
            new Syntactic.Parameter("entity", new Syntactic.TypeReference(entity.Name)),
            UseThisReference: true,
            ActionParameterNames: paramNames,
            LowerStageTransitions: true,
            StageEnumTypeName: stageEnumTypeName,
            PostTransitionNodes: postTransitionNodes
        );
        var effectPass = new EffectLoweringPass(entity, context);
        var composite = new CompositeEffect(action.Effects);
        return effectPass.TryLowerVmNode(composite);
    }

    internal static Poly.Syntax.Node? LowerExpressionToMethodBody(
        DomainExpression expr, Entity entity) {
        var context = new LoweringContext(
            new Syntactic.Parameter("entity", new Syntactic.TypeReference(entity.Name)),
            UseThisReference: true
        );
        var pass = new DomainExpressionLoweringPass(context);
        var lowered = pass.Lower(expr, new Syntactic.Parameter("entity"));
        return lowered is not null
            ? new Syntactic.Block([new Syntactic.Return(lowered)])
            : null;
    }

    // ── Type mapping ────────────────────────────────────────────

    internal static Poly.Syntax.Node MapDomainTypeRef(DomainTypeReference domainType) {
        var typeName = domainType.TypeName;
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

    // ── String helpers ──────────────────────────────────────────

    internal static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name.Substring(1);

    internal static string ToPascalCase(string name) =>
        string.IsNullOrEmpty(name) || char.IsUpper(name[0])
            ? name
            : char.ToUpperInvariant(name[0]) + name.Substring(1);
}