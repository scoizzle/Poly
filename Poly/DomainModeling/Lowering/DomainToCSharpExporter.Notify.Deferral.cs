using Poly.DomainModeling.Analysis;
using Poly.Interpretation.CSharp;

using AccessModifier = Poly.Introspection.AccessModifier;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Notify deferral (#630): a create-time entry chain runs before the new child is linked to its creator, so a
// `transition` in it raised its stage notification into an empty registry and the parent's `when` never fired.
// While the initial entry runs, a watched stage's notification is recorded on the child instead of delivered;
// the create-in wrapper links the child (collection + subscriber registration) and replays the record in order.
public sealed partial class DomainToCSharpExporter {
    /// <summary>
    /// The stages a create-time entry raised, in the order it raised them. A field, so the record survives the
    /// <c>__EnterInitialStage</c> call that made it; <c>__</c> is a reserved prefix.
    /// </summary>
    internal const string PendingStageNotificationsFieldName = "__pendingStageNotifications";

    /// <summary>True only while <c>__EnterInitialStage</c> runs: a notify raised then is recorded, not delivered.</summary>
    internal const string DeferringStageNotificationsFieldName = "__deferringStageNotifications";

    internal const string DeferStageNotificationMethodName = "__DeferStageNotification";
    internal const string ReplayStageNotificationsMethodName = "__ReplayStageNotifications";

    /// <summary>
    /// True when this entity defers a creation-time notification: a subscription watches one of its stages, so the
    /// emitter writes the record, its queue and the replay. Read by <c>AddSubscriptionRegistry</c> (the dispatcher
    /// and the members it consults) and by <c>AddCreateFactory</c> (the entry window), one fact, two sites.
    /// </summary>
    private static bool DefersCreationNotifications(
        EntityStructureMetadata esm, IReadOnlySet<string>? postTransitionNotifyStages) =>
        esm.HasStages && postTransitionNotifyStages is { Count: > 0 };

    /// <summary>
    /// The dispatcher's first statement: while the initial entry runs, record the stage and report success — the
    /// child is in no subscriber's collection and in no registry yet. The create-in replay delivers it afterwards.
    /// </summary>
    private static Node DeferredStageNotificationCheck(string stageName) =>
        new IfStatement(
            new Member(new ThisReference(), DeferringStageNotificationsFieldName),
            new Block([
                new Invoke(
                    new Member(new ThisReference(), DeferStageNotificationMethodName),
                    new Constant(stageName)),
                DomainResultSuccess()
            ]));

    /// <summary>
    /// The queue field, the record method and <c>__ReplayStageNotifications</c>, which dispatches each recorded stage
    /// to its own <c>Notify{Stage}Subscribers</c> in the order the entry recorded it. A recorded notification carries
    /// no previous stage — the child was related to nobody while the entry ran, so no subscriber saw it enter one —
    /// and the argument is typed, because a bare <c>null</c> does not resolve against a nullable stage enum on the VM.
    /// A handler failure during the replay is returned, and the create-in that ran it rolls back like any other.
    /// </summary>
    private static void AddDeferredStageNotifications(
        IReadOnlyList<(string Stage, bool NeedsPrevious)> stages, string stageEnumTypeName,
        List<FieldDefinitionNode> fields, List<MethodDefinitionNode> methods) {
        var stringType = TypeReference.To(typeof(string));
        var listType = new NamedTypeReference("List", TypeArguments: [stringType]);
        var pending = new Member(new ThisReference(), PendingStageNotificationsFieldName);

        fields.Add(new FieldDefinitionNode(
            PendingStageNotificationsFieldName,
            new OptionalTypeReference(listType),
            AccessModifier: AccessModifier.Private));
        fields.Add(new FieldDefinitionNode(
            DeferringStageNotificationsFieldName,
            TypeReference.To(typeof(bool)),
            AccessModifier: AccessModifier.Private));

        var stageParameter = new Parameter("stage", stringType);
        methods.Add(new MethodDefinitionNode(
            DeferStageNotificationMethodName,
            new TypeReference("void"),
            Parameters: [stageParameter],
            Body: new Block([
                TouchThis(),
                new IfStatement(
                    new Equal(pending, new Constant(null!)),
                    new Block([new Assignment(pending, new New(listType))])),
                new Invoke(new Member(pending, "Add"), stageParameter)
            ]),
            AccessModifier: AccessModifier.Private));

        var recorded = new Variable("__pending");
        var stage = new Variable("__stage");
        var names = new LocalNames();
        var arms = new List<Node>();
        foreach (var (stageName, needsPrevious) in stages) {
            var notified = names.Next("notified");
            arms.Add(new IfStatement(
                new Equal(stage, new Constant(stageName)),
                new Block([
                    new Assignment(notified, new Invoke(
                        new Member(new ThisReference(), $"Notify{stageName}Subscribers"),
                        needsPrevious ? [TypedNull(stageEnumTypeName)] : [])),
                    new IfStatement(
                        new Syntactic.Not(new Member(notified, "IsSuccess")),
                        new Block([new Return(notified)]))
                ], [notified])));
        }

        // Take the record before dispatching: a handler that transitions this entity notifies normally, and a stage
        // it raises is not replayed twice.
        methods.Add(new MethodDefinitionNode(
            ReplayStageNotificationsMethodName,
            new NamedTypeReference("DomainResult"),
            Body: new Block([
                TouchThis(),
                new IfStatement(
                    new Equal(pending, new Constant(null!)),
                    new Block([DomainResultSuccess()])),
                new Assignment(recorded, pending),
                new Assignment(pending, new Constant(null!)),
                new ForEachLoop(stage, recorded, new Block(arms)),
                DomainResultSuccess()
            ], [recorded]),
            AccessModifier: AccessModifier.Internal));
    }
}