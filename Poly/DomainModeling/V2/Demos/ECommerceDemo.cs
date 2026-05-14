using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation.Constraints;

namespace Poly.DomainModeling.V2.Demos;

/// <summary>
/// E-commerce order-processing domain built using the DomainModeling.V2 session API.
/// Demonstrates the unified intent-driven authoring path for UI, API, and MCP consumers.
/// </summary>
public static class ECommerceDemo {
    public static DomainSession Build() {
        var store = new DomainSessionStore();
        var (_, session) = store.Create("E-commerce Order Processing");

        AddPrimitives(session);
        AddEntities(session);
        AddStages(session);
        AddEventTypes(session);
        WireEntityEvents(session);
        AddActions(session);
        WireEffects(session);
        AddRelationships(session);
        AddPolicies(session);

        return session;
    }

    // ── Primitives ─────────────────────────────────────────────────────────────

    private static void AddPrimitives(DomainSession session) {
        session.Apply([
            new AddPrimitiveTypeIntent("string",  TypeCategory.Text),
            new AddPrimitiveTypeIntent("int",     TypeCategory.Integer),
            new AddPrimitiveTypeIntent("decimal", TypeCategory.HighPrecision),
            new AddPrimitiveTypeIntent("bool",    TypeCategory.Primitive),
            new AddPrimitiveTypeIntent("instant", TypeCategory.Instant),
            new AddPrimitiveTypeIntent("email",   TypeCategory.Text),
            new AddPrimitiveTypeIntent("phone",   TypeCategory.Text),
            new AddPrimitiveTypeIntent("address", TypeCategory.Text),
            new AddPrimitiveTypeIntent("sku",     TypeCategory.Text),
        ]);
    }

    // ── Entities ──────────────────────────────────────────────────────────────

    private static void AddEntities(DomainSession session) {
        // Pass 1: create all base entity stubs
        session.Apply([
            new AddEntityTypeIntent("User"),
            new AddEntityTypeIntent("Product"),
            new AddEntityTypeIntent("Order"),
            new AddEntityTypeIntent("OrderItem"),
            new AddEntityTypeIntent("Payment"),
            new AddEntityTypeIntent("Shipment"),
            new AddEntityTypeIntent("Review"),
            new AddEntityTypeIntent("Category"),
        ]);

        // Pass 2: create derived entities (parents now committed)
        session.Apply([
            new AddEntityTypeIntent("Customer", new DomainNodeReference("User")),
            new AddEntityTypeIntent("Admin",    new DomainNodeReference("User")),
        ]);

        // Pass 3: add properties to all entities (all entities now committed)
        session.Apply([
            new AddPropertyToEntityIntent("User", "Username",    "string"),
            new AddPropertyToEntityIntent("User", "Email",       "email"),
            new AddPropertyToEntityIntent("User", "PhoneNumber", "phone"),
            new AddPropertyToEntityIntent("User", "IsActive",    "bool"),

            new AddPropertyToEntityIntent("Customer", "CustomerId",       "string"),
            new AddPropertyToEntityIntent("Customer", "ShippingAddress",  "address"),
            new AddPropertyToEntityIntent("Customer", "BillingAddress",   "address"),
            new AddPropertyToEntityIntent("Customer", "LoyaltyPoints",    "int"),

            new AddPropertyToEntityIntent("Admin", "EmployeeId",  "string"),
            new AddPropertyToEntityIntent("Admin", "Role",        "string"),
            new AddPropertyToEntityIntent("Admin", "Department",  "string"),

            new AddPropertyToEntityIntent("Product", "SKU",           "sku"),
            new AddPropertyToEntityIntent("Product", "Name",          "string"),
            new AddPropertyToEntityIntent("Product", "Description",   "string"),
            new AddPropertyToEntityIntent("Product", "Price",         "decimal"),
            new AddPropertyToEntityIntent("Product", "StockQuantity", "int"),
            new AddPropertyToEntityIntent("Product", "Weight",        "decimal"),
            new AddPropertyToEntityIntent("Product", "IsAvailable",   "bool"),

            new AddPropertyToEntityIntent("Order", "OrderId",         "string"),
            new AddPropertyToEntityIntent("Order", "OrderDate",       "instant"),
            new AddPropertyToEntityIntent("Order", "TotalAmount",     "decimal"),
            new AddPropertyToEntityIntent("Order", "ShippingAddress", "address"),
            new AddPropertyToEntityIntent("Order", "Status",          "string"),

            new AddPropertyToEntityIntent("OrderItem", "Quantity",  "int"),
            new AddPropertyToEntityIntent("OrderItem", "UnitPrice", "decimal"),
            new AddPropertyToEntityIntent("OrderItem", "LineTotal",  "decimal"),

            new AddPropertyToEntityIntent("Payment", "PaymentDate",   "instant"),
            new AddPropertyToEntityIntent("Payment", "Amount",        "decimal"),
            new AddPropertyToEntityIntent("Payment", "PaymentMethod", "string"),
            new AddPropertyToEntityIntent("Payment", "TransactionId", "string"),
            new AddPropertyToEntityIntent("Payment", "IsSuccessful",  "bool"),

            new AddPropertyToEntityIntent("Shipment", "TrackingNumber",    "string"),
            new AddPropertyToEntityIntent("Shipment", "Carrier",           "string"),
            new AddPropertyToEntityIntent("Shipment", "ShippedDate",       "instant"),
            new AddPropertyToEntityIntent("Shipment", "EstimatedDelivery", "instant"),
            new AddPropertyToEntityIntent("Shipment", "ActualDelivery",    "instant"),

            new AddPropertyToEntityIntent("Review", "Rating",     "int"),
            new AddPropertyToEntityIntent("Review", "Comment",    "string"),
            new AddPropertyToEntityIntent("Review", "ReviewDate", "instant"),
            new AddPropertyToEntityIntent("Review", "IsVerified", "bool"),

            new AddPropertyToEntityIntent("Category", "Name",        "string"),
            new AddPropertyToEntityIntent("Category", "Description", "string"),
        ]);
    }

