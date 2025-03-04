using System.Diagnostics;
using System.Text;

namespace Poly.Introspection.Core;

sealed class ClrFieldAdapter(
    string name,
    ClrAccessModifier accessModifier,
    ClrLifetimeModifier lifetimeModifier,
    Lazy<ITypeAdapter> typeInfoFactory) : ITypeMemberAdapter
{
    public string Name => name;
    public ITypeAdapter Type => typeInfoFactory.Value;
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