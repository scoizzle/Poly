namespace Poly.Introspection;

public interface ITypeDefinition {
    public string Name { get; }
    public string? Namespace { get; }
    public string FullName => Namespace != null ? $"{Namespace}.{Name}" : Name;
    public IEnumerable<ITypeMember> Members { get; }
    public IEnumerable<IMethod> Methods { get; }
    public Type ReflectedType { get; }

    public ITypeMember? GetMember(string name);
}