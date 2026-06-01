namespace Poly.DomainModeling.Effects;

public sealed record InvokeActionEffect(
    string ActionName,
    IReadOnlyList<PropertyBinding> ParameterBindings
) : Effect {
    public sealed override IEnumerable<Node?> Children => [.. ParameterBindings];
}