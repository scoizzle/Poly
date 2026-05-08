using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling;

public class ActionEventConstraintAnalysisTests {
    [Test]
    public async Task ActionEventContract_MismatchedHandlerPayload_ReportsContractDiagnostic() {
        var domain = new Domain("Support");
        var text = new Primitive(domain, "Text", TypeCategory.Text);
        var producer = new Entity(domain, "Producer");
        var consumer = new Entity(domain, "Consumer");
        var triggerEvent = new Event(domain, "TicketAssigned");
        var payloadEvent = new Event(domain, "DifferentPayload");
        var eventId = new Property(domain, "EventId", text);
        var handler = new DomainAction(domain, "HandleAssigned", consumer);
        var payloadParam = new Property(domain, "evt", payloadEvent);
        var subscription = new EventSubscription(domain, consumer, triggerEvent, handler, "evt");

        new Domain.AddTypeCommand(domain, text).Apply();
        new Domain.AddTypeCommand(domain, producer).Apply();
        new Domain.AddTypeCommand(domain, consumer).Apply();
        new Domain.AddTypeCommand(domain, triggerEvent).Apply();
        new Domain.AddTypeCommand(domain, payloadEvent).Apply();
        new Event.AddPropertyCommand(triggerEvent, eventId).Apply();
        new Entity.AddActionCommand(consumer, handler).Apply();
        new Poly.Data.Modeling.Action.AddParameterCommand(handler, payloadParam).Apply();
        new Entity.AddEventSubscriptionCommand(consumer, subscription).Apply();

        var analysis = new DomainModelAnalyzer().Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.ActionEventContract)).IsTrue();
    }

    [Test]
    public async Task EventFlowLiveness_UnpublishedEvent_ReportsHint() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var @event = new Event(domain, "TicketEscalated");

        domain.CreateMutation()
            .AddType(entity)
            .AddType(@event)
            .Apply();

        var analysis = new DomainModelAnalyzer().Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EventFlowLiveness &&
            d.Message.Contains("not observed as published", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task EventFlowLiveness_PublishedWithoutSubscriber_ReportsHint() {
        var domain = new Domain("Support");
        var text = new Primitive(domain, "Text", TypeCategory.Text);
        var entity = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "Escalate", entity);
        var @event = new Event(domain, "TicketEscalated");
        var reason = new Property(domain, "Reason", text);
        var entityReason = new Property(domain, "Reason", text);
        var publish = new PublishEvent(domain) { Event = @event };

        domain.CreateMutation()
            .AddType(text)
            .AddType(entity)
            .AddType(@event)
            .AddProperty(entity, entityReason)
            .AddProperty(@event, reason)
            .AddAction(entity, action)
            .AddEffect(action, publish)
            .SetEventPropertyBinding(action, publish, "Reason", new EventPropertyBindingSource.EntityProperty("Reason"))
            .Apply();

        var analysis = new DomainModelAnalyzer().Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.EventFlowLiveness)).IsTrue();
    }

    [Test]
    public async Task EventCorrelationSoundness_DuplicateConsumerKey_ReportsDiagnostic() {
        var domain = new Domain("Support");
        var text = new Primitive(domain, "Text", TypeCategory.Text);
        var producer = new Entity(domain, "Producer");
        var consumer = new Entity(domain, "Consumer");
        var keyProp = new Property(domain, "TenantId", text);
        var @event = new Event(domain, "TicketChanged");
        var eTenant = new Property(domain, "TenantId", text);
        var eEnv = new Property(domain, "Environment", text);
        var handler = new DomainAction(domain, "Handle", consumer);
        var param = new Property(domain, "evt", @event);
        var subscription = new EventSubscription(domain, consumer, @event, handler, "evt");

        domain.CreateMutation()
            .AddType(text)
            .AddType(producer)
            .AddType(consumer)
            .AddType(@event)
            .AddProperty(consumer, keyProp)
            .AddProperty(@event, eTenant)
            .AddProperty(@event, eEnv)
            .AddAction(consumer, handler)
            .AddParameter(handler, param)
            .AddEventSubscription(consumer, subscription)
            .SetEventSubscriptionAudience(subscription, new EventSubscriptionAudience.Correlated())
            .AddEventSubscriptionCorrelation(subscription, new EventCorrelationBinding(domain, "TenantId", "TenantId"))
            .AddEventSubscriptionCorrelation(subscription, new EventCorrelationBinding(domain, "Environment", "TenantId"))
            .Apply();

        var analysis = new DomainModelAnalyzer().Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.EventCorrelationSoundness)).IsTrue();
    }

    [Test]
    public async Task ActionOrderingCausality_InvokeCycle_ReportsWarning() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var first = new DomainAction(domain, "First", entity);
        var second = new DomainAction(domain, "Second", entity);
        var callSecond = new InvokeAction(domain) { TargetAction = second };
        var callFirst = new InvokeAction(domain) { TargetAction = first };

        domain.CreateMutation()
            .AddType(entity)
            .AddAction(entity, first)
            .AddAction(entity, second)
            .AddEffect(first, callSecond)
            .AddEffect(second, callFirst)
            .Apply();

        var analysis = new DomainModelAnalyzer().Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.ActionOrderingCausality)).IsTrue();
    }

    [Test]
    public async Task ActionIdempotencyReplay_EventHandlerCreateEffect_ReportsWarning() {
        var domain = new Domain("Support");
        var producer = new Entity(domain, "Producer");
        var consumer = new Entity(domain, "Consumer");
        var @event = new Event(domain, "TicketCreated");
        var handler = new DomainAction(domain, "HandleCreated", consumer);
        var evtParam = new Property(domain, "evt", @event);
        var create = new CreateEntityInstance(domain) { EntityType = consumer };
        var subscription = new EventSubscription(domain, consumer, @event, handler, "evt");

        domain.CreateMutation()
            .AddType(producer)
            .AddType(consumer)
            .AddType(@event)
            .AddAction(consumer, handler)
            .AddParameter(handler, evtParam)
            .AddEffect(handler, create)
            .AddEventSubscription(consumer, subscription)
            .Apply();

        var analysis = new DomainModelAnalyzer().Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.ActionIdempotencyReplay)).IsTrue();
    }

    [Test]
    public async Task EffectPrePostCondition_DeleteThenMutate_ReportsWarning() {
        var domain = new Domain("Support");
        var text = new Primitive(domain, "Text", TypeCategory.Text);
        var entity = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "DestroyAndMutate", entity);
        var title = new Property(domain, "Title", text);
        var incoming = new Property(domain, "IncomingTitle", text);
        var delete = new DeleteEntityInstance(domain) { EntityType = entity };
        var assign = new Assign(domain) { Target = title, Value = incoming };

        domain.CreateMutation()
            .AddType(text)
            .AddType(entity)
            .AddProperty(entity, title)
            .AddAction(entity, action)
            .AddParameter(action, incoming)
            .AddEffect(action, delete)
            .AddEffect(action, assign)
            .Apply();

        var analysis = new DomainModelAnalyzer().Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.EffectPrePostCondition)).IsTrue();
    }

    [Test]
    public async Task ConstraintFixedPoint_ChildWeakensParentRequired_ReportsWarning() {
        var domain = new Domain("Support");
        var text = new Primitive(domain, "Text", TypeCategory.Text);
        var parent = new Entity(domain, "Ticket");
        var child = new Entity(domain, "EscalatedTicket", parent);
        var parentTitle = new Property(domain, "Title", text);
        var childTitle = new Property(domain, "Title", text);

        domain.CreateMutation()
            .AddType(text)
            .AddType(parent)
            .AddType(child)
            .AddProperty(parent, parentTitle)
            .AddConstraint(parentTitle, new RequiredConstraint())
            .AddProperty(child, childTitle)
            .Apply();

        var analysis = new DomainModelAnalyzer().Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.ConstraintFixedPoint)).IsTrue();
    }

    [Test]
    public async Task ConstraintSatisfiability_InvalidRangeBounds_ReportsError() {
        var domain = new Domain("Support");
        var number = new Primitive(domain, "Number", TypeCategory.Numeric);
        var entity = new Entity(domain, "Ticket");
        var score = new Property(domain, "Score", number);

        new Domain.AddTypeCommand(domain, number).Apply();
        new Domain.AddTypeCommand(domain, entity).Apply();
        new Entity.AddPropertyCommand(entity, score).Apply();
        new Property.AddConstraintCommand(score, new RangeConstraint(10, 1)).Apply();

        var analysis = new DomainModelAnalyzer().Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsTrue();
    }

    [Test]
    public async Task RuleCoverage_MutationWithoutRequiredAssignments_ReportsHint() {
        var domain = new Domain("Support");
        var text = new Primitive(domain, "Text", TypeCategory.Text);
        var entity = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "TransitionOnly", entity);
        var required = new Property(domain, "Title", text);
        var stage = new Stage(domain, "Open");
        var transition = new StageTransition(domain) { TargetStage = stage };

        domain.CreateMutation()
            .AddType(text)
            .AddType(entity)
            .AddProperty(entity, required)
            .AddConstraint(required, new RequiredConstraint())
            .AddStage(entity, stage)
            .AddAction(entity, action)
            .AddEffect(action, transition)
            .Apply();

        var analysis = new DomainModelAnalyzer().Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.RuleCoverage)).IsTrue();
    }
}