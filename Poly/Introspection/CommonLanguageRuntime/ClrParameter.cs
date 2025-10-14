
namespace Poly.Introspection.CommonLanguageRuntime;

public class ClrParameter(string name, Lazy<ClrTypeDefinition> type, int position, bool isOptional, object? defaultValue) : IParameter {
    private readonly Lazy<ClrTypeDefinition> _type = type;
    public int Position { get; } = position;
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
    public ClrTypeDefinition Type => _type.Value;
    public bool IsOptional { get; } = isOptional;
    public object? DefaultValue { get; } = defaultValue;

    ITypeDefinition IParameter.ParameterType => Type;
}
