namespace Poly.Introspection;

public sealed class PropertyInformation(
    string name,
    AccessModifiers accessModifiers,
    LifetimeModifiers lifetimeModifiers,
    Lazy<TypeInformation> type,
    Lazy<TypeInformation>? declaringType = null)
{
    public string Name => name;
    public AccessModifiers AccessModifiers => accessModifiers;
    public LifetimeModifiers LifetimeModifiers => lifetimeModifiers;
    public TypeInformation Type => type.Value;
    public TypeInformation? DeclaringType => declaringType?.Value;

    internal void ToStringBuilder(StringBuilder sb, int tabCount = 0)
    {
        Debug.Assert(sb != null, "StringBuilder cannot be null.");
        Debug.Assert(tabCount >= 0, "Tab count must be non-negative.");

        sb.Append('\t', tabCount);
        sb.AppendLine($"{AccessModifiers} {LifetimeModifiers} property {Name}: {Type.Name};");
    }
}
