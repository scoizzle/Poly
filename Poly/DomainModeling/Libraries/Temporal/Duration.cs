namespace Poly.DomainModeling.Libraries.Temporal;

public enum DurationUnit { Days, Months }

/// <summary>
/// A parsed duration amount + unit (e.g. <c>12 days</c>), produced by the temporal
/// <see cref="DurationForm"/>. Resolves into a <see cref="DateOperation"/>
/// when combined with a clock date node (<c>Now - 12 days</c>) at parse time; a bare
/// <see cref="Duration"/> has no lowering (fail loud) and is rejected at analysis
/// when it lacks a temporal left operand.
/// </summary>
public sealed record Duration(
    long Amount,
    DurationUnit Unit
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [];
}