using System.Text;

namespace Poly.Introspection;

public sealed class TypeInformation(
    string name,
    string namespace_,
    string globallyUniqueName,
    Lazy<IEnumerable<FieldInformation>> fields,
    Lazy<IEnumerable<PropertyInformation>> properties,
    Lazy<IEnumerable<MethodInformation>> methods
) {
    public string Name => name;
    public string Namespace => namespace_;
    public string GloballyUniqueName => globallyUniqueName;
    public IEnumerable<FieldInformation> Fields => fields.Value;
    public IEnumerable<PropertyInformation> Properties => properties.Value;
    public IEnumerable<MethodInformation> Methods => methods.Value;

    public override string ToString()
    {
        StringBuilder sb = new();
        ToStringBuilder(sb);
        return sb.ToString();
    }

    internal void ToStringBuilder(StringBuilder sb, int tabCount = 0)
    {
        Debug.Assert(sb != null, "StringBuilder cannot be null.");
        Debug.Assert(tabCount >= 0, "Tab count must be non-negative.");

        sb.AppendLine($"namespace {Namespace};");
        // string typeName = type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : "class";
        // sb.AppendLine($"{accessModifiers.ToString().ToLowerInvariant()} {typeName} {Name}");
        sb.AppendLine("{");
        // foreach (var constructor in Constructors)
        // {
        //     sb.AppendLine($"    {constructor};");
        // }
        foreach (var field in Fields)
        {
            field.ToStringBuilder(sb, tabCount + 1);
        }
        foreach (var prop in Properties)
        {
            prop.ToStringBuilder(sb, tabCount + 1);
        }
        foreach (var method in Methods)
        {
            method.ToStringBuilder(sb, tabCount + 1);
        }
        sb.AppendLine("}");
    }
}
