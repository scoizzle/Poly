namespace Poly.Introspection;

public sealed class MethodInformation(
    string name,
    AccessModifiers accessModifiers,
    LifetimeModifiers lifetimeModifiers,
    Lazy<TypeInformation> returnType,
    IEnumerable<MethodParameterInformation> parameters,
    Lazy<TypeInformation>? declaringType = null)
{
    public string Name => name; 
    public AccessModifiers AccessModifiers => accessModifiers; 
    public LifetimeModifiers LifetimeModifiers => lifetimeModifiers; 
    public TypeInformation ReturnType => returnType.Value; 
    public IEnumerable<MethodParameterInformation> Parameters => parameters; 
    public TypeInformation? DeclaringType => declaringType?.Value;

    internal void ToStringBuilder(StringBuilder sb, int tabCount = 0) {
        Debug.Assert(sb != null, "StringBuilder cannot be null.");
        Debug.Assert(tabCount >= 0, "Tab count must be non-negative.");

        sb.Append('\t', tabCount);
        sb.AppendLine($"{AccessModifiers} {LifetimeModifiers} method {Name}(): {ReturnType.Name};");

        foreach (var parameter in Parameters) {
            parameter.ToStringBuilder(sb, tabCount + 1);
        }
    }
}