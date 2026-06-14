using Poly.Introspection;

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

    private readonly MemberReadDelegate? _read;
    private readonly MemberWriteDelegate? _write;
    private readonly MemberWriteDelegate? _initialize;

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

        var arrayRank = parameters?.Count() ?? 0;
        _read = (owner, arguments) => {
            if (owner is not Array array) {
                return null;
            }
            var indexes = ConvertToArrayIndexes(arguments, arrayRank);
            return array.GetValue(indexes);
        };

        _write = (owner, value, arguments) => {
            if (owner is not Array array) {
                throw new InvalidOperationException($"Synthetic indexer '{name}' can only write array owners.");
            }
            var indexes = ConvertToArrayIndexes(arguments, arrayRank);
            array.SetValue(value, indexes);
            return owner;
        };
        _initialize = null;
    }

    public override ClrTypeDefinition MemberTypeDefinition => _memberType.Value;
    public override ClrTypeDefinition DeclaringTypeDefinition => _declaringType;
    public override IEnumerable<ClrParameter> Parameters => _parameters;
    public override string Name => _name;

    public override MemberReadDelegate? Read => _read;
    public override MemberWriteDelegate? Write => _write;
    public override MemberWriteDelegate? Initialize => _initialize;

    public override AccessModifier AccessModifier => AccessModifier.Public;
    public override LifetimeModifier LifetimeModifier => _isStatic ? LifetimeModifier.Static : LifetimeModifier.Instance;

    public override Mutability Mutability => Mutability.Mutable; // synthetic indexers are mutable / no special semantics

    public override string ToString() => $"{MemberTypeDefinition} {DeclaringTypeDefinition}.{Name}[{string.Join(", ", _parameters)}]";

    private static int[] ConvertToArrayIndexes(object?[]? arguments, int expectedRank) {
        if (expectedRank <= 0) {
            return [];
        }

        if (arguments is null || arguments.Length != expectedRank) {
            throw new InvalidOperationException($"Expected {expectedRank} index arguments but received {arguments?.Length ?? 0}.");
        }

        var indexes = new int[expectedRank];
        for (var i = 0; i < expectedRank; i++) {
            if (arguments[i] is not int index) {
                throw new InvalidOperationException($"Array index at position {i} is not an Int32 value.");
            }

            indexes[i] = index;
        }

        return indexes;
    }
}