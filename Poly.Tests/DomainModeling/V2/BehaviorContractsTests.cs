using Poly.DomainModeling.V2.Core;

namespace Poly.Tests.DomainModeling.V2;

public class BehaviorContractsTests {
    [Test]
    public async Task ParameterDefinition_ValidTypeExpression_CreatesParameter()
    {
        var parameter = new ParameterDefinition(new SemanticId("PARM_1"), "invoiceId", "Billing.Invoice");

        await Assert.That(parameter.Name).IsEqualTo("invoiceId");
    }

    [Test]
    public async Task ParameterDefinition_InvalidTypeExpression_Throws()
    {
        await Assert.That(() => new ParameterDefinition(new SemanticId("PARM_2"), "bad", "Invoice")).Throws<ArgumentException>();
    }

    [Test]
    public async Task Command_WhitespaceName_Throws()
    {
        await Assert.That(() => new Command(
            new SemanticId("CMD_1"),
            " ",
            new SemanticId("TYPE_1"),
            Array.Empty<ParameterDefinition>())).Throws<ArgumentException>();
    }

    [Test]
    public async Task Mutation_NullSourceCommandId_Throws()
    {
        await Assert.That(() => new Mutation(
            new SemanticId("MUT_1"),
            "Apply",
            null!,
            new SemanticId("TYPE_2"),
            Array.Empty<PropertyEffect>())).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task DomainEvent_NullSourceMutationId_Throws()
    {
        await Assert.That(() => new DomainEvent(
            new SemanticId("EVT_1"),
            "Applied",
            null!,
            Array.Empty<ParameterDefinition>())).Throws<ArgumentNullException>();
    }
}