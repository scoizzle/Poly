namespace Poly.Reflection;

public interface IMethodAdapter
{
    public string Name { get; }

    public ITypeAdapter ReturnTypeInterface { get; }

    public IParameterAdapter[] Parameters { get; }
}