    // ── Stages ────────────────────────────────────────────────────────────────

    private static void AddStages(DomainSession session) {
        // Order stages — parents must be committed before children reference them
        session.Apply([
            new AddStageToEntityIntent("Order", "Cart"),
            new AddStageToEntityIntent("Order", "Pending"),
            new AddStageToEntityIntent("Order", "Cancelled"),
        ]);
        session.Apply([
            new AddStageToEntityIntent("Order", "Paid",       "Pending"),
        ]);
        session.Apply([
            new AddStageToEntityIntent("Order", "Processing", "Paid"),
            new AddStageToEntityIntent("Order", "Refunded",   "Paid"),
        ]);
        session.Apply([
            new AddStageToEntityIntent("Order", "Shipped",    "Processing"),
        ]);
        session.Apply([
            new AddStageToEntityIntent("Order", "Delivered",  "Shipped"),
        ]);

        // Payment stages
        session.Apply([
            new AddStageToEntityIntent("Payment", "Initiated"),
            new AddStageToEntityIntent("Payment", "Failed"),
            new AddStageToEntityIntent("Payment", "Refunded"),
        ]);
        session.Apply([
            new AddStageToEntityIntent("Payment", "Authorized", "Initiated"),
        ]);
        session.Apply([
            new AddStageToEntityIntent("Payment", "Captured",   "Authorized"),
        ]);

        // Shipment stages
        session.Apply([
            new AddStageToEntityIntent("Shipment", "Preparing"),
            new AddStageToEntityIntent("Shipment", "Returned"),
        ]);
        session.Apply([
            new AddStageToEntityIntent("Shipment", "LabelCreated",    "Preparing"),
        ]);
        session.Apply([
            new AddStageToEntityIntent("Shipment", "InTransit",       "LabelCreated"),
        ]);
        session.Apply([
            new AddStageToEntityIntent("Shipment", "OutForDelivery",  "InTransit"),
        ]);
        session.Apply([
            new AddStageToEntityIntent("Shipment", "Delivered",       "OutForDelivery"),
        ]);

        // Product stages
        session.Apply([
            new AddStageToEntityIntent("Product", "Draft"),
            new AddStageToEntityIntent("Product", "Discontinued"),
        ]);
        session.Apply([
            new AddStageToEntityIntent("Product", "Active",     "Draft"),
        ]);
        session.Apply([
            new AddStageToEntityIntent("Product", "OutOfStock", "Active"),
        ]);
    }

