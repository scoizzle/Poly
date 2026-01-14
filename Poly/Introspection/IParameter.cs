namespace Poly.Introspection;

public interface IParameter {
    int Position { get; }
    string Name { get; }
    ITypeDefinition ParameterTypeDefinition { get; }
    bool IsOptional { get; }
    object? DefaultValue { get; }
}