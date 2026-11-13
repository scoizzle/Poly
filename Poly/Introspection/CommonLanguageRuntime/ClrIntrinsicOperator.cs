namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// An operator the .NET platform applies itself: static, taking the owner for every operand, with no
/// member to invoke. Named <c>op_*</c> like a declared operator; And and Or have no declared name, so
/// theirs is <c>op_&lt;Kind&gt;</c>.
/// </summary>
internal sealed class ClrIntrinsicOperator : ITypeMethod {
    private readonly ClrTypeDefinition _owner;
    private readonly ClrTypeDefinition _result;
    private readonly IParameter[] _parameters;

    public ClrIntrinsicOperator(OperatorKind kind, ClrTypeDefinition owner, ClrTypeDefinition result) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(result);
        _owner = owner;
        _result = result;
        Name = ModeledOperators.NameOf(kind) ?? $"op_{kind}";
        _parameters = OperatorShape.ArityOf(kind) == 1
            ? [new ClrIntrinsicParameter(0, "operand", owner)]
            : [new ClrIntrinsicParameter(0, "left", owner), new ClrIntrinsicParameter(1, "right", owner)];
    }

    public string Name { get; }
    public ITypeDefinition MemberTypeDefinition => _result;
    public ITypeDefinition DeclaringTypeDefinition => _owner;
    public IEnumerable<IParameter> Parameters => _parameters;
    public AccessModifier AccessModifier => AccessModifier.Public;
    public LifetimeModifier LifetimeModifier => LifetimeModifier.Static;
    public Mutability Mutability => Mutability.Mutable;

    public override string ToString() => $"{_result} {_owner}.{Name}({string.Join(", ", _parameters.Select(static p => p.ParameterTypeDefinition))})";
}

internal sealed class ClrIntrinsicParameter(int position, string name, ITypeDefinition type) : IParameter {
    public int Position { get; } = position;
    public string Name { get; } = name;
    public ITypeDefinition ParameterTypeDefinition { get; } = type;
    public bool IsOptional => false;
    public object? DefaultValue => null;
}