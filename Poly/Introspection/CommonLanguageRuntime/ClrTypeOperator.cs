namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// An operator of a CLR type: either one C# applies itself (no <see cref="Method"/>; its parameters need not be its
/// declaring type: a shift takes an Int32 count, string concatenation an Object), or a reflected static special-name
/// <c>op_*</c> member (<see cref="ClrOperatorNames"/>). Instances are immutable and safe for concurrent reads.
/// </summary>
[DebuggerDisplay("{ToString()}")]
internal sealed class ClrTypeOperator : ITypeOperator {
    private readonly IParameter[] _parameters;

    /// <summary>An operator the platform applies itself.</summary>
    public ClrTypeOperator(OperatorKind kind, ClrTypeDefinition declaring, ClrTypeDefinition result, IReadOnlyList<ClrTypeDefinition> parameters) {
        ArgumentNullException.ThrowIfNull(declaring);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Count != OperatorShape.ArityOf(kind))
            throw new ArgumentException($"Operator '{kind}' takes {OperatorShape.ArityOf(kind)} operands; {parameters.Count} were given.", nameof(parameters));
        Kind = kind;
        DeclaringTypeDefinition = declaring;
        ResultTypeDefinition = result;
        _parameters = parameters.Count == 1
            ? [new ClrOperatorParameter(0, "operand", parameters[0])]
            : [new ClrOperatorParameter(0, "left", parameters[0]), new ClrOperatorParameter(1, "right", parameters[1])];
    }

    /// <summary>An operator implemented by a reflected <c>op_*</c> member.</summary>
    public ClrTypeOperator(OperatorKind kind, ClrMethod method) {
        ArgumentNullException.ThrowIfNull(method);
        if (method.Parameters.Count() != OperatorShape.ArityOf(kind))
            throw new ArgumentException($"Operator '{kind}' takes {OperatorShape.ArityOf(kind)} operands; '{method.Name}' has {method.Parameters.Count()}.", nameof(method));
        Kind = kind;
        DeclaringTypeDefinition = method.DeclaringTypeDefinition;
        ResultTypeDefinition = method.MemberTypeDefinition;
        Method = method;
        _parameters = [.. method.Parameters];
    }

    public OperatorKind Kind { get; }
    public ClrTypeDefinition DeclaringTypeDefinition { get; }
    public ClrTypeDefinition ResultTypeDefinition { get; }
    public ClrMethod? Method { get; }
    public IEnumerable<IParameter> Parameters => _parameters;

    ITypeDefinition ITypeOperator.DeclaringTypeDefinition => DeclaringTypeDefinition;
    ITypeDefinition ITypeOperator.ResultTypeDefinition => ResultTypeDefinition;
    ITypeMethod? ITypeOperator.Method => Method;

    public override string ToString() =>
        $"{ResultTypeDefinition} {DeclaringTypeDefinition}.{Method?.Name ?? $"operator {Kind}"}({string.Join(", ", _parameters.Select(static p => p.ParameterTypeDefinition))})";
}

internal sealed class ClrOperatorParameter(int position, string name, ITypeDefinition type) : IParameter {
    public int Position { get; } = position;
    public string Name { get; } = name;
    public ITypeDefinition ParameterTypeDefinition { get; } = type;
    public bool IsOptional => false;
    public object? DefaultValue => null;
}