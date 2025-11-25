using Poly.Validation;

namespace Poly.DataModeling;

public abstract record DataProperty(string Name, IEnumerable<Constraint> Constraints);