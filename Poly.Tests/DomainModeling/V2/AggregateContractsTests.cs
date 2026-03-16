using Poly.DomainModeling.V2.Core;

namespace Poly.Tests.DomainModeling.V2;

public class AggregateContractsTests {
    [Test]
    public async Task Aggregate_ValidInput_CreatesAggregate()
    {
        var root = new SemanticId("TYPE_ROOT");
        var member = new SemanticId("TYPE_MEMBER");
        var aggregate = new Aggregate(
            new SemanticId("AGG_1"),
            "InvoiceAggregate",
            new SemanticId("CTX_3"),
            root,
            new[] { root, member });

        await Assert.That(aggregate.DomainTypeIds.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Aggregate_NoTypes_Throws()
    {
        await Assert.That(() => new Aggregate(
            new SemanticId("AGG_2"),
            "InvoiceAggregate",
            new SemanticId("CTX_4"),
            new SemanticId("TYPE_ROOT"),
            Array.Empty<SemanticId>())).Throws<ArgumentException>();
    }

    [Test]
    public async Task Aggregate_RootNotInCollection_Throws()
    {
        await Assert.That(() => new Aggregate(
            new SemanticId("AGG_3"),
            "InvoiceAggregate",
            new SemanticId("CTX_5"),
            new SemanticId("TYPE_ROOT"),
            new[] { new SemanticId("TYPE_OTHER") })).Throws<ArgumentException>();
    }
}