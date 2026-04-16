namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// Introspection-only property member synthesized by the CLR type system abstraction when the
/// runtime type does not expose a corresponding <see cref="System.Reflection.PropertyInfo"/>.
/// </summary>
[DebuggerDisplay("{MemberType} {DeclaringType}.{Name}")]
internal sealed class ClrTypeSyntheticProperty : ClrPropertyMember {
    private readonly Lazy<ClrTypeDefinition> _memberType;
    private readonly ClrTypeDefinition _declaringType;
    private readonly ClrParameter[] _parameters;
    private readonly string _name;
    private readonly bool _isStatic;

    public ClrTypeSyntheticProperty(Lazy<ClrTypeDefinition> memberType, ClrTypeDefinition declaringType, IEnumerable<ClrParameter> parameters, string name, bool isStatic) {
        ArgumentNullException.ThrowIfNull(memberType);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _memberType = memberType;
        _declaringType = declaringType;
        _parameters = parameters.ToArray();
        _name = name;
        _isStatic = isStatic;
    }

    public override ClrTypeDefinition MemberTypeDefinition => _memberType.Value;
    public override ClrTypeDefinition DeclaringTypeDefinition => _declaringType;
    public override IEnumerable<ClrParameter>? Parameters => _parameters;
    public override string Name => _name;
    public override bool IsStatic => _isStatic;

    public override string ToString() => $"{MemberTypeDefinition} {DeclaringTypeDefinition}.{Name}[{string.Join(", ", _parameters)}]";
}