    // ── Event Types ───────────────────────────────────────────────────────────

    private static void AddEventTypes(DomainSession session) {
        // Create event type stubs first
        session.Apply([
            new AddEventTypeIntent("OrderPlaced"),
            new AddEventTypeIntent("OrderCancelled"),
            new AddEventTypeIntent("PaymentProcessed"),
            new AddEventTypeIntent("PaymentFailed"),
            new AddEventTypeIntent("ShipmentCreated"),
            new AddEventTypeIntent("ShipmentDelivered"),
            new AddEventTypeIntent("ProductCreated"),
            new AddEventTypeIntent("StockUpdated"),
        ]);

        // Then add properties (event types now committed)
        session.Apply([
            new AddPropertyToEventTypeIntent("OrderPlaced",      "OrderId",      "string"),
            new AddPropertyToEventTypeIntent("OrderPlaced",      "CustomerName", "string"),
            new AddPropertyToEventTypeIntent("OrderCancelled",   "Reason",       "string"),
            new AddPropertyToEventTypeIntent("PaymentProcessed", "TransactionId","string"),
            new AddPropertyToEventTypeIntent("PaymentProcessed", "Amount",       "decimal"),
            new AddPropertyToEventTypeIntent("PaymentFailed",    "FailureReason","string"),
            new AddPropertyToEventTypeIntent("ShipmentCreated",  "TrackingNumber","string"),
            new AddPropertyToEventTypeIntent("ShipmentCreated",  "Carrier",       "string"),
            new AddPropertyToEventTypeIntent("ShipmentDelivered","DeliveryDate",  "instant"),
            new AddPropertyToEventTypeIntent("ProductCreated",   "SKU",           "sku"),
            new AddPropertyToEventTypeIntent("ProductCreated",   "Name",          "string"),
            new AddPropertyToEventTypeIntent("StockUpdated",     "NewQuantity",   "int"),
        ]);
    }

    // ── Wire entity-to-event associations ────────────────────────────────────

    private static void WireEntityEvents(DomainSession session) {
        session.Apply([
            new AddEventToEntityIntent("Order",    "OrderPlaced"),
            new AddEventToEntityIntent("Order",    "OrderCancelled"),
            new AddEventToEntityIntent("Payment",  "PaymentProcessed"),
            new AddEventToEntityIntent("Payment",  "PaymentFailed"),
            new AddEventToEntityIntent("Shipment", "ShipmentCreated"),
            new AddEventToEntityIntent("Shipment", "ShipmentDelivered"),
            new AddEventToEntityIntent("Product",  "ProductCreated"),
            new AddEventToEntityIntent("Product",  "StockUpdated"),
        ]);
    }

    // ── Actions (intent-based) ────────────────────────────────────────────────

    private static void AddActions(DomainSession session) {
        // Add all entity actions first
        session.Apply([
            new AddActionToEntityIntent("Order", "PlaceOrder"),
            new AddActionToEntityIntent("Order", "CancelOrder"),
            new AddActionToEntityIntent("Order", "MarkPaid"),
            new AddActionToEntityIntent("Order", "ProcessOrder"),
            new AddActionToEntityIntent("Order", "MarkShipped"),
        ]);
        // Add parameters (actions must already be committed)
        session.Apply([
            new AddActionParameterIntent("Order", "CancelOrder", "Reason", "string"),
        ]);
        // Register actions on stages (action must exist first)
        session.Apply([
            new AddActionToStageIntent("Order", "Cart",       "PlaceOrder"),
            new AddActionToStageIntent("Order", "Pending",    "CancelOrder"),
            new AddActionToStageIntent("Order", "Paid",       "CancelOrder"),
            new AddActionToStageIntent("Order", "Processing", "CancelOrder"),
            new AddActionToStageIntent("Order", "Pending",    "MarkPaid"),
            new AddActionToStageIntent("Order", "Paid",       "ProcessOrder"),
            new AddActionToStageIntent("Order", "Processing", "MarkShipped"),
        ]);

        // Payment actions
        session.Apply([
            new AddActionToEntityIntent("Payment", "ProcessPayment"),
            new AddActionToEntityIntent("Payment", "FailPayment"),
        ]);
        session.Apply([
            new AddActionParameterIntent("Payment", "ProcessPayment", "TransactionId", "string"),
            new AddActionParameterIntent("Payment", "FailPayment", "FailureReason", "string"),
        ]);

        // Shipment actions
        session.Apply([
            new AddActionToEntityIntent("Shipment", "CreateShipment"),
            new AddActionToEntityIntent("Shipment", "MarkDelivered"),
        ]);
        session.Apply([
            new AddActionParameterIntent("Shipment", "CreateShipment", "TrackingNumber", "string"),
            new AddActionParameterIntent("Shipment", "CreateShipment", "Carrier",        "string"),
        ]);
        session.Apply([
            new AddActionToStageIntent("Shipment", "OutForDelivery", "MarkDelivered"),
        ]);

        // Product actions
        session.Apply([
            new AddActionToEntityIntent("Product", "AddProduct"),
            new AddActionToEntityIntent("Product", "ActivateProduct"),
            new AddActionToEntityIntent("Product", "UpdateStock"),
        ]);
        session.Apply([
            new AddActionParameterIntent("Product", "UpdateStock", "NewQuantity", "int"),
        ]);
        session.Apply([
            new AddActionToStageIntent("Product", "Draft", "ActivateProduct"),
        ]);

        // OrderItem actions
        session.Apply([
            new AddActionToEntityIntent("OrderItem", "AddOrderItem"),
        ]);
    }

