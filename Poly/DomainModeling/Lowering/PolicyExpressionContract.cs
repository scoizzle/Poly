namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Constrained contract for building policy guard expressions from structured
/// input (MCP tool args, UI forms). Prevents free-form AST construction by agents.
///
/// Supports only the shapes proven by Slice 2:
/// <list type="bullet">
///   <item>Property comparisons: <c>{"property": "Age", "op": ">=", "value": 18}</c></item>
///   <item>Composite: <c>{"and": [left, right]}</c>, <c>{"or": [left, right]}</c>, <c>{"not": operand}</c></item>
///   <item>Literal boolean/value: <c>{"literal": true}</c> (for always-true guards)</item>
/// </list>
/// </summary>
public sealed record PolicyExpressionContract {
    /// <summary>Property name for comparison expressions.</summary>
    public string? Property { get; init; }

    /// <summary>Comparison operator: "==", "!=", "&gt;", "&gt;=", "&lt;", "&lt;="</summary>
    public string? Op { get; init; }

    /// <summary>Literal value for comparison or standalone literal.</summary>
    public object? Value { get; init; }

    /// <summary>Composite AND: both sub-expressions must be satisfied.</summary>
    public PolicyExpressionContract[]? And { get; init; }

    /// <summary>Composite OR: at least one sub-expression must be satisfied.</summary>
    public PolicyExpressionContract[]? Or { get; init; }

    /// <summary>Composite NOT: the sub-expression must be false.</summary>
    public PolicyExpressionContract? Not { get; init; }

    /// <summary>Standalone literal expression (e.g. always-true guard).</summary>
    public object? Literal { get; init; }
}

/// <summary>
/// Pure function: <see cref="PolicyExpressionContract"/> → <see cref="DomainExpression"/>.
/// Throws <see cref="ArgumentException"/> for invalid contracts (missing fields, unknown ops).
/// </summary>
public static class PolicyExpressionParser {
    /// <summary>
    /// Converts a constrained contract to a <see cref="DomainExpression"/>.
    /// </summary>
    public static DomainExpression Parse(PolicyExpressionContract contract) {
        ArgumentNullException.ThrowIfNull(contract);
        return ParseCore(contract);
    }

    private static DomainExpression ParseCore(PolicyExpressionContract c) {
        // Count set branches to detect ambiguous/malformed contracts
        int branches = 0;
        if (c.And is not null) branches++;
        if (c.Or is not null) branches++;
        if (c.Not is not null) branches++;
        if (c.Property is not null) branches++;
        if (c.Literal is not null) branches++;

        if (branches == 0)
            throw new ArgumentException("Policy expression contract must specify at least one branch (property, and, or, not, or literal).");

        if (branches > 1)
            throw new ArgumentException("Policy expression contract must specify exactly one branch, not multiple (e.g. cannot set both 'property' and 'and').");

        // ── Property comparison ──────────────────────────────────
        if (c.Property is not null) {
            if (string.IsNullOrWhiteSpace(c.Property))
                throw new ArgumentException("Property name must not be empty.");

            if (string.IsNullOrWhiteSpace(c.Op))
                throw new ArgumentException($"Comparison operator is required for property '{c.Property}'.");

            var prop = DomainExpression.Property(c.Property);
            var value = DomainExpression.Literal(c.Value);

            return c.Op switch {
                "==" => DomainExpression.Equal(prop, value),
                "!=" => DomainExpression.NotEqual(prop, value),
                ">" => DomainExpression.GreaterThan(prop, value),
                ">=" => DomainExpression.GreaterThanOrEqual(prop, value),
                "<" => DomainExpression.LessThan(prop, value),
                "<=" => DomainExpression.LessThanOrEqual(prop, value),
                _ => throw new ArgumentException($"Unknown comparison operator '{c.Op}'. Supported: ==, !=, >, >=, <, <=")
            };
        }

        // ── Literal ──────────────────────────────────────────────
        if (c.Literal is not null) {
            return DomainExpression.Literal(c.Literal);
        }

        // ── Composite: AND ───────────────────────────────────────
        if (c.And is not null) {
            if (c.And.Length < 2)
                throw new ArgumentException("AND requires at least 2 operands.");
            var result = ParseCore(c.And[0]);
            for (int i = 1; i < c.And.Length; i++)
                result = DomainExpression.And(result, ParseCore(c.And[i]));
            return result;
        }

        // ── Composite: OR ────────────────────────────────────────
        if (c.Or is not null) {
            if (c.Or.Length < 2)
                throw new ArgumentException("OR requires at least 2 operands.");
            var result = ParseCore(c.Or[0]);
            for (int i = 1; i < c.Or.Length; i++)
                result = DomainExpression.Or(result, ParseCore(c.Or[i]));
            return result;
        }

        // ── Composite: NOT ───────────────────────────────────────
        if (c.Not is not null) {
            return DomainExpression.Not(ParseCore(c.Not));
        }

        // Unreachable given the branches check above
        throw new InvalidOperationException("Unexpected: no branch matched.");
    }
}