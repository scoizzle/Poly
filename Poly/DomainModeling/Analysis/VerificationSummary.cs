using Poly.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The three-way check verdict (V-04): a model is <see cref="Proven"/> only when every write was
/// decided inside its constraint and nothing else reported an error, <see cref="Violated"/> when
/// something is proven outside, and <see cref="Unknown"/> when a write is not decided — never
/// "proven" while a write is unverified.
/// </summary>
public enum CheckVerdict {
    /// <summary>Every write is decided inside its constraint and no error is in scope.</summary>
    Proven,

    /// <summary>At least one write could not be decided, so the model is not proven.</summary>
    Unknown,

    /// <summary>A write is proven outside its constraint, or an error is in scope.</summary>
    Violated,
}

/// <summary>
/// One write the summary could not prove inside its constraint, projected from a finding and
/// enriched from the recorded fact on the same node (A2). <see cref="Path"/> is the finding's
/// element path; <see cref="Property"/>, <see cref="Constraint"/> and <see cref="Reason"/> are
/// null when no fact is attached to that node.
/// </summary>
public sealed record UnverifiedWriteFact(
    string Path,
    string Code,
    string Message,
    string? Property,
    string? Constraint,
    UnverifiedReason? Reason);

/// <summary>
/// The derived verdict for one scope of one domain (V-04). <see cref="VerifiedWrites"/> is the
/// whole domain's count — V-03 records it without a node, so a scoped summary cannot narrow it —
/// while <see cref="UnverifiedWrites"/> and <see cref="Violations"/> are the in-scope rows.
/// </summary>
public sealed record VerificationSummary(
    CheckVerdict Verdict,
    int VerifiedWrites,
    IReadOnlyList<UnverifiedWriteFact> UnverifiedWrites,
    int Violations);

/// <summary>
/// Derives the <see cref="VerificationSummary"/> on demand from V-03's write facts plus the final
/// diagnostics (D1: not a second <see cref="IAnalysisMetadata"/> — an analyzer cannot see the
/// final diagnostic list at publish time).
/// </summary>
public static class VerificationSummaryExtensions {
    /// <summary>
    /// The verdict for <paramref name="domain"/>, optionally narrowed to <paramref name="path"/>
    /// (the MCP select form: <c>Entity</c> or <c>Entity.Member</c>, dot or slash, with an optional
    /// domain prefix). A missing facts bucket fails closed to <see cref="CheckVerdict.Unknown"/>.
    /// </summary>
    public static VerificationSummary GetVerification(
        this AnalysisResult analysis, Domain domain, string? path = null) {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(domain);

        return new VerificationSummary(CheckVerdict.Unknown, 0, [], 0);
    }
}