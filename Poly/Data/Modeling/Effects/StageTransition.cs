using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed class StageTransition : Effect {
    public required Stage TargetStage { get; init; }

    public override void Validate(Entity entity) {
        ArgumentNullException.ThrowIfNull(entity);

        TargetStage.ThrowIfMismatchedDomain(entity.Domain);

        if (entity.Stages.Count > 0 && !entity.Stages.Contains(TargetStage)) {
            throw new InvalidOperationException(
                $"Target stage '{TargetStage.Name}' must belong to entity '{entity.Name}'.");
        }
    }
}