    // ── Effect wiring (uses low-level mutation API for effects not covered by intents) ─

    private static void WireEffects(DomainSession session) {
        var domain = session.Domain;

        var order    = domain.RequireEntity("Order");
        var payment  = domain.RequireEntity("Payment");
        var shipment = domain.RequireEntity("Shipment");
        var product  = domain.RequireEntity("Product");
        var orderItem = domain.RequireEntity("OrderItem");

        // PlaceOrder → StageTransition:Pending, PublishEvent:OrderPlaced
        var placeOrder = order.RequireAction("PlaceOrder");
        var publishOrderPlaced = new PublishEvent(domain) { Event = order.RequireEvent("OrderPlaced") };
        domain.CreateMutation()
            .AddEffect(placeOrder, new StageTransition(domain) { TargetStage = order.RequireStage("Pending") })
            .AddEffect(placeOrder, publishOrderPlaced)
            .SetEventPropertyBinding(placeOrder, publishOrderPlaced, "OrderId", new EventPropertyBindingSource.EntityProperty("OrderId"))
            .Apply();

        // CancelOrder → StageTransition:Cancelled, PublishEvent:OrderCancelled
        var cancelOrder = order.RequireAction("CancelOrder");
        var publishOrderCancelled = new PublishEvent(domain) { Event = order.RequireEvent("OrderCancelled") };
        domain.CreateMutation()
            .AddEffect(cancelOrder, new StageTransition(domain) { TargetStage = order.RequireStage("Cancelled") })
            .AddEffect(cancelOrder, publishOrderCancelled)
            .SetEventPropertyBinding(cancelOrder, publishOrderCancelled, "Reason", new EventPropertyBindingSource.ActionParameter("Reason"))
            .Apply();

        // MarkPaid → StageTransition:Paid
        var markPaid = order.RequireAction("MarkPaid");
        domain.CreateMutation()
            .AddEffect(markPaid, new StageTransition(domain) { TargetStage = order.RequireStage("Paid") })
            .Apply();

        // ProcessOrder → StageTransition:Processing
        var processOrder = order.RequireAction("ProcessOrder");
        domain.CreateMutation()
            .AddEffect(processOrder, new StageTransition(domain) { TargetStage = order.RequireStage("Processing") })
            .Apply();

        // MarkShipped → StageTransition:Shipped
        var markShipped = order.RequireAction("MarkShipped");
        domain.CreateMutation()
            .AddEffect(markShipped, new StageTransition(domain) { TargetStage = order.RequireStage("Shipped") })
            .Apply();

        // ProcessPayment → StageTransition:Captured, PublishEvent:PaymentProcessed
        var processPayment = payment.RequireAction("ProcessPayment");
        var publishPaymentProcessed = new PublishEvent(domain) { Event = payment.RequireEvent("PaymentProcessed") };
        domain.CreateMutation()
            .AddEffect(processPayment, new StageTransition(domain) { TargetStage = payment.RequireStage("Captured") })
            .AddEffect(processPayment, publishPaymentProcessed)
            .SetEventPropertyBinding(processPayment, publishPaymentProcessed, "TransactionId", new EventPropertyBindingSource.ActionParameter("TransactionId"))
            .SetEventPropertyBinding(processPayment, publishPaymentProcessed, "Amount",        new EventPropertyBindingSource.EntityProperty("Amount"))
            .Apply();

        // FailPayment → StageTransition:Failed, PublishEvent:PaymentFailed
        var failPayment = payment.RequireAction("FailPayment");
        var publishPaymentFailed = new PublishEvent(domain) { Event = payment.RequireEvent("PaymentFailed") };
        domain.CreateMutation()
            .AddEffect(failPayment, new StageTransition(domain) { TargetStage = payment.RequireStage("Failed") })
            .AddEffect(failPayment, publishPaymentFailed)
            .SetEventPropertyBinding(failPayment, publishPaymentFailed, "FailureReason", new EventPropertyBindingSource.ActionParameter("FailureReason"))
            .Apply();

        // CreateShipment → StageTransition:Preparing, PublishEvent:ShipmentCreated
        var createShipment = shipment.RequireAction("CreateShipment");
        var publishShipmentCreated = new PublishEvent(domain) { Event = shipment.RequireEvent("ShipmentCreated") };
        domain.CreateMutation()
            .AddEffect(createShipment, new StageTransition(domain) { TargetStage = shipment.RequireStage("Preparing") })
            .AddEffect(createShipment, publishShipmentCreated)
            .SetEventPropertyBinding(createShipment, publishShipmentCreated, "TrackingNumber", new EventPropertyBindingSource.ActionParameter("TrackingNumber"))
            .SetEventPropertyBinding(createShipment, publishShipmentCreated, "Carrier",        new EventPropertyBindingSource.ActionParameter("Carrier"))
            .Apply();

        // MarkDelivered → StageTransition:Delivered, PublishEvent:ShipmentDelivered
        var markDelivered = shipment.RequireAction("MarkDelivered");
        var publishShipmentDelivered = new PublishEvent(domain) { Event = shipment.RequireEvent("ShipmentDelivered") };
        domain.CreateMutation()
            .AddEffect(markDelivered, new StageTransition(domain) { TargetStage = shipment.RequireStage("Delivered") })
            .AddEffect(markDelivered, publishShipmentDelivered)
            .SetEventPropertyBinding(markDelivered, publishShipmentDelivered, "DeliveryDate", new EventPropertyBindingSource.EntityProperty("ActualDelivery"))
            .Apply();

        // AddProduct → StageTransition:Draft, PublishEvent:ProductCreated
        var addProduct = product.RequireAction("AddProduct");
        var publishProductCreated = new PublishEvent(domain) { Event = product.RequireEvent("ProductCreated") };
        domain.CreateMutation()
            .AddEffect(addProduct, new StageTransition(domain) { TargetStage = product.RequireStage("Draft") })
            .AddEffect(addProduct, publishProductCreated)
            .SetEventPropertyBinding(addProduct, publishProductCreated, "SKU",  new EventPropertyBindingSource.EntityProperty("SKU"))
            .SetEventPropertyBinding(addProduct, publishProductCreated, "Name", new EventPropertyBindingSource.EntityProperty("Name"))
            .Apply();

        // ActivateProduct → StageTransition:Active
        var activateProduct = product.RequireAction("ActivateProduct");
        domain.CreateMutation()
            .AddEffect(activateProduct, new StageTransition(domain) { TargetStage = product.RequireStage("Active") })
            .Apply();

        // UpdateStock → PublishEvent:StockUpdated
        var updateStock = product.RequireAction("UpdateStock");
        var publishStockUpdated = new PublishEvent(domain) { Event = product.RequireEvent("StockUpdated") };
        domain.CreateMutation()
            .AddEffect(updateStock, publishStockUpdated)
            .SetEventPropertyBinding(updateStock, publishStockUpdated, "NewQuantity", new EventPropertyBindingSource.ActionParameter("NewQuantity"))
            .Apply();

        // AddOrderItem → CreateEntityInstance:OrderItem
        var addOrderItem = orderItem.RequireAction("AddOrderItem");
        domain.CreateMutation()
            .AddEffect(addOrderItem, new CreateEntityInstance(domain, orderItem, null))
            .Apply();
    }

