namespace Poly.Introspection.Core;

internal class ClrMethodParameterAdapter(
    string name,
    Lazy<ITypeAdapter> typeInfoFactory) : IMethodParameterAdapter
{
    public string Name { get; } = name;
    public ITypeAdapter Type { get; } = typeInfoFactory.Value;

    public sealed override string ToString() => $"{Type.Name} {Name}";
}
