using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

/// <remarks>
/// Effectively, within the Domain's type system, an Entity is equivalent to the following in C#:
/// <code>
/// public partial class Domain {
///     public partial class `EntityName` {
///         [Flags]
///         public enum Stage {
///             Stage1 = 1 << 0,
///             Stage2 = 1 << 1,
///             Stage3 = 1 << 2 | Stage2, // Stage 3 is a substage of Stage 2
///             Stage4 = 1 << 3,
///             ...
///         }
/// 
///         public Stage CurrentStage { get; private set; }
/// 
///         // Properties
///         public Type1 Property1 { get; private set; }
///         public Type2 Property2 { get; private set; }
/// 
///         // Rules - See Rule.cs for how rules are represented
/// 
///         // Actions
///         public void Action1(TypeA parameterA, TypeB parameterB) {
///             // Validate parameters against rules/constraints
///             // Check that action is allowed in CurrentStage based on stage rules
///             // Action implementation that can modify properties and trigger events
///         }
/// 
///         // Events - See Event.cs for how events are represented
///     }
/// }
/// </code>
/// </remarks>
public partial record Entity : DomainType {
    private readonly List<Property> _properties = [];
    private readonly List<Stage> _stages = [];
    private readonly List<Policy> _policies = [];
    private readonly List<Action> _actions = [];
    private readonly List<Event> _events = [];
    private readonly List<Relationship> _relationships = [];

    public Entity(Domain domain, string name, Entity? parentEntity = null) : base(domain) {
        ArgumentNullException.ThrowIfNull(name);

        parentEntity?.ThrowIfMismatchedDomain(domain);

        Name = name;
        ParentEntity = parentEntity;
    }

    public Entity? ParentEntity { get; }

    public override IReadOnlyCollection<Property> Properties => _properties.AsReadOnly();
    public IReadOnlyCollection<Stage> Stages => _stages.AsReadOnly();
    public IReadOnlyCollection<Policy> Policies => _policies.AsReadOnly();
    public IReadOnlyCollection<Action> Actions => _actions.AsReadOnly();
    public IReadOnlyCollection<Event> Events => _events.AsReadOnly();
    public IReadOnlyCollection<Relationship> Relationships => _relationships.AsReadOnly();

    private void AddProperty(Property property) {
        property.ThrowIfNullOrMismatchedDomain(Domain);

        if (_properties.Any(existing => string.Equals(existing.Name, property.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Property '{property.Name}' already exists on entity '{Name}'.");
        }

        _properties.Add(property);
    }

    private bool RemoveProperty(Property property) {
        property.ThrowIfNullOrMismatchedDomain(Domain);
        return _properties.Remove(property);
    }

    private void AddStage(Stage stage) {
        stage.ThrowIfNullOrMismatchedDomain(Domain);

        if (_stages.Any(existing => string.Equals(existing.Name, stage.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Stage '{stage.Name}' already exists on entity '{Name}'.");
        }

        if (ParentEntity is not null && ParentEntity.Stages.Count > 0) {
            if (stage.Parent is null) {
                throw new InvalidOperationException(
                    $"Stage '{stage.Name}' on child entity '{Name}' must have a parent stage when parent entity '{ParentEntity.Name}' defines stages.");
            }

            if (!ParentEntity.Stages.Contains(stage.Parent)) {
                throw new InvalidOperationException(
                    $"Stage '{stage.Name}' on child entity '{Name}' must directly inherit from a stage defined on parent entity '{ParentEntity.Name}'.");
            }
        }

        stage.AttachToEntity(this);
        _stages.Add(stage);
    }

    private bool RemoveStage(Stage stage) {
        stage.ThrowIfNullOrMismatchedDomain(Domain);
        return _stages.Remove(stage);
    }

    private void AddPolicy(Policy policy) {
        policy.ThrowIfNullOrMismatchedDomain(Domain);

        if (_policies.Any(existing => string.Equals(existing.Name, policy.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Policy '{policy.Name}' already exists on entity '{Name}'.");
        }

        _policies.Add(policy);
    }

    private bool RemovePolicy(Policy policy) {
        policy.ThrowIfNullOrMismatchedDomain(Domain);
        return _policies.Remove(policy);
    }

    private void AddAction(Action action) {
        action.ThrowIfNullOrMismatchedDomain(Domain);

        if (!ReferenceEquals(action.Entity, this)) {
            throw new InvalidOperationException($"Action '{action.Name}' must belong to entity '{Name}'.");
        }

        if (_actions.Any(existing => string.Equals(existing.Name, action.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Action '{action.Name}' already exists on entity '{Name}'.");
        }

        _actions.Add(action);
    }

    private bool RemoveAction(Action action) {
        action.ThrowIfNullOrMismatchedDomain(Domain);
        return _actions.Remove(action);
    }

    private void AddEvent(Event @event) {
        @event.ThrowIfNullOrMismatchedDomain(Domain);

        if (_events.Any(existing => string.Equals(existing.Name, @event.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Event '{@event.Name}' already exists on entity '{Name}'.");
        }

        _events.Add(@event);
    }

    private bool RemoveEvent(Event @event) {
        @event.ThrowIfNullOrMismatchedDomain(Domain);
        return _events.Remove(@event);
    }

    private void AddRelationship(Relationship relationship) {
        relationship.ThrowIfNullOrMismatchedDomain(Domain);

        if (!Domain.Relationships.Contains(relationship)) {
            throw new InvalidOperationException($"Relationship '{relationship.Name}' must be registered in domain '{Domain.Name}' before attaching to entity '{Name}'.");
        }

        if (!ReferenceEquals(relationship.Source, this)) {
            throw new InvalidOperationException($"Relationship '{relationship.Name}' source must match entity '{Name}'.");
        }

        if (_relationships.Any(existing => string.Equals(existing.Name, relationship.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Relationship '{relationship.Name}' already exists on entity '{Name}'.");
        }

        _relationships.Add(relationship);
    }

    private bool RemoveRelationship(Relationship relationship) {
        relationship.ThrowIfNullOrMismatchedDomain(Domain);
        return _relationships.Remove(relationship);
    }
}