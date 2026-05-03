using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public abstract record Rule(Domain Domain, string Name, DomainValue Value, Constraint Constraints) : DomainMember(Domain, Name);