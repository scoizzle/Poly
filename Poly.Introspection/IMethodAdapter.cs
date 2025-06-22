namespace Poly.Introspection;

public interface IMethodAdapter
{
    public string Name { get; }
    public IEnumerable<IMethodParameterAdapter> Parameters { get; }
    public ITypeAdapter ReturnType { get; }
}
