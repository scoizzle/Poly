using System;
using System.Linq;

using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Benchmarks.DomainModeling.Demos;

internal static class ECommerceDomain {
    public static Domain BuildECommerceDomain() {
        var domain = new Domain("E-commerce Order Processing");

        CreatePrimitives(domain);
        CreateEntities(domain);
        CreateStages(domain);
        CreateEvents(domain);
        CreateActions(domain);
        CreateRelationships(domain);
        CreatePolicies(domain);

        return domain;
    }

    private static void CreatePrimitives(Domain domain) {
        domain.AddType(new Primitive(domain, "string", TypeCategory.Text));
        domain.AddType(new Primitive(domain, "int", TypeCategory.Integer));
        domain.AddType(new Primitive(domain, "decimal", TypeCategory.HighPrecision));
        domain.AddType(new Primitive(domain, "bool", TypeCategory.Primitive));
        domain.AddType(new Primitive(domain, "instant", TypeCategory.Instant));
        domain.AddType(new Primitive(domain, "email", TypeCategory.Text));
        domain.AddType(new Primitive(domain, "phone", TypeCategory.Text));
        domain.AddType(new Primitive(domain, "address", TypeCategory.Text));
        domain.AddType(new Primitive(domain, "sku", TypeCategory.Text));
    }

    private static void CreateEntities(Domain domain) {
        var stringType = domain.RequirePrimitive("string");
        var intType = domain.RequirePrimitive("int");
        var decimalType = domain.RequirePrimitive("decimal");
        var boolType = domain.RequirePrimitive("bool");
        var instantType = domain.RequirePrimitive("instant");
        var emailType = domain.RequirePrimitive("email");
        var phoneType = domain.RequirePrimitive("phone");
        var addressType = domain.RequirePrimitive("address");
        var skuType = domain.RequirePrimitive("sku");

        var user = new Entity(domain, "User");
        user.AddProperty(new Property(domain, "Username", stringType));
        user.AddProperty(new Property(domain, "Email", emailType));
        user.AddProperty(new Property(domain, "PhoneNumber", phoneType));
        user.AddProperty(new Property(domain, "IsActive", boolType));
        domain.AddType(user);

        var customer = new Entity(domain, "Customer", user);
        customer.AddProperty(new Property(domain, "CustomerId", stringType));
        customer.AddProperty(new Property(domain, "ShippingAddress", addressType));
        customer.AddProperty(new Property(domain, "BillingAddress", addressType));
        customer.AddProperty(new Property(domain, "LoyaltyPoints", intType));
        domain.AddType(customer);

        var admin = new Entity(domain, "Admin", user);
        admin.AddProperty(new Property(domain, "EmployeeId", stringType));
        admin.AddProperty(new Property(domain, "Role", stringType));
        admin.AddProperty(new Property(domain, "Department", stringType));
        domain.AddType(admin);

        var product = new Entity(domain, "Product");
        product.AddProperty(new Property(domain, "SKU", skuType));
        product.AddProperty(new Property(domain, "Name", stringType));
        product.AddProperty(new Property(domain, "Description", stringType));
        product.AddProperty(new Property(domain, "Price", decimalType));
        product.AddProperty(new Property(domain, "StockQuantity", intType));
        product.AddProperty(new Property(domain, "Weight", decimalType));
        product.AddProperty(new Property(domain, "IsAvailable", boolType));
        domain.AddType(product);

        var order = new Entity(domain, "Order");
        order.AddProperty(new Property(domain, "OrderId", stringType));
        order.AddProperty(new Property(domain, "OrderDate", instantType));
        order.AddProperty(new Property(domain, "TotalAmount", decimalType));
        order.AddProperty(new Property(domain, "ShippingAddress", addressType));
        order.AddProperty(new Property(domain, "Status", stringType));
        domain.AddType(order);

        var orderItem = new Entity(domain, "OrderItem");
        orderItem.AddProperty(new Property(domain, "Quantity", intType));
        orderItem.AddProperty(new Property(domain, "UnitPrice", decimalType));
        orderItem.AddProperty(new Property(domain, "LineTotal", decimalType));
        domain.AddType(orderItem);

        var payment = new Entity(domain, "Payment");
        payment.AddProperty(new Property(domain, "PaymentDate", instantType));
        payment.AddProperty(new Property(domain, "Amount", decimalType));
        payment.AddProperty(new Property(domain, "PaymentMethod", stringType));
        payment.AddProperty(new Property(domain, "TransactionId", stringType));
        payment.AddProperty(new Property(domain, "IsSuccessful", boolType));
        domain.AddType(payment);

        var shipment = new Entity(domain, "Shipment");
        shipment.AddProperty(new Property(domain, "TrackingNumber", stringType));
        shipment.AddProperty(new Property(domain, "Carrier", stringType));
        shipment.AddProperty(new Property(domain, "ShippedDate", instantType));
        shipment.AddProperty(new Property(domain, "EstimatedDelivery", instantType));
        shipment.AddProperty(new Property(domain, "ActualDelivery", instantType));
        domain.AddType(shipment);

        var review = new Entity(domain, "Review");
        review.AddProperty(new Property(domain, "Rating", intType));
        review.AddProperty(new Property(domain, "Comment", stringType));
        review.AddProperty(new Property(domain, "ReviewDate", instantType));
        review.AddProperty(new Property(domain, "IsVerified", boolType));
        domain.AddType(review);

        var category = new Entity(domain, "Category");
        category.AddProperty(new Property(domain, "Name", stringType));
        category.AddProperty(new Property(domain, "Description", stringType));
        domain.AddType(category);
    }

