using Poly.DomainModeling.Bootstrap;
using Poly.DomainModeling.Evolution;

namespace Poly.DomainModeling.Examples.Demos;

/// <summary>
/// E-Commerce Order Processing domain built with the V3 evolution API.
///
/// Mirrors the V2 ECommerceDomain demo but uses only V3 types and the
/// <see cref="DomainFactory"/> / <see cref="EvolutionBuilder"/> fluent API.
/// Contract/recipe integration is V2-specific and omitted here.
/// </summary>
public static class ECommerceDomain {
    public static Domain Build() =>
        DomainFactory.Create("E-commerce Order Processing", builder =>
            builder
                // Additional primitives
                .AddPrimitiveType("SKU", Poly.Introspection.TypeCategory.Text)
                .AddPrimitiveType("Email", Poly.Introspection.TypeCategory.Text)
                .AddPrimitiveType("Address", Poly.Introspection.TypeCategory.Text)
                .AddPrimitiveType("Decimal", Poly.Introspection.TypeCategory.HighPrecision)

                // User entity
                .AddEntity("User")
                .AddPropertyToEntity("User", new("Username", new("Text"), []))
                .AddPropertyToEntity("User", new("Email", new("Email"), []))
                .AddPropertyToEntity("User", new("IsActive", new("Boolean"), []))

                // Order entity — the core lifecycle entity
                .AddEntity("Order")
                .AddPropertyToEntity("Order", new("OrderDate", new("DateTime"), []))
                .AddPropertyToEntity("Order", new("TotalAmount", new("Decimal"), []))
                .AddPropertyToEntity("Order", new("ShippingAddress", new("Address"), []))
                .AddPropertyToEntity("Order", new("TrackingNumber", new("Text"), []))

                // Product entity
                .AddEntity("Product")
                .AddPropertyToEntity("Product", new("SKU", new("SKU"), []))
                .AddPropertyToEntity("Product", new("Name", new("Text"), []))
                .AddPropertyToEntity("Product", new("Price", new("Decimal"), []))
                .AddPropertyToEntity("Product", new("StockQuantity", new("Number"), []))

                // Inventory entity
                .AddEntity("InventoryItem")
                .AddPropertyToEntity("InventoryItem", new("Quantity", new("Number"), []))
                .AddPropertyToEntity("InventoryItem", new("Location", new("Text"), []))
                .AddPropertyToEntity("InventoryItem", new("ReorderThreshold", new("Number"), []))

                // Payment entity
                .AddEntity("Payment")
                .AddPropertyToEntity("Payment", new("Amount", new("Decimal"), []))
                .AddPropertyToEntity("Payment", new("PaymentMethod", new("Text"), []))
                .AddPropertyToEntity("Payment", new("TransactionId", new("Text"), []))
                .AddPropertyToEntity("Payment", new("PaidAt", new("DateTime"), []))

                // Order stages — the order lifecycle
                .AddStage("Order", "Pending")
                .AddStage("Order", "Confirmed")
                .AddStage("Order", "Processing")
                .AddStage("Order", "Shipped")
                .AddStage("Order", "Delivered")
                .AddStage("Order", "Cancelled")
                .AddStage("Order", "Returned")

                // Payment stages
                .AddStage("Payment", "Pending")
                .AddStage("Payment", "Completed")
                .AddStage("Payment", "Failed")
                .AddStage("Payment", "Refunded")

                // Inventory stages
                .AddStage("InventoryItem", "InStock")
                .AddStage("InventoryItem", "LowStock")
                .AddStage("InventoryItem", "OutOfStock")
                .AddStage("InventoryItem", "Discontinued")

                // Events
                .AddEventToEntity("Order", "OrderPlaced",
                    new Property("OrderDate", new DomainTypeReference("DateTime"), []),
                    new Property("Total", new DomainTypeReference("Decimal"), []))
                .AddEventToEntity("Order", "OrderShipped",
                    new Property("TrackingNumber", new DomainTypeReference("Text"), []))
                .AddEventToEntity("Order", "OrderDelivered")
                .AddEventToEntity("Payment", "PaymentReceived",
                    new Property("Amount", new DomainTypeReference("Decimal"), []),
                    new Property("Method", new DomainTypeReference("Text"), []))
                .AddEventToEntity("Product", "StockLow",
                    new Property("CurrentStock", new DomainTypeReference("Number"), []),
                    new Property("SKU", new DomainTypeReference("SKU"), []))

                // Relationships
                .AddRelationship("UserOrders", "User", "Order",
                    RelationshipCardinality.OneToMany, sourceOwnsTarget: true)
                .AddRelationship("OrderProducts", "Order", "Product",
                    RelationshipCardinality.ManyToMany, sourceOwnsTarget: false)
                .AddRelationship("OrderPayments", "Order", "Payment",
                    RelationshipCardinality.OneToMany, sourceOwnsTarget: true)
                .AddRelationship("ProductInventory", "Product", "InventoryItem",
                    RelationshipCardinality.OneToOne, sourceOwnsTarget: true)

                // Actions
                .AddAction("Order", "PlaceOrder")
                .AddActionToStage("Order", "Pending", "PlaceOrder")
                .AddAction("Order", "ConfirmOrder")
                .AddActionToStage("Order", "Confirmed", "ConfirmOrder")
                .AddAction("Order", "ShipOrder")
                .AddActionToStage("Order", "Processing", "ShipOrder")
                .AddAction("Order", "DeliverOrder")
                .AddActionToStage("Order", "Shipped", "DeliverOrder")
                .AddAction("Order", "CancelOrder")
                .AddActionToStage("Order", "Pending", "CancelOrder")
                .AddActionToStage("Order", "Confirmed", "CancelOrder")
                .AddAction("Order", "ReturnOrder")
                .AddActionToStage("Order", "Delivered", "ReturnOrder")

                // Policies
                .AddPolicyToEntity("Order", "ValidOrderTotal",
                    DomainExpression.GreaterThan(
                        DomainExpression.Property("TotalAmount"),
                        DomainExpression.Literal(0)))
                .AddPolicyToEntity("Product", "PositiveStock",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("StockQuantity"),
                        DomainExpression.Literal(0)))
        );
}