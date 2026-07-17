namespace Poly.DomainModeling;

/// <summary>
/// Helper for composing on-entry effects during stage construction.
/// </summary>
public sealed class OnEntryBuilder {
    private readonly List<Effect> _effects = new();

    internal void AddEffect(Effect effect) => _effects.Add(effect);

    internal List<Effect> BuildEffects() => _effects;
}