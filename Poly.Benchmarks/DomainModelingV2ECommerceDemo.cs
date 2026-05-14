namespace Poly.Benchmarks;

using Poly.DomainModeling.V2;

public static class DomainModelingV2ECommerceDemo {
    public static Domain BuildWithDsl()
    {
        return DomainFactory.Create("ECommerce", domain => domain
            .Entity("Customer", customer => customer
                .Property("Id", "Uuid", isRequired: true)
                .Property("Email", "Text", isRequired: true)
                .Property("FullName", "Text", isRequired: true)
                .Stage("Active", isInitial: true)
                .Action("Deactivate", action => action.Effect(new TransitionStage("Archived"))))
            .Entity("Product", product => product
                .Property("Id", "Uuid", isRequired: true)
                .Property("Sku", "Text", isRequired: true)
                .Property("Price", "Decimal", isRequired: true)
                .Property("Stock", "Integer", isRequired: true, defaultValue: "0")
                .Stage("Sellable", isInitial: true)
                .Action("AdjustStock", action => action
                    .Parameter("delta", "Integer")
                    .Effect(new SetProperty("Stock", "Stock + delta"))))
            .Entity("Order", order => order
                .Property("Id", "Uuid", isRequired: true)
                .Property("Status", "Text", isRequired: true, defaultValue: "Draft")
                .Property("TotalAmount", "Decimal", isRequired: true, defaultValue: "0")
                .Stage("Draft", isInitial: true)
                .Stage("Paid")
                .Stage("Shipped")
                .Action("MarkPaid", action => action.Effect(new TransitionStage("Paid")))
                .Action("Ship", action => action.Effect(new TransitionStage("Shipped"))))
            .Relationship("CustomerOrders", "Customer", "Order", RelationshipKind.OneToMany)
            .Relationship("OrderProducts", "Order", "Product", RelationshipKind.ManyToMany));
    }

    public static Domain BuildWithSessionMcpTools()
    {
        var sessions = new DomainSessionManager();
        var session = sessions.CreateSession("ECommerce");
        session = DomainMcpTools.CreateEntityWithPattern(sessions, session.SessionId, "Customer", "AggregateRoot");
        session = DomainMcpTools.CreateEntityWithPattern(sessions, session.SessionId, "Order", "AggregateRoot");
        session = DomainMcpTools.CreateEntityWithPattern(sessions, session.SessionId, "Product", "AggregateRoot");

        session = DomainMcpTools.AddCRUD(sessions, session.SessionId, "Customer");
        session = DomainMcpTools.AddCRUD(sessions, session.SessionId, "Order");
        session = DomainMcpTools.AddCRUD(sessions, session.SessionId, "Product");

        session = sessions.Mutate(session.SessionId, DomainMutation.AddRelationship("CustomerOrders", "Customer", "Order", RelationshipKind.OneToMany));
        session = sessions.Mutate(session.SessionId, DomainMutation.AddRelationship("OrderProducts", "Order", "Product", RelationshipKind.ManyToMany));
        session = sessions.Mutate(session.SessionId, DomainMutation.AddProperty("Order", "TotalAmount", "Decimal", isRequired: true, defaultValue: "0"));
        session = sessions.Mutate(session.SessionId, DomainMutation.AddProperty("Order", "Status", "Text", isRequired: true, defaultValue: "Draft"));

        return session.Domain;
    }
}
