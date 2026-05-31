namespace Poly.DomainModeling;

/// <summary>
/// Helper to support .OnEntry(Publish("Event", ...)) style from the original Ugh sketch.
/// </summary>
public sealed class OnEntryBuilder {
    private readonly List<Effect> _effects = new();

    public OnEntryBuilder Publish(string eventName, Action<PublishEventBuilder> configure) {
        ArgumentNullException.ThrowIfNull(configure);
        var publishBuilder = new PublishEventBuilder(Guard.ThrowIfNullOrEmpty(eventName));
        configure(publishBuilder);
        _effects.Add(publishBuilder.Build());
        return this;
    }

    internal List<Effect> BuildEffects() => _effects;
}