    private static void CreateStages(Domain domain) {
        var order = domain.RequireEntity("Order");
        var payment = domain.RequireEntity("Payment");
        var shipment = domain.RequireEntity("Shipment");
        var product = domain.RequireEntity("Product");

        order.AddStage(new Stage(domain, "Cart"));
        order.AddStage(new Stage(domain, "Pending"));
        order.AddStage(new Stage(domain, "Paid") { Parent = order.RequireStage("Pending") });
        order.AddStage(new Stage(domain, "Processing") { Parent = order.RequireStage("Paid") });
        order.AddStage(new Stage(domain, "Shipped") { Parent = order.RequireStage("Processing") });
        order.AddStage(new Stage(domain, "Delivered") { Parent = order.RequireStage("Shipped") });
        order.AddStage(new Stage(domain, "Cancelled"));
        order.AddStage(new Stage(domain, "Refunded") { Parent = order.RequireStage("Paid") });

        payment.AddStage(new Stage(domain, "Initiated"));
        payment.AddStage(new Stage(domain, "Authorized") { Parent = payment.RequireStage("Initiated") });
        payment.AddStage(new Stage(domain, "Captured") { Parent = payment.RequireStage("Authorized") });
        payment.AddStage(new Stage(domain, "Failed"));
        payment.AddStage(new Stage(domain, "Refunded"));

        shipment.AddStage(new Stage(domain, "Preparing"));
        shipment.AddStage(new Stage(domain, "LabelCreated") { Parent = shipment.RequireStage("Preparing") });
        shipment.AddStage(new Stage(domain, "InTransit") { Parent = shipment.RequireStage("LabelCreated") });
        shipment.AddStage(new Stage(domain, "OutForDelivery") { Parent = shipment.RequireStage("InTransit") });
        shipment.AddStage(new Stage(domain, "Delivered") { Parent = shipment.RequireStage("OutForDelivery") });
        shipment.AddStage(new Stage(domain, "Returned"));

        product.AddStage(new Stage(domain, "Draft"));
        product.AddStage(new Stage(domain, "Active") { Parent = product.RequireStage("Draft") });
        product.AddStage(new Stage(domain, "OutOfStock") { Parent = product.RequireStage("Active") });
        product.AddStage(new Stage(domain, "Discontinued"));
    }

