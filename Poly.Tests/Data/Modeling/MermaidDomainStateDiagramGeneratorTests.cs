using Poly.Data.Modeling.Mermaid;

namespace Poly.Tests.Data.Modeling;

public class MermaidDomainStateDiagramGeneratorTests {
    [Test]
    public async Task Generate_UsesStateDiagramV2Header() {
        var domain = MermaidTestDomainFactory.BuildSupportCaseDomain();

        var result = new MermaidDomainStateDiagramGenerator().Generate(domain);

        await Assert.That(result).StartsWith("stateDiagram-v2");
    }

    [Test]
    public async Task Generate_IncludesSupportCaseStateMachineAndStages() {
        var domain = MermaidTestDomainFactory.BuildSupportCaseDomain();

        var result = new MermaidDomainStateDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("state \"SupportCase\" as SupportCase");
        await Assert.That(result).Contains("state \"New\" as SupportCase_New");
        await Assert.That(result).Contains("state \"InProgress\" as SupportCase_InProgress");
        await Assert.That(result).Contains("state \"Assigned\" as SupportCase_Assigned");
        await Assert.That(result).Contains("state \"Resolved\" as SupportCase_Resolved");
    }

    [Test]
    public async Task Generate_EmitsTransitionsFromActions() {
        var domain = MermaidTestDomainFactory.BuildSupportCaseDomain();

        var result = new MermaidDomainStateDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("SupportCase_New --> SupportCase_Assigned : Assign");
        await Assert.That(result).Contains("SupportCase_InProgress --> SupportCase_Resolved : Resolve");
    }

    [Test]
    public async Task Generate_EmitsSubstageRelationships() {
        var domain = MermaidTestDomainFactory.BuildSupportCaseDomain();

        var result = new MermaidDomainStateDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("SupportCase_InProgress --> SupportCase_Assigned : substage");
    }

    [Test]
    public async Task Generate_IncludesRelationshipStateMachines() {
        var domain = MermaidTestDomainFactory.BuildSupportCaseDomain();

        var result = new MermaidDomainStateDiagramGenerator().Generate(domain);

        await Assert.That(result).Contains("state \"AgentSupportCases\" as AgentSupportCases");
        await Assert.That(result).Contains("state \"Active\" as AgentSupportCases_Active");
        await Assert.That(result).Contains("state \"Inactive\" as AgentSupportCases_Inactive");
    }
}