namespace Poly.Introspection;

public interface ITypeInfoProvider
{
    public ITypeInfo GetTypeInfo(Type type);
    public ITypeInfo GetTypeInfo<T>();
    public ITypeInfo GetTypeInfo(string typeName);
}
