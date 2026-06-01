namespace Poly.DomainModeling;

public sealed record EventCorrelationBinding(
    string EventPropertyName,
    string ConsumerPropertyName
) : DomainObject {
    public sealed override IEnumerable<Node?> Children => [];
}