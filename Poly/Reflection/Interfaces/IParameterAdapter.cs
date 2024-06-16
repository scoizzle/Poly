namespace Poly.Reflection;

public interface IParameterAdapter
{
    string Name { get; }

    int Position { get; }

    ITypeAdapter ParameterTypeInterface { get; }

    object? DefaultValueObject { get; }
}