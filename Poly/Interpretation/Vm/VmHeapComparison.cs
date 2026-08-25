namespace Poly.Interpretation.Vm;

/// <summary>
/// Value comparison for heap-resident operands (reference types and non-long
/// value types like DateOnly/DateTime/TimeOnly/TimeSpan/Guid).
/// The VM ABI represents these as heap handles; relational operators must
/// compare the boxed values, not the handles. Same-typed IComparable values
/// compare by value; nulls order before non-nulls; mixed runtime types fail
/// loud rather than comparing garbage.
/// </summary>
internal static class VmHeapComparison {
    public static int Compare(object? left, object? right) {
        if (left is null && right is null)
            return 0;
        if (left is null)
            return -1;
        if (right is null)
            return 1;
        if (left.GetType() != right.GetType())
            throw new InvalidOperationException(
                $"Cannot compare values of different runtime types: '{left.GetType().Name}' vs '{right.GetType().Name}'.");
        if (left is IComparable comparable)
            return comparable.CompareTo(right);
        throw new InvalidOperationException(
            $"Value of type '{left.GetType().Name}' does not support ordering comparisons.");
    }
}