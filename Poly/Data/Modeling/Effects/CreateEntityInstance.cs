using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed class CreateEntityInstance : Effect {
    private static readonly DomainModelAnalyzer Analyzer = new();

    public required Entity EntityType { get; init; }
    public Stage? InitialStage { get; init; }

    public override IReadOnlyCollection<IDomainValue> RequiredParameters => GetRequiredProperties().Cast<IDomainValue>().ToArray();

    public override void Validate(Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);

        EntityType.ThrowIfMismatchedDomain(entity.Domain);

        if (InitialStage is not null) {
            InitialStage.ThrowIfMismatchedDomain(entity.Domain);

            if (!EntityType.Stages.Contains(InitialStage)) {
                throw new InvalidOperationException(
                    $"Initial stage '{InitialStage.Name}' must belong to entity '{EntityType.Name}'.");
            }
        }
    }

    public IReadOnlyCollection<Property> GetRequiredProperties() {
        return Analyzer.AnalyzeRequiredProperties(EntityType, ResolveInitialStage());
    }

    private Stage? ResolveInitialStage() {
        if (InitialStage is null) {
            return null;
        }

        if (!EntityType.Stages.Contains(InitialStage)) {
            throw new InvalidOperationException($"Initial stage '{InitialStage.Name}' must belong to entity '{EntityType.Name}'.");
        }

        return InitialStage;
    }
}