    // ── Relationships ─────────────────────────────────────────────────────────

    private static void AddRelationships(DomainSession session) {
        session.Apply([
            new AddRelationshipIntent("CustomerOrders",   new DomainNodeReference("Customer"), new DomainNodeReference("Order"),     RelationshipCardinality.OneToMany, true),
            new AddRelationshipIntent("OrderItems",       new DomainNodeReference("Order"),    new DomainNodeReference("OrderItem"), RelationshipCardinality.OneToMany, true),
            new AddRelationshipIntent("ProductOrders",    new DomainNodeReference("Product"),  new DomainNodeReference("OrderItem"), RelationshipCardinality.OneToMany, false),
            new AddRelationshipIntent("OrderPayments",    new DomainNodeReference("Order"),    new DomainNodeReference("Payment"),   RelationshipCardinality.OneToMany, true),
            new AddRelationshipIntent("OrderShipments",   new DomainNodeReference("Order"),    new DomainNodeReference("Shipment"),  RelationshipCardinality.OneToMany, true),
            new AddRelationshipIntent("ProductReviews",   new DomainNodeReference("Product"),  new DomainNodeReference("Review"),    RelationshipCardinality.OneToMany, false),
            new AddRelationshipIntent("CustomerReviews",  new DomainNodeReference("Customer"), new DomainNodeReference("Review"),    RelationshipCardinality.OneToMany, false),
            new AddRelationshipIntent("ProductCategories",new DomainNodeReference("Product"),  new DomainNodeReference("Category"),  RelationshipCardinality.ManyToMany, false),
            new AddRelationshipIntent("AdminOrders",      new DomainNodeReference("Admin"),    new DomainNodeReference("Order"),     RelationshipCardinality.OneToMany, false),
        ]);
    }

