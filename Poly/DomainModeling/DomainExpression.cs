namespace Poly.DomainModeling;

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

    public static DomainExpression Add(DomainExpression left, DomainExpression right) =>
        new Add(left, right);

    public static DomainExpression Multiply(DomainExpression left, DomainExpression right) =>
        new Multiply(left, right);

    public static DomainExpression Divide(DomainExpression left, DomainExpression right) =>
        new Divide(left, right);

    public static DomainExpression DateOp(DomainExpression date, DomainExpression offset, DateOperationKind kind) =>
        new DateOperation(date, offset, kind);

    public static DomainExpression RelationshipNav(string relationshipName, DomainExpression targetProperty) =>
        new RelationshipNavigation(Guard.ThrowIfNullOrEmpty(relationshipName), targetProperty);

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

public sealed record Subtract(
    DomainExpression Left,
    DomainExpression Right
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Left, Right];
}

public sealed record Add(
    DomainExpression Left,
    DomainExpression Right
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Left, Right];
}

public sealed record Multiply(
    DomainExpression Left,
    DomainExpression Right
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Left, Right];
}

public sealed record Divide(
    DomainExpression Left,
    DomainExpression Right
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Left, Right];
}

public enum DateOperationKind { AddDays, AddMonths, DiffDays }

public sealed record DateOperation(
    DomainExpression Date,
    DomainExpression Offset,
    DateOperationKind Kind
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [Date, Offset];
}

public sealed record RelationshipNavigation(
    string RelationshipName,
    DomainExpression TargetProperty
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [TargetProperty];
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