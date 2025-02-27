namespace Poly.Introspection;

public interface ITypeInfo
{
    public string Name { get; }
    public string FullName { get; }
    public IEnumerable<IMemberInfo> Fields { get; }
    public IEnumerable<IMemberInfo> Properties { get; }
    public IEnumerable<IMethodInfo> Constructors { get; }
    public IEnumerable<IMethodInfo> Methods { get; }
    public IEnumerable<Attribute> Attributes { get; }
}
