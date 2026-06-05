namespace Poly.Syntax.Nodes;

/// <summary>
/// AST node representing a field definition on a type.
/// </summary>
/// <param name="Name">The field name.</param>
/// <param name="FieldType">The type of the field.</param>
/// <param name="DefaultValue">Optional default value expression.</param>
/// <param name="IsStatic">Whether this is a static field.</param>
/// <param name="IsReadOnly">Whether this is a readonly field.</param>
/// <param name="IsVolatile">Whether this field has volatile semantics (un-knowable impact).</param>
/// <param name="IsConst">Whether this is a compile-time const (literal value, safe for folding; implies readonly).
/// For CLR-backed fields: IsLiteral (from FieldInfo) can never be true if IsConst is false (i.e. IsLiteral implies IsConst).
/// </param>
public sealed record FieldDefinitionNode(
    string Name,
    Node FieldType,
    Node? DefaultValue = null,
    bool IsStatic = false,
    bool IsReadOnly = false,
    bool IsVolatile = false,
    bool IsConst = false,
    AccessModifier AccessModifier = AccessModifier.Public
) : MemberDefinitionNode(Name, FieldType, IsStatic, AccessModifier) {
    // Enforce: IsConst implies IsReadOnly (as documented). IsLiteral (CLR) implies IsConst.
    public bool IsReadOnly { get; } = IsReadOnly || IsConst;
    public bool IsConst { get; } = IsConst;

    /// <summary>
    /// The unified mutability semantics for this field, derived from the individual flags.
    /// This is the first-class concept exposed on ITypeMember.
    /// </summary>
    public Mutability Mutability {
        get {
            var m = Mutability.Mutable;
            if (IsReadOnly) m |= Mutability.ReadOnlyAfterInit;
            if (IsConst) m |= Mutability.CompileTimeConst;
            if (IsVolatile) m |= Mutability.VolatileAccess;
            return m;
        }
    }

    public override IEnumerable<Node?> Children {
        get {
            yield return FieldType;
            yield return DefaultValue;
        }
    }

    public override string ToString() {
        var suffix = DefaultValue != null ? $" = {DefaultValue}" : "";
        var staticPrefix = IsStatic ? "static " : "";
        var constPrefix = IsConst ? "const " : "";
        var readonlyPrefix = (IsReadOnly && !IsConst) ? "readonly " : "";
        var volatilePrefix = IsVolatile ? "volatile " : "";
        return $"{staticPrefix}{constPrefix}{readonlyPrefix}{volatilePrefix}{FieldType} {Name}{suffix}";
    }
}