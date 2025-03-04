namespace Poly.Introspection;

public interface ITypeMemberAdapter
{
    public string Name { get; }
    public ITypeAdapter Type { get; }
}
