namespace Poly.Introspection;

public interface IMethodInfo
{
    public string Name { get; }
    public IEnumerable<IMethodParameterInfo> Parameters { get; }
    public IEnumerable<Attribute> Attributes { get; }
    public ITypeInfo ReturnType { get; }
}
