namespace Poly.Reflection;

public interface IMethodAdapter
{
    string Name { get; }

    ITypeAdapter ReturnTypeInterface { get; }

    IParameterAdapter[] Parameters { get; }
}
