namespace Poly.Reflection;

public interface IParameterAdapter
{
    public string Name { get; }

    public int Position { get; }

    public ITypeAdapter ParameterTypeInterface { get; }

    public object? DefaultValueObject { get; }
}