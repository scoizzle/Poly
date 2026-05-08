using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public sealed record EventCorrelationBinding(Domain Domain, string EventPropertyName, string ConsumerPropertyName)
    : DomainMember(Domain, $"{EventPropertyName}->{ConsumerPropertyName}");