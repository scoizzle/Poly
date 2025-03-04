using System.Text;

namespace Poly.Introspection.Core;

sealed class ClrPropertyAdapter(
    string name,
    ClrAccessModifier accessModifiers,
    ClrLifetimeModifier lifetimeModifiers,
    bool isReadable,
    bool isWritable,
    Lazy<ITypeAdapter> typeInfoFactory) : ITypeMemberAdapter
{
    public string Name => name;
    public bool IsReadable => isReadable;
    public bool IsWritable => isWritable;
    public ITypeAdapter Type => typeInfoFactory.Value;
    public ClrAccessModifier AccessModifiers => accessModifiers;
    public ClrLifetimeModifier LifetimeModifiers => lifetimeModifiers;

    public sealed override string ToString()
    {
        StringBuilder sb = new();
        if (AccessModifiers != ClrAccessModifier.None)
        {
            sb.Append(AccessModifiers.ToString().ToLowerInvariant());
            sb.Append(' ');
        }
        if (LifetimeModifiers != ClrLifetimeModifier.Instance)
        {
            sb.Append(LifetimeModifiers.ToString().ToLowerInvariant());
            sb.Append(' ');
        }
        sb.Append(Type.Name);
        sb.Append(' ');
        sb.Append(Name);
        sb.Append(" {");
        if (IsReadable)
            sb.Append(" get; ");
        if (IsWritable)
            sb.Append(" set; ");
        sb.Append('}');
        return sb.ToString();
    }
}
