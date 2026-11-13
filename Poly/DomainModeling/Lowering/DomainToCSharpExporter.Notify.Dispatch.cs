using Poly.DomainModeling.Ontology;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Notify dispatch: subscriber registration, registry fields, and cascade-depth guards.
public sealed partial class DomainToCSharpExporter {
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
                                        SubscriberRegisterMethodName(info)),
                                    [new ThisReference()])
                            ])
                        ));
                    }
                }
                else {
                    // A singular navigation is optional (unlinked until a later link),
                    // so every per-stage registration shares ONE null guard: dereferencing
                    // this.{Nav} in the constructor would NRE when the target is absent.
                    var registrations = new List<Node>();
                    foreach (var info in perStage) {
                        registrations.Add(new Invoke(
                            new Member(
                                new Member(new ThisReference(), pascalNavName),
                                SubscriberRegisterMethodName(info)),
                            [new ThisReference()]));
                    }
                    bodyNodes.Add(new IfStatement(
                        new NotEqual(
                            new Member(new ThisReference(), pascalNavName),
                            new Constant(null!)),
                        new Block(registrations)));
                }
            }
        }
    }

    /// <summary>Printed notify-cascade bound. Nested calls past this throw instead of overflowing the stack.</summary>
    private const int MaxNotifyDepth = 16;

    /// <summary>
    /// Nested <c>Notify{Stage}Subscribers</c> calls past <see cref="MaxNotifyDepth"/>
    /// throw instead of overflowing the stack. <c>EnterNotifyCascade</c> returns the depth it entered at and
    /// <c>ExitNotifyCascade</c> assigns that depth back (L-03c, F2): a rollback inside the cascade restores the depth
    /// the action started with, and a relative decrement in the unwinding <c>finally</c> would then leave it one below.
    /// </summary>
    private static void AddNotifyCascadeDepthGuard(
        Entity entity, List<FieldDefinitionNode> fields, List<MethodDefinitionNode> methods) {
        fields.Add(new FieldDefinitionNode(
            "_notifyDepth",
            new PrimitiveTypeReference(PrimType.Int32),
            AccessModifier: AccessModifier.Private));
        var depth = new Member(new ThisReference(), "_notifyDepth");
        methods.Add(new MethodDefinitionNode(
            "EnterNotifyCascade",
            new PrimitiveTypeReference(PrimType.Int32),
            Parameters: [new Parameter("stageName", new PrimitiveTypeReference(PrimType.String))],
            Body: new Block([
                new IfStatement(
                    new GreaterThanOrEqual(
                        new Member(new ThisReference(), "_notifyDepth"),
                        new Constant(MaxNotifyDepth)),
                    new ThrowStatement(new New(
                        new NamedTypeReference("System.InvalidOperationException"),
                        new Invoke(
                            new Member(new PrimitiveTypeReference(PrimType.String), "Concat"),
                            new Constant($"Subscription cascade on entity '{entity.Name}' exceeded max depth ({MaxNotifyDepth}) while notifying '"),
                            new Parameter("stageName"),
                            new Constant("'."))))),
                new Assignment(depth, new Syntactic.Add(depth, new Constant(1))),
                new Return(new Syntactic.Subtract(depth, new Constant(1)))
            ]),
            AccessModifier: AccessModifier.Private));
        var entered = new Parameter("depth", new PrimitiveTypeReference(PrimType.Int32));
        methods.Add(new MethodDefinitionNode(
            "ExitNotifyCascade",
            new TypeReference("void"),
            Parameters: [entered],
            Body: new Block([new Assignment(depth, entered)]),
            AccessModifier: AccessModifier.Private));
    }

    /// <summary>
    /// Target entity: one registry field + register method per (relationship, stage,
    /// subscriber) triple, and one <c>Notify{Stage}Subscribers</c> per watched stage
    /// that fans out every registry for that stage through the cascade-depth guard.
    /// </summary>
    private static void AddSubscriptionRegistry(
        List<SubscriptionInfo>? targetSubs, string? stageEnumTypeName,
        IReadOnlyDictionary<SubscriptionInfo, string>? handlerNames,
        List<FieldDefinitionNode> fields, List<MethodDefinitionNode> methods) {
        // ── Target entity: subscription registry ──────────────────
        // One registry field + register method per (relationship, stage, subscriber)
        // triple; the notify method calls EVERY subscription's handler for that
        // triple — a subscriber may declare any/all/Each (or multiple Each) reactions
        // on the same relation+stage, each with a quantifier-disambiguated handler name.
        if (targetSubs is { Count: > 0 }) {
            var groups = targetSubs
                .GroupBy(i => (i.StageName, i.SourceEntity.Name, i.Relationship.Name))
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
                // Idempotent registration: a create-in child is already linked by
                // the store under simulate, and printed hosts may attach twice.
                // ReferenceEquals keeps distinct-but-equal records from deduping.
                // The body must use this parameter, not a same-named copy: a copy
                // has no type, so the VM cannot place the argument. The registry is the target's state, so it is
                // touched first (L-03c): a subscriber created in the action registers on an existing target.
                var existing = new Variable("existing");
                var subscriber = new Parameter(paramName, srcType);
                var registerBody = new Block([
                    TouchThis(),
                    new IfStatement(
                        new Equal(fieldAcc, new Constant(null)),
                        new Block([
                            new Assignment(fieldAcc,
                                new New(
                                    new NamedTypeReference("List",
                                        TypeArguments: [srcType])))
                        ])),
                    new ForEachLoop(
                        existing,
                        fieldAcc,
                        new IfStatement(
                            new Invoke(
                                new Member(new ClrTypeReference(typeof(object)), "ReferenceEquals"),
                                [existing, subscriber]),
                            new Block([new Return(null)]))),
                    new Invoke(
                        new Member(fieldAcc, "Add"),
                        [subscriber])
                ]);
                methods.Add(new MethodDefinitionNode(
                    SubscriberRegisterMethodName(infos[0]),
                    new TypeReference("void"),
                    Parameters: [subscriber],
                    Body: registerBody,
                    AccessModifier: AccessModifier.Internal
                ));
            }

            // One Notify{Stage}Subscribers per watched stage, fanning out every
            // (source, relationship, stage) registry. Two subscribers on the same
            // target stage (Student + Section on Enrollment.Dropped) used to emit
            // duplicate _droppedSubscribers / NotifyDroppedSubscribers members.
            foreach (var stageGroup in groups.GroupBy(infos => infos[0].StageName)) {
                // Nullable: a create-in lands the child in its initial stage with no prior
                // stage, so the caller passes null. `when all` guards read it as
                // "transitioned from" and a fresh entry must not match.
                var needsPrevious = stageGroup.Any(infos => infos.Any(NeedsAllPreviousStage));
                var notifyPrevious = new Parameter(
                    "previousStage",
                    new OptionalTypeReference(new NamedTypeReference(stageEnumTypeName!)));
                var notifyNodes = new List<Node>();
                var names = new LocalNames();
                foreach (var infos in stageGroup) {
                    var fieldName = SubscriberRegistryFieldName(infos[0]);
                    var handlerLocals = new List<Node>();
                    var notifyCalls = new List<Node>();
                    foreach (var info in infos) {
                        var args = new List<Node>();
                        if (HasPeerBinding(info))
                            args.Add(new ThisReference());
                        if (NeedsAllPreviousStage(info))
                            args.Add(notifyPrevious);
                        // The first failing handler stops the fan-out and its result is the dispatcher's.
                        var handlerResult = names.Next("handlerResult");
                        handlerLocals.Add(handlerResult);
                        notifyCalls.Add(new Assignment(handlerResult, new Invoke(
                            new Member(new Variable("sub"), handlerNames![info]),
                            [.. args])));
                        notifyCalls.Add(new IfStatement(
                            new Syntactic.Not(new Member(handlerResult, "IsSuccess")),
                            new Block([new Return(handlerResult)])));
                    }
                    notifyNodes.Add(new IfStatement(
                        new NotEqual(
                            new Member(new ThisReference(), fieldName),
                            new Constant(null)),
                        new ForEachLoop(
                            new Variable("sub"),
                            new Member(new ThisReference(), fieldName),
                            new Block(notifyCalls, handlerLocals))));
                }
                // `__depth` is reserved, so no handler local or parameter can collide with it.
                var depth = new Variable("__depth");
                methods.Add(new MethodDefinitionNode(
                    $"Notify{stageGroup.Key}Subscribers",
                    new NamedTypeReference("DomainResult"),
                    Parameters: needsPrevious ? [notifyPrevious] : null,
                    Body: new Block([
                        new Assignment(depth, new Invoke(
                            new Member(new ThisReference(), "EnterNotifyCascade"),
                            new Constant(stageGroup.Key))),
                        new TryCatchFinally(
                            notifyNodes.Count == 1 ? notifyNodes[0] : new Block(notifyNodes),
                            FinallyBlock: new Invoke(
                                new Member(new ThisReference(), "ExitNotifyCascade"), depth)),
                        DomainResultSuccess()
                    ], [depth]),
                    AccessModifier: AccessModifier.Internal
                ));
            }
        }
    }

    /// <summary>
    /// <c>InitializeSubscriptions</c> — for post-load subscription wiring. Called as
    /// the last constructor step so all state is set before registration runs.
    /// </summary>
    private static void AddSubscriptionInitializer(
        List<SubscriptionInfo>? subscriberSubs, List<MethodDefinitionNode> methods) {
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
    }
}