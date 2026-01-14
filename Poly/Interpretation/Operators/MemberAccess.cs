using Poly.Introspection;

namespace Poly.Interpretation.Operators;

/// <summary>
/// Represents a member access operation (property, field, or method access) in an interpretation tree.
/// </summary>
/// <remarks>
/// This operator enables accessing members of a value using dot notation (e.g., <c>person.Name</c>).
/// The member is resolved at interpretation time using the type definition system.
/// </remarks>
public sealed class MemberAccess(Value value, string memberName) : Operator {
    private ITypeMember? _cachedMember;
    /// <summary>
    /// Gets the value whose member is being accessed.
    /// </summary>
    public Value Value { get; } = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>
    /// Gets the name of the member to access.
    /// </summary>
    public string MemberName { get; } = memberName ?? throw new ArgumentNullException(nameof(memberName));

    /// <summary>
    /// Gets the type member from the value's type definition.
    /// </summary>
    /// <param name="context">The interpretation context.</param>
    /// <returns>The member metadata.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the member is not found on the type.</exception>
    private ITypeMember GetMember(InterpretationContext context) {
        if (_cachedMember is not null)
            return _cachedMember;

        var typeDefinition = Value.GetTypeDefinition(context);
        var members = typeDefinition.Members.Where(e => e.Name == MemberName);

        var memberEnumerator = members.GetEnumerator();
        if (!memberEnumerator.MoveNext())
            throw new InvalidOperationException($"Member '{MemberName}' not found on type '{typeDefinition.Name}'.");

        var firstMember = memberEnumerator.Current;
        if (memberEnumerator.MoveNext())
            throw new InvalidOperationException($"Ambiguous member access: multiple members named '{MemberName}' found on type '{typeDefinition.Name}'.");
            
        return _cachedMember = firstMember;
    }

    /// <inheritdoc />
    public override ITypeDefinition GetTypeDefinition(InterpretationContext context) {
        return GetMember(context).MemberTypeDefinition;
    }

    /// <inheritdoc />
    public override Expression BuildExpression(InterpretationContext context) {
        ITypeMember member = GetMember(context);
        Value memberAccessor = member.GetMemberAccessor(Value);
        return memberAccessor.BuildExpression(context);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Value}.{MemberName}";
}