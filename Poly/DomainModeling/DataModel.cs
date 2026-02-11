namespace Poly.DomainModeling;

public sealed record DataModel(IEnumerable<DataType> Types, IEnumerable<Relationship> Relationships);