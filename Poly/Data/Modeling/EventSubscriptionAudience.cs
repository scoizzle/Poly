namespace Poly.Data.Modeling;

public abstract record EventSubscriptionAudience {
    public sealed record Broadcast : EventSubscriptionAudience;

    public sealed record Correlated : EventSubscriptionAudience;

    public static EventSubscriptionAudience Default { get; } = new Broadcast();
}