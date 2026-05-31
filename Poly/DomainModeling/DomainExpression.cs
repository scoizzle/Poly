namespace Poly.DomainModeling;

/// <summary>
/// Unified expression system for V3 domain modeling.
/// 
/// <see cref="DomainExpression"/> is the single expression model used across policies, stage guards,
/// event property bindings, and effect initializers. It supports property access, parameter references,
/// navigation into owned structures, existence checks, basic arithmetic, and logical composition.
/// 
/// The expression nodes themselves are intentionally lightweight. All resolution, type derivation,
/// scope validation, data availability analysis, and lowering metadata are the responsibility of analyzers.
/// </summary>
public abstract record DomainExpression : DomainObject {
    // Factory helpers for ergonomic construction (builders and tests)

    public static DomainExpression Property(string name) =>
        new PropertyAccess(Guard.ThrowIfNullOrEmpty(name));

    public static DomainExpression Parameter(string name) =>
        new ParameterAccess(Guard.ThrowIfNullOrEmpty(name));

    public static DomainExpression Literal(object? value, DomainTypeReference? typeHint = null) =>
        new Literal(value, typeHint);

    public static DomainExpression Owned(string ownedName, DomainExpression inner) =>
        new OwnedAccess(Guard.ThrowIfNullOrEmpty(ownedName), inner);

    public static DomainExpression Exists(DomainExpression target) =>
        new Exists(target);

    public static DomainExpression NotExists(DomainExpression target) =>
        new NotExists(target);

    public static DomainExpression Subtract(DomainExpression left, DomainExpression right) =>
        new Subtract(left, right);

    // Logical composition (so policies and guards can be expressed directly with DomainExpression)
    public static DomainExpression And(DomainExpression left, DomainExpression right) =>
        new And(left, right);

    public static DomainExpression Or(DomainExpression left, DomainExpression right) =>
        new Or(left, right);

    public static DomainExpression Not(DomainExpression operand) =>
        new Not(operand);
}

// === Concrete nodes (deliberately small set) ===

/// <summary>
/// References a property on the current subject entity.
/// </summary>
public sealed record PropertyAccess(string Name) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [];
}

/// <summary>
/// References a parameter passed to the current action.
/// </summary>
public sealed record ParameterAccess(string Name) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [];
}

/// <summary>
/// A literal constant value. An optional <see cref="TypeHint"/> can be provided to guide analyzers.
/// </summary>
public sealed record Literal(
    object? Value,
    DomainTypeReference? TypeHint = null
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => TypeHint is not null ? [TypeHint] : [];
}

/// <summary>
/// Navigates into an owned structure (document / composite value) on the current entity.
/// </summary>
public sealed record OwnedAccess(
    string OwnedName,
    DomainExpression Inner
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Inner];
}

/// <summary>
/// Checks whether the target expression resolves to an existing value (commonly used for owned documents).
/// </summary>
public sealed record Exists(DomainExpression Target) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Target];
}

/// <summary>
/// Checks whether the target expression does not resolve to an existing value.
/// </summary>
public sealed record NotExists(DomainExpression Target) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Target];
}

// Minimal operator set — expanded only when real modeling needs appear
public sealed record Subtract(
    DomainExpression Left,
    DomainExpression Right
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Left, Right];
}

// Logical composition nodes

/// <summary>
/// Logical conjunction (AND) of two expressions.
/// </summary>
public sealed record And(
    DomainExpression Left,
    DomainExpression Right
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Left, Right];
}

/// <summary>
/// Logical disjunction (OR) of two expressions.
/// </summary>
public sealed record Or(
    DomainExpression Left,
    DomainExpression Right
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Left, Right];
}

/// <summary>
/// Logical negation (NOT) of an expression.
/// </summary>
public sealed record Not(DomainExpression Operand) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Operand];
}