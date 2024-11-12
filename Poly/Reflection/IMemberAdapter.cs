namespace Poly.Reflection;

public interface IMemberAdapter
{
    public StringView Name { get; }

    public ITypeAdapter TypeInterface { get; }

    public bool TryGetValue(object instance, out object? value) { value = default; return false; }

    public bool TrySetValue(object instance, object? value) { return false; }
}