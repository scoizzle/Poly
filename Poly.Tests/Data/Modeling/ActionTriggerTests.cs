using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling;

public class ActionTriggerTests {
    [Test]
    public async Task EventHandlerTrigger_ValidEventParameter_Succeeds() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "OnTicketCreated", entity);
        var triggerEvent = new Event(domain, "TicketCreated");
        var subscription = new EventSubscription(domain, entity, triggerEvent, action);

        domain.CreateMutation()
            .AddType(entity)
            .AddType(triggerEvent)
            .AddEvent(entity, triggerEvent)
            .AddAction(entity, action)
            .AddParameter(action, new Property(domain, "event", triggerEvent))
            .SetEventHandlerTrigger(action, triggerEvent, "event")
            .AddEventSubscription(entity, subscription)
            .Apply();

        var analysis = new DomainModelAnalyzer().Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ActionTrigger);

        await Assert.That(diagnostic).IsNull();
    }

    [Test]
    public async Task EventHandlerTrigger_MissingEventParameter_ReportsError() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "OnTicketCreated", entity);
        var triggerEvent = new Event(domain, "TicketCreated");
        var subscription = new EventSubscription(domain, entity, triggerEvent, action);

        var result = domain.CreateMutation()
            .AddType(entity)
            .AddType(triggerEvent)
            .AddEvent(entity, triggerEvent)
            .AddAction(entity, action)
            .SetEventHandlerTrigger(action, triggerEvent, "event")
            .AddEventSubscription(entity, subscription)
            .Apply();

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ActionTrigger);

        await Assert.That(diagnostic).IsNotNull();
    }

    [Test]
    public async Task EventHandlerTrigger_EventParameterTypeMismatch_ReportsError() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "OnTicketCreated", entity);
        var triggerEvent = new Event(domain, "TicketCreated");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var subscription = new EventSubscription(domain, entity, triggerEvent, action);

        var result = domain.CreateMutation()
            .AddType(stringType)
            .AddType(entity)
            .AddType(triggerEvent)
            .AddEvent(entity, triggerEvent)
            .AddAction(entity, action)
            .AddParameter(action, new Property(domain, "event", stringType))
            .SetEventHandlerTrigger(action, triggerEvent, "event")
            .AddEventSubscription(entity, subscription)
            .Apply();

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ActionTrigger);

        await Assert.That(diagnostic).IsNotNull();
    }

    [Test]
    public async Task EventHandlerTrigger_InvalidChange_RollsBackPreviousTrigger() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "OnTicketCreated", entity);
        var ticketCreated = new Event(domain, "TicketCreated");
        var ticketAssigned = new Event(domain, "TicketAssigned");
        var subscription = new EventSubscription(domain, entity, ticketCreated, action);

        domain.CreateMutation()
            .AddType(entity)
            .AddType(ticketCreated)
            .AddType(ticketAssigned)
            .AddEvent(entity, ticketCreated)
            .AddEvent(entity, ticketAssigned)
            .AddAction(entity, action)
            .AddParameter(action, new Property(domain, "createdEvent", ticketCreated))
            .AddParameter(action, new Property(domain, "assignedEvent", ticketAssigned))
            .SetEventHandlerTrigger(action, ticketCreated, "createdEvent")
            .AddEventSubscription(entity, subscription)
            .Apply();

        var result = domain.CreateMutation()
            .SetEventHandlerTrigger(action, ticketAssigned, "missing")
            .Apply();

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ActionTrigger);
        await Assert.That(diagnostic).IsNotNull();

        await Assert.That(action.Trigger is ActionTrigger.EventHandler).IsTrue();
        var trigger = (ActionTrigger.EventHandler)action.Trigger;
        await Assert.That(trigger.EventType.Name).IsEqualTo("TicketCreated");
        await Assert.That(trigger.EventParameterName).IsEqualTo("createdEvent");
    }
}