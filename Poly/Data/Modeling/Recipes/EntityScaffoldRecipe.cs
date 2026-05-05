using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Recipes;

/// <summary>
/// Fluent recipe to scaffold a basic entity with properties and lifecycle stages.
/// </summary>
public sealed class EntityScaffoldRecipe : IScaffoldRecipe {
    private readonly string _entityName;
    private readonly List<(string name, DomainType type)> _properties = [];
    private readonly List<string> _stageNames = [];

    public string Name => $"Entity[{_entityName}]";

    public EntityScaffoldRecipe(string entityName) {
        ArgumentNullException.ThrowIfNull(entityName);
        _entityName = entityName;
    }

    /// <summary>Adds a property to the entity.</summary>
    public EntityScaffoldRecipe WithProperty(string propertyName, DomainType propertyType) {
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(propertyType);
        _properties.Add((propertyName, propertyType));
        return this;
    }

    /// <summary>Adds a lifecycle stage to the entity.</summary>
    public EntityScaffoldRecipe WithStage(string stageName) {
        ArgumentNullException.ThrowIfNull(stageName);
        _stageNames.Add(stageName);
        return this;
    }

    /// <summary>Builds the entity and all child structures into the domain via transactional mutation.</summary>
    public void BuildInto(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        var mutation = domain.CreateMutation();

        // Create the entity
        var entity = new Entity(domain, _entityName);
        mutation.AddType(entity);

        // Add properties
        foreach (var (propName, propType) in _properties) {
            var property = new Property(domain, propName, propType);
            mutation.AddProperty(entity, property);
        }

        // Add stages
        foreach (var stageName in _stageNames) {
            var stage = new Stage(domain, stageName);
            mutation.AddStage(entity, stage);
        }

        mutation.Apply();
    }
}