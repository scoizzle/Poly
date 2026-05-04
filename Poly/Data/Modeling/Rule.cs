using Poly.Data.Modeling.TypeSystem;

namespace Poly.Data.Modeling;

public abstract record Rule(Domain Domain, string Name) : DomainMember(Domain, Name);