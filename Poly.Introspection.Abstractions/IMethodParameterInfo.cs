namespace Poly.Introspection;

public interface IMethodParameterInfo
{
    public string Name { get; }
    public ITypeInfo Type { get; }
}