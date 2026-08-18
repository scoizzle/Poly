using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.ContractFill;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;

namespace Poly.Tests.DomainModeling.Packs;

/// <summary>
/// pack-3b-1: <see cref="InternalDomainProducer"/> fills an <see cref="ImportedContract"/>
/// from a loaded <see cref="Domain"/> — published value types + actions as outbound
/// operations. No child entities, no merge.
/// </summary>
public sealed class InternalDomainProducerTests {
    private static Domain BillingDomain() =>
        DomainFactory.Create("billing", b => b
            .AddValueType("ChargeRequest",
                new Property("Amount", new DomainTypeReference("Number"), []),
                new Property("Currency", new DomainTypeReference("Text"), []))
            .AddEntity("Ledger")
            .AddActionWithParameters("Ledger", "Charge",
                new Property("request", new DomainTypeReference("ChargeRequest"), [])));

    [Test]
    public async Task Produce_BillingDomain_ProjectsValueTypesAndActions() {
        var contract = new InternalDomainProducer().Produce(BillingDomain());

        await Assert.That(contract.Name).IsEqualTo("Billing");
        await Assert.That(contract.SourceKind).IsEqualTo(ContractSourceKind.InternalDomain);
        await Assert.That(contract.SourceIdentifier).IsEqualTo("billing");
        await Assert.That(contract.Version).IsEqualTo("v1");
        await Assert.That(contract.Types.Select(t => t.Name)).Contains("ChargeRequest");
        await Assert.That(contract.Types.Select(t => t.Name)).DoesNotContain("Ledger");
        var charge = contract.Endpoints.Single(e => e.Name == "Charge");
        await Assert.That(charge.Kind).IsEqualTo(ContractEndpointKind.Operation);
        await Assert.That(charge.Direction).IsEqualTo(ContractEndpointDirection.Outbound);
        await Assert.That(charge.PayloadType.TypeName).IsEqualTo("ChargeRequest");
    }

    [Test]
    public async Task Produce_StageAction_ProjectsAsOutboundOperation() {
        var domain = DomainFactory.Create("ledger", b => b
            .AddValueType("LedgerLine",
                new Property("Amount", new DomainTypeReference("Number"), []))
            .AddEntity("Ledger")
            .AddStage("Ledger", "Active")
            .AddActionToStage("Ledger", "Active", "Post")
            .AddParameterToAction("Ledger", "Post",
                new Property("line", new DomainTypeReference("LedgerLine"), [])));

        var contract = new InternalDomainProducer().Produce(domain);

        var post = contract.Endpoints.Single(e => e.Name == "Post");
        await Assert.That(post.Kind).IsEqualTo(ContractEndpointKind.Operation);
        await Assert.That(post.Direction).IsEqualTo(ContractEndpointDirection.Outbound);
        await Assert.That(post.PayloadType.TypeName).IsEqualTo("LedgerLine");
    }

    [Test]
    public async Task Produce_ValueTypeProperties_ArePreserved() {
        var contract = new InternalDomainProducer().Produce(BillingDomain());

        var request = contract.Types.Single(t => t.Name == "ChargeRequest");
        await Assert.That(request.Properties.Count).IsEqualTo(2);
        await Assert.That(request.Properties.Select(p => p.Name)).Contains("Amount");
        await Assert.That(request.Properties.Select(p => p.Name)).Contains("Currency");
    }

    [Test]
    public async Task Produce_EmptyDomain_ProducesEmptyContract() {
        var contract = new InternalDomainProducer().Produce(DomainFactory.Create("empty"));

        await Assert.That(contract.Name).IsEqualTo("Empty");
        await Assert.That(contract.Types).IsEmpty();
        await Assert.That(contract.Endpoints).IsEmpty();
    }

    [Test]
    public async Task Produce_ActionWithMultipleParameters_Throws() {
        var domain = DomainFactory.Create("billing", b => b
            .AddEntity("Ledger")
            .AddActionWithParameters("Ledger", "Bad",
                new Property("a", new DomainTypeReference("Number"), []),
                new Property("b", new DomainTypeReference("Text"), [])));

        await Assert.That(() => new InternalDomainProducer().Produce(domain))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Bad");
    }

    [Test]
    public async Task Produce_ActionWithNoParameters_Throws() {
        var domain = DomainFactory.Create("billing", b => b
            .AddEntity("Ledger")
            .AddAction("Ledger", "Nop"));

        await Assert.That(() => new InternalDomainProducer().Produce(domain))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Nop");
    }

    [Test]
    public async Task Produce_DuplicateActionNames_Throws() {
        var domain = DomainFactory.Create("billing", b => b
            .AddEntity("Ledger")
            .AddEntity("Audit")
            .AddActionWithParameters("Ledger", "Charge",
                new Property("request", new DomainTypeReference("Number"), []))
            .AddActionWithParameters("Audit", "Charge",
                new Property("request", new DomainTypeReference("Number"), [])));

        await Assert.That(() => new InternalDomainProducer().Produce(domain))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Charge");
    }
}