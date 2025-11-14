using Poly.Validation;

namespace Poly.DataModeling;

public sealed record DataType(string Name, IEnumerable<DataProperty> Properties, IEnumerable<Rule> Rules);