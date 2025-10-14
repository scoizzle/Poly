namespace Poly.Introspection;

public interface IParameter {
    public int Position { get; }
    public string Name { get; }
    public ITypeDefinition ParameterType { get; }
    public bool IsOptional { get; }
    public object? DefaultValue { get; }
}