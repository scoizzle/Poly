using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling;

public class ActionTriggerTests {
    [Test]
    public async Task EventSubscription_ValidEventParameter_Succeeds() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "OnTicketCreated", entity);
        var triggerEvent = new Event(domain, "TicketCreated");
        var subscription = new EventSubscription(domain, entity, triggerEvent, action, "event");

        domain.CreateMutation()
            .AddType(entity)
            .AddType(triggerEvent)
            .AddEvent(entity, triggerEvent)
            .AddAction(entity, action)
            .AddParameter(action, new Property(domain, "event", triggerEvent))
            .AddEventSubscription(entity, subscription)
            .Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.EventSubscription);

        await Assert.That(diagnostic).IsNull();
    }

    [Test]
    public async Task EventSubscription_MissingEventParameter_ReportsError() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "OnTicketCreated", entity);
        var triggerEvent = new Event(domain, "TicketCreated");
        var subscription = new EventSubscription(domain, entity, triggerEvent, action, "missing");

        var result = domain.CreateMutation()
            .AddType(entity)
            .AddType(triggerEvent)
            .AddEvent(entity, triggerEvent)
            .AddAction(entity, action)
            .AddEventSubscription(entity, subscription)
            .Apply();

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.EventSubscription);

        await Assert.That(diagnostic).IsNotNull();
    }

    [Test]
    public async Task EventSubscription_EventParameterTypeMismatch_ReportsError() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "OnTicketCreated", entity);
        var triggerEvent = new Event(domain, "TicketCreated");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var subscription = new EventSubscription(domain, entity, triggerEvent, action, "event");

        var result = domain.CreateMutation()
            .AddType(stringType)
            .AddType(entity)
            .AddType(triggerEvent)
            .AddEvent(entity, triggerEvent)
            .AddAction(entity, action)
            .AddParameter(action, new Property(domain, "event", stringType))
            .AddEventSubscription(entity, subscription)
            .Apply();

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.EventSubscription);

        await Assert.That(diagnostic).IsNotNull();
    }

    [Test]
    public async Task EventSubscription_InvalidParameterChange_RollsBackPreviousValue() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "OnTicketCreated", entity);
        var ticketCreated = new Event(domain, "TicketCreated");
        var subscription = new EventSubscription(domain, entity, ticketCreated, action, "createdEvent");

        domain.CreateMutation()
            .AddType(entity)
            .AddType(ticketCreated)
            .AddEvent(entity, ticketCreated)
            .AddAction(entity, action)
            .AddParameter(action, new Property(domain, "createdEvent", ticketCreated))
            .AddEventSubscription(entity, subscription)
            .Apply();

        var result = domain.CreateMutation()
            .SetEventSubscriptionEventParameter(subscription, "missing")
            .Apply();

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.EventSubscription);
        await Assert.That(diagnostic).IsNotNull();

        await Assert.That(subscription.EventParameterName).IsEqualTo("createdEvent");
    }
}