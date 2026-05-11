namespace Poly.Data.Modeling.Effects;

public sealed record CreateEntityInstance(Domain Domain, Entity EntityType, Stage? InitialStage = default)
    : Effect(Domain, res => _ = res.SetOutput(ResultParameterName, EntityType)) {
    public const string ResultParameterName = "entity";
}