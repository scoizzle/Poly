namespace Poly.Introspection.CommonLanguageRuntime;

public class ClrMethod(string name, ClrTypeDefinition declaringType, Lazy<ClrTypeDefinition> returnType, IEnumerable<ClrParameter> parameters) : IMethod {
    private readonly Lazy<ClrTypeDefinition> _returnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
    private readonly ClrParameter[] _parameters = parameters?.ToArray() ?? throw new ArgumentNullException(nameof(parameters));

    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
    public ClrTypeDefinition DeclaringType { get; } = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
    public ClrTypeDefinition ReturnType => _returnType.Value;
    public IEnumerable<IParameter> Parameters => _parameters;
    ITypeDefinition IMethod.ReturnType => ReturnType;
    IEnumerable<IParameter> IMethod.Parameters => Parameters;
}