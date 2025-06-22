namespace Poly.Introspection;

public interface ITypeAdapterProvider
{
    public ITypeAdapter GetTypeInfo(Type type);
    public ITypeAdapter GetTypeInfo<T>();
    public ITypeAdapter GetTypeInfo(string typeName);
}
