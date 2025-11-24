namespace Poly.DataModeling.Mutations;

public abstract record ValueSource;

public sealed record ConstantValue(object? Value) : ValueSource;

public sealed record ParameterValue(string Name) : ValueSource;
