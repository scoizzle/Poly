namespace Poly.DataModeling;

public sealed record DataModel(IEnumerable<DataType> Types, IEnumerable<Relationship> Relationships);