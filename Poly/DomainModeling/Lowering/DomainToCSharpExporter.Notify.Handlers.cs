using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Lowering;

// Notify handlers: subscription handler bodies (stage gates, quantifier loops, peer parameters).
public sealed partial class DomainToCSharpExporter {
    /// <summary>Splice a lowered handler body's statements/locals after a prefix (D13).</summary>
    private static Block Prefixed(List<Node> prefix, Node body, List<Node>? locals = null) {
        if (body is Block block) {
            prefix.AddRange(block.Nodes);
            locals ??= [];
            locals.AddRange(block.Variables);
        }
        else {
            prefix.Add(body);
        }
        return new Block(prefix, locals ?? []);
    }

    private static Block WithSuccessTail(Node body) =>
        body is Block block
            ? new Block([.. block.Nodes, DomainResultSuccess()], [.. block.Variables])
            : new Block([body, DomainResultSuccess()]);

    /// <summary>
    /// Subscriber entity: one handler method per subscription. Each handler carries the
    /// peer parameter (<c>when … as name</c>), the <c>when all</c> previous-stage gate,
    /// the subscriber-stage gate, and the lowered subscription effects.
    /// </summary>
    private static void AddSubscriptionHandlers(
        Entity entity, Domain domain, INodeMetadataProvider metadata,
        EntityStructureMetadata esm, List<SubscriptionInfo>? subscriberSubs,
        IReadOnlyDictionary<SubscriptionInfo, string>? handlerNames,
        IReadOnlySet<string>? postTransitionNotifyStages,
        IReadOnlySet<string>? previousStageNotifyStages,
        string? stageEnumTypeName,
        Dictionary<SubscriptionDispatchPlanEntry, Dictionary<string, Node>>? subscriptionBodies,
        List<MethodDefinitionNode> methods) {
        // ── Subscriber entity: subscription handler methods ──────
        if (subscriberSubs is { Count: > 0 }) {
            foreach (var info in subscriberSubs) {
                var handlerName = handlerNames![info];
                var peerBinding = info.Subscription.PeerBinding;
                var targetStageEnumName = metadata.GetStructure(info.TargetEntity)
                    ?.StageEnumTypeName ?? $"{info.TargetEntity.Name}Stage";
                Parameter? previousStageParam = NeedsAllPreviousStage(info)
                    ? new Parameter("__previousStage",
                        new OptionalTypeReference(new NamedTypeReference(targetStageEnumName)))
                    : null;
                var handlerParamList = new List<Parameter>();
                if (HasPeerBinding(info))
                    handlerParamList.Add(new Parameter(peerBinding!, new NamedTypeReference(info.TargetEntity.Name)));
                if (previousStageParam is not null)
                    handlerParamList.Add(previousStageParam);
                IReadOnlyList<Parameter>? handlerParams = handlerParamList.Count > 0
                    ? handlerParamList
                    : null;

                // Lower subscription effects into the handler body.
                // Peer path-prefix roots (binder name) resolve via Parameters to the
                // peer method parameter; bare props / this stay on the subscriber.
                var subscriptionEffects = info.Subscription.Effects;
                Node handlerBody;
                if (subscriptionEffects.Count > 0) {
                    IReadOnlyDictionary<string, Node>? peerParams = null;
                    if (HasPeerBinding(info)) {
                        peerParams = new Dictionary<string, Node>(StringComparer.Ordinal) {
                            [peerBinding!] = new Parameter(peerBinding!, new NamedTypeReference(info.TargetEntity.Name))
                        };
                    }
                    var context = new LoweringContext(
                        new ThisReference(),
                        Parameters: peerParams,
                        Analysis: metadata,
                        Domain: domain,
                        EnumPropertyNames: esm.EnumPropertyNames,
                        LifecyclePropertyName: esm.LifecyclePropertyName,
                        LifecycleEnumTypeName: esm.LifecycleEnumTypeName,
                        PostTransitionNotifyStages: postTransitionNotifyStages,
                        PreviousStageNotifyStages: previousStageNotifyStages,
                        ActionResultType: new NamedTypeReference("DomainResult"));
                    var effectPass = new EffectLoweringPass(entity, context);
                    var composite = new CompositeEffect(subscriptionEffects);
                    handlerBody = effectPass.TryLowerVmNode(composite)
                        ?? throw new InvalidOperationException(
                            "Subscription effects could not be lowered to a Syntax AST node.");
                    // A subscription is its own trigger for the automatic-transition loop guard.
                    if (EffectHelpers.HasAutomaticTransitions(entity))
                        handlerBody = Prefixed(
                            [new Invoke(new Member(new ThisReference(), "ClearAutomaticStageChain"))],
                            handlerBody);
                    // L-03c: the handler writes the subscriber, so it touches it first. The gates below are prefixed
                    // over this, so the order is gates, Touch, chain clear, effects; the Touch is in the cached body
                    // the simulator runs.
                    handlerBody = Prefixed([TouchThis()], handlerBody);
                }
                else {
                    handlerBody = new Block([]);
                }

                // Stage-scoped subscriptions (`when` inside a subscriber stage) fire only
                // while the subscriber is in that stage — the handler returns when
                // CurrentStage has moved on. Entity-level subscriptions
                // (SubscriberStageName == null) are always active.
                if (info.SubscriberStageName is { Length: > 0 }) {
                    var stageGate = new IfStatement(
                        new NotEqual(
                            new Member(new ThisReference(), "CurrentStage"),
                            new Member(new NamedTypeReference(stageEnumTypeName!), info.SubscriberStageName)),
                        new Block([DomainResultSuccess()]));
                    handlerBody = Prefixed([stageGate], handlerBody);
                }

                // `when all Rel Stage…` fires once, on the transition where the last
                // linked record reaches a watched stage (union of StageNames). At least
                // one linked record must exist. Later transitions while every linked
                // record still matches do not fire again.
                if (previousStageParam is not null) {
                    var linkedVar = new Variable("__linkedTarget");
                    var matchedVar = new Variable("__linkedMatched");
                    var currentStage = new Member(linkedVar, "CurrentStage");
                    var gateLoop = new ForEachLoop(
                        linkedVar,
                        new Member(new ThisReference(), ToPascalCase(info.Relationship.Name)),
                        new Block([
                            new Assignment(matchedVar, new Constant(true)),
                            new IfStatement(
                                NotInWatchedStages(
                                    currentStage, targetStageEnumName, info.Subscription.StageNames),
                                new Block([DomainResultSuccess()]))
                        ]));
                    var emptyCheck = new IfStatement(
                        new Poly.Ast.Nodes.Not(matchedVar),
                        new Block([DomainResultSuccess()]));
                    var alreadyWatched = new IfStatement(
                        InWatchedStages(
                            previousStageParam, targetStageEnumName, info.Subscription.StageNames),
                        new Block([DomainResultSuccess()]));
                    handlerBody = Prefixed(
                        [
                            new Assignment(matchedVar, new Constant(false)),
                            gateLoop,
                            emptyCheck,
                            alreadyWatched
                        ],
                        handlerBody,
                        [matchedVar]);
                }

                // `when any Rel Stage…` is level-triggered: on each notified
                // transition it fires whenever at least one linked record is in
                // any watched stage (union of StageNames).
                if (info.Subscription.Quantifier == StageSubscriptionQuantifier.Any
                    && metadata.GetStructure(info.TargetEntity)?.HasStages == true
                    && info.Subscription.StageNames.Count > 0) {
                    var linkedVar = new Variable("__linkedTarget");
                    var matchedVar = new Variable("__linkedMatched");
                    var gateLoop = new ForEachLoop(
                        linkedVar,
                        new Member(new ThisReference(), ToPascalCase(info.Relationship.Name)),
                        new Block([
                            new IfStatement(
                                InWatchedStages(
                                    new Member(linkedVar, "CurrentStage"),
                                    targetStageEnumName,
                                    info.Subscription.StageNames),
                                new Block([
                                    new Assignment(matchedVar, new Constant(true))
                                ]))
                        ]));
                    var noneMatched = new IfStatement(
                        new Poly.Ast.Nodes.Not(matchedVar),
                        new Block([DomainResultSuccess()]));
                    handlerBody = Prefixed(
                        [new Assignment(matchedVar, new Constant(false)), gateLoop, noneMatched],
                        handlerBody,
                        [matchedVar]);
                }

                // The handler returns a result: a failed invoke returns it and the tail is success. The tail goes
                // on before the body is cached, so the simulator runs the same body.
                handlerBody = WithSuccessTail(handlerBody);

                if (subscriptionBodies is not null) {
                    if (!subscriptionBodies.TryGetValue(info.Subscription, out var byStage)) {
                        byStage = new Dictionary<string, Node>(StringComparer.Ordinal);
                        subscriptionBodies[info.Subscription] = byStage;
                    }
                    byStage.TryAdd(info.StageName, handlerBody);
                }

                methods.Add(new MethodDefinitionNode(
                    handlerName,
                    new NamedTypeReference("DomainResult"),
                    Parameters: handlerParams,
                    Body: handlerBody,
                    AccessModifier: AccessModifier.Internal
                ));
            }
        }
    }
}