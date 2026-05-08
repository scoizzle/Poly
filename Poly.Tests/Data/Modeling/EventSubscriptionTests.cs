using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling;

public class EventSubscriptionTests {
    [Test]
    public async Task EventSubscription_CorrelatedWithMatchingBinding_Succeeds() {
        var domain = new Domain("Commerce");
        var payment = new Entity(domain, "Payment");
        var order = new Entity(domain, "Order");
        var orderIdType = new Primitive(domain, "OrderId", TypeCategory.Text);
        var paymentReceived = new Event(domain, "PaymentReceived");
        var eventOrderId = new Property(domain, "OrderId", orderIdType);
        var consumerOrderId = new Property(domain, "OrderId", orderIdType);
        var handler = new DomainAction(domain, "OnPaymentReceived", order);
        var eventParameter = new Property(domain, "event", paymentReceived);
        var subscription = new EventSubscription(domain, order, paymentReceived, handler, "event");

        var result = domain.CreateMutation()
            .AddType(orderIdType)
            .AddType(payment)
            .AddType(order)
            .AddType(paymentReceived)
            .AddEvent(payment, paymentReceived)
            .AddProperty(paymentReceived, eventOrderId)
            .AddProperty(order, consumerOrderId)
            .AddAction(order, handler)
            .AddParameter(handler, eventParameter)
            .SetEventHandlerTrigger(handler, paymentReceived, eventParameter.Name)
            .AddEventSubscription(order, subscription)
            .AddEventSubscriptionCorrelation(subscription, new EventCorrelationBinding(domain, eventOrderId.Name, consumerOrderId.Name))
            .SetEventSubscriptionAudience(subscription, new EventSubscriptionAudience.Correlated())
            .Apply();

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.EventSubscription);
        await Assert.That(diagnostic).IsNull();
    }

    [Test]
    public async Task EventSubscription_CorrelatedWithoutBindings_ReportsError() {
        var domain = new Domain("Commerce");
        var payment = new Entity(domain, "Payment");
        var order = new Entity(domain, "Order");
        var paymentReceived = new Event(domain, "PaymentReceived");
        var handler = new DomainAction(domain, "OnPaymentReceived", order);
        var eventParameter = new Property(domain, "event", paymentReceived);
        var subscription = new EventSubscription(domain, order, paymentReceived, handler, "event");

        var result = domain.CreateMutation()
            .AddType(payment)
            .AddType(order)
            .AddType(paymentReceived)
            .AddEvent(payment, paymentReceived)
            .AddAction(order, handler)
            .AddParameter(handler, eventParameter)
            .SetEventHandlerTrigger(handler, paymentReceived, eventParameter.Name)
            .AddEventSubscription(order, subscription)
            .SetEventSubscriptionAudience(subscription, new EventSubscriptionAudience.Correlated())
            .Apply();

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.EventSubscription);
        await Assert.That(diagnostic).IsNotNull();
    }

    [Test]
    public async Task EventSubscription_HandlerOnDifferentEntity_ReportsError() {
        var domain = new Domain("Commerce");
        var payment = new Entity(domain, "Payment");
        var order = new Entity(domain, "Order");
        var paymentReceived = new Event(domain, "PaymentReceived");
        var handler = new DomainAction(domain, "OnPaymentReceived", payment);
        var eventParameter = new Property(domain, "event", paymentReceived);
        var subscription = new EventSubscription(domain, order, paymentReceived, handler, "event");

        var result = domain.CreateMutation()
            .AddType(payment)
            .AddType(order)
            .AddType(paymentReceived)
            .AddEvent(payment, paymentReceived)
            .AddAction(payment, handler)
            .AddParameter(handler, eventParameter)
            .SetEventHandlerTrigger(handler, paymentReceived, eventParameter.Name)
            .AddEventSubscription(order, subscription)
            .Apply();

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.EventSubscription);
        await Assert.That(diagnostic).IsNotNull();
    }

    [Test]
    public async Task EventSubscription_Correlation_WhenConsumerTypeIsBase_AllowsEventSubtype() {
        var domain = new Domain("Commerce");
        var payment = new Entity(domain, "Payment");
        var order = new Entity(domain, "Order");
        var baseIdentity = new Entity(domain, "IdentityBase");
        var derivedIdentity = new Entity(domain, "IdentityDerived", baseIdentity);
        var paymentReceived = new Event(domain, "PaymentReceived");
        var eventIdentity = new Property(domain, "Identity", derivedIdentity);
        var consumerIdentity = new Property(domain, "Identity", baseIdentity);
        var handler = new DomainAction(domain, "OnPaymentReceived", order);
        var eventParameter = new Property(domain, "event", paymentReceived);
        var subscription = new EventSubscription(domain, order, paymentReceived, handler, "event");

        var result = domain.CreateMutation()
            .AddType(payment)
            .AddType(order)
            .AddType(baseIdentity)
            .AddType(derivedIdentity)
            .AddType(paymentReceived)
            .AddEvent(payment, paymentReceived)
            .AddProperty(paymentReceived, eventIdentity)
            .AddProperty(order, consumerIdentity)
            .AddAction(order, handler)
            .AddParameter(handler, eventParameter)
            .SetEventHandlerTrigger(handler, paymentReceived, eventParameter.Name)
            .AddEventSubscription(order, subscription)
            .AddEventSubscriptionCorrelation(subscription, new EventCorrelationBinding(domain, eventIdentity.Name, consumerIdentity.Name))
            .SetEventSubscriptionAudience(subscription, new EventSubscriptionAudience.Correlated())
            .Apply();

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.EventSubscription);
        await Assert.That(diagnostic).IsNull();
    }

    [Test]
    public async Task EventSubscription_Correlation_WhenConsumerTypeIsDerived_RejectsEventBaseType() {
        var domain = new Domain("Commerce");
        var payment = new Entity(domain, "Payment");
        var order = new Entity(domain, "Order");
        var baseIdentity = new Entity(domain, "IdentityBase");
        var derivedIdentity = new Entity(domain, "IdentityDerived", baseIdentity);
        var paymentReceived = new Event(domain, "PaymentReceived");
        var eventIdentity = new Property(domain, "Identity", baseIdentity);
        var consumerIdentity = new Property(domain, "Identity", derivedIdentity);
        var handler = new DomainAction(domain, "OnPaymentReceived", order);
        var eventParameter = new Property(domain, "event", paymentReceived);
        var subscription = new EventSubscription(domain, order, paymentReceived, handler, "event");

        var result = domain.CreateMutation()
            .AddType(payment)
            .AddType(order)
            .AddType(baseIdentity)
            .AddType(derivedIdentity)
            .AddType(paymentReceived)
            .AddEvent(payment, paymentReceived)
            .AddProperty(paymentReceived, eventIdentity)
            .AddProperty(order, consumerIdentity)
            .AddAction(order, handler)
            .AddParameter(handler, eventParameter)
            .SetEventHandlerTrigger(handler, paymentReceived, eventParameter.Name)
            .AddEventSubscription(order, subscription)
            .AddEventSubscriptionCorrelation(subscription, new EventCorrelationBinding(domain, eventIdentity.Name, consumerIdentity.Name))
            .SetEventSubscriptionAudience(subscription, new EventSubscriptionAudience.Correlated())
            .Apply();

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.EventSubscription);
        await Assert.That(diagnostic).IsNotNull();
    }

    [Test]
    public async Task EventSubscription_MissingHandlerParameter_ReportsError() {
        var domain = new Domain("Commerce");
        var payment = new Entity(domain, "Payment");
        var order = new Entity(domain, "Order");
        var paymentReceived = new Event(domain, "PaymentReceived");
        var handler = new DomainAction(domain, "OnPaymentReceived", order);
        var subscription = new EventSubscription(domain, order, paymentReceived, handler, "event");

        var result = domain.CreateMutation()
            .AddType(payment)
            .AddType(order)
            .AddType(paymentReceived)
            .AddEvent(payment, paymentReceived)
            .AddAction(order, handler)
            .AddEventSubscription(order, subscription)
            .Apply();

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.EventSubscription);
        await Assert.That(diagnostic).IsNotNull();
    }
}