using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Contradictions;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.DomainModeling.Libraries.Contradictions;

/// <summary>
/// Merges the session's bound flow claims with the domain's declared facts and publishes the
/// <see cref="ContradictionBag"/>. Always publishes a bag (empty with no claims). It never
/// lowers, mutates the domain, or reports a diagnostic for a valid open entry: the entry is
/// data, and only the Lower gate refuses it.
/// </summary>
internal sealed class ContradictionFlowPass : INodeAnalyzer {
    public const string Id = "ContradictionFlowPass";
    public string PassName => Id;
    private const string InvalidClaimCode = "DMCONT001";

    private readonly IReadOnlyList<ContradictionClaim> _claims;

    public ContradictionFlowPass(IReadOnlyList<ContradictionClaim> claims) {
        _claims = claims;
    }

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        var entries = new List<ContradictionEntry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in _claims) {
            // Validate first and report every problem: a claim the engine cannot check is an
            // error, never a dropped entry.
            var valid = Validate(context, domain, claim, names);
            var source = valid ? Resolve(domain, claim.SourceElementPath) : null;
            var target = valid ? Resolve(domain, claim.TargetElementPath) : null;
            if (valid && source is null)
                Invalid(context, domain, claim, $"source path '{claim.SourceElementPath}' does not resolve to an entity property (use Entity.Property)");
            if (valid && target is null)
                Invalid(context, domain, claim, $"target path '{claim.TargetElementPath}' does not resolve to an entity property (use Entity.Property)");
            if (source is null || target is null)
                continue;
            var sourceShape = Effective(domain, source, claim.SourceShape);
            var targetShape = Effective(domain, target, claim.TargetShape);
            var kinds = new List<ContradictionKind>();
            if (sourceShape.MaxLength is { } sw && targetShape.MaxLength is { } tw && tw < sw)
                kinds.Add(ContradictionKind.WidthNarrowing);
            if (sourceShape is { Precision: { } sp, Scale: { } ss } && targetShape is { Precision: { } tp, Scale: { } ts }
                && (tp < sp || ts < ss))
                kinds.Add(ContradictionKind.ScaleNarrowing);
            if (sourceShape.IsNullable == true && targetShape.IsNullable == false)
                kinds.Add(ContradictionKind.Nullability);
            if (sourceShape.Family is { } sf && targetShape.Family is { } tf && sf != tf)
                kinds.Add(ContradictionKind.TypeCoercion);
            // A resolved or withdrawn claim stays in the bag even when the facets now agree:
            // that record is how a later compile sees a contradiction existed and was closed.
            // Only an open claim the engine finds compatible is dropped (the engine's verdict).
            if (kinds.Count == 0 && claim.Status == ContradictionStatus.Open)
                continue;
            entries.Add(new ContradictionEntry(
                claim.Name, [.. kinds], claim.SourceElementPath, claim.TargetElementPath,
                sourceShape, targetShape, [.. claim.ClaimIds], claim.Status));
        }
        context.SetMetadata(domain, new ContradictionMetadata(
            entries.Count == 0 ? ContradictionBag.Empty : new ContradictionBag([.. entries])));
    }

    private bool Validate(AnalysisContext context, Domain domain, ContradictionClaim claim, HashSet<string> names) {
        var ok = true;
        void Fail(string problem) {
            Invalid(context, domain, claim, problem);
            ok = false;
        }
        if (string.IsNullOrWhiteSpace(claim.Name))
            Fail("Name must be non-blank");
        else if (!names.Add(claim.Name))
            Fail("Name is used by more than one claim");
        if (claim.ClaimIds is null || claim.ClaimIds.Count == 0 || claim.ClaimIds.Any(string.IsNullOrWhiteSpace))
            Fail("ClaimIds must be non-empty with no blank entries");
        if (!Enum.IsDefined(claim.Status))
            Fail($"Status {(int)claim.Status} is not defined");
        if (string.IsNullOrWhiteSpace(claim.SourceElementPath) || string.IsNullOrWhiteSpace(claim.TargetElementPath))
            Fail("SourceElementPath and TargetElementPath must be non-blank");
        else if (string.Equals(claim.SourceElementPath, claim.TargetElementPath, StringComparison.Ordinal))
            Fail("SourceElementPath and TargetElementPath must differ");
        foreach (var (side, shape) in new[] { ("SourceShape", claim.SourceShape), ("TargetShape", claim.TargetShape) }) {
            if (shape is { MaxLength: < 0 } or { Precision: < 0 } or { Scale: < 0 })
                Fail($"{side} MaxLength, Precision, and Scale must not be negative");
        }
        return ok;
    }

    private static void Invalid(AnalysisContext context, Domain domain, ContradictionClaim claim, string problem) =>
        context.ReportDiagnostic(domain, DiagnosticSeverity.Error,
            $"Contradiction claim '{claim.Name}': {problem}.", code: InvalidClaimCode);

    // Entity.Property only: exactly two segments, the owner an Entity.
    private static Property? Resolve(Domain domain, string path) {
        var parts = path.Split('.');
        if (parts.Length != 2) return null;
        var entity = domain.Types.OfType<Entity>().FirstOrDefault(e => e.Name == parts[0]);
        return entity?.Properties.FirstOrDefault(p => p.Name == parts[1]);
    }

    private static ContradictionShape Effective(Domain domain, Property property, ContradictionShape? observed) {
        var type = domain.Types.FirstOrDefault(t => t.Name == property.Type.TypeName);
        var constraints = (type?.Constraints ?? []).Concat(property.Constraints).ToList();
        var maxLength = constraints.OfType<LengthConstraint>().LastOrDefault()?.MaxLength;
        return new ContradictionShape(
            observed?.MaxLength ?? maxLength,
            observed?.Precision,
            observed?.Scale,
            observed?.IsNullable ?? constraints.OfType<OptionalConstraint>().Any(),
            observed?.Family ?? (type as Poly.DomainModeling.Ontology.PrimitiveType) switch {
                null => null,
                var primitive => FamilyOf(primitive.Semantics)
            },
            observed?.DisplayType ?? property.Type.TypeName);
    }

    private static ContradictionFamily? FamilyOf(PrimitiveSemantics semantics) => semantics switch {
        PrimitiveSemantics.Text => ContradictionFamily.Text,
        PrimitiveSemantics.Number or PrimitiveSemantics.Decimal => ContradictionFamily.Number,
        PrimitiveSemantics.Boolean => ContradictionFamily.Boolean,
        PrimitiveSemantics.Date or PrimitiveSemantics.Time or PrimitiveSemantics.DateTime
            or PrimitiveSemantics.Duration => ContradictionFamily.Temporal,
        PrimitiveSemantics.Binary => ContradictionFamily.Binary,
        PrimitiveSemantics.Uuid => ContradictionFamily.Uuid,
        _ => null
    };
}