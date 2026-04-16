namespace Poly.DomainModeling;

/// <summary>
/// Describes a business domain as a workflow-first authoring surface for analysts, UIs, and LLMs.
/// </summary>
public sealed record class DomainModel : DomainNode {
    private readonly Dictionary<DomainId, WorkflowDefinition> _workflows;
    private readonly Dictionary<DomainId, DataRecordDefinition> _records;
    private readonly Dictionary<DomainId, EnumerationDefinition> _enumerations;
    private readonly Dictionary<DomainId, RoleDefinition> _roles;
    private readonly Dictionary<DomainId, FormDefinition> _forms;
    private readonly Dictionary<DomainId, RuleDefinition> _rules;
    private readonly Dictionary<DomainId, DeadlineDefinition> _deadlines;
    private readonly Dictionary<DomainId, IntegrationDefinition> _integrations;
    private readonly Dictionary<DomainId, GlossaryTerm> _glossary;

    public DomainModel(
        string id,
        string name,
        string? description = null,
        IEnumerable<WorkflowDefinition>? workflows = null,
        IEnumerable<DataRecordDefinition>? records = null,
        IEnumerable<EnumerationDefinition>? enumerations = null,
        IEnumerable<RoleDefinition>? roles = null,
        IEnumerable<FormDefinition>? forms = null,
        IEnumerable<RuleDefinition>? rules = null,
        IEnumerable<DeadlineDefinition>? deadlines = null,
        IEnumerable<IntegrationDefinition>? integrations = null,
        IEnumerable<GlossaryTerm>? glossary = null)
        : base(id, name, description) {
        _workflows = DomainModelValidation.CreateNodeDictionary(workflows, nameof(workflows));
        _records = DomainModelValidation.CreateNodeDictionary(records, nameof(records));
        _enumerations = DomainModelValidation.CreateNodeDictionary(enumerations, nameof(enumerations));
        _roles = DomainModelValidation.CreateNodeDictionary(roles, nameof(roles));
        _forms = DomainModelValidation.CreateNodeDictionary(forms, nameof(forms));
        _rules = DomainModelValidation.CreateNodeDictionary(rules, nameof(rules));
        _deadlines = DomainModelValidation.CreateNodeDictionary(deadlines, nameof(deadlines));
        _integrations = DomainModelValidation.CreateNodeDictionary(integrations, nameof(integrations));
        _glossary = DomainModelValidation.CreateNodeDictionary(glossary, nameof(glossary));

        DomainModelValidation.ValidateModel(this);
    }

    public IReadOnlyCollection<WorkflowDefinition> Workflows => _workflows.Values;

    public IReadOnlyCollection<DataRecordDefinition> Records => _records.Values;

    public IReadOnlyCollection<EnumerationDefinition> Enumerations => _enumerations.Values;

    public IReadOnlyCollection<RoleDefinition> Roles => _roles.Values;

    public IReadOnlyCollection<FormDefinition> Forms => _forms.Values;

    public IReadOnlyCollection<RuleDefinition> Rules => _rules.Values;

    public IReadOnlyCollection<DeadlineDefinition> Deadlines => _deadlines.Values;

    public IReadOnlyCollection<IntegrationDefinition> Integrations => _integrations.Values;

    public IReadOnlyCollection<GlossaryTerm> Glossary => _glossary.Values;

    public DomainModel Rename(string newName) {
        SetName(newName);
        return this;
    }

