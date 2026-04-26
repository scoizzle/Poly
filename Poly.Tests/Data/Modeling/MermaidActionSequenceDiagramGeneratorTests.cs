using Poly.Data.Modeling;
using Poly.Data.Modeling.Mermaid;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling;

public class MermaidActionSequenceDiagramGeneratorTests {
    [Test]
    public async Task Generate_UsesSequenceDiagramHeader() {
        var action = GetSupportCaseAction("New", "Assign");

        var result = new MermaidActionSequenceDiagramGenerator().Generate(action);

        await Assert.That(result).StartsWith("sequenceDiagram");
        await Assert.That(result).Contains("actor Caller");
        await Assert.That(result).Contains("participant Aggregate as SupportCase");
    }

    [Test]
    public async Task Generate_AssignAction_EmitsTransitionAndPublishFlow() {
        var action = GetSupportCaseAction("New", "Assign");

        var result = new MermaidActionSequenceDiagramGenerator().Generate(action);

        await Assert.That(result).Contains("Caller->>Aggregate: Assign(Agent)");
        await Assert.That(result).Contains("Aggregate->>Aggregate: transition to Assigned");
        await Assert.That(result).Contains("Aggregate-->>EventBus: publish CaseAssigned");
        await Assert.That(result).Contains("Aggregate-->>Caller: completed");
    }

    [Test]
    public async Task Generate_AddNoteAction_EmitsCreateEntityFlow() {
        var action = GetSupportCaseAction("InProgress", "AddNote");

        var result = new MermaidActionSequenceDiagramGenerator().Generate(action);

        await Assert.That(result).Contains("Caller->>Aggregate: AddNote(NoteText)");
        await Assert.That(result).Contains("participant Factory_Note as Note Factory");
        await Assert.That(result).Contains("Aggregate->>Factory_Note: create Note");
        await Assert.That(result).Contains("Factory_Note->>Factory_Note: set stage Draft");
    }

    [Test]
    public async Task Generate_ResolveAction_EmitsTransitionAndResolutionEvent() {
        var action = GetSupportCaseAction("InProgress", "Resolve");

        var result = new MermaidActionSequenceDiagramGenerator().Generate(action);

        await Assert.That(result).Contains("Caller->>Aggregate: Resolve(ResolutionSummary)");
        await Assert.That(result).Contains("Aggregate->>Aggregate: transition to Resolved");
        await Assert.That(result).Contains("Aggregate-->>EventBus: publish CaseResolved");
    }

    private static DomainAction GetSupportCaseAction(string stageName, string actionName) {
        var domain = MermaidTestDomainFactory.BuildSupportCaseDomain();
        var supportCase = domain.Types.OfType<Entity>().Single(e => e.Name == "SupportCase");
        var stage = supportCase.Stages.Single(s => s.Name == stageName);
        return stage.Actions.Single(a => a.Name == actionName);
    }
}