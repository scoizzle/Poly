namespace Poly.Introspection;

public interface ITypeAdapter
{
    public string Name { get; }
    public string FullName { get; }
    public IEnumerable<ITypeMemberAdapter> Fields { get; }
    public IEnumerable<ITypeMemberAdapter> Properties { get; }
    public IEnumerable<IMethodAdapter> Constructors { get; }
    public IEnumerable<IMethodAdapter> Methods { get; }
    public IEnumerable<Attribute> Attributes { get; }
}
