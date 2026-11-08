using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// A copy the platform's reader found between two entity properties, as dotted
/// <c>Entity.Property</c> paths. The existing assign checks run over it; it lowers nothing.
/// </summary>
public sealed record ObservedFlow(string SourcePath, string TargetPath);

/// <summary>
/// Acknowledges one diagnostic an <see cref="ObservedFlow"/> check emitted: <see cref="Code"/> plus
/// the finding's <see cref="ElementPath"/> (<c>Domain/Entity/Property</c>). A matching allowance
/// downgrades that diagnostic to a warning, except DMEFF014: a nullable-to-required copy is cleared
/// only by a provable guard, and an allowance naming it is DMALW002. <see cref="ProvenanceIds"/> are
/// the platform's ids, required and non-blank; the engine does not interpret them.
/// </summary>
public sealed record DiagnosticAllowance(
    string Code,
    string ElementPath,
    IReadOnlyList<string> ProvenanceIds);

/// <summary>Allowances a flow diagnostic matched during this analysis; read by the unused check.</summary>
internal sealed class AllowanceLedger : IAnalysisMetadata {
    public HashSet<DiagnosticAllowance> Consumed { get; } = new(ReferenceEqualityComparer.Instance);
    public HashSet<string> NullabilityPaths { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// What the two flow arms (<see cref="EffectAnalyzer"/>, <see cref="ExpressionTypeAnalyzer"/>)
/// share: path resolution, the allowance lookup that picks a flow diagnostic's severity, and the
/// unused-allowance check. The compatibility decisions stay in those analyzers.
/// </summary>
internal static class ObservedFlowChecks {
    /// <summary>Exactly two segments, the owner an Entity.</summary>
    public static (Entity Entity, Property Property)? Resolve(Domain domain, string? path) {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var parts = path.Split('.');
        if (parts.Length != 2)
            return null;
        var entity = domain.Types.OfType<Entity>().FirstOrDefault(e => e.Name == parts[0]);
        var property = entity?.Properties.FirstOrDefault(p => p.Name == parts[1]);
        return property is null ? null : (entity!, property);
    }

    /// <summary>
    /// Warning when an acknowledged allowance matches <paramref name="code"/> and the target
    /// property's element path, else Error. A match is recorded so it is not reported unused.
    /// DMEFF014 is never downgraded and never consumes an allowance.
    /// </summary>
    public static DiagnosticSeverity SeverityFor(
        AnalysisContext context, Domain domain, Property target, string code) {
        if (string.Equals(code, DomainModelDiagnosticCodes.ObservedFlowNullability, StringComparison.Ordinal))
            return DiagnosticSeverity.Error;
        var allowances = RuntimeAnalysisCache.Session(domain).DiagnosticAllowances;
        if (allowances.Count == 0)
            return DiagnosticSeverity.Error;
        var path = DomainElementPath.Resolve(domain, target);
        foreach (var allowance in allowances) {
            if (!string.Equals(allowance.Code, code, StringComparison.Ordinal)
                || !string.Equals(allowance.ElementPath, path, StringComparison.Ordinal)
                || !IsAcknowledged(allowance))
                continue;
            context.GetOrAddMetadata(domain, () => new AllowanceLedger()).Consumed.Add(allowance);
            return DiagnosticSeverity.Warning;
        }
        return DiagnosticSeverity.Error;
    }

    /// <summary>Records the element path of a DMEFF014 finding so an allowance naming it is DMALW002.</summary>
    public static void NoteNullabilityFinding(AnalysisContext context, Domain domain, Node finding) {
        var path = DomainElementPath.Resolve(domain, finding);
        if (path is not null)
            context.GetOrAddMetadata(domain, () => new AllowanceLedger()).NullabilityPaths.Add(path);
    }

    /// <summary>
    /// DMALW001 for each allowance no flow diagnostic consumed, or DMALW002 when it names a
    /// DMEFF014 finding that was reported. Run after both flow arms.
    /// </summary>
    public static void ReportUnusedAllowances(AnalysisContext context, Domain domain) {
        var allowances = RuntimeAnalysisCache.Session(domain).DiagnosticAllowances;
        if (allowances.Count == 0)
            return;
        var ledger = context.GetMetadata<AllowanceLedger>(domain);
        var consumed = ledger?.Consumed;
        foreach (var allowance in allowances) {
            if (consumed is not null && consumed.Contains(allowance))
                continue;
            if (string.Equals(allowance.Code, DomainModelDiagnosticCodes.ObservedFlowNullability, StringComparison.Ordinal)
                && ledger is not null && ledger.NullabilityPaths.Contains(allowance.ElementPath)) {
                context.ReportError(
                    domain,
                    $"Diagnostic allowance '{allowance.Code}' at '{allowance.ElementPath}' cannot acknowledge a " +
                    "nullable-to-required copy. Guard the copy with an if, a require, or a stage require, " +
                    "or make the target optional.",
                    DomainModelDiagnosticCodes.UnacknowledgeableAllowance);
                continue;
            }
            var why = IsAcknowledged(allowance)
                ? "no flow diagnostic with that code was reported at that element path"
                : "it carries no non-blank provenance ids";
            context.ReportError(
                domain,
                $"Diagnostic allowance '{allowance.Code}' at '{allowance.ElementPath}' acknowledges nothing: {why}.",
                DomainModelDiagnosticCodes.UnusedDiagnosticAllowance);
        }
    }

    private static bool IsAcknowledged(DiagnosticAllowance allowance) =>
        allowance.ProvenanceIds is { Count: > 0 } ids && ids.All(id => !string.IsNullOrWhiteSpace(id));
}