namespace Poly.Runtime;

/// <summary>
/// The one kind→HTTP-status table for <see cref="DomainFailureKind"/>, #677 R-06.
/// A host maps a domain failure through this table; it never parses failure text, and it
/// never prints a second table. P-14 (<c>HttpFailureMapping</c>) is the intended consumer.
/// </summary>
/// <remarks>
/// Unique, Link and Conflict are 409; Cascade is 500; the other ten kinds are 400.
/// A kind added to the closed, append-only enum must gain an explicit arm here: an
/// out-of-range value throws, it never inherits "bad request" silently.
/// </remarks>
public static class DomainFailureStatus {
    /// <summary>
    /// The HTTP status for <paramref name="kind"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is not one of the closed <see cref="DomainFailureKind"/> members.
    /// </exception>
    public static int Http(DomainFailureKind kind) => kind switch {
        // An occupied to-one link only: "the source already has a target", today's only
        // 409 text. R-04 emits Link for that case and no other.
        DomainFailureKind.Link => 409,

        // Q2: a conflicting save returns 409. P-09 raises Conflict from a failed
        // DbUpdateConcurrencyException; there is no producer today.
        DomainFailureKind.Conflict => 409,

        // A duplicated unique key; the persisted unique index is the final authority.
        DomainFailureKind.Unique => 409,

        // Today a subscription-cascade depth overflow is an InvalidOperationException
        // (DomainToCSharpExporter.Notify.Dispatch.cs:88-95), so it surfaces as 500. A
        // runaway cycle is not client-correctable (orchestrator ruling O-4, vetoable).
        DomainFailureKind.Cascade => 500,

        // Q7: an omitted required value is a bad request.
        DomainFailureKind.Required => 400,

        // Stage and Policy are 400 provisionally and unruled; 409, 403 and 422 are open
        // alternatives for the owner (#687 risk 3).
        DomainFailureKind.Stage => 400,
        DomainFailureKind.Policy => 400,

        DomainFailureKind.Range => 400,
        DomainFailureKind.Length => 400,
        DomainFailureKind.Pattern => 400,
        DomainFailureKind.Enum => 400,
        DomainFailureKind.Arithmetic => 400,
        DomainFailureKind.Argument => 400,
        DomainFailureKind.Constraint => 400,

        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            $"No HTTP status is defined for domain failure kind '{kind}' ({(int)kind}); add an explicit arm to the table.")
    };
}