    // ── Policies ──────────────────────────────────────────────────────────────

    private static void AddPolicies(DomainSession session) {
        session.Apply([
            new AddPolicyToEntityIntent("Order",    "RequireShippingAddress"),
            new AddPolicyToEntityIntent("Customer", "RequireActiveCustomer"),
            new AddPolicyToEntityIntent("Product",  "RequireStockAvailable"),
            new AddPolicyToEntityIntent("Payment",  "RequirePaymentMethod"),
            new AddPolicyToStageIntent("Shipment",  "LabelCreated", "RequireTrackingNumber"),
        ]);

        // Wire low-level property rules that are not yet reachable via intent API
        var domain = session.Domain;
        var order    = domain.RequireEntity("Order");
        var customer = domain.RequireEntity("Customer");
        var product  = domain.RequireEntity("Product");
        var payment  = domain.RequireEntity("Payment");
        var shipment = domain.RequireEntity("Shipment");

        domain.CreateMutation()
            .AddRule(order.RequirePolicy("RequireShippingAddress"),
                new PropertyRule(domain, "ShippingAddressRequired", order.RequireProperty("ShippingAddress"), new RequiredConstraint()))
            .AddRule(customer.RequirePolicy("RequireActiveCustomer"),
                new PropertyRule(domain, "IsActiveCheck", FindInHierarchy(customer, "IsActive"), new RequiredConstraint()))
            .AddRule(product.RequirePolicy("RequireStockAvailable"),
                new PropertyRule(domain, "StockQuantityCheck", product.RequireProperty("StockQuantity"), new RequiredConstraint()))
            .AddRule(payment.RequirePolicy("RequirePaymentMethod"),
                new PropertyRule(domain, "PaymentMethodRequired", payment.RequireProperty("PaymentMethod"), new RequiredConstraint()))
            .AddRule(shipment.RequireStage("LabelCreated").RequirePolicy("RequireTrackingNumber"),
                new PropertyRule(domain, "TrackingNumberRequired", shipment.RequireProperty("TrackingNumber"), new RequiredConstraint()))
            .Apply();
    }

    private static Property FindInHierarchy(Entity entity, string propertyName) {
        for (var current = entity; current is not null; current = current.ParentEntity) {
            var prop = current.FindProperty(propertyName);
            if (prop is not null) return prop;
        }
        throw new InvalidOperationException($"Property '{propertyName}' not found in hierarchy of '{entity.Name}'.");
    }
}
