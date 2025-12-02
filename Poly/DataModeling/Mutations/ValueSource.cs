namespace Poly.DataModeling.Mutations;

public abstract record ValueSource;

public sealed record ConstantValue(object? Value) : ValueSource;

public sealed record ParameterValue(DataPropertyPath Path) : ValueSource;

public sealed record PropertyValue(DataPropertyPath Path) : ValueSource;