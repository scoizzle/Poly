namespace Poly.Introspection;

public interface ITypeDefinition {
    public string Name { get; }
    public string? Namespace { get; }
    public string FullName => Namespace != null ? $"{Namespace}.{Name}" : Name;
    public IEnumerable<ITypeMember> Members { get; }
    public Type ReflectedType { get; }

    public IEnumerable<ITypeMember> GetMembers(string name);
    public IEnumerable<ITypeMember> GetIndexers() => GetMembers("Item");
}