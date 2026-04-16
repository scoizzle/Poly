using Poly.DomainModeling;

namespace Poly.Tests.DomainModeling;

public class DomainModelTests {
    [Test]
    public async Task Examples_CreateAll_ReturnsValidWorkflowFirstModels() {
        var models = DomainModelExamples.CreateAll();

        await Assert.That(models.Count).IsEqualTo(2);
        await Assert.That(models.SelectMany(model => model.Workflows).Count()).IsGreaterThan(0);
        await Assert.That(models.SelectMany(model => model.Records).Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task Model_GetWorkflowContext_ReturnsRelatedArtifacts() {
        var model = DomainModelExamples.CreateLoanOriginationModel();

        var context = model.GetWorkflowContext("loan-origination");

        await Assert.That(context.Workflow.Name).IsEqualTo("Loan Origination");
        await Assert.That(context.Steps.Count).IsEqualTo(5);
        await Assert.That(context.Forms.Select(form => form.Id)).Contains("application-form");
        await Assert.That(context.Records.Select(record => record.Id)).Contains("loan-application");
        await Assert.That(context.Rules.Select(rule => rule.Id)).Contains("minimum-credit");
    }

    [Test]
    public async Task Model_GetStepContext_ReturnsLocalNeighborhood() {
        var context = DomainModelExamples.CreateLoanOriginationUnderwritingContext();

        await Assert.That(context.Step.Id).IsEqualTo("underwrite-application");
        await Assert.That(context.Roles.Select(role => role.Id)).Contains("underwriter");
        await Assert.That(context.OutgoingTransitions.Select(transition => transition.Id)).Contains("underwrite-to-prepare");
        await Assert.That(context.OutgoingTransitions.Select(transition => transition.Id)).Contains("underwrite-to-declined");
        await Assert.That(context.Deadlines.Select(deadline => deadline.Id)).Contains("underwriting-sla");
    }

    [Test]
    public async Task Model_GetMutationPaths_ForDecisionStep_IncludesDecisionSpecificOperations() {
        var model = DomainModelExamples.CreateLoanOriginationModel();

        var paths = model.GetMutationPaths("loan-origination", "underwrite-application");

        await Assert.That(paths.Select(path => path.Operation)).Contains("AttachRule");
        await Assert.That(paths.Select(path => path.Operation)).Contains("AddConditionalPath");
        await Assert.That(paths.Select(path => path.Operation)).DoesNotContain("AttachForm");
    }

    [Test]
    public async Task Model_AddAndRemoveWorkflow_UsesNamedOperations() {
        var model = CreateSimpleModel();
        var workflow = new WorkflowDefinition(
            id: "triage-workflow",
            name: "Triage Workflow",
            startStepId: "collect",
            steps: [
                new WorkflowStep("collect", "Collect", WorkflowStepKind.DataCapture)
            ],
            outcomes: [
                new WorkflowOutcome("done-triage", "Done", WorkflowOutcomeCategory.Completed)
            ]);

        model.AddWorkflow(workflow);
        await Assert.That(model.Workflows.Select(item => item.Id)).Contains("triage-workflow");

        model.RemoveWorkflow("triage-workflow");
        await Assert.That(model.Workflows.Select(item => item.Id)).DoesNotContain("triage-workflow");
    }

    [Test]
    public async Task Model_AddStep_WithWorkflowObject_UpdatesWorkflow() {
        var model = CreateSimpleModel();
        var workflow = model.GetWorkflow("support-case-workflow");

        model.AddStep(
            workflow,
            new WorkflowStep(
                id: "quality-check",
                name: "Quality Check",
                kind: WorkflowStepKind.ManualTask,
                actorRoleId: "analyst",
                inputRecordIds: ["support-case"],
                outputRecordIds: ["support-case"]));

        await Assert.That(workflow.Steps.Select(step => step.Id)).Contains("quality-check");
    }

    [Test]
    public async Task Workflow_RemoveStep_WithExistingTransitions_Throws() {
        var workflow = CreateSimpleModel().GetWorkflow("support-case-workflow");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            _ = workflow.RemoveStep("capture");
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Constructor_MissingWorkflowStartStep_Throws() {
        await Assert.ThrowsAsync<ArgumentException>(async () => {
            _ = CreateSimpleModel(startStepId: "missing-step");
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Constructor_TransitionToUnknownNode_Throws() {
        await Assert.ThrowsAsync<ArgumentException>(async () => {
            _ = CreateSimpleModel(finalTargetId: "unknown-node");
            await Task.CompletedTask;
        });
    }

    private static DomainModel CreateSimpleModel(string startStepId = "capture", string finalTargetId = "done") {
        return new DomainModel(
            id: "support-studio",
            name: "Support Studio",
            roles: [
                new RoleDefinition("analyst", "Analyst")
            ],
            records: [
                new DataRecordDefinition(
                    id: "support-case",
                    name: "Support Case",
                    fields: [
                        new DataFieldDefinition("case-id", "Case Id", new FieldTypeReference(FieldValueKind.Primitive, "string"), isRequired: true, isIdentifier: true),
                        new DataFieldDefinition("status", "Status", new FieldTypeReference(FieldValueKind.Enumeration, "case-status"), isRequired: true)
                    ])
            ],
            enumerations: [
                new EnumerationDefinition(
                    id: "case-status",
                    name: "Case Status",
                    values: [
                        new EnumerationValueDefinition("open", "Open"),
                        new EnumerationValueDefinition("closed", "Closed")
                    ])
            ],
            forms: [
                new FormDefinition(
                    id: "case-form",
                    name: "Case Form",
                    recordId: "support-case",
                    sections: [
                        new FormSectionDefinition("main", "Main", [ "case-id", "status" ])
                    ])
            ],
            rules: [
                new RuleDefinition("case-valid", "Case Valid", RuleKind.Validation, "status != null")
            ],
            deadlines: [
                new DeadlineDefinition("case-sla", "Case SLA", TimeSpan.FromHours(1), escalationRoleId: "analyst")
            ],
            integrations: [
                new IntegrationDefinition("notify", "Notify Customer", "SendEmail")
            ],
            workflows: [
                new WorkflowDefinition(
                    id: "support-case-workflow",
                    name: "Support Case Workflow",
                    startStepId: startStepId,
                    steps: [
                        new WorkflowStep(
                            id: "capture",
                            name: "Capture Case",
                            kind: WorkflowStepKind.DataCapture,
                            actorRoleId: "analyst",
                            formId: "case-form",
                            outputRecordIds: [ "support-case" ],
                            ruleIds: [ "case-valid" ],
                            deadlineId: "case-sla"),
                        new WorkflowStep(
                            id: "notify-step",
                            name: "Notify Customer",
                            kind: WorkflowStepKind.AutomatedTask,
                            inputRecordIds: [ "support-case" ],
                            integrationIds: [ "notify" ])
                    ],
                    outcomes: [
                        new WorkflowOutcome("done", "Done", WorkflowOutcomeCategory.Completed)
                    ],
                    transitions: [
                        new WorkflowTransition("capture-to-notify", "Notify", "capture", "notify-step"),
                        new WorkflowTransition("notify-to-done", "Complete", "notify-step", finalTargetId)
                    ])
            ]);
    }
}