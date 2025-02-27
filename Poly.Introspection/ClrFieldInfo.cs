using System.Text;

namespace Poly.Introspection;

sealed class ClrFieldInfo(
    string name,
    ClrAccessModifier accessModifier,
    ClrLifetimeModifier lifetimeModifier,
    Lazy<ITypeInfo> typeInfoFactory) : IMemberInfo
{
    public string Name => name;
    public ITypeInfo Type => typeInfoFactory.Value;
    public ClrAccessModifier AccessModifier => accessModifier;
    public ClrLifetimeModifier LifetimeModifier => lifetimeModifier;

    public sealed override string ToString()
    {
        StringBuilder sb = new();
        sb.Append(AccessModifier.ToString().ToLowerInvariant());
        sb.Append(' ');
        sb.Append(LifetimeModifier.ToString().ToLowerInvariant());
        sb.Append(' ');
        sb.Append(Type.Name);
        sb.Append(' ');
        sb.Append(Name);
        return sb.ToString();
    }
}
