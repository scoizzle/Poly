using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling.Effects;

/// <summary>
/// Reference to a specific named value produced by a prior effect in the wiring model.
/// At code generation time, this becomes: priorEffectResult.OutputName.
/// </summary>
public sealed record EffectValueRef(string SourceEffectName, string OutputName) : DomainValue(
    null!,
    $"{SourceEffectName}.{OutputName}",
    null!) {

    /// <summary>
    /// The name of the effect that produces the value.
    /// </summary>
    public string SourceEffectName => SourceEffectName;

    /// <summary>
    /// The specific named output on that effect.
    /// </summary>
    public string OutputName => OutputName;
}