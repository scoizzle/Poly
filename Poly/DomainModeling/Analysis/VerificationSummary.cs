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
    /// <summary>
    /// At least one write could not be decided, so the model is not proven. This is the zero value,
    /// so a defaulted verdict fails closed to Unknown rather than reading Proven.
    /// </summary>
    Unknown,

    /// <summary>Every write is decided inside its constraint and no error is in scope.</summary>
    Proven,

    /// <summary>A write is proven outside its constraint, or an error is in scope.</summary>
    Violated,
}

/// <summary>
/// One write the summary could not prove inside its constraint, projected from a finding and
/// enriched from the recorded fact on the same node (A2). <see cref="Path"/> is the finding's
/// element path; <see cref="Property"/>, <see cref="Constraint"/> and <see cref="Reason"/> are
/// null when no fact is attached to that node — a non-Range parameter-arm
/// warning records no fact (the call chain records one since V-05b), and a <c>DMCS003</c> row may not be a write finding at all
/// (<c>ConstraintQualityAnalyzer</c> reports that code too), so the three fields are optional on
/// every row, not only on those.
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
/// <para>
/// <see cref="Violations"/> counts distinct nodes carrying a violation fact <em>or</em> any
/// <see cref="DiagnosticSeverity.Error"/> of any code, so it is deliberately larger than the
/// fact-backed <c>WriteVerificationFacts.Violations</c>: an error with no fact behind it still counts.
/// With a facts bucket and a scope that names an element, <see cref="CheckVerdict.Unknown"/> implies
/// at least one row: a recorded fact with no finding is synthesised into one.
/// </para>
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
    /// <para>
    /// A call-chain finding is attributed to the <em>callee</em> path — the effect the callee
    /// assigns, not the caller that invokes it — so a caller-scoped select can read
    /// <see cref="CheckVerdict.Proven"/> while the whole domain reads
    /// <see cref="CheckVerdict.Violated"/>. Narrow the scope on the callee, or read the whole domain.
    /// </para>
    /// </summary>
    public static VerificationSummary GetVerification(
        this AnalysisResult analysis, Domain domain, string? path = null) {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(domain);

        // D3: the domain pass creates the bucket before anything else, so a missing one means the
        // pass never ran — never "there was nothing to report".
        var facts = analysis.GetMetadata<WriteVerificationFacts>(domain);
        if (facts is null)
            return new VerificationSummary(CheckVerdict.Unknown, 0, [], 0);

        // A3: the select form is the MCP one, checked against the domain tree. A path that names
        // nothing is not a proof, so it fails closed to Unknown with no rows.
        var scope = SelectScope.Parse(domain, path);
        if (scope is null)
            return new VerificationSummary(CheckVerdict.Unknown, facts.Verified, [], 0);

        var rows = UnverifiedRows(analysis, domain, facts, scope);
        var violations = ViolationNodes(analysis, domain, facts, scope);
        // Every in-scope unverified fact has a row (a finding, or a synthesised one), so rows alone
        // decide the middle arm.
        var verdict = violations.Count > 0
            ? CheckVerdict.Violated
            : rows.Count > 0
                ? CheckVerdict.Unknown
                : CheckVerdict.Proven;
        return new VerificationSummary(verdict, facts.Verified, rows, violations.Count);
    }

    /// <summary>
    /// A1: the fold's first arm and the violation count, as distinct nodes. A proven violation fact
    /// and any error diagnostic name the nodes that failed, so an error with no fact behind it
    /// (a non-Range kind, V-08) still counts, and no diagnostic-to-fact match can mask it. That makes
    /// <see cref="VerificationSummary.Violations"/> deliberately stricter than
    /// <see cref="WriteVerificationFacts.Violations"/>.
    /// </summary>
    private static HashSet<Node> ViolationNodes(
        AnalysisResult analysis, Domain domain, WriteVerificationFacts facts, SelectScope scope) {
        var nodes = new HashSet<Node>(NodeIdentityComparer.Instance);
        foreach (var violation in facts.Violations) {
            if (scope.Contains(domain, violation.Effect))
                nodes.Add(violation.Effect);
        }
        foreach (var diagnostic in analysis.Diagnostics) {
            if (diagnostic.Severity == DiagnosticSeverity.Error && scope.Contains(domain, diagnostic.Node))
                nodes.Add(diagnostic.Node);
        }
        return nodes;
    }

    /// <summary>
    /// A2: the rows are the write findings, not the facts — a non-Range kind (V-08)
    /// reports a diagnostic and records no fact, and the summary must show it. A fact on the same node <em>and</em> with the same reported code supplies the property,
    /// constraint and reason, so two findings on one node keep their own (E4); when several facts
    /// share that key the worst reason wins (G4). A fact with no finding is synthesised into a row,
    /// except on a node that carries an in-scope error — that write is Violated, not unknown (G3).
    /// Rows are de-duplicated by (node, code) so one write is one row.
    /// </summary>
    private static List<UnverifiedWriteFact> UnverifiedRows(
        AnalysisResult analysis, Domain domain, WriteVerificationFacts facts, SelectScope scope) {
        var rows = new List<UnverifiedWriteFact>();
        var seen = new HashSet<(Node Node, string Code)>(NodeCodeComparer.Instance);
        var failed = new HashSet<Node>(NodeIdentityComparer.Instance);
        foreach (var diagnostic in analysis.Diagnostics) {
            if (diagnostic.Severity == DiagnosticSeverity.Error && scope.Contains(domain, diagnostic.Node))
                failed.Add(diagnostic.Node);
        }

        foreach (var diagnostic in analysis.Diagnostics) {
            var code = diagnostic.Code;
            if (code is null || !IsUnverifiedWriteFinding(code, diagnostic.Severity))
                continue;
            if (!scope.Contains(domain, diagnostic.Node))
                continue;
            if (!seen.Add((diagnostic.Node, code)))
                continue;

            var fact = WorstFact(facts.Unverified, diagnostic.Node, code);
            rows.Add(new UnverifiedWriteFact(
                DomainElementPath.Resolve(domain, diagnostic.Node),
                code,
                diagnostic.Message,
                fact?.TargetProperty,
                fact?.Constraint,
                fact?.Reason));
        }

        // A recorded fact whose finding did not surface still means Unknown; without a row the
        // verdict would say not-proven and show nothing. A fact on a node that carries an in-scope
        // error is excluded: that write is Violated, never unknown (G3).
        foreach (var fact in InWorstFirstOrder(facts.Unverified)) {
            if (!scope.Contains(domain, fact.Effect) || failed.Contains(fact.Effect))
                continue;
            if (!seen.Add((fact.Effect, fact.ReportedCode)))
                continue;
            rows.Add(new UnverifiedWriteFact(
                DomainElementPath.Resolve(domain, fact.Effect),
                fact.ReportedCode,
                $"Write to '{fact.TargetProperty}' against {fact.Constraint} is not verified: " +
                $"{UnverifiedReasonText.Of(fact.Reason)}.",
                fact.TargetProperty,
                fact.Constraint,
                fact.Reason));
        }
        return rows;
    }

    /// <summary>
    /// The fact belonging to one finding: same node and same reported code. When a node carries
    /// several facts of one code, the worst reason wins deterministically instead of the first
    /// declared (G4).
    /// </summary>
    private static UnverifiedWrite? WorstFact(
        IReadOnlyList<UnverifiedWrite> facts, Node node, string code) {
        UnverifiedWrite? worst = null;
        foreach (var fact in facts) {
            if (!ReferenceEquals(fact.Effect, node)
                || !string.Equals(fact.ReportedCode, code, StringComparison.Ordinal))
                continue;
            if (worst is null || WriteVerdict.RankFor(fact.Reason) > WriteVerdict.RankFor(worst.Reason))
                worst = fact;
        }
        return worst;
    }

    /// <summary>The facts worst reason first, so a de-duplicating pass keeps the worst of a tie.</summary>
    private static IEnumerable<UnverifiedWrite> InWorstFirstOrder(IReadOnlyList<UnverifiedWrite> facts) =>
        facts.OrderByDescending(fact => WriteVerdict.RankFor(fact.Reason));

    /// <summary>
    /// The write-unverified warning vocabulary (A2): every <c>DMCS003</c> and a <c>DMEFF008</c>
    /// warning. A <c>DMEFF008</c> error is a proven violation, not an unknown.
    /// </summary>
    private static bool IsUnverifiedWriteFinding(string code, DiagnosticSeverity severity) =>
        string.Equals(code, DomainModelDiagnosticCodes.ConstraintUndecided, StringComparison.Ordinal)
        || (string.Equals(code, DomainModelDiagnosticCodes.EffectConstraintViolation, StringComparison.Ordinal)
            && severity == DiagnosticSeverity.Warning);

    /// <summary>
    /// A3: the select form is the MCP one — <c>Entity</c> or <c>Entity.Member</c>, dot or slash,
    /// with an optional domain prefix — resolved once against the domain tree rather than against
    /// the rows, so an entity with no writes is still a valid scope. An empty or whitespace path is
    /// the whole domain; a path that names no element is not a proof, so it yields no scope.
    /// </summary>
    private sealed class SelectScope {
        private readonly string[] _segments;

        private SelectScope(string[] segments) => _segments = segments;

        /// <summary>The whole domain for an empty path, null when <paramref name="path"/> names nothing.</summary>
        public static SelectScope? Parse(Domain domain, string? path) {
            if (string.IsNullOrWhiteSpace(path))
                return new SelectScope([]);

            var parts = path.Split(['.', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1 && string.Equals(parts[0], domain.Name, StringComparison.Ordinal))
                parts = parts[1..];
            if (parts.Length == 0)
                return new SelectScope([]);
            return NamesAnElement(domain, parts) ? new SelectScope(parts) : null;
        }

        /// <summary>
        /// True when <paramref name="node"/> is inside this scope. A node that does not resolve —
        /// <see cref="DomainElementPath.Resolve"/> falls back to the domain name — is in every
        /// scope, so it can never be scoped out and leave the model reading proven.
        /// </summary>
        public bool Contains(Domain domain, Node node) {
            if (_segments.Length == 0)
                return true;
            return Matches(Segments(domain, node));
        }

        /// <summary>The node's element path as segments, domain prefix dropped; empty when unresolved.</summary>
        private static string[] Segments(Domain domain, Node node) {
            var path = DomainElementPath.Resolve(domain, node).Split('/');
            if (path.Length <= 1)
                return [];
            return string.Equals(path[0], domain.Name, StringComparison.Ordinal) ? path[1..] : path;
        }

        private bool Matches(string[] row) {
            if (row.Length == 0)
                return true; // unresolved: in every scope
            if (!string.Equals(row[0], _segments[0], StringComparison.Ordinal))
                return false;

            // Entity, Entity.Member: a prefix of the row's path.
            if (_segments.Length <= row.Length) {
                var prefix = true;
                for (var i = 1; i < _segments.Length; i++) {
                    if (!string.Equals(row[i], _segments[i], StringComparison.Ordinal)) {
                        prefix = false;
                        break;
                    }
                }
                if (prefix)
                    return true;
            }

            // A stage action is selected without its stage segment: Entity.Action over
            // Domain/Entity/Stage/Action.
            return _segments.Length == 2
                && row.Length >= 3
                && string.Equals(row[2], _segments[1], StringComparison.Ordinal);
        }

        /// <summary>The MCP <c>select</c> existence rule, read from the tree.</summary>
        private static bool NamesAnElement(Domain domain, string[] parts) {
            var entity = domain.Types.OfType<Entity>()
                .FirstOrDefault(e => string.Equals(e.Name, parts[0], StringComparison.Ordinal));
            if (entity is null)
                return false;
            if (parts.Length == 1)
                return true;

            var member = parts[1];
            return entity.Properties.Any(p => string.Equals(p.Name, member, StringComparison.Ordinal))
                || entity.Actions.Any(a => string.Equals(a.Name, member, StringComparison.Ordinal))
                || entity.Policies.Any(p => string.Equals(p.Name, member, StringComparison.Ordinal))
                || entity.Stages.Any(s => string.Equals(s.Name, member, StringComparison.Ordinal)
                    || s.Actions.Any(a => string.Equals(a.Name, member, StringComparison.Ordinal)));
        }
    }

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