namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// An operator the .NET platform applies itself: static, with no member to invoke. Its parameters need not be its
/// declaring type (a shift takes an Int32 count, string concatenation an Object). Named <c>op_*</c> like a declared
/// operator; And and Or have no declared name, so theirs is <c>op_&lt;Kind&gt;</c>.
/// </summary>
internal sealed class ClrIntrinsicOperator : ITypeMethod {
    private readonly ClrTypeDefinition _declaring;
    private readonly ClrTypeDefinition _result;
    private readonly IParameter[] _parameters;

    public ClrIntrinsicOperator(OperatorKind kind, ClrTypeDefinition declaring, ClrTypeDefinition result, IReadOnlyList<ClrTypeDefinition> parameters) {
        ArgumentNullException.ThrowIfNull(declaring);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Count != OperatorShape.ArityOf(kind))
            throw new ArgumentException($"Operator '{kind}' takes {OperatorShape.ArityOf(kind)} operands; {parameters.Count} were given.", nameof(parameters));
        _declaring = declaring;
        _result = result;
        Name = ModeledOperators.NameOf(kind) ?? $"op_{kind}";
        _parameters = parameters.Count == 1
            ? [new ClrIntrinsicParameter(0, "operand", parameters[0])]
            : [new ClrIntrinsicParameter(0, "left", parameters[0]), new ClrIntrinsicParameter(1, "right", parameters[1])];
    }

    public string Name { get; }
    public ITypeDefinition MemberTypeDefinition => _result;
    public ITypeDefinition DeclaringTypeDefinition => _declaring;
    public IEnumerable<IParameter> Parameters => _parameters;
    public AccessModifier AccessModifier => AccessModifier.Public;
    public LifetimeModifier LifetimeModifier => LifetimeModifier.Static;
    public Mutability Mutability => Mutability.Mutable;

    public override string ToString() => $"{_result} {_declaring}.{Name}({string.Join(", ", _parameters.Select(static p => p.ParameterTypeDefinition))})";
}

internal sealed class ClrIntrinsicParameter(int position, string name, ITypeDefinition type) : IParameter {
    public int Position { get; } = position;
    public string Name { get; } = name;
    public ITypeDefinition ParameterTypeDefinition { get; } = type;
    public bool IsOptional => false;
    public object? DefaultValue => null;
}