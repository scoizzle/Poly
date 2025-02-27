namespace Poly.Introspection;

internal class ClrMethodParameterInfo(
    string name,
    Lazy<ITypeInfo> typeInfoFactory) : IMethodParameterInfo
{
    public string Name { get; } = name;
    public ITypeInfo Type { get; } = typeInfoFactory.Value;

    public sealed override string ToString() => $"{Type.Name} {Name}";
}
