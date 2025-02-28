using System.Text;

namespace Poly.Introspection.Core;

internal class ClrMethodInfo(
    string name,
    Lazy<IEnumerable<IMethodParameterInfo>> parameters,
    Lazy<ITypeInfo> returnTypeFactory,
    Lazy<IEnumerable<Attribute>> attributesFactory) : IMethodInfo
{
    public string Name => name;
    public IEnumerable<IMethodParameterInfo> Parameters => parameters.Value;
    public IEnumerable<Attribute> Attributes => attributesFactory.Value;
    public ITypeInfo ReturnType => returnTypeFactory.Value;

    public sealed override string ToString()
    {
        StringBuilder sb = new();
        sb.Append($"{ReturnType.Name} {Name}(");
        sb.AppendJoin(", ", Parameters);
        sb.Append(")");
        return sb.ToString();
    }
}

