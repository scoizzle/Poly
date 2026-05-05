using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Recipes;

/// <summary>
/// Fluent recipe to scaffold a staged workflow with entities, stages, and state transitions.
/// </summary>
public sealed class WorkflowScaffoldRecipe : IScaffoldRecipe {
    private readonly string _workflowName;
    private readonly List<(string entityName, Entity? entity)> _entities = [];
    private readonly List<string> _stageNames = [];
    private readonly List<(string from, string to)> _transitions = [];

    public string Name => $"Workflow[{_workflowName}]";

    public WorkflowScaffoldRecipe(string workflowName) {
        ArgumentNullException.ThrowIfNull(workflowName);
        _workflowName = workflowName;
    }

    /// <summary>Adds an entity to the workflow.</summary>
    public WorkflowScaffoldRecipe WithEntity(string entityName) {
        ArgumentNullException.ThrowIfNull(entityName);
        _entities.Add((entityName, null));
        return this;
    }

    /// <summary>Adds a stage name to be created on all entities.</summary>
    public WorkflowScaffoldRecipe WithStage(params string[] stageNames) {
        if (stageNames != null) {
            _stageNames.AddRange(stageNames);
        }
        return this;
    }

    /// <summary>Adds a state transition between stages (for documentation/validation).</summary>
    public WorkflowScaffoldRecipe WithStateTransition(string fromStageName, string toStageName) {
        ArgumentNullException.ThrowIfNull(fromStageName);
        ArgumentNullException.ThrowIfNull(toStageName);
        _transitions.Add((fromStageName, toStageName));
        return this;
    }

    /// <summary>Builds the workflow entities, stages, and transitions into the domain via transactional mutation.</summary>
    public void BuildInto(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        if (_entities.Count == 0) {
            throw new InvalidOperationException($"Workflow recipe '{_workflowName}' has no entities. Call WithEntity().");
        }
        if (_stageNames.Count == 0) {
            throw new InvalidOperationException($"Workflow recipe '{_workflowName}' has no stages. Call WithStage().");
        }

        var mutation = domain.CreateMutation();

        // Create entities and stages
        var entityMap = new Dictionary<string, Entity>();
        foreach (var (entityName, _) in _entities) {
            var entity = new Entity(domain, entityName);
            mutation.AddType(entity);
            entityMap[entityName] = entity;

            // Add stages to each entity
            foreach (var stageName in _stageNames) {
                var stage = new Stage(domain, stageName);
                mutation.AddStage(entity, stage);
            }
        }

        // Validate transitions (they reference valid stages)
        foreach (var (fromStage, toStage) in _transitions) {
            if (!_stageNames.Contains(fromStage)) {
                throw new InvalidOperationException($"Workflow transition references unknown stage '{fromStage}'.");
            }
            if (!_stageNames.Contains(toStage)) {
                throw new InvalidOperationException($"Workflow transition references unknown stage '{toStage}'.");
            }
        }

        mutation.Apply();
    }
}