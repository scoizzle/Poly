namespace Poly.DomainModeling.V2;

using System.Collections.ObjectModel;

internal sealed class DomainBuilder {
    private readonly string _name;
    private readonly List<Entity> _entities = [];
    private readonly List<Relationship> _relationships = [];

    public DomainBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    public DomainBuilder Entity(string name, global::System.Action<EntityBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var builder = new EntityBuilder(name);
        configure?.Invoke(builder);
        _entities.Add(builder.Build());
        return this;
    }

    public DomainBuilder Relationship(string name, string sourceEntity, string targetEntity, RelationshipKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEntity);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetEntity);
        _relationships.Add(new Relationship(name, sourceEntity, targetEntity, kind));
        return this;
    }

    public Domain Build() => new(_name, new ReadOnlyCollection<Entity>(_entities), new ReadOnlyCollection<Relationship>(_relationships));
}

internal sealed class EntityBuilder {
    private readonly string _name;
    private readonly List<Property> _properties = [];
    private readonly List<Stage> _stages = [];
    private readonly List<Action> _actions = [];

    public EntityBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    public EntityBuilder Property(string name, string type, bool isRequired = false, string? defaultValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        _properties.Add(new Property(name, type, isRequired, defaultValue));
        return this;
    }

    public EntityBuilder Stage(string name, bool isInitial = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _stages.Add(new Stage(name, isInitial));
        return this;
    }

    public EntityBuilder Action(string name, global::System.Action<ActionBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var builder = new ActionBuilder(name);
        configure?.Invoke(builder);
        _actions.Add(builder.Build());
        return this;
    }

    public Entity Build() => new(
        _name,
        new ReadOnlyCollection<Property>(_properties),
        new ReadOnlyCollection<Stage>(_stages),
        new ReadOnlyCollection<Action>(_actions)
    );
}

internal sealed class ActionBuilder {
    private readonly string _name;
    private readonly List<Property> _parameters = [];
    private readonly List<IEffect> _effects = [];

    public ActionBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    public ActionBuilder Parameter(string name, string type, bool isRequired = true, string? defaultValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        _parameters.Add(new Property(name, type, isRequired, defaultValue));
        return this;
    }

    public ActionBuilder Effect(IEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _effects.Add(effect);
        return this;
    }

    public Action Build() => new(_name, new ReadOnlyCollection<Property>(_parameters), new ReadOnlyCollection<IEffect>(_effects));
}
