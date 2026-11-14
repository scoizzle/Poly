using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Libraries.Temporal;

public enum DurationUnit { Milliseconds, Seconds, Minutes, Hours, Days, Weeks, Months, Years }

/// <summary>
/// A parsed duration amount + unit (e.g. <c>12 days</c>), produced by the temporal
/// <see cref="DurationForm"/>. Resolves into a <see cref="DateOperation"/>
/// when combined with a clock date node (<c>Now - 12 days</c>) at parse time. A bare
/// <see cref="Duration"/> is a value only where a <c>Duration</c> takes it: the default of a
/// <c>Duration</c> property, an assign to one, or an operand compared with one; it lowers to its
/// <c>TimeSpan</c> there (months/years have no fixed length and are rejected). Anywhere else it is
/// rejected at analysis for lacking a temporal left operand.
/// </summary>
public sealed record Duration(
    long Amount,
    DurationUnit Unit
) : DomainExpression {
    public sealed override IEnumerable<Node?> Children => [];
}