using Poly.Interpretation;

namespace Poly.Introspection;

public interface IMethod {
    public string Name { get; }
    public ITypeDefinition ReturnTypeDefinition { get; }
    public IEnumerable<IParameter> Parameters { get; }

    public Value GetMethodInvocation(Value target, params IEnumerable<Value> arguments);
}