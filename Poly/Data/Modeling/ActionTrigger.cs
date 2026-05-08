namespace Poly.Data.Modeling;

public abstract record ActionTrigger {
    public sealed record Command : ActionTrigger;

    public sealed record EventHandler(Event EventType, string EventParameterName) : ActionTrigger;

    public static ActionTrigger Default { get; } = new Command();
}