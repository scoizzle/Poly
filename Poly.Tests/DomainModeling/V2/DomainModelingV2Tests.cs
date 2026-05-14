using Poly.DomainModeling.V2;

namespace Poly.Tests.DomainModeling.V2;

public class DomainModelingV2Tests {
    [Test]
    public async Task DomainFactory_CreatesImmutableDomainWithNestedDsl()
    {
        var domain = DomainFactory.Create("Commerce", model => model
            .Entity("Order", order => order
                .Property("Id", "Uuid", isRequired: true)
                .Stage("Draft", isInitial: true)
                .Action("Submit", action => action.Effect(new TransitionStage("Submitted"))))
            .Entity("Customer", customer => customer.Property("Id", "Uuid", isRequired: true))
            .Relationship("CustomerOrders", "Customer", "Order", RelationshipKind.OneToMany));

        await Assert.That(domain.Name).IsEqualTo("Commerce");
        await Assert.That(domain.Entities.Count).IsEqualTo(2);
        await Assert.That(domain.Types.Count).IsEqualTo(2);
        await Assert.That(domain.Relationships.Count).IsEqualTo(1);
        await Assert.That(domain.Entities.Single(entity => entity.Name == "Order").Actions.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SessionManager_CreateMutateAndGetTrace_WorkAsExpected()
    {
        var sessions = new DomainSessionManager();
        var session = sessions.CreateSession("Commerce");
        session = sessions.Mutate(session.SessionId, DomainMutation.AddEntity("Order"));
        session = sessions.Mutate(session.SessionId, DomainMutation.AddProperty("Order", "Id", "Uuid", isRequired: true));
        session = sessions.Mutate(session.SessionId, DomainMutation.AddAction("Order", "Submit"));
        session = sessions.Mutate(session.SessionId, DomainMutation.AddActionEffect("Order", "Submit", new TransitionStage("Submitted")));

        var trace = sessions.GetTrace(session.SessionId);
        await Assert.That(session.Revision).IsEqualTo(4);
        await Assert.That(trace.Count).IsEqualTo(4);
        await Assert.That(trace.Last().Mutation.Kind).IsEqualTo(DomainMutationKind.AddActionEffect);
    }

    [Test]
    public async Task DomainMcpTools_CreateEntityWithPatternAndAddCrud_ConfiguresEntity()
    {
        var sessions = new DomainSessionManager();
        var session = sessions.CreateSession("Commerce");
        session = DomainMcpTools.CreateEntityWithPattern(sessions, session.SessionId, "Order", "AggregateRoot");
        session = DomainMcpTools.AddCRUD(sessions, session.SessionId, "Order");

        var order = session.Domain.Entities.Single(entity => entity.Name == "Order");
        await Assert.That(order.Properties.Any(property => property.Name == "Id")).IsTrue();
        await Assert.That(order.Stages.Any(stage => stage.Name == "Draft" && stage.IsInitial)).IsTrue();
        await Assert.That(order.Actions.Select(action => action.Name)).Contains("Create");
        await Assert.That(order.Actions.Select(action => action.Name)).Contains("Delete");
    }

    [Test]
    public async Task AnalysisRenderingAndValidation_AreAvailable()
    {
        var domain = DomainFactory.Create("Commerce", model => model
            .Entity("Product", product => product
                .Property("Id", "Uuid", isRequired: true)
                .Property("Price", "Decimal", isRequired: true)
                .Stage("Sellable", isInitial: true)));

        var analysis = DomainAnalyzer.Analyze(domain);
        var rendered = DomainRenderer.Render(domain);

        await Assert.That(analysis.Validation.IsValid).IsTrue();
        await Assert.That(analysis.EntityCount).IsEqualTo(1);
        await Assert.That(rendered).Contains("Entity: Product");
        await Assert.That(rendered).Contains("Property: Price (Decimal) required");
    }

    [Test]
    public async Task Mutate_WhenEntityMissing_Throws()
    {
        var sessions = new DomainSessionManager();
        var session = sessions.CreateSession("Commerce");

        await Assert.ThrowsAsync<InvalidOperationException>(() => {
            sessions.Mutate(session.SessionId, DomainMutation.AddProperty("Order", "Id", "Uuid", isRequired: true));
            return Task.CompletedTask;
        });
    }
}
