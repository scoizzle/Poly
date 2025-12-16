namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Reflection-backed method or indexer parameter description used by CLR member types.
/// Evaluates its parameter type lazily via the owning registry.
/// </summary>
public class ClrParameter(string name, Lazy<ClrTypeDefinition> type, int position, bool isOptional, object? defaultValue) : IParameter {
    private readonly Lazy<ClrTypeDefinition> _type = type;
    /// <summary>
    /// Gets the zero-based position of the parameter.
    /// </summary>
    public int Position { get; } = position;
    /// <summary>
    /// Gets the parameter name.
    /// </summary>
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
    /// <summary>
    /// Gets the parameter's type definition.
    /// </summary>
    public ClrTypeDefinition ParameterTypeDefinition => _type.Value;
    /// <summary>
    /// Gets whether the parameter is optional.
    /// </summary>
    public bool IsOptional { get; } = isOptional;
    /// <summary>
    /// Gets the default value if optional, otherwise null.
    /// </summary>
    public object? DefaultValue { get; } = defaultValue;

    ITypeDefinition IParameter.ParameterTypeDefinition => ParameterTypeDefinition;
}