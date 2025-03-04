namespace Poly.Introspection;

public interface IMethodParameterAdapter
{
    public string Name { get; }
    public ITypeAdapter Type { get; }
}