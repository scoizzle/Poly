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
public sealed class Entity : IDomainType {
    private readonly List<Property> _properties = [];
    private readonly List<Stage> _stages = [];
    private readonly List<Rule> _rules = [];
    private readonly List<Action> _actions = [];
    private readonly List<Event> _events = [];
    private readonly List<Relationship> _relationships = [];

    public required Domain Domain { get; init; }
    public required string Name { get; set; }

    public IReadOnlyCollection<Property> Properties => _properties.AsReadOnly();
    public IReadOnlyCollection<Stage> Stages => _stages.AsReadOnly();
    public IReadOnlyCollection<Rule> Rules => _rules.AsReadOnly();
    public IReadOnlyCollection<Action> Actions => _actions.AsReadOnly();
    public IReadOnlyCollection<Event> Events => _events.AsReadOnly();
    public IReadOnlyCollection<Relationship> Relationships => _relationships.AsReadOnly();

    public void AddProperty(Property property) => _properties.Add(property);
    public void AddStage(Stage stage) => _stages.Add(stage);
    public void AddRule(Rule rule) => _rules.Add(rule);
    public void AddAction(Action action) => _actions.Add(action);
    public void AddEvent(Event @event) => _events.Add(@event);
    public void AddRelationship(Relationship relationship) {
        ArgumentNullException.ThrowIfNull(relationship);

        if (!ReferenceEquals(relationship.Source, this)) {
            throw new InvalidOperationException($"Relationship '{relationship.Name}' source must be '{Name}'.");
        }

        _relationships.Add(relationship);
    }
}