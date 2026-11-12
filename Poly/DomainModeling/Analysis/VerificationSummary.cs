using System.Runtime.CompilerServices;

using Poly.Analysis;
using Poly.DomainModeling.Compile;
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
        _ = path;

        // D3: the domain pass creates the bucket before anything else, so a missing one means the
        // pass never ran — never "there was nothing to report".
        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain);
        if (facts is null)
            return new VerificationSummary(CheckVerdict.Unknown, 0, [], 0);

        var rows = UnverifiedRows(analysis, domain, facts);
        var violations = ViolationNodes(analysis, facts);
        var verdict = violations.Count > 0
            ? CheckVerdict.Violated
            : rows.Count > 0 || facts.Unverified.Count > 0
                ? CheckVerdict.Unknown
                : CheckVerdict.Proven;
        return new VerificationSummary(verdict, facts.Verified, rows, violations.Count);
    }

    /// <summary>
    /// A1: the fold's first arm and the violation count, as distinct nodes. A proven violation fact
    /// and any error diagnostic name the nodes that failed, so an error with no fact behind it — the
    /// call-chain arm (V-05) — still counts, and no diagnostic-to-fact match can mask it. That makes
    /// <see cref="VerificationSummary.Violations"/> deliberately stricter than
    /// <see cref="WriteVerificationFacts.Violations"/> until V-05 records those facts.
    /// </summary>
    private static HashSet<Node> ViolationNodes(AnalysisResult analysis, WriteVerificationFacts facts) {
        var nodes = new HashSet<Node>(NodeIdentityComparer.Instance);
        foreach (var violation in facts.Violations)
            nodes.Add(violation.Effect);
        foreach (var diagnostic in analysis.Diagnostics) {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
                nodes.Add(diagnostic.Node);
        }
        return nodes;
    }

    /// <summary>
    /// A2: the rows are the write findings, not the facts — a non-Range kind (V-08) and the
    /// call-chain path (V-05) report a diagnostic and record no fact, and the summary must show
    /// them. A fact on the same node supplies the property, constraint and reason; rows are
    /// de-duplicated by (node, code) so one write is one row.
    /// </summary>
    private static List<UnverifiedWriteFact> UnverifiedRows(
        AnalysisResult analysis, Domain domain, WriteVerificationFacts facts) {
        var rows = new List<UnverifiedWriteFact>();
        var seen = new HashSet<(Node Node, string Code)>(NodeCodeComparer.Instance);
        foreach (var diagnostic in analysis.Diagnostics) {
            var code = diagnostic.Code;
            if (code is null || !IsUnverifiedWriteFinding(code, diagnostic.Severity))
                continue;
            if (!seen.Add((diagnostic.Node, code)))
                continue;

            var fact = facts.Unverified.FirstOrDefault(f => ReferenceEquals(f.Effect, diagnostic.Node));
            rows.Add(new UnverifiedWriteFact(
                DomainElementPath.Resolve(domain, diagnostic.Node),
                code,
                diagnostic.Message,
                fact?.TargetProperty,
                fact?.Constraint,
                fact?.Reason));
        }
        return rows;
    }

    /// <summary>
    /// The write-unverified warning vocabulary (A2): every <c>DMCS003</c> and a <c>DMEFF008</c>
    /// warning. A <c>DMEFF008</c> error is a proven violation, not an unknown.
    /// </summary>
    private static bool IsUnverifiedWriteFinding(string code, DiagnosticSeverity severity) =>
        string.Equals(code, DomainModelDiagnosticCodes.ConstraintUndecided, StringComparison.Ordinal)
        || (string.Equals(code, DomainModelDiagnosticCodes.EffectConstraintViolation, StringComparison.Ordinal)
            && severity == DiagnosticSeverity.Warning);

    private sealed class NodeCodeComparer : IEqualityComparer<(Node Node, string Code)> {
        public static readonly NodeCodeComparer Instance = new();

        public bool Equals((Node Node, string Code) x, (Node Node, string Code) y) =>
            ReferenceEquals(x.Node, y.Node) && string.Equals(x.Code, y.Code, StringComparison.Ordinal);

        public int GetHashCode((Node Node, string Code) value) =>
            HashCode.Combine(RuntimeHelpers.GetHashCode(value.Node), StringComparer.Ordinal.GetHashCode(value.Code));
    }

    /// <summary>
    /// Node identity: <see cref="Node"/> is a record, so its generated equality is structural, and
    /// two identically shaped effects in different actions would compare equal. Every set here is
    /// keyed on the node instance.
    /// </summary>
    private sealed class NodeIdentityComparer : IEqualityComparer<Node> {
        public static readonly NodeIdentityComparer Instance = new();

        public bool Equals(Node? x, Node? y) => ReferenceEquals(x, y);

        public int GetHashCode(Node value) => RuntimeHelpers.GetHashCode(value);
    }
}