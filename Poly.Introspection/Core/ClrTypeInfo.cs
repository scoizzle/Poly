using System.Text;

namespace Poly.Introspection.Core;

sealed class ClrTypeInfo(
    Type type,
    ClrAccessModifier accessModifiers,
    Lazy<IEnumerable<IMemberInfo>> fields,
    Lazy<IEnumerable<IMemberInfo>> properties,
    Lazy<IEnumerable<IMethodInfo>> constructors,
    Lazy<IEnumerable<IMethodInfo>> methods,
    Lazy<IEnumerable<Attribute>> attributes) : ITypeInfo
{
    public string Name { get; } = GetNameString(type);
    public string FullName { get; } = type.FullName ?? type.Name;
    public Type Type => type;
    public ClrAccessModifier AccessModifiers => accessModifiers;
    public IEnumerable<IMemberInfo> Fields => fields.Value;
    public IEnumerable<IMemberInfo> Properties => properties.Value;
    public IEnumerable<IMethodInfo> Constructors => constructors.Value;
    public IEnumerable<IMethodInfo> Methods => methods.Value;
    public IEnumerable<Attribute> Attributes => attributes.Value;

    static string GetNameString(Type type)
    {
        if (type.IsGenericType)
        {
            StringBuilder sb = new();
            sb.Append(type.Name.Substring(0, type.Name.IndexOf('`')));
            sb.Append('<');
            sb.AppendJoin(", ", type.GetGenericArguments().Select(GetNameString));
            sb.Append('>');
            return sb.ToString();
        }
        if (type.IsArray)
        {
            return GetNameString(type.GetElementType()) + "[]";
        }
        return type.Name;
    }

    public sealed override string ToString()
    {
        StringBuilder sb = new();
        sb.AppendLine($"namespace {type.Namespace};");
        string typeName = type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : "class";
        sb.AppendLine($"{accessModifiers.ToString().ToLowerInvariant()} {typeName} {Name}");
        sb.AppendLine("{");
        foreach (var field in Fields)
        {
            sb.AppendLine($"    {field};");
        }
        foreach (var constructor in Constructors)
        {
            sb.AppendLine($"    {constructor};");
        }
        foreach (var property in Properties)
        {
            sb.AppendLine($"    {property}");
        }
        foreach (var method in Methods)
        {
            sb.AppendLine($"    {method};");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }
}
