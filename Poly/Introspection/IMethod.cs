namespace Poly.Introspection;

public interface IMethod {
    public string Name { get; }
    public ITypeDefinition ReturnType { get; }
    public IEnumerable<IParameter> Parameters { get; }
}