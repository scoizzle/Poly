using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

public sealed record CreateEntityInstance(Domain Domain) : Effect(Domain) {
    public required Entity EntityType { get; init; }
    public Stage? InitialStage { get; init; }

    /// <summary>
    /// Initialize the result declarations. Called when effect is added to an action.
    /// </summary>
    internal void InitializeResult() {
        Produces("entity", EntityType);
    }
}