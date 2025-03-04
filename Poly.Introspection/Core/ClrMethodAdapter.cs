using System.Text;

namespace Poly.Introspection.Core;

internal class ClrMethodAdapter(
    string name,
    Lazy<IEnumerable<IMethodParameterAdapter>> parameters,
    Lazy<ITypeAdapter> returnTypeFactory,
    Lazy<IEnumerable<Attribute>> attributesFactory) : IMethodAdapter
{
    public string Name => name;
    public IEnumerable<IMethodParameterAdapter> Parameters => parameters.Value;
    public IEnumerable<Attribute> Attributes => attributesFactory.Value;
    public ITypeAdapter ReturnType => returnTypeFactory.Value;

    public sealed override string ToString()
    {
        StringBuilder sb = new();
        sb.Append($"{ReturnType.Name} {Name}(");
        sb.AppendJoin(", ", Parameters);
        sb.Append(")");
        return sb.ToString();
    }
}

