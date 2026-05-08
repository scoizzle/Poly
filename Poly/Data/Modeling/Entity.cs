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
    private readonly List<Stage> _stages = [];
    private readonly List<Policy> _policies = [];
    private readonly List<Action> _actions = [];
    private readonly List<Event> _events = [];
    private readonly List<EventSubscription> _eventSubscriptions = [];
    private readonly List<Relationship> _relationships = [];

    public Entity(Domain domain, string name, Entity? parentEntity = null) : base(domain, name) {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        ParentEntity = parentEntity;
    }

    public Entity? ParentEntity { get; }

    public IReadOnlyCollection<Stage> Stages => _stages.AsReadOnly();
    public IReadOnlyCollection<Policy> Policies => _policies.AsReadOnly();
    public IReadOnlyCollection<Action> Actions => _actions.AsReadOnly();
    public IReadOnlyCollection<Event> Events => _events.AsReadOnly();
    public IReadOnlyCollection<EventSubscription> EventSubscriptions => _eventSubscriptions.AsReadOnly();
    public IReadOnlyCollection<Relationship> Relationships => _relationships.AsReadOnly();

    public override IEnumerable<DomainObject> ChildObjects => [.. _properties, .. _stages, .. _policies, .. _actions, .. _events, .. _eventSubscriptions];
}