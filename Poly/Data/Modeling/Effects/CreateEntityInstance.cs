using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed record CreateEntityInstance(Domain Domain, Entity EntityType, Stage? InitialStage = default)
    : Effect(Domain, res => res.Produces(ResultParameterName, EntityType)) {
    public const string ResultParameterName = "entity";
}