    public DomainModel AddWorkflow(WorkflowDefinition workflow) {
        ArgumentNullException.ThrowIfNull(workflow);

        if (_workflows.ContainsKey(workflow.Id)) {
            throw new InvalidOperationException($"Workflow '{workflow.Id}' already exists.");
        }

        _workflows.Add(workflow.Id, workflow);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel RemoveWorkflow(string workflowId) {
        DomainId id = workflowId;

        if (!_workflows.ContainsKey(id)) {
            throw new InvalidOperationException($"Workflow '{workflowId}' was not found.");
        }

        _workflows.Remove(id);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel AddRecordType(DataRecordDefinition record) {
        ArgumentNullException.ThrowIfNull(record);

        if (_records.ContainsKey(record.Id)) {
            throw new InvalidOperationException($"Record '{record.Id}' already exists.");
        }

        _records.Add(record.Id, record);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel RemoveRecordType(string recordId) {
        DomainId id = recordId;

        if (!_records.ContainsKey(id)) {
            throw new InvalidOperationException($"Record '{recordId}' was not found.");
        }

        _records.Remove(id);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel AddRole(RoleDefinition role) {
        ArgumentNullException.ThrowIfNull(role);

        if (_roles.ContainsKey(role.Id)) {
            throw new InvalidOperationException($"Role '{role.Id}' already exists.");
        }

        _roles.Add(role.Id, role);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel RemoveRole(string roleId) {
        DomainId id = roleId;

        if (!_roles.ContainsKey(id)) {
            throw new InvalidOperationException($"Role '{roleId}' was not found.");
        }

        _roles.Remove(id);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel AddStep(WorkflowDefinition workflow, WorkflowStep step) {
        var ownedWorkflow = RequireOwnedWorkflow(workflow);
        ownedWorkflow.AddStep(step);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel RemoveStep(WorkflowDefinition workflow, string stepId) {
        var ownedWorkflow = RequireOwnedWorkflow(workflow);
        ownedWorkflow.RemoveStep(stepId);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel AddTransition(WorkflowDefinition workflow, WorkflowTransition transition) {
        var ownedWorkflow = RequireOwnedWorkflow(workflow);
        ownedWorkflow.AddTransition(transition);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel RemoveTransition(WorkflowDefinition workflow, string transitionId) {
        var ownedWorkflow = RequireOwnedWorkflow(workflow);
        ownedWorkflow.RemoveTransition(transitionId);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel AssignStepRole(WorkflowDefinition workflow, string stepId, string roleId) {
        var ownedWorkflow = RequireOwnedWorkflow(workflow);
        ownedWorkflow.AssignStepRole(stepId, roleId);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel AttachStepForm(WorkflowDefinition workflow, string stepId, string formId) {
        var ownedWorkflow = RequireOwnedWorkflow(workflow);
        ownedWorkflow.AttachStepForm(stepId, formId);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel AttachStepRule(WorkflowDefinition workflow, string stepId, string ruleId) {
        var ownedWorkflow = RequireOwnedWorkflow(workflow);
        ownedWorkflow.AttachStepRule(stepId, ruleId);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel SetStepDeadline(WorkflowDefinition workflow, string stepId, string deadlineId) {
        var ownedWorkflow = RequireOwnedWorkflow(workflow);
        ownedWorkflow.SetStepDeadline(stepId, deadlineId);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public DomainModel AttachStepIntegration(WorkflowDefinition workflow, string stepId, string integrationId) {
        var ownedWorkflow = RequireOwnedWorkflow(workflow);
        ownedWorkflow.AttachStepIntegration(stepId, integrationId);
        DomainModelValidation.ValidateModel(this);
        return this;
    }

    public WorkflowDefinition GetWorkflow(string workflowId) {
        DomainId id = workflowId;
        return _workflows.TryGetValue(id, out var workflow)
            ? workflow
            : throw new InvalidOperationException($"Workflow '{workflowId}' was not found.");
    }

    public WorkflowContextSlice GetWorkflowContext(string workflowId) {
        var workflow = GetWorkflow(workflowId);

        var roleIds = workflow.Steps
            .SelectMany(step => new[] { step.ActorRoleId, step.EscalationRoleId })
            .Where(static id => id != null)
            .Select(static id => id!.Value)
            .ToHashSet();

        var formIds = workflow.Steps
            .Where(static step => step.FormId != null)
            .Select(static step => step.FormId!.Value)
            .ToHashSet();

        var recordIds = workflow.Steps
            .SelectMany(step => step.InputRecordIds.Concat(step.OutputRecordIds))
            .Concat(Forms.Where(form => formIds.Contains(form.Id)).Select(form => form.RecordId))
            .ToHashSet();

        var ruleIds = workflow.Transitions
            .Where(static t => t.RuleId != null)
            .Select(static t => t.RuleId!.Value)
            .Concat(workflow.Steps.SelectMany(step => step.RuleIds))
            .ToHashSet();

        var deadlineIds = workflow.Steps
            .Where(static step => step.DeadlineId != null)
            .Select(static step => step.DeadlineId!.Value)
            .ToHashSet();

        var integrationIds = workflow.Steps
            .SelectMany(step => step.IntegrationIds)
            .ToHashSet();

        return new WorkflowContextSlice(
            workflow,
            workflow.Steps,
            workflow.Outcomes,
            workflow.Transitions,
            Roles.Where(role => roleIds.Contains(role.Id)).ToArray(),
            Forms.Where(form => formIds.Contains(form.Id)).ToArray(),
            Records.Where(record => recordIds.Contains(record.Id)).ToArray(),
            Rules.Where(rule => ruleIds.Contains(rule.Id)).ToArray(),
            Deadlines.Where(deadline => deadlineIds.Contains(deadline.Id)).ToArray(),
            Integrations.Where(integration => integrationIds.Contains(integration.Id)).ToArray());
    }

    public StepContextSlice GetStepContext(string workflowId, string stepId) {
        var workflow = GetWorkflow(workflowId);
        DomainId normalizedStepId = stepId;
        var step = workflow.Steps.SingleOrDefault(candidate => candidate.Id == normalizedStepId)
            ?? throw new InvalidOperationException($"Step '{stepId}' was not found in workflow '{workflowId}'.");

        var incomingTransitions = workflow.Transitions.Where(transition => transition.TargetNodeId == step.Id).ToArray();
        var outgoingTransitions = workflow.Transitions.Where(transition => transition.SourceStepId == step.Id).ToArray();
        var forms = Forms.Where(form => step.FormId != null && form.Id == step.FormId.Value).ToArray();
        var roles = Roles.Where(role =>
            (step.ActorRoleId != null && role.Id == step.ActorRoleId.Value) ||
            (step.EscalationRoleId != null && role.Id == step.EscalationRoleId.Value)).ToArray();
        var records = Records.Where(record =>
            step.InputRecordIds.Contains(record.Id) ||
            step.OutputRecordIds.Contains(record.Id) ||
            forms.Any(form => form.RecordId == record.Id)).ToArray();
        var rules = Rules.Where(rule =>
            step.RuleIds.Contains(rule.Id) ||
            incomingTransitions.Any(transition => transition.RuleId != null && transition.RuleId.Value == rule.Id) ||
            outgoingTransitions.Any(transition => transition.RuleId != null && transition.RuleId.Value == rule.Id)).ToArray();
        var deadlines = Deadlines.Where(deadline => step.DeadlineId != null && deadline.Id == step.DeadlineId.Value).ToArray();
        var integrations = Integrations.Where(integration => step.IntegrationIds.Contains(integration.Id)).ToArray();

        return new StepContextSlice(
            workflow,
            step,
            incomingTransitions,
            outgoingTransitions,
            roles,
            forms,
            records,
            rules,
            deadlines,
            integrations);
    }

    public IReadOnlyList<MutationPath> GetMutationPaths(string workflowId, string? stepId = null) {
        var workflow = GetWorkflow(workflowId);
        var paths = new List<MutationPath> {
            new(
                Operation: "RenameWorkflow",
                TargetId: workflow.Id,
                TargetKind: nameof(WorkflowDefinition),
                Description: "Change the workflow display name without altering its identity.",
                RequiredArguments: [ "workflowId", "newName" ]),
            new(
                Operation: "AddStep",
                TargetId: workflow.Id,
                TargetKind: nameof(WorkflowDefinition),
                Description: "Insert a new workflow step into the process graph.",
                RequiredArguments: [ "workflowId", "step" ]),
            new(
                Operation: "AddTransition",
                TargetId: workflow.Id,
                TargetKind: nameof(WorkflowDefinition),
                Description: "Connect one step to another step or outcome.",
                RequiredArguments: [ "workflowId", "transition" ]),
            new(
                Operation: "AddOutcome",
                TargetId: workflow.Id,
                TargetKind: nameof(WorkflowDefinition),
                Description: "Add a new terminal outcome to the workflow.",
                RequiredArguments: [ "workflowId", "outcome" ])
        };

        if (stepId == null) {
            return paths;
        }

        DomainId normalizedStepId2 = stepId;
        var step = workflow.Steps.SingleOrDefault(candidate => candidate.Id == normalizedStepId2)
            ?? throw new InvalidOperationException($"Step '{stepId}' was not found in workflow '{workflowId}'.");

        paths.Add(new MutationPath(
            Operation: "RenameStep",
            TargetId: step.Id,
            TargetKind: nameof(WorkflowStep),
            Description: "Change the step label while keeping references stable.",
            RequiredArguments: ["workflowId", "stepId", "newName"]));

        paths.Add(new MutationPath(
            Operation: "AttachRule",
            TargetId: step.Id,
            TargetKind: nameof(WorkflowStep),
            Description: "Bind an existing rule to this step.",
            RequiredArguments: ["workflowId", "stepId", "ruleId"]));

        if (step.Kind is WorkflowStepKind.ManualTask or WorkflowStepKind.Approval or WorkflowStepKind.DataCapture) {
            paths.Add(new MutationPath(
                Operation: "AssignRole",
                TargetId: step.Id,
                TargetKind: nameof(WorkflowStep),
                Description: "Set or change the role responsible for this step.",
                RequiredArguments: ["workflowId", "stepId", "roleId"]));

            paths.Add(new MutationPath(
                Operation: "AttachForm",
                TargetId: step.Id,
                TargetKind: nameof(WorkflowStep),
                Description: "Attach a form that captures or edits workflow data.",
                RequiredArguments: ["workflowId", "stepId", "formId"]));

            paths.Add(new MutationPath(
                Operation: "SetDeadline",
                TargetId: step.Id,
                TargetKind: nameof(WorkflowStep),
                Description: "Apply or update the SLA for this human step.",
                RequiredArguments: ["workflowId", "stepId", "deadlineId"]));
        }

        if (step.Kind == WorkflowStepKind.AutomatedTask) {
            paths.Add(new MutationPath(
                Operation: "AttachIntegration",
                TargetId: step.Id,
                TargetKind: nameof(WorkflowStep),
                Description: "Bind an automation or external system call to this step.",
                RequiredArguments: ["workflowId", "stepId", "integrationId"]));
        }

        if (step.Kind == WorkflowStepKind.Decision) {
            paths.Add(new MutationPath(
                Operation: "AddConditionalPath",
                TargetId: step.Id,
                TargetKind: nameof(WorkflowStep),
                Description: "Add a rule-driven branch leaving this decision step.",
                RequiredArguments: ["workflowId", "stepId", "transition"]));
        }

        return paths;
    }

    private WorkflowDefinition RequireOwnedWorkflow(WorkflowDefinition workflow) {
        ArgumentNullException.ThrowIfNull(workflow);

        if (!_workflows.TryGetValue((DomainId)workflow.Id, out var ownedWorkflow)) {
            throw new InvalidOperationException($"Workflow '{workflow.Id}' was not found in this domain model.");
        }

        if (!ReferenceEquals(ownedWorkflow, workflow)) {
            throw new InvalidOperationException("Use the workflow instance fetched from this domain model.");
        }

        return ownedWorkflow;
    }

}

/// <summary>
/// A normalised, case-insensitive identifier for any domain node or cross-reference.
/// Equality and hashing are OrdinalIgnoreCase so callers never need to normalise manually.
/// </summary>
public readonly record struct DomainId : IComparable<DomainId> {
    private readonly string _value;

    public DomainId(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value.Trim();
    }

    public bool Equals(DomainId other) =>
        string.Equals(_value, other._value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(_value ?? string.Empty);

    public int CompareTo(DomainId other) =>
        StringComparer.OrdinalIgnoreCase.Compare(_value, other._value);

    public override string ToString() => _value ?? string.Empty;

    public static implicit operator DomainId(string value) => new(value);
    public static implicit operator string(DomainId id) => id.ToString();
}

/// <summary>
/// Base type for all addressable nodes in the model.
/// </summary>
public abstract record class DomainNode {
    private string _name;
    private string? _description;

    protected DomainNode(string id, string name, string? description = null) {
        Id = id;
        _name = Guard.ThrowIfNullOrWhiteSpace(name).Trim();
        _description = DomainModelValidation.NormalizeText(description);
    }

    public DomainId Id { get; }

    public string Name => _name;

    public string? Description => _description;

    protected void SetName(string name) {
        _name = Guard.ThrowIfNullOrWhiteSpace(name).Trim();
    }

    protected void SetDescription(string? description) {
        _description = DomainModelValidation.NormalizeText(description);
    }
}

/// <summary>
/// Describes a business workflow with typed steps, outcomes, and transitions.
/// </summary>
public sealed record class WorkflowDefinition : DomainNode {
    private readonly Dictionary<DomainId, WorkflowStep> _steps;
    private readonly Dictionary<DomainId, WorkflowOutcome> _outcomes;
    private readonly Dictionary<DomainId, WorkflowTransition> _transitions;
    private DomainId _startStepId;

    public WorkflowDefinition(
        string id,
        string name,
        string startStepId,
        string? description = null,
        string? trigger = null,
        IEnumerable<WorkflowStep>? steps = null,
        IEnumerable<WorkflowOutcome>? outcomes = null,
        IEnumerable<WorkflowTransition>? transitions = null)
        : base(id, name, description) {
        _startStepId = startStepId;
        Trigger = DomainModelValidation.NormalizeText(trigger);
        _steps = DomainModelValidation.CreateNodeDictionary(steps, nameof(steps));
        _outcomes = DomainModelValidation.CreateNodeDictionary(outcomes, nameof(outcomes));
        _transitions = DomainModelValidation.CreateNodeDictionary(transitions, nameof(transitions));
    }

    public DomainId StartStepId => _startStepId;

    public string? Trigger { get; }

    public IReadOnlyCollection<WorkflowStep> Steps => _steps.Values;

    public IReadOnlyCollection<WorkflowOutcome> Outcomes => _outcomes.Values;

    public IReadOnlyCollection<WorkflowTransition> Transitions => _transitions.Values;

    public WorkflowDefinition Rename(string newName) {
        SetName(newName);
        return this;
    }

    public WorkflowDefinition SetStartStep(string stepId) {
        DomainId id = stepId;

        if (!_steps.ContainsKey(id)) {
            throw new InvalidOperationException($"Step '{stepId}' was not found in workflow '{Id}'.");
        }

        _startStepId = id;
        return this;
    }

    public WorkflowDefinition AddStep(WorkflowStep step) {
        ArgumentNullException.ThrowIfNull(step);

        if (_steps.ContainsKey(step.Id)) {
            throw new InvalidOperationException($"Step '{step.Id}' already exists in workflow '{Id}'.");
        }

        _steps.Add(step.Id, step);
        return this;
    }

    public WorkflowDefinition RemoveStep(string stepId) {
        DomainId id = stepId;

        if (StartStepId == id) {
            throw new InvalidOperationException("Cannot remove the start step. Set a new start step first.");
        }

        if (!_steps.ContainsKey(id)) {
            throw new InvalidOperationException($"Step '{stepId}' was not found in workflow '{Id}'.");
        }

        if (Transitions.Any(transition => transition.SourceStepId == id || transition.TargetNodeId == id)) {
            throw new InvalidOperationException($"Step '{stepId}' is used by transitions. Remove those transitions first.");
        }

        _steps.Remove(id);
        return this;
    }

    public WorkflowDefinition AddOutcome(WorkflowOutcome outcome) {
        ArgumentNullException.ThrowIfNull(outcome);

        if (_outcomes.ContainsKey(outcome.Id) || _steps.ContainsKey(outcome.Id)) {
            throw new InvalidOperationException($"Outcome '{outcome.Id}' conflicts with an existing node id in workflow '{Id}'.");
        }

        _outcomes.Add(outcome.Id, outcome);
        return this;
    }

    public WorkflowDefinition RemoveOutcome(string outcomeId) {
        DomainId id = outcomeId;

        if (!_outcomes.ContainsKey(id)) {
            throw new InvalidOperationException($"Outcome '{outcomeId}' was not found in workflow '{Id}'.");
        }

        if (Transitions.Any(transition => transition.TargetNodeId == id)) {
            throw new InvalidOperationException($"Outcome '{outcomeId}' is used by transitions. Remove those transitions first.");
        }

        _outcomes.Remove(id);
        return this;
    }

    public WorkflowDefinition AddTransition(WorkflowTransition transition) {
        ArgumentNullException.ThrowIfNull(transition);

        if (_transitions.ContainsKey(transition.Id)) {
            throw new InvalidOperationException($"Transition '{transition.Id}' already exists in workflow '{Id}'.");
        }

        if (!_steps.ContainsKey(transition.SourceStepId)) {
            throw new InvalidOperationException($"Source step '{transition.SourceStepId}' does not exist in workflow '{Id}'.");
        }

        if (!_steps.ContainsKey(transition.TargetNodeId) && !_outcomes.ContainsKey(transition.TargetNodeId)) {
            throw new InvalidOperationException($"Target node '{transition.TargetNodeId}' does not exist in workflow '{Id}'.");
        }

        _transitions.Add(transition.Id, transition);
        return this;
    }

    public WorkflowDefinition RemoveTransition(string transitionId) {
        DomainId id = transitionId;

        if (!_transitions.ContainsKey(id)) {
            throw new InvalidOperationException($"Transition '{transitionId}' was not found in workflow '{Id}'.");
        }

        _transitions.Remove(id);
        return this;
    }

    public WorkflowDefinition RenameStep(string stepId, string newName) {
        return UpdateStep(stepId, step => step.Rename(newName));
    }

    public WorkflowDefinition AssignStepRole(string stepId, string roleId) {
        return UpdateStep(stepId, step => step.AssignActorRole(roleId));
    }

    public WorkflowDefinition AttachStepForm(string stepId, string formId) {
        return UpdateStep(stepId, step => step.AttachForm(formId));
    }

    public WorkflowDefinition AttachStepRule(string stepId, string ruleId) {
        return UpdateStep(stepId, step => step.AddRule(ruleId));
    }

    public WorkflowDefinition SetStepDeadline(string stepId, string deadlineId) {
        return UpdateStep(stepId, step => step.SetDeadline(deadlineId));
    }

    public WorkflowDefinition AttachStepIntegration(string stepId, string integrationId) {
        return UpdateStep(stepId, step => step.AddIntegration(integrationId));
    }

    private WorkflowDefinition UpdateStep(string stepId, Func<WorkflowStep, WorkflowStep> updateStep) {
        DomainId id = stepId;

        if (!_steps.TryGetValue(id, out var existingStep)) {
            throw new InvalidOperationException($"Step '{stepId}' was not found in workflow '{Id}'.");
        }

        var updatedStep = updateStep(existingStep);

        if (!ReferenceEquals(updatedStep, existingStep)) {
            _steps[id] = updatedStep;
        }

        return this;
    }
}

/// <summary>
/// Describes a single step in a workflow.
/// </summary>
public sealed record class WorkflowStep : DomainNode {
    private DomainId? _actorRoleId;
    private readonly DomainId? _escalationRoleId;
    private DomainId? _formId;
    private DomainId? _deadlineId;
    private readonly List<DomainId> _inputRecordIds;
    private readonly List<DomainId> _outputRecordIds;
    private readonly List<DomainId> _ruleIds;
    private readonly List<DomainId> _integrationIds;

    public WorkflowStep(
        string id,
        string name,
        WorkflowStepKind kind,
        string? description = null,
        string? actorRoleId = null,
        string? escalationRoleId = null,
        string? formId = null,
        string? deadlineId = null,
        IEnumerable<string>? inputRecordIds = null,
        IEnumerable<string>? outputRecordIds = null,
        IEnumerable<string>? ruleIds = null,
        IEnumerable<string>? integrationIds = null)
        : base(id, name, description) {
        Kind = kind;
        _actorRoleId = actorRoleId is null ? (DomainId?)null : new DomainId(actorRoleId);
        _escalationRoleId = escalationRoleId is null ? (DomainId?)null : new DomainId(escalationRoleId);
        _formId = formId is null ? (DomainId?)null : new DomainId(formId);
        _deadlineId = deadlineId is null ? (DomainId?)null : new DomainId(deadlineId);
        _inputRecordIds = DomainModelValidation.CopyDomainIds(inputRecordIds, nameof(inputRecordIds));
        _outputRecordIds = DomainModelValidation.CopyDomainIds(outputRecordIds, nameof(outputRecordIds));
        _ruleIds = DomainModelValidation.CopyDomainIds(ruleIds, nameof(ruleIds));
        _integrationIds = DomainModelValidation.CopyDomainIds(integrationIds, nameof(integrationIds));
    }

    public WorkflowStepKind Kind { get; }

    public DomainId? ActorRoleId => _actorRoleId;

    public DomainId? EscalationRoleId => _escalationRoleId;

    public DomainId? FormId => _formId;

    public DomainId? DeadlineId => _deadlineId;

    public IReadOnlyList<DomainId> InputRecordIds => _inputRecordIds;

    public IReadOnlyList<DomainId> OutputRecordIds => _outputRecordIds;

    public IReadOnlyList<DomainId> RuleIds => _ruleIds;

    public IReadOnlyList<DomainId> IntegrationIds => _integrationIds;

    public WorkflowStep Rename(string newName) {
        SetName(newName);
        return this;
    }

    public WorkflowStep AssignActorRole(string roleId) {
        _actorRoleId = roleId;
        return this;
    }

    public WorkflowStep AttachForm(string formId) {
        _formId = formId;
        return this;
    }

    public WorkflowStep SetDeadline(string deadlineId) {
        _deadlineId = deadlineId;
        return this;
    }

    public WorkflowStep AddRule(string ruleId) {
        DomainId id = ruleId;

        if (_ruleIds.Contains(id)) {
            throw new InvalidOperationException($"Rule '{ruleId}' is already attached to step '{Id}'.");
        }

        _ruleIds.Add(id);
        return this;
    }

    public WorkflowStep RemoveRule(string ruleId) {
        DomainId id = ruleId;
        _ruleIds.RemoveAll(existing => existing == id);
        return this;
    }

    public WorkflowStep AddIntegration(string integrationId) {
        DomainId id = integrationId;

        if (_integrationIds.Contains(id)) {
            throw new InvalidOperationException($"Integration '{integrationId}' is already attached to step '{Id}'.");
        }

        _integrationIds.Add(id);
        return this;
    }

    public WorkflowStep RemoveIntegration(string integrationId) {
        DomainId id = integrationId;
        _integrationIds.RemoveAll(existing => existing == id);
        return this;
    }
}

/// <summary>
/// Describes a terminal business outcome.
/// </summary>
public sealed record class WorkflowOutcome : DomainNode {
    public WorkflowOutcome(
        string id,
        string name,
        WorkflowOutcomeCategory category,
        string? description = null)
        : base(id, name, description) {
        Category = category;
    }

    public WorkflowOutcomeCategory Category { get; }
}

/// <summary>
/// Connects a step to another step or outcome.
/// </summary>
public sealed record class WorkflowTransition : DomainNode {
    public WorkflowTransition(
        string id,
        string name,
        string sourceStepId,
        string targetNodeId,
        string? description = null,
        string? ruleId = null,
        string? conditionSummary = null)
        : base(id, name, description) {
        SourceStepId = sourceStepId;
        TargetNodeId = targetNodeId;
        RuleId = ruleId is null ? (DomainId?)null : new DomainId(ruleId);
        ConditionSummary = DomainModelValidation.NormalizeText(conditionSummary);
    }

    public DomainId SourceStepId { get; }

    public DomainId TargetNodeId { get; }

    public DomainId? RuleId { get; }

    public string? ConditionSummary { get; }
}

/// <summary>
/// Describes a business role that can own or participate in work.
/// </summary>
public sealed record class RoleDefinition : DomainNode {
    public RoleDefinition(
        string id,
        string name,
        string? description = null,
        bool canApprove = false,
        bool isExternal = false)
        : base(id, name, description) {
        CanApprove = canApprove;
        IsExternal = isExternal;
    }

    public bool CanApprove { get; }

    public bool IsExternal { get; }
}

/// <summary>
/// Describes a typed business record manipulated by workflows.
/// </summary>
public sealed record class DataRecordDefinition : DomainNode {
    private readonly Dictionary<DomainId, DataFieldDefinition> _fields;

    public DataRecordDefinition(
        string id,
        string name,
        string? description = null,
        IEnumerable<DataFieldDefinition>? fields = null)
        : base(id, name, description) {
        _fields = DomainModelValidation.CreateNodeDictionary(fields, nameof(fields));
    }

    public IReadOnlyCollection<DataFieldDefinition> Fields => _fields.Values;
}

/// <summary>
/// Describes a field on a business record.
/// </summary>
public sealed record class DataFieldDefinition : DomainNode {
    public DataFieldDefinition(
        string id,
        string name,
        FieldTypeReference type,
        string? description = null,
        bool isRequired = false,
        bool isIdentifier = false)
        : base(id, name, description) {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        IsRequired = isRequired;
        IsIdentifier = isIdentifier;
    }

    public FieldTypeReference Type { get; }

    public bool IsRequired { get; }

    public bool IsIdentifier { get; }
}

/// <summary>
/// Describes the type of a record field.
/// </summary>
public sealed record class FieldTypeReference {
    public FieldTypeReference(FieldValueKind kind, string typeId, bool isCollection = false) {
        Kind = kind;
        TypeId = typeId;
        IsCollection = isCollection;
    }

    public FieldValueKind Kind { get; }

    public DomainId TypeId { get; }

    public bool IsCollection { get; }
}

/// <summary>
/// Describes an allowed set of values.
/// </summary>
public sealed record class EnumerationDefinition : DomainNode {
    private readonly Dictionary<DomainId, EnumerationValueDefinition> _values;

    public EnumerationDefinition(
        string id,
        string name,
        string? description = null,
        IEnumerable<EnumerationValueDefinition>? values = null)
        : base(id, name, description) {
        _values = DomainModelValidation.CreateNodeDictionary(values, nameof(values));
    }

    public IReadOnlyCollection<EnumerationValueDefinition> Values => _values.Values;
}

/// <summary>
/// Describes a single enumeration value.
/// </summary>
public sealed record class EnumerationValueDefinition : DomainNode {
    public EnumerationValueDefinition(string id, string name, string? description = null, string? value = null)
        : base(id, name, description) {
        Value = DomainModelValidation.NormalizeText(value) ?? name;
    }

    public string Value { get; }
}

/// <summary>
/// Describes a UI form used to capture or review a record.
/// </summary>
public sealed record class FormDefinition : DomainNode {
    private readonly Dictionary<DomainId, FormSectionDefinition> _sections;

    public FormDefinition(
        string id,
        string name,
        string recordId,
        string? description = null,
        IEnumerable<FormSectionDefinition>? sections = null)
        : base(id, name, description) {
        RecordId = recordId;
        _sections = DomainModelValidation.CreateNodeDictionary(sections, nameof(sections));
    }

    public DomainId RecordId { get; }

    public IReadOnlyCollection<FormSectionDefinition> Sections => _sections.Values;
}

/// <summary>
/// Groups a set of fields on a form.
/// </summary>
public sealed record class FormSectionDefinition : DomainNode {
    public FormSectionDefinition(
        string id,
        string name,
        IEnumerable<string>? fieldIds = null,
        string? description = null)
        : base(id, name, description) {
        FieldIds = DomainModelValidation.CopyDomainIds(fieldIds, nameof(fieldIds));
    }

    public IReadOnlyList<DomainId> FieldIds { get; }
}

/// <summary>
/// Describes a business rule that can validate, route, or calculate process state.
/// </summary>
public sealed record class RuleDefinition : DomainNode {
    public RuleDefinition(
        string id,
        string name,
        RuleKind kind,
        string expression,
        string? description = null)
        : base(id, name, description) {
        Kind = kind;
        Expression = Guard.ThrowIfNullOrWhiteSpace(expression).Trim();
    }

    public RuleKind Kind { get; }

    public string Expression { get; }
}

/// <summary>
/// Describes an SLA or deadline that applies to a workflow step.
/// </summary>
public sealed record class DeadlineDefinition : DomainNode {
    public DeadlineDefinition(
        string id,
        string name,
        TimeSpan targetDuration,
        string? description = null,
        string? escalationRoleId = null)
        : base(id, name, description) {
        if (targetDuration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(targetDuration), "Deadline duration must be positive.");
        }

        TargetDuration = targetDuration;
        EscalationRoleId = escalationRoleId is null ? (DomainId?)null : new DomainId(escalationRoleId);
    }

    public TimeSpan TargetDuration { get; }

    public DomainId? EscalationRoleId { get; }
}

/// <summary>
/// Describes an external automation or system integration.
/// </summary>
public sealed record class IntegrationDefinition : DomainNode {
    public IntegrationDefinition(
        string id,
        string name,
        string operation,
        string? description = null,
        string? systemName = null)
        : base(id, name, description) {
        Operation = Guard.ThrowIfNullOrWhiteSpace(operation).Trim();
        SystemName = DomainModelValidation.NormalizeText(systemName);
    }

    public string Operation { get; }

    public string? SystemName { get; }
}

/// <summary>
/// Describes a glossary term used in the business domain.
/// </summary>
public sealed record class GlossaryTerm : DomainNode {
    public GlossaryTerm(string id, string name, string meaning, string? description = null)
        : base(id, name, description) {
        Meaning = Guard.ThrowIfNullOrWhiteSpace(meaning).Trim();
    }

    public string Meaning { get; }
}

/// <summary>
/// Provides a workflow-level context slice for LLM or UI retrieval.
/// </summary>
public sealed record class WorkflowContextSlice(
    WorkflowDefinition Workflow,
    IReadOnlyCollection<WorkflowStep> Steps,
    IReadOnlyCollection<WorkflowOutcome> Outcomes,
    IReadOnlyCollection<WorkflowTransition> Transitions,
    IReadOnlyCollection<RoleDefinition> Roles,
    IReadOnlyCollection<FormDefinition> Forms,
    IReadOnlyCollection<DataRecordDefinition> Records,
    IReadOnlyCollection<RuleDefinition> Rules,
    IReadOnlyCollection<DeadlineDefinition> Deadlines,
    IReadOnlyCollection<IntegrationDefinition> Integrations);

/// <summary>
/// Provides a step-local context slice for precise inspection and editing.
/// </summary>
public sealed record class StepContextSlice(
    WorkflowDefinition Workflow,
    WorkflowStep Step,
    IReadOnlyCollection<WorkflowTransition> IncomingTransitions,
    IReadOnlyCollection<WorkflowTransition> OutgoingTransitions,
    IReadOnlyCollection<RoleDefinition> Roles,
    IReadOnlyCollection<FormDefinition> Forms,
    IReadOnlyCollection<DataRecordDefinition> Records,
    IReadOnlyCollection<RuleDefinition> Rules,
    IReadOnlyCollection<DeadlineDefinition> Deadlines,
    IReadOnlyCollection<IntegrationDefinition> Integrations);

/// <summary>
/// Describes a safe mutation operation that a UI or LLM can perform.
/// </summary>
public sealed record class MutationPath(
    string Operation,
    DomainId TargetId,
    string TargetKind,
    string Description,
    IReadOnlyList<string> RequiredArguments);

public enum WorkflowStepKind {
    DataCapture,
    ManualTask,
    Decision,
    Approval,
    AutomatedTask,
    Wait,
    Notification
}

public enum WorkflowOutcomeCategory {
    Completed,
    Rejected,
    Cancelled,
    Escalated
}

public enum FieldValueKind {
    Primitive,
    Enumeration,
    Record,
    Reference
}

public enum RuleKind {
    Validation,
    Routing,
    Eligibility,
    Calculation,
    Approval
}

/// <summary>
/// Provides realistic examples of workflow-first domain models, context retrieval, and mutations.
/// </summary>
public static class DomainModelExamples {
    public static IReadOnlyList<DomainModel> CreateAll() {
        return [
            CreateLoanOriginationModel(),
            CreateFieldServiceModel()
        ];
    }

    public static DomainModel CreateLoanOriginationModel() {
        return new DomainModel(
            id: "loan-origination-studio",
            name: "Loan Origination Studio",
            description: "A workflow-first model for capturing, reviewing, and funding loan applications.",
            roles: [
                new RoleDefinition("applicant", "Applicant", "External customer providing information.", isExternal: true),
                new RoleDefinition("loan-officer", "Loan Officer", "Reviews submissions and resolves missing data."),
                new RoleDefinition("underwriter", "Underwriter", "Approves or declines applications.", canApprove: true),
                new RoleDefinition("operations-manager", "Operations Manager", "Receives escalations for overdue work.", canApprove: true)
            ],
            records: [
                new DataRecordDefinition(
                    id: "loan-application",
                    name: "Loan Application",
                    fields: [
                        new DataFieldDefinition("application-id", "Application Id", new FieldTypeReference(FieldValueKind.Primitive, "string"), isRequired: true, isIdentifier: true),
                        new DataFieldDefinition("borrower-name", "Borrower Name", new FieldTypeReference(FieldValueKind.Primitive, "string"), isRequired: true),
                        new DataFieldDefinition("requested-amount", "Requested Amount", new FieldTypeReference(FieldValueKind.Primitive, "decimal"), isRequired: true),
                        new DataFieldDefinition("term-months", "Term Months", new FieldTypeReference(FieldValueKind.Primitive, "int"), isRequired: true),
                        new DataFieldDefinition("stage", "Stage", new FieldTypeReference(FieldValueKind.Enumeration, "application-stage"), isRequired: true),
                        new DataFieldDefinition("credit-score", "Credit Score", new FieldTypeReference(FieldValueKind.Primitive, "int")),
                        new DataFieldDefinition("decision", "Decision", new FieldTypeReference(FieldValueKind.Enumeration, "decision-outcome"))
                    ]),
                new DataRecordDefinition(
                    id: "funding-package",
                    name: "Funding Package",
                    fields: [
                        new DataFieldDefinition("package-id", "Package Id", new FieldTypeReference(FieldValueKind.Primitive, "string"), isRequired: true, isIdentifier: true),
                        new DataFieldDefinition("application-ref", "Application Ref", new FieldTypeReference(FieldValueKind.Reference, "loan-application"), isRequired: true),
                        new DataFieldDefinition("approved-amount", "Approved Amount", new FieldTypeReference(FieldValueKind.Primitive, "decimal"), isRequired: true)
                    ])
            ],
            enumerations: [
                new EnumerationDefinition(
                    id: "application-stage",
                    name: "Application Stage",
                    values: [
                        new EnumerationValueDefinition("draft", "Draft"),
                        new EnumerationValueDefinition("submitted", "Submitted"),
                        new EnumerationValueDefinition("under-review", "Under Review"),
                        new EnumerationValueDefinition("approved", "Approved"),
                        new EnumerationValueDefinition("declined", "Declined"),
                        new EnumerationValueDefinition("funded", "Funded")
                    ]),
                new EnumerationDefinition(
                    id: "decision-outcome",
                    name: "Decision Outcome",
                    values: [
                        new EnumerationValueDefinition("approve", "Approve"),
                        new EnumerationValueDefinition("refer", "Refer"),
                        new EnumerationValueDefinition("decline", "Decline")
                    ])
            ],
            forms: [
                new FormDefinition(
                    id: "application-form",
                    name: "Application Form",
                    recordId: "loan-application",
                    sections: [
                        new FormSectionDefinition("borrower-section", "Borrower", [ "borrower-name", "requested-amount", "term-months" ]),
                        new FormSectionDefinition("review-section", "Review", [ "credit-score", "decision" ])
                    ]),
                new FormDefinition(
                    id: "funding-form",
                    name: "Funding Checklist",
                    recordId: "funding-package",
                    sections: [
                        new FormSectionDefinition("funding-section", "Funding", [ "approved-amount" ])
                    ])
            ],
            rules: [
                new RuleDefinition("application-complete", "Application Complete", RuleKind.Validation, "borrower-name != null && requested-amount > 0 && term-months > 0"),
                new RuleDefinition("minimum-credit", "Minimum Credit Threshold", RuleKind.Eligibility, "credit-score >= 640"),
                new RuleDefinition("route-approved", "Route Approved", RuleKind.Routing, "decision == 'Approve'"),
                new RuleDefinition("route-declined", "Route Declined", RuleKind.Routing, "decision == 'Decline'")
            ],
            deadlines: [
                new DeadlineDefinition("initial-review-sla", "Initial Review SLA", TimeSpan.FromHours(24), escalationRoleId: "operations-manager"),
                new DeadlineDefinition("underwriting-sla", "Underwriting SLA", TimeSpan.FromHours(8), escalationRoleId: "operations-manager")
            ],
            integrations: [
                new IntegrationDefinition("pull-credit", "Pull Credit Bureau", operation: "FetchCreditScore", systemName: "Equifax"),
                new IntegrationDefinition("fund-loan", "Fund Loan", operation: "CreateDisbursement", systemName: "Core Banking")
            ],
            workflows: [
                new WorkflowDefinition(
                    id: "loan-origination",
                    name: "Loan Origination",
                    startStepId: "capture-application",
                    trigger: "A borrower starts or submits a loan request.",
                    steps: [
                        new WorkflowStep(
                            id: "capture-application",
                            name: "Capture Application",
                            kind: WorkflowStepKind.DataCapture,
                            actorRoleId: "loan-officer",
                            formId: "application-form",
                            outputRecordIds: [ "loan-application" ],
                            ruleIds: [ "application-complete" ]),
                        new WorkflowStep(
                            id: "pull-credit-data",
                            name: "Pull Credit Data",
                            kind: WorkflowStepKind.AutomatedTask,
                            inputRecordIds: [ "loan-application" ],
                            outputRecordIds: [ "loan-application" ],
                            integrationIds: [ "pull-credit" ]),
                        new WorkflowStep(
                            id: "underwrite-application",
                            name: "Underwrite Application",
                            kind: WorkflowStepKind.Decision,
                            actorRoleId: "underwriter",
                            deadlineId: "underwriting-sla",
                            inputRecordIds: [ "loan-application" ],
                            outputRecordIds: [ "loan-application" ],
                            ruleIds: [ "minimum-credit", "route-approved", "route-declined" ]),
                        new WorkflowStep(
                            id: "prepare-funding",
                            name: "Prepare Funding",
                            kind: WorkflowStepKind.ManualTask,
                            actorRoleId: "loan-officer",
                            formId: "funding-form",
                            inputRecordIds: [ "loan-application" ],
                            outputRecordIds: [ "funding-package" ],
                            deadlineId: "initial-review-sla"),
                        new WorkflowStep(
                            id: "fund-loan-step",
                            name: "Fund Loan",
                            kind: WorkflowStepKind.AutomatedTask,
                            inputRecordIds: [ "funding-package" ],
                            integrationIds: [ "fund-loan" ])
                    ],
                    outcomes: [
                        new WorkflowOutcome("approved", "Approved", WorkflowOutcomeCategory.Completed),
                        new WorkflowOutcome("declined", "Declined", WorkflowOutcomeCategory.Rejected),
                        new WorkflowOutcome("funded", "Funded", WorkflowOutcomeCategory.Completed)
                    ],
                    transitions: [
                        new WorkflowTransition("capture-to-credit", "Submit Application", "capture-application", "pull-credit-data"),
                        new WorkflowTransition("credit-to-underwrite", "Send To Underwriting", "pull-credit-data", "underwrite-application"),
                        new WorkflowTransition("underwrite-to-prepare", "Approve For Funding", "underwrite-application", "prepare-funding", ruleId: "route-approved"),
                        new WorkflowTransition("underwrite-to-declined", "Decline Application", "underwrite-application", "declined", ruleId: "route-declined"),
                        new WorkflowTransition("prepare-to-fund", "Ready To Fund", "prepare-funding", "fund-loan-step"),
                        new WorkflowTransition("fund-to-funded", "Funding Complete", "fund-loan-step", "funded")
                    ])
            ],
            glossary: [
                new GlossaryTerm("underwriting-term", "Underwriting", "The process of evaluating and deciding credit risk."),
                new GlossaryTerm("funding-term", "Funding", "The release of approved loan proceeds to the borrower."),
                new GlossaryTerm("decision-term", "Decision", "The business outcome produced by underwriting.")
            ]);
    }

    public static DomainModel CreateFieldServiceModel() {
        return new DomainModel(
            id: "field-service-studio",
            name: "Field Service Studio",
            description: "A workflow-first model for scheduling, dispatching, and closing service work.",
            roles: [
                new RoleDefinition("dispatcher", "Dispatcher", "Schedules work and assigns technicians."),
                new RoleDefinition("technician", "Technician", "Performs field work and records completion."),
                new RoleDefinition("service-manager", "Service Manager", "Approves escalations and exceptions.", canApprove: true)
            ],
            records: [
                new DataRecordDefinition(
                    id: "work-order",
                    name: "Work Order",
                    fields: [
                        new DataFieldDefinition("work-order-id", "Work Order Id", new FieldTypeReference(FieldValueKind.Primitive, "string"), isRequired: true, isIdentifier: true),
                        new DataFieldDefinition("site-name", "Site Name", new FieldTypeReference(FieldValueKind.Primitive, "string"), isRequired: true),
                        new DataFieldDefinition("priority", "Priority", new FieldTypeReference(FieldValueKind.Enumeration, "work-priority"), isRequired: true),
                        new DataFieldDefinition("status", "Status", new FieldTypeReference(FieldValueKind.Enumeration, "work-status"), isRequired: true),
                        new DataFieldDefinition("assigned-technician", "Assigned Technician", new FieldTypeReference(FieldValueKind.Primitive, "string"))
                    ])
            ],
            enumerations: [
                new EnumerationDefinition(
                    id: "work-priority",
                    name: "Work Priority",
                    values: [
                        new EnumerationValueDefinition("normal", "Normal"),
                        new EnumerationValueDefinition("high", "High"),
                        new EnumerationValueDefinition("emergency", "Emergency")
                    ]),
                new EnumerationDefinition(
                    id: "work-status",
                    name: "Work Status",
                    values: [
                        new EnumerationValueDefinition("new", "New"),
                        new EnumerationValueDefinition("scheduled", "Scheduled"),
                        new EnumerationValueDefinition("in-progress", "In Progress"),
                        new EnumerationValueDefinition("completed", "Completed")
                    ])
            ],
            forms: [
                new FormDefinition(
                    id: "dispatch-form",
                    name: "Dispatch Form",
                    recordId: "work-order",
                    sections: [
                        new FormSectionDefinition("dispatch-section", "Dispatch", [ "site-name", "priority", "assigned-technician" ])
                    ])
            ],
            rules: [
                new RuleDefinition("emergency-routing", "Emergency Routing", RuleKind.Routing, "priority == 'Emergency'"),
                new RuleDefinition("assignment-required", "Assignment Required", RuleKind.Validation, "assigned-technician != null")
            ],
            deadlines: [
                new DeadlineDefinition("dispatch-sla", "Dispatch SLA", TimeSpan.FromHours(2), escalationRoleId: "service-manager")
            ],
            workflows: [
                new WorkflowDefinition(
                    id: "work-order-lifecycle",
                    name: "Work Order Lifecycle",
                    startStepId: "triage-request",
                    steps: [
                        new WorkflowStep(
                            id: "triage-request",
                            name: "Triage Request",
                            kind: WorkflowStepKind.Decision,
                            actorRoleId: "dispatcher",
                            outputRecordIds: [ "work-order" ],
                            ruleIds: [ "emergency-routing" ]),
                        new WorkflowStep(
                            id: "schedule-visit",
                            name: "Schedule Visit",
                            kind: WorkflowStepKind.ManualTask,
                            actorRoleId: "dispatcher",
                            formId: "dispatch-form",
                            inputRecordIds: [ "work-order" ],
                            outputRecordIds: [ "work-order" ],
                            ruleIds: [ "assignment-required" ],
                            deadlineId: "dispatch-sla"),
                        new WorkflowStep(
                            id: "perform-work",
                            name: "Perform Work",
                            kind: WorkflowStepKind.ManualTask,
                            actorRoleId: "technician",
                            inputRecordIds: [ "work-order" ],
                            outputRecordIds: [ "work-order" ]),
                        new WorkflowStep(
                            id: "notify-completion",
                            name: "Notify Completion",
                            kind: WorkflowStepKind.Notification,
                            inputRecordIds: [ "work-order" ])
                    ],
                    outcomes: [
                        new WorkflowOutcome("closed", "Closed", WorkflowOutcomeCategory.Completed)
                    ],
                    transitions: [
                        new WorkflowTransition("triage-to-schedule", "Schedule Work", "triage-request", "schedule-visit"),
                        new WorkflowTransition("schedule-to-perform", "Dispatch Technician", "schedule-visit", "perform-work"),
                        new WorkflowTransition("perform-to-notify", "Mark Complete", "perform-work", "notify-completion"),
                        new WorkflowTransition("notify-to-closed", "Close Work Order", "notify-completion", "closed")
                    ])
            ],
            glossary: [
                new GlossaryTerm("dispatch-term", "Dispatch", "The act of assigning a technician to a work order.")
            ]);
    }

    public static StepContextSlice CreateLoanOriginationUnderwritingContext() {
        return CreateLoanOriginationModel().GetStepContext("loan-origination", "underwrite-application");
    }

    public static DomainModel ApplyLoanOriginationExtensions(DomainModel model) {
        ArgumentNullException.ThrowIfNull(model);

        var workflow = model.GetWorkflow("loan-origination");
        model.AddStep(
            workflow,
            new WorkflowStep(
                id: "fraud-screen",
                name: "Fraud Screen",
                kind: WorkflowStepKind.AutomatedTask,
                inputRecordIds: ["loan-application"],
                outputRecordIds: ["loan-application"]));

        model.AddTransition(
            workflow,
            new WorkflowTransition(
                id: "credit-to-fraud",
                name: "Route To Fraud Screen",
                sourceStepId: "pull-credit-data",
                targetNodeId: "fraud-screen"));

        model.AssignStepRole(workflow, "prepare-funding", "loan-officer");
        model.SetStepDeadline(workflow, "prepare-funding", "initial-review-sla");
        return model;
    }
}

internal static class DomainModelValidation {
    public static string NormalizeIdentifier(string value, [CallerArgumentExpression("value")] string paramName = "") {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }

    public static string? NormalizeOptionalIdentifier(string? value) {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    public static string? NormalizeText(string? value) {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    public static IReadOnlyList<T> CopyNodes<T>(IEnumerable<T>? values, string paramName) where T : DomainNode {
        var items = values?.ToArray() ?? [];
        EnsureUniqueIds(items, static item => item.Id, paramName);
        EnsureUniqueNames(items, static item => item.Name, paramName);
        return Array.AsReadOnly(items);
    }

    public static Dictionary<DomainId, T> CreateNodeDictionary<T>(IEnumerable<T>? values, string paramName) where T : DomainNode {
        var items = CopyNodes(values, paramName);
        var dictionary = new Dictionary<DomainId, T>(items.Count);

        foreach (var item in items) {
            dictionary.Add(item.Id, item);
        }

        return dictionary;
    }

    public static List<DomainId> CopyDomainIds(IEnumerable<string>? values, string paramName) {
        if (values is null) {
            return [];
        }

        var result = new List<DomainId>();
        var seen = new HashSet<DomainId>();

        foreach (var raw in values) {
            DomainId id = raw;
            if (!seen.Add(id)) {
                throw new ArgumentException($"Duplicate id '{id}' is not allowed.", paramName);
            }

            result.Add(id);
        }

        return result;
    }

    public static IReadOnlyList<string> CopyIdentifiers(IEnumerable<string>? values, string paramName) {
        var items = values?.Select(value => NormalizeIdentifier(value, paramName)).ToArray() ?? [];
        EnsureUniqueIds(items, static item => item, paramName);
        return Array.AsReadOnly(items);
    }


    public static void ValidateModel(DomainModel model) {
        ArgumentNullException.ThrowIfNull(model);

        var roleIds = model.Roles.Select(role => role.Id).ToHashSet();
        var recordIds = model.Records.Select(record => record.Id).ToHashSet();
        var enumerationIds = model.Enumerations.Select(enumeration => enumeration.Id).ToHashSet();
        var formIds = model.Forms.Select(form => form.Id).ToHashSet();
        var ruleIds = model.Rules.Select(rule => rule.Id).ToHashSet();
        var deadlineIds = model.Deadlines.Select(deadline => deadline.Id).ToHashSet();
        var integrationIds = model.Integrations.Select(integration => integration.Id).ToHashSet();

        foreach (var record in model.Records) {
            ValidateRecord(record, recordIds, enumerationIds);
        }

        foreach (var form in model.Forms) {
            ValidateForm(form, model.Records);
        }

        foreach (var deadline in model.Deadlines) {
            EnsureReferenceExists(deadline.EscalationRoleId, roleIds, nameof(deadline.EscalationRoleId), deadline.Id);
        }

        foreach (var workflow in model.Workflows) {
            ValidateWorkflow(workflow, roleIds, recordIds, formIds, ruleIds, deadlineIds, integrationIds);
        }
    }

    private static void ValidateRecord(
        DataRecordDefinition record,
        ISet<DomainId> recordIds,
        ISet<DomainId> enumerationIds) {
        foreach (var field in record.Fields) {
            if (field.Type.Kind == FieldValueKind.Enumeration) {
                EnsureReferenceExists(field.Type.TypeId, enumerationIds, nameof(field.Type.TypeId), field.Id);
            }

            if (field.Type.Kind is FieldValueKind.Record or FieldValueKind.Reference) {
                EnsureReferenceExists(field.Type.TypeId, recordIds, nameof(field.Type.TypeId), field.Id);
            }
        }
    }

    private static void ValidateForm(FormDefinition form, IReadOnlyCollection<DataRecordDefinition> records) {
        var record = records.SingleOrDefault(candidate => candidate.Id == form.RecordId)
            ?? throw new ArgumentException($"Form '{form.Id}' references missing record '{form.RecordId}'.", nameof(form.RecordId));

        var recordFieldIds = record.Fields.Select(field => field.Id).ToHashSet();

        foreach (var section in form.Sections) {
            foreach (var fieldId in section.FieldIds) {
                EnsureReferenceExists(fieldId, recordFieldIds, nameof(section.FieldIds), form.Id);
            }
        }
    }

    private static void ValidateWorkflow(
        WorkflowDefinition workflow,
        ISet<DomainId> roleIds,
        ISet<DomainId> recordIds,
        ISet<DomainId> formIds,
        ISet<DomainId> ruleIds,
        ISet<DomainId> deadlineIds,
        ISet<DomainId> integrationIds) {
        if (workflow.Outcomes.Count == 0) {
            throw new ArgumentException($"Workflow '{workflow.Id}' must define at least one outcome.", nameof(workflow.Outcomes));
        }

        var stepIds = workflow.Steps.Select(step => step.Id).ToHashSet();
        var outcomeIds = workflow.Outcomes.Select(outcome => outcome.Id).ToHashSet();
        var nodeIds = new HashSet<DomainId>(stepIds);

        foreach (var outcomeId in outcomeIds) {
            if (!nodeIds.Add(outcomeId)) {
                throw new ArgumentException($"Workflow '{workflow.Id}' contains a duplicate node id '{outcomeId}'.", nameof(workflow.Outcomes));
            }
        }

        EnsureReferenceExists(workflow.StartStepId, stepIds, nameof(workflow.StartStepId), workflow.Id);

        foreach (var step in workflow.Steps) {
            EnsureReferenceExists(step.ActorRoleId, roleIds, nameof(step.ActorRoleId), step.Id);
            EnsureReferenceExists(step.EscalationRoleId, roleIds, nameof(step.EscalationRoleId), step.Id);
            EnsureReferenceExists(step.FormId, formIds, nameof(step.FormId), step.Id);
            EnsureReferencesExist(step.InputRecordIds, recordIds, nameof(step.InputRecordIds), step.Id);
            EnsureReferencesExist(step.OutputRecordIds, recordIds, nameof(step.OutputRecordIds), step.Id);
            EnsureReferencesExist(step.RuleIds, ruleIds, nameof(step.RuleIds), step.Id);
            EnsureReferenceExists(step.DeadlineId, deadlineIds, nameof(step.DeadlineId), step.Id);
            EnsureReferencesExist(step.IntegrationIds, integrationIds, nameof(step.IntegrationIds), step.Id);
        }

        foreach (var transition in workflow.Transitions) {
            EnsureReferenceExists(transition.SourceStepId, stepIds, nameof(transition.SourceStepId), transition.Id);
            EnsureReferenceExists(transition.TargetNodeId, nodeIds, nameof(transition.TargetNodeId), transition.Id);
            EnsureReferenceExists(transition.RuleId, ruleIds, nameof(transition.RuleId), transition.Id);
        }
    }

    private static void EnsureUniqueIds<T>(IEnumerable<T> values, Func<T, string> getId, string paramName) {
        var seenIds = new HashSet<DomainId>();

        foreach (var value in values) {
            DomainId id = NormalizeIdentifier(getId(value), paramName);
            if (!seenIds.Add(id)) {
                throw new ArgumentException($"Duplicate id '{id}' is not allowed.", paramName);
            }
        }
    }

    private static void EnsureUniqueNames<T>(IEnumerable<T> values, Func<T, string> getName, string paramName) {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values) {
            var name = Guard.ThrowIfNullOrWhiteSpace(getName(value));
            if (!seenNames.Add(name.Trim())) {
                throw new ArgumentException($"Duplicate name '{name}' is not allowed.", paramName);
            }
        }
    }

    private static void EnsureReferenceExists(DomainId id, ISet<DomainId> validIds, string paramName, DomainId ownerId) {
        if (!validIds.Contains(id)) {
            throw new ArgumentException($"'{ownerId}' references missing id '{id}'.", paramName);
        }
    }

    private static void EnsureReferenceExists(DomainId? id, ISet<DomainId> validIds, string paramName, DomainId ownerId) {
        if (id is null) {
            return;
        }

        EnsureReferenceExists(id.Value, validIds, paramName, ownerId);
    }

    private static void EnsureReferencesExist(IEnumerable<DomainId> ids, ISet<DomainId> validIds, string paramName, DomainId ownerId) {
        foreach (var id in ids) {
            EnsureReferenceExists(id, validIds, paramName, ownerId);
        }
    }
}