    private static void CreateEvents(Domain domain) {
        var order = domain.RequireEntity("Order");
        var payment = domain.RequireEntity("Payment");
        var shipment = domain.RequireEntity("Shipment");
        var product = domain.RequireEntity("Product");
        var stringType = domain.RequirePrimitive("string");
        var instantType = domain.RequirePrimitive("instant");
        var decimalType = domain.RequirePrimitive("decimal");

        var orderPlaced = new Event(domain, "OrderPlaced");
        orderPlaced.AddProperty(new Property(domain, "OrderId", stringType));
        orderPlaced.AddProperty(new Property(domain, "CustomerName", stringType));
        order.AddEvent(orderPlaced);
        domain.AddType(orderPlaced);

        var orderCancelled = new Event(domain, "OrderCancelled");
        orderCancelled.AddProperty(new Property(domain, "Reason", stringType));
        order.AddEvent(orderCancelled);
        domain.AddType(orderCancelled);

        var paymentProcessed = new Event(domain, "PaymentProcessed");
        paymentProcessed.AddProperty(new Property(domain, "TransactionId", stringType));
        paymentProcessed.AddProperty(new Property(domain, "Amount", decimalType));
        payment.AddEvent(paymentProcessed);
        domain.AddType(paymentProcessed);

        var paymentFailed = new Event(domain, "PaymentFailed");
        paymentFailed.AddProperty(new Property(domain, "FailureReason", stringType));
        payment.AddEvent(paymentFailed);
        domain.AddType(paymentFailed);

        var shipmentCreated = new Event(domain, "ShipmentCreated");
        shipmentCreated.AddProperty(new Property(domain, "TrackingNumber", stringType));
        shipmentCreated.AddProperty(new Property(domain, "Carrier", stringType));
        shipment.AddEvent(shipmentCreated);
        domain.AddType(shipmentCreated);

        var shipmentDelivered = new Event(domain, "ShipmentDelivered");
        shipmentDelivered.AddProperty(new Property(domain, "DeliveryDate", instantType));
        shipment.AddEvent(shipmentDelivered);
        domain.AddType(shipmentDelivered);

        var productCreated = new Event(domain, "ProductCreated");
        productCreated.AddProperty(new Property(domain, "SKU", domain.RequirePrimitive("sku")));
        productCreated.AddProperty(new Property(domain, "Name", stringType));
        product.AddEvent(productCreated);
        domain.AddType(productCreated);

        var stockUpdated = new Event(domain, "StockUpdated");
        stockUpdated.AddProperty(new Property(domain, "NewQuantity", domain.RequirePrimitive("int")));
        product.AddEvent(stockUpdated);
        domain.AddType(stockUpdated);
    }

    private static void CreateActions(Domain domain) {
        var order = domain.RequireEntity("Order");
        var payment = domain.RequireEntity("Payment");
        var shipment = domain.RequireEntity("Shipment");
        var product = domain.RequireEntity("Product");
        var orderItem = domain.RequireEntity("OrderItem");
        var customer = domain.RequireEntity("Customer");
        var stringType = domain.RequirePrimitive("string");
        var decimalType = domain.RequirePrimitive("decimal");
        var instantType = domain.RequirePrimitive("instant");

        var placeOrder = new DomainAction(domain, "PlaceOrder", order);
        placeOrder.AddEffect(new StageTransition(domain) { TargetStage = order.RequireStage("Pending") });
        var publishOrderPlaced = new PublishEvent(domain) { Event = order.RequireEvent("OrderPlaced") };
        domain.CreateMutation()
            .AddEffect(placeOrder, publishOrderPlaced)
            .SetEventPropertyBinding(placeOrder, publishOrderPlaced, "OrderId", new EventPropertyBindingSource.EntityProperty("OrderId"))
            .Apply();
        order.AddAction(placeOrder);
        order.RequireStage("Cart").AddAction(placeOrder);

        var cancelOrder = new DomainAction(domain, "CancelOrder", order);
        var cancelReasonParam = new Property(domain, "Reason", stringType);
        cancelOrder.AddParameter(cancelReasonParam);
        cancelOrder.AddEffect(new StageTransition(domain) { TargetStage = order.RequireStage("Cancelled") });
        var publishOrderCancelled = new PublishEvent(domain) { Event = order.RequireEvent("OrderCancelled") };
        domain.CreateMutation()
            .AddEffect(cancelOrder, publishOrderCancelled)
            .SetEventPropertyBinding(cancelOrder, publishOrderCancelled, "Reason", new EventPropertyBindingSource.ActionParameter(cancelReasonParam.Name))
            .Apply();
        order.AddAction(cancelOrder);
        order.RequireStage("Pending").AddAction(cancelOrder);
        order.RequireStage("Paid").AddAction(cancelOrder);
        order.RequireStage("Processing").AddAction(cancelOrder);

        var processPayment = new DomainAction(domain, "ProcessPayment", payment);
        var transactionIdParam = new Property(domain, "TransactionId", stringType);
        processPayment.AddParameter(transactionIdParam);
        processPayment.AddEffect(new StageTransition(domain) { TargetStage = payment.RequireStage("Captured") });
        var publishPaymentProcessed = new PublishEvent(domain) { Event = payment.RequireEvent("PaymentProcessed") };
        domain.CreateMutation()
            .AddEffect(processPayment, publishPaymentProcessed)
            .SetEventPropertyBinding(processPayment, publishPaymentProcessed, "TransactionId", new EventPropertyBindingSource.ActionParameter(transactionIdParam.Name))
            .SetEventPropertyBinding(processPayment, publishPaymentProcessed, "Amount", new EventPropertyBindingSource.EntityProperty("Amount"))
            .Apply();
        payment.AddAction(processPayment);

        var failPayment = new DomainAction(domain, "FailPayment", payment);
        var failureReasonParam = new Property(domain, "FailureReason", stringType);
        failPayment.AddParameter(failureReasonParam);
        failPayment.AddEffect(new StageTransition(domain) { TargetStage = payment.RequireStage("Failed") });
        var publishPaymentFailed = new PublishEvent(domain) { Event = payment.RequireEvent("PaymentFailed") };
        domain.CreateMutation()
            .AddEffect(failPayment, publishPaymentFailed)
            .SetEventPropertyBinding(failPayment, publishPaymentFailed, "FailureReason", new EventPropertyBindingSource.ActionParameter(failureReasonParam.Name))
            .Apply();
        payment.AddAction(failPayment);

        var markPaid = new DomainAction(domain, "MarkPaid", order);
        markPaid.AddEffect(new StageTransition(domain) { TargetStage = order.RequireStage("Paid") });
        order.AddAction(markPaid);
        order.RequireStage("Pending").AddAction(markPaid);

        var processOrder = new DomainAction(domain, "ProcessOrder", order);
        processOrder.AddEffect(new StageTransition(domain) { TargetStage = order.RequireStage("Processing") });
        order.AddAction(processOrder);
        order.RequireStage("Paid").AddAction(processOrder);

        var createShipment = new DomainAction(domain, "CreateShipment", shipment);
        var trackingNumberParam = new Property(domain, "TrackingNumber", stringType);
        var carrierParam = new Property(domain, "Carrier", stringType);
        createShipment.AddParameter(trackingNumberParam);
        createShipment.AddParameter(carrierParam);
        createShipment.AddEffect(new StageTransition(domain) { TargetStage = shipment.RequireStage("Preparing") });
        var publishShipmentCreated = new PublishEvent(domain) { Event = shipment.RequireEvent("ShipmentCreated") };
        domain.CreateMutation()
            .AddEffect(createShipment, publishShipmentCreated)
            .SetEventPropertyBinding(createShipment, publishShipmentCreated, "TrackingNumber", new EventPropertyBindingSource.ActionParameter(trackingNumberParam.Name))
            .SetEventPropertyBinding(createShipment, publishShipmentCreated, "Carrier", new EventPropertyBindingSource.ActionParameter(carrierParam.Name))
            .Apply();
        shipment.AddAction(createShipment);

        var markShipped = new DomainAction(domain, "MarkShipped", order);
        markShipped.AddEffect(new StageTransition(domain) { TargetStage = order.RequireStage("Shipped") });
        order.AddAction(markShipped);
        order.RequireStage("Processing").AddAction(markShipped);

        var markDelivered = new DomainAction(domain, "MarkDelivered", shipment);
        markDelivered.AddEffect(new StageTransition(domain) { TargetStage = shipment.RequireStage("Delivered") });
        var publishShipmentDelivered = new PublishEvent(domain) { Event = shipment.RequireEvent("ShipmentDelivered") };
        domain.CreateMutation()
            .AddEffect(markDelivered, publishShipmentDelivered)
            .SetEventPropertyBinding(markDelivered, publishShipmentDelivered, "DeliveryDate", new EventPropertyBindingSource.EntityProperty("ActualDelivery"))
            .Apply();
        shipment.AddAction(markDelivered);
        shipment.RequireStage("OutForDelivery").AddAction(markDelivered);

        var addProduct = new DomainAction(domain, "AddProduct", product);
        addProduct.AddEffect(new StageTransition(domain) { TargetStage = product.RequireStage("Draft") });
        var publishProductCreated = new PublishEvent(domain) { Event = product.RequireEvent("ProductCreated") };
        domain.CreateMutation()
            .AddEffect(addProduct, publishProductCreated)
            .SetEventPropertyBinding(addProduct, publishProductCreated, "SKU", new EventPropertyBindingSource.EntityProperty("SKU"))
            .SetEventPropertyBinding(addProduct, publishProductCreated, "Name", new EventPropertyBindingSource.EntityProperty("Name"))
            .Apply();
        product.AddAction(addProduct);

        var activateProduct = new DomainAction(domain, "ActivateProduct", product);
        activateProduct.AddEffect(new StageTransition(domain) { TargetStage = product.RequireStage("Active") });
        product.AddAction(activateProduct);
        product.RequireStage("Draft").AddAction(activateProduct);

        var updateStock = new DomainAction(domain, "UpdateStock", product);
        var newQuantityParam = new Property(domain, "NewQuantity", domain.RequirePrimitive("int"));
        updateStock.AddParameter(newQuantityParam);
        var publishStockUpdated = new PublishEvent(domain) { Event = product.RequireEvent("StockUpdated") };
        domain.CreateMutation()
            .AddEffect(updateStock, publishStockUpdated)
            .SetEventPropertyBinding(updateStock, publishStockUpdated, "NewQuantity", new EventPropertyBindingSource.ActionParameter(newQuantityParam.Name))
            .Apply();
        product.AddAction(updateStock);

        var addOrderItem = new DomainAction(domain, "AddOrderItem", orderItem);
        addOrderItem.AddEffect(new CreateEntityInstance(domain) {
            EntityType = orderItem,
            InitialStage = null
        });
        orderItem.AddAction(addOrderItem);
    }

    private static void CreateRelationships(Domain domain) {
        var customer = domain.RequireEntity("Customer");
        var order = domain.RequireEntity("Order");
        var product = domain.RequireEntity("Product");
        var orderItem = domain.RequireEntity("OrderItem");
        var payment = domain.RequireEntity("Payment");
        var shipment = domain.RequireEntity("Shipment");
        var review = domain.RequireEntity("Review");
        var category = domain.RequireEntity("Category");
        var admin = domain.RequireEntity("Admin");

        var customerOrders = new Relationship(domain, "CustomerOrders", customer, order, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(customerOrders);
        customer.AddRelationship(customerOrders);

        var orderItems = new Relationship(domain, "OrderItems", order, orderItem, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(orderItems);
        order.AddRelationship(orderItems);

        var productOrders = new Relationship(domain, "ProductOrders", product, orderItem, RelationshipCardinality.OneToMany, false);
        domain.AddRelationship(productOrders);
        product.AddRelationship(productOrders);

        var orderPayments = new Relationship(domain, "OrderPayments", order, payment, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(orderPayments);
        order.AddRelationship(orderPayments);

        var orderShipments = new Relationship(domain, "OrderShipments", order, shipment, RelationshipCardinality.OneToMany, true);
        domain.AddRelationship(orderShipments);
        order.AddRelationship(orderShipments);

        var productReviews = new Relationship(domain, "ProductReviews", product, review, RelationshipCardinality.OneToMany, false);
        domain.AddRelationship(productReviews);
        product.AddRelationship(productReviews);

        var customerReviews = new Relationship(domain, "CustomerReviews", customer, review, RelationshipCardinality.OneToMany, false);
        domain.AddRelationship(customerReviews);
        customer.AddRelationship(customerReviews);

        var productCategories = new Relationship(domain, "ProductCategories", product, category, RelationshipCardinality.ManyToMany, false);
        domain.AddRelationship(productCategories);
        product.AddRelationship(productCategories);

        var adminOrders = new Relationship(domain, "AdminOrders", admin, order, RelationshipCardinality.OneToMany, false);
        domain.AddRelationship(adminOrders);
        admin.AddRelationship(adminOrders);
    }

    private static void CreatePolicies(Domain domain) {
        var customer = domain.RequireEntity("Customer");
        var order = domain.RequireEntity("Order");
        var product = domain.RequireEntity("Product");
        var payment = domain.RequireEntity("Payment");
        var shipment = domain.RequireEntity("Shipment");
        var user = domain.RequireEntity("User");

        var requireShippingAddress = new Policy(domain, "RequireShippingAddress") { AggregationStrategy = PolicyAggregationStrategy.All };
        requireShippingAddress.AddRule(new PropertyRule(domain, "ShippingAddressRequired", order.RequireProperty("ShippingAddress"), new RequiredConstraint()));
        order.AddPolicy(requireShippingAddress);

        var requireActiveCustomer = new Policy(domain, "RequireActiveCustomer") { AggregationStrategy = PolicyAggregationStrategy.All };
        requireActiveCustomer.AddRule(new PropertyRule(domain, "IsActiveCheck", GetPropertyInHierarchy(customer, "IsActive"), new RequiredConstraint()));
        customer.AddPolicy(requireActiveCustomer);

        var requireStockForOrder = new Policy(domain, "RequireStockAvailable") { AggregationStrategy = PolicyAggregationStrategy.All };
        requireStockForOrder.AddRule(new PropertyRule(domain, "StockQuantityCheck", product.RequireProperty("StockQuantity"), new RequiredConstraint()));
        product.AddPolicy(requireStockForOrder);

        var requirePaymentMethod = new Policy(domain, "RequirePaymentMethod") { AggregationStrategy = PolicyAggregationStrategy.All };
        requirePaymentMethod.AddRule(new PropertyRule(domain, "PaymentMethodRequired", payment.RequireProperty("PaymentMethod"), new RequiredConstraint()));
        payment.AddPolicy(requirePaymentMethod);

        var requireTrackingNumber = new Policy(domain, "RequireTrackingNumber");
        requireTrackingNumber.AddRule(new PropertyRule(domain, "TrackingNumberRequired", shipment.RequireProperty("TrackingNumber"), new RequiredConstraint()));
        shipment.RequireStage("LabelCreated").AddPolicy(requireTrackingNumber);
    }

    private static Property GetPropertyInHierarchy(Entity entity, string name) {
        for (var current = entity; current is not null; current = current.ParentEntity) {
            var property = current.FindProperty(name);
            if (property is not null)
                return property;
        }
        throw new InvalidOperationException($"Property '{name}' was not found in hierarchy of entity '{entity.Name}'.");
    }
}