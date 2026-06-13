using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;
using Poly.Introspection;

namespace Poly.Tests.DomainModeling.Analysis;

public class EventAnalysisTests {
    [Test]
    public async Task ActionEventContract_MismatchedHandlerPayload_ReportsContractDiagnostic() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var ticketAssigned = new Event("TicketAssigned",
            [new Property("EventId", new DomainTypeReference("Text"), [])], []);
        var differentPayload = new Event("DifferentPayload", [], []);
        var handler = new Poly.DomainModeling.Action("HandleAssigned", InvocationResult.Void,
            [new Property("evt", new DomainTypeReference("DifferentPayload"), [])], [], []);
        var consumer = new Entity("Consumer", [new Property("Name", new DomainTypeReference("Text"), [])],
            [new DomainTypeReference("TicketAssigned")], [handler], [], []);
        var subscription = new EventSubscription(
            new DomainTypeReference("TicketAssigned"), "HandleAssigned", "evt",
            EventSubscriptionRoutingMode.Broadcast, []);
        var entity = consumer with { EventSubscriptions = [subscription] };
        var domain = new Domain("Test", [text, ticketAssigned, differentPayload, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ActionEventContract)).IsTrue();
    }

    [Test]
    public async Task EventFlowLiveness_UnpublishedEvent_ReportsHint() {
        var entity = new Entity("Ticket", [], [], [], [], []);
        var @event = new Event("TicketEscalated", [], []);
        var domain = new Domain("Test", [entity, @event], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EventFlowLiveness &&
            d.Message.Contains("not published"))).IsTrue();
    }

    [Test]
    public async Task EventFlowLiveness_PublishedWithoutSubscriber_ReportsHint() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var @event = new Event("TicketEscalated",
            [new Property("Reason", new DomainTypeReference("Text"), [])], []);
        var action = new Poly.DomainModeling.Action("Escalate", InvocationResult.Void, [], [
            new PublishEventEffect(new DomainTypeReference("TicketEscalated"), [
                new PropertyBinding("Reason", new PropertyAccess("Reason"))
            ])
        ], []);
        var entity = new Entity("Ticket",
            [new Property("Reason", new DomainTypeReference("Text"), [])],
            [], [action], [], []);
        var domain = new Domain("Test", [text, @event, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EventFlowLiveness)).IsTrue();
    }

    [Test]
    public async Task EventCorrelationSoundness_DuplicateConsumerKey_ReportsDiagnostic() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var @event = new Event("TicketChanged", [
            new Property("TenantId", new DomainTypeReference("Text"), []),
            new Property("Environment", new DomainTypeReference("Text"), [])
        ], []);
        var handler = new Poly.DomainModeling.Action("Handle", InvocationResult.Void,
            [new Property("evt", new DomainTypeReference("TicketChanged"), [])], [], []);
        var consumer = new Entity("Consumer",
            [new Property("TenantId", new DomainTypeReference("Text"), [])],
            [], [handler], [], []);
        var subscription = new EventSubscription(
            new DomainTypeReference("TicketChanged"), "Handle", "evt",
            EventSubscriptionRoutingMode.Correlated, [
                new EventCorrelationBinding("TenantId", "TenantId"),
                new EventCorrelationBinding("Environment", "TenantId")
            ]);
        var entity = consumer with { EventSubscriptions = [subscription] };
        var domain = new Domain("Test", [text, @event, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EventCorrelationSoundness)).IsTrue();
    }

    [Test]
    public async Task ActionOrderingCausality_InvokeCycle_ReportsWarning() {
        var first = new Poly.DomainModeling.Action("First", InvocationResult.Void, [], [
            new InvokeActionEffect("Second", [])
        ], []);
        var second = new Poly.DomainModeling.Action("Second", InvocationResult.Void, [], [
            new InvokeActionEffect("First", [])
        ], []);
        var entity = new Entity("Ticket", [], [], [first, second], [], []);
        var domain = new Domain("Test", [entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ActionOrderingCausality)).IsTrue();
    }

    [Test]
    public async Task ActionIdempotencyReplay_EventHandlerCreateEffect_ReportsWarning() {
        var @event = new Event("TicketCreated", [], []);
        var handler = new Poly.DomainModeling.Action("HandleCreated", InvocationResult.Void,
            [new Property("evt", new DomainTypeReference("TicketCreated"), [])], [
                new CreateEntityInstance(new DomainTypeReference("Consumer"))
            ], []);
        var consumer = new Entity("Consumer", [], [], [handler], [], []);
        var subscription = new EventSubscription(
            new DomainTypeReference("TicketCreated"), "HandleCreated", "evt",
            EventSubscriptionRoutingMode.Broadcast, []);
        var entity = consumer with { EventSubscriptions = [subscription] };
        var domain = new Domain("Test", [@event, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ActionIdempotencyReplay)).IsTrue();
    }
}

public class EffectAnalysisTests {
    [Test]
    public async Task EffectPrePostCondition_DeleteThenMutate_ReportsWarning() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var entity = new Entity("Ticket",
            [new Property("Title", new DomainTypeReference("Text"), [])], [], [], [], []);
        var action = new Poly.DomainModeling.Action("DestroyAndMutate", InvocationResult.Void,
            [new Property("IncomingTitle", new DomainTypeReference("Text"), [])], [
                new DeleteEntityInstance(new DomainTypeReference("Ticket")),
                new AssignEffect(new PropertyAccess("Title"), new ParameterAccess("IncomingTitle"))
            ], []);
        var ticket = entity with { Actions = [action] };
        var domain = new Domain("Test", [text, ticket], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectPrePostCondition)).IsTrue();
    }

    [Test]
    public async Task ConstraintFixedPoint_ChildWeakensParentRequired_ReportsWarning() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var parentTitle = new Property("Title", new DomainTypeReference("Text"),
            [new RequiredConstraint()]);
        var childTitle = new Property("Title", new DomainTypeReference("Text"), []);
        var parent = new Entity("Ticket", [parentTitle], [], [], [], []);
        var child = new Entity("EscalatedTicket",
            [childTitle], [], [], [], []) { ParentEntityName = "Ticket" };
        var domain = new Domain("Test", [text, parent, child], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ConstraintFixedPoint)).IsTrue();
    }

    [Test]
    public async Task ConstraintSatisfiability_InvalidRangeBounds_ReportsError() {
        var number = new Poly.DomainModeling.PrimitiveType("Number", TypeCategory.Numeric, []);
        var score = new Property("Score", new DomainTypeReference("Number"),
            [new RangeConstraint(10, 1)]);
        var entity = new Entity("Ticket", [score], [], [], [], []);
        var domain = new Domain("Test", [number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsTrue();
    }

    [Test]
    public async Task RuleCoverage_MutationWithoutRequiredAssignments_ReportsHint() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var required = new Property("Title", new DomainTypeReference("Text"),
            [new RequiredConstraint()]);
        var stage = new Stage("Open", default(StageReference?),
        Array.Empty<Poly.DomainModeling.Action>(),
        Array.Empty<Poly.DomainModeling.Policy>(),
        Array.Empty<Poly.DomainModeling.Effect>(),
        Array.Empty<Poly.DomainModeling.Effect>());
        var action = new Poly.DomainModeling.Action("TransitionOnly", InvocationResult.Void, [], [
            new StageTransitionEffect(new StageReference("Open"))
        ], []);
        var entity = new Entity("Ticket", [required], [], [action], [], [stage]);
        var domain = new Domain("Test", [text, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.RuleCoverage)).IsTrue();
    }

    [Test]
    public async Task ActionParameterUnused_ReportsHint() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var stage = new Stage("Open", default(StageReference?),
        Array.Empty<Poly.DomainModeling.Action>(),
        Array.Empty<Poly.DomainModeling.Policy>(),
        Array.Empty<Poly.DomainModeling.Effect>(),
        Array.Empty<Poly.DomainModeling.Effect>());
        var action = new Poly.DomainModeling.Action("DoStuff", InvocationResult.Void,
            [new Property("unused", new DomainTypeReference("Text"), [])], [
                new StageTransitionEffect(new StageReference("Open"))
            ], []);
        var entity = new Entity("MyEntity", [], [], [action], [], [stage]);
        var domain = new Domain("Test", [text, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnusedParameter)).IsTrue();
    }

    [Test]
    public async Task ActionParameterUsed_DoesNotReportHint() {
        var text = new Poly.DomainModeling.PrimitiveType("Text", TypeCategory.Text, []);
        var action = new Poly.DomainModeling.Action("DoStuff", InvocationResult.Void,
            [new Property("used", new DomainTypeReference("Text"), [])], [
                new AssignEffect(new PropertyAccess("Title"), new ParameterAccess("used"))
            ], []);
        var entity = new Entity("MyEntity",
            [new Property("Title", new DomainTypeReference("Text"), [])],
            [], [action], [], []);
        var domain = new Domain("Test", [text, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnusedParameter)).IsFalse();
    }
}