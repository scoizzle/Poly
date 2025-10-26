namespace Poly.Introspection;

public interface IParameter {
    public int Position { get; }
    public string Name { get; }
    public ITypeDefinition ParameterTypeDefinition { get; }
    public bool IsOptional { get; }
    public object? DefaultValue { get; }
}