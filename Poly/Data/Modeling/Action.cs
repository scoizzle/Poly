using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed class Action : IDomainObject {
    private readonly List<IDomainValue> _parameters = [];
    private readonly List<Effect> _effects = [];

    public required Domain Domain { get; init; }
    public required Entity Entity { get; init; }
    public required string Name { get; set; }

    public IReadOnlyCollection<IDomainValue> Parameters => _parameters.AsReadOnly();
    public IReadOnlyCollection<Effect> Effects => _effects.AsReadOnly();

    public void AddParameter(IDomainValue parameter) {
        parameter.ThrowIfNullOrMismatchedDomain(Domain);

        if (parameter is Property property && _parameters.OfType<Property>().Any(existing => string.Equals(existing.Name, property.Name, StringComparison.Ordinal))) {
            throw new InvalidOperationException($"Parameter '{property.Name}' already exists on action '{Name}'.");
        }

        _parameters.Add(parameter);
    }

    public void AddEffect(Effect effect) {
        ArgumentNullException.ThrowIfNull(effect);

        switch (effect) {
            case CreateEntityInstance create:
                create.EntityType.ThrowIfMismatchedDomain(Domain);

                if (create.InitialStage is not null) {
                    create.InitialStage.ThrowIfMismatchedDomain(Domain);

                    if (!create.EntityType.Stages.Contains(create.InitialStage)) {
                        throw new InvalidOperationException(
                            $"Initial stage '{create.InitialStage.Name}' must belong to entity '{create.EntityType.Name}'.");
                    }
                }

                break;
            case PublishEvent publishEvent:
                publishEvent.Event.ThrowIfMismatchedDomain(Domain);
                break;
            case StageTransition stageTransition:
                stageTransition.TargetStage.ThrowIfMismatchedDomain(Domain);
                break;
            case InvokeAction invokeAction:
                invokeAction.TargetAction.ThrowIfMismatchedDomain(Domain);
                break;
        }

        _effects.Add(effect);
    }
}