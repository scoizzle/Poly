using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;

namespace Poly.DomainModeling.Analysis;

/// <summary>Lint-only: constraint quality diagnostics; writes no metadata others read.</summary>
internal sealed class ConstraintQualityAnalyzer : INodeAnalyzer {
    public const string Id = "DomainConstraintQualityAnalyzer";
    public string PassName => Id;
    // Reads DomainTypeLookupMetadata / ResolvedTypeReferenceMetadata.
    public void Analyze(AnalysisContext context, Node node) {

        switch (node) {
            case Domain domain:
                ValidateDomainFixedPoint(context, domain);
                break;
            case Property property:
                ValidatePropertyConstraints(context, property);
                break;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void ValidateDomainFixedPoint(AnalysisContext context, Domain domain) {
        var lookup = context.GetTypeLookup();
        if (lookup is null) return;

        // Inheritance removed — no parent-entity fixed-point validation needed.
    }

    private static void ValidatePropertyConstraints(AnalysisContext context, Property property) {
        var constraints = property.Constraints;
        ValidateRequiredAndOptional(context, property, constraints);
        ValidateRangeSatisfiability(context, property, constraints);
        ValidateLengthSatisfiability(context, property, constraints);
        ValidatePatternWithinLength(context, property, constraints);
        ValidatePatternSyntax(context, property, constraints);
        ValidateEqualitySatisfiability(context, property, constraints);
        ValidateJointConstraintSatisfiability(context, property, constraints);
        ValidateConstraintTypeCompatibility(context, property, constraints);
        ValidateDefaultWithinConstraints(context, property, constraints);
        NoteUnprojectedStorageConstraints(context, property, constraints);
    }

    /// <summary>
    /// Required is the default and <c>optional</c> is its opt-out, so carrying both is
    /// a contradiction, not two unrelated constraints.
    /// </summary>
    private static void ValidateRequiredAndOptional(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        if (constraints.Any(c => c is RequiredConstraint)
            && constraints.Any(c => c is OptionalConstraint)) {
            context.ReportError(
                property,
                $"Property '{property.Name}' cannot be both 'required' and 'optional': " +
                "required is the default and 'optional' is its opt-out.",
                DomainModelDiagnosticCodes.ConstraintSatisfiability);
        }
    }

    /// <summary>
    /// Storage emits HasMaxLength and unique indexes. A pattern and a length minimum
    /// stay write-time checks; say so instead of dropping them quietly.
    /// </summary>
    private static void NoteUnprojectedStorageConstraints(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        if (constraints.OfType<PatternConstraint>().Any()) {
            context.ReportHint(
                property,
                $"Property '{property.Name}' pattern is enforced when the value is written and is not projected to SQL.",
                DomainModelDiagnosticCodes.ConstraintSatisfiability);
        }
        var length = constraints.OfType<LengthConstraint>().FirstOrDefault();
        if (length is { MinLength: > 0 }) {
            context.ReportHint(
                property,
                $"Property '{property.Name}' length minimum {length.MinLength} is enforced when the value is written. Storage projects only HasMaxLength({length.MaxLength}).",
                DomainModelDiagnosticCodes.ConstraintSatisfiability);
        }
    }

    /// <summary>
    /// Joint satisfiability — the deeper suite. A property's constraints must be satisfiable
    /// together, not just individually:
    ///  - duplicate constraints of the same type must merge (intersect) without becoming empty
    ///    (two disjoint ranges, two incompatible lengths, two differing patterns);
    ///  - an EqualityConstraint's value must satisfy the length/pattern constraints;
    ///  - a literal default must satisfy the pattern/equality constraints (range/length are
    ///    covered by <see cref="ValidateDefaultWithinConstraints"/>).
    /// </summary>
    private static void ValidateJointConstraintSatisfiability(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        foreach (var group in constraints
                     .Where(c => c is not EqualityConstraint)
                     .GroupBy(c => c.GetType())) {
            if (group.Count() < 2) continue;
            Constraint? net = null;
            foreach (var c in group) {
                if (net is null) {
                    net = c;
                    continue;
                }
                var merge = net.MergeResult(c);
                if (merge.IsEmpty) {
                    context.ReportError(
                        property,
                        $"Property '{property.Name}' has contradictory {ConstraintValidation.Describe(group.First())} constraints — unsatisfiable.",
                        DomainModelDiagnosticCodes.ConstraintSatisfiability);
                    break;
                }
                if (!merge.IsNet)
                    break; // cannot decide; never read it as empty
                net = merge.Net;
            }
        }

        var equality = constraints.OfType<EqualityConstraint>().FirstOrDefault();
        if (equality is not null) {
            foreach (var c in constraints) {
                if (c is EqualityConstraint or RangeConstraint) continue; // range covered elsewhere
                if (HasInvalidPattern(c)) continue; // #513: already its own diagnostic, never a contradiction
                var verdict = ConstraintValidation.Evaluate(c, equality.ExpectedValue);
                if (verdict == ConstraintVerdict.Undecided) {
                    ConstraintValidation.ReportUndecided(context, property, c, $"on property '{property.Name}'");
                    continue;
                }
                if (verdict == ConstraintVerdict.Violated) {
                    context.ReportError(
                        property,
                        $"Property '{property.Name}' EqualityConstraint value '{equality.ExpectedValue}' " +
                        $"violates {ConstraintValidation.Describe(c)}.",
                        DomainModelDiagnosticCodes.ConstraintSatisfiability);
                }
            }
        }

        if (constraints.OfType<DefaultValueConstraint>().FirstOrDefault()?.Expression is Literal lit) {
            foreach (var c in constraints) {
                if (c is DefaultValueConstraint or RangeConstraint or LengthConstraint) continue; // covered elsewhere
                if (HasInvalidPattern(c)) continue; // #513: already its own diagnostic, never a contradiction
                var verdict = ConstraintValidation.Evaluate(c, lit.Value);
                if (verdict == ConstraintVerdict.Undecided) {
                    ConstraintValidation.ReportUndecided(context, property, c, $"on property '{property.Name}'");
                    continue;
                }
                if (verdict == ConstraintVerdict.Violated) {
                    context.ReportError(
                        property,
                        $"Property '{property.Name}' default value '{lit.Value}' violates {ConstraintValidation.Describe(c)}.",
                        DomainModelDiagnosticCodes.ConstraintSatisfiability);
                }
            }
        }
    }

    /// <summary>
    /// Multiple equality constraints that disagree (must equal 'a' and 'b') make a property
    /// unsatisfiable — a structural contradiction, reported at authoring. An equality value
    /// outside the declared range is the same satisfiability class.
    /// </summary>
    private static void ValidateEqualitySatisfiability(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        var equalities = constraints.OfType<EqualityConstraint>().ToList();
        if (equalities.Count > 1) {
            var first = equalities[0].ExpectedValue;
            var conflicting = equalities.Skip(1)
                .FirstOrDefault(e => !Equals(e.ExpectedValue, first));
            if (conflicting is not null) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has contradictory EqualityConstraints " +
                    $"(must equal both '{first}' and '{conflicting.ExpectedValue}') — unsatisfiable.",
                    DomainModelDiagnosticCodes.ConstraintSatisfiability);
            }
        }

        var equality = equalities.FirstOrDefault();
        var range = constraints.OfType<RangeConstraint>().FirstOrDefault();
        if (equality is not null && range is not null
            // RangeConstraint is always modelled, so its verdict is never Undecided here.
            && ConstraintValidation.Evaluate(range, equality.ExpectedValue) == ConstraintVerdict.Violated) {
            context.ReportError(
                property,
                $"Property '{property.Name}' EqualityConstraint ({equality.ExpectedValue}) is outside its " +
                $"RangeConstraint {ConstraintValidation.Describe(range)} — unsatisfiable.",
                DomainModelDiagnosticCodes.ConstraintSatisfiability);
        }
    }

    /// <summary>
    /// A literal <c>default(...)</c> that violates the property's own range/length is a
    /// contradiction: the export's Create factory fails on every unoverridden create, and
    /// the runtime silently stores the out-of-range value. Reject at authoring.
    /// </summary>
    private static void ValidateDefaultWithinConstraints(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        var defaultConstraint = constraints.OfType<DefaultValueConstraint>().FirstOrDefault();
        if (defaultConstraint is null) return;

        var checks = constraints.Where(c => c is RangeConstraint or LengthConstraint).ToList();
        if (checks.Count == 0) return; // pattern/equality are checked in ValidateJointConstraintSatisfiability

        if (defaultConstraint.Expression is not Literal literal) {
            // A non-literal default (clock, arithmetic, reference) has no value at analysis time, so
            // it cannot be compared with the range/length: say so instead of passing it silently.
            foreach (var c in checks) {
                context.ReportWarning(
                    property,
                    $"Property '{property.Name}' default({defaultConstraint.Expression}) is not a literal, so it was not validated against {ConstraintValidation.Describe(c)}.",
                    DomainModelDiagnosticCodes.ConstraintUndecided);
            }
            return;
        }

        // Evaluate decides every literal type through the registered kind (ordering rule shared with
        // merge/join): a value that cannot be ordered against a bound is Violated (fail closed).
        foreach (var c in checks) {
            if (ConstraintValidation.Evaluate(c, literal.Value) != ConstraintVerdict.Violated) continue;
            context.ReportError(
                property,
                $"Property '{property.Name}' default value '{literal.Value}' violates {ConstraintValidation.Describe(c)}.",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
    }

    private static void ValidateRangeSatisfiability(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        foreach (var range in constraints.OfType<RangeConstraint>()) {
            if (range.Minimum is not null && range.Maximum is not null
                && ConstraintMerge.TryCompare(range.Minimum, range.Maximum) is > 0) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has unsatisfiable RangeConstraint: minimum exceeds maximum.",
                    DomainModelDiagnosticCodes.ConstraintSatisfiability);
            }
        }
    }

    private static void ValidateLengthSatisfiability(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        foreach (var length in constraints.OfType<LengthConstraint>()) {
            if (length.MinLength < 0 || length.MaxLength < 0 || length.MinLength > length.MaxLength) {
                context.ReportError(
                    property,
                    $"Property '{property.Name}' has unsatisfiable LengthConstraint bounds (min={length.MinLength}, max={length.MaxLength}).",
                    DomainModelDiagnosticCodes.ConstraintSatisfiability);
            }
        }
    }

    private static void ValidateConstraintTypeCompatibility(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        var hasRange = constraints.OfType<RangeConstraint>().Any();
        var hasLength = constraints.OfType<LengthConstraint>().Any();
        var hasPattern = constraints.OfType<PatternConstraint>().Any();

        if (!hasRange && !hasLength && !hasPattern) return;

        var resolved = context.GetMetadata<ResolvedTypeReferenceMetadata>(property.Type);
        if (resolved?.Type is not PrimitiveType primitiveType) return;

        var semantics = primitiveType.Semantics;

        if (hasRange && semantics is not (PrimitiveSemantics.Number or PrimitiveSemantics.Decimal)) {
            context.ReportError(
                property,
                $"Property '{property.Name}' has a RangeConstraint but its type '{property.Type.TypeName}' does not resolve to a numeric type.",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }

        if (hasLength && semantics is not PrimitiveSemantics.Text) {
            context.ReportError(
                property,
                $"Property '{property.Name}' has a LengthConstraint but its type '{property.Type.TypeName}' does not resolve to a text type.",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }

        if (hasPattern && semantics is not PrimitiveSemantics.Text) {
            context.ReportError(
                property,
                $"Property '{property.Name}' has a PatternConstraint but its type '{property.Type.TypeName}' does not resolve to a text type.",
                DomainModelDiagnosticCodes.SemanticTypeCompatibility);
        }
    }

    /// <summary>
    /// #583: a pattern's length envelope can exclude its <c>length(...)</c> range. Patterns and
    /// lengths merge separately, so the joint check never sees them together; compare the
    /// computed <see cref="PatternShape"/> here. At most one report per property. An invalid
    /// regex and a malformed length are already reported elsewhere, so neither is reread as a
    /// contradiction.
    /// </summary>
    private static void ValidatePatternWithinLength(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        var patterns = constraints.OfType<PatternConstraint>().ToList();
        if (patterns.Count == 0) return;

        var lengths = constraints.OfType<LengthConstraint>().ToList();
        if (lengths.Count == 0) return;

        var resolved = context.GetMetadata<ResolvedTypeReferenceMetadata>(property.Type);
        if (resolved?.Type is not PrimitiveType { Semantics: PrimitiveSemantics.Text }) return;

        foreach (var pattern in patterns) {
            var shape = PatternShape.Of(pattern.Pattern);
            foreach (var length in lengths) {
                if (length.MinLength < 0 || length.MinLength > length.MaxLength) continue;
                if (!shape.ContradictsLength(length)) continue;

                context.ReportError(
                    property,
                    $"Property '{property.Name}' has contradictory {ConstraintValidation.Describe(length)} " +
                    $"and {ConstraintValidation.Describe(pattern)} constraints: " +
                    "no non-null value satisfies both — unsatisfiable.",
                    DomainModelDiagnosticCodes.ConstraintSatisfiability);
                return;
            }
        }
    }

    /// <summary>
    /// #513: a body .NET cannot parse matches nothing, so every write of the property fails at
    /// runtime with the raw regex error. Validity is <see cref="PatternShape"/>'s decision (the one
    /// <c>new Regex</c> site); each distinct body is one defect, however often it is declared.
    /// </summary>
    private static void ValidatePatternSyntax(
        AnalysisContext context, Property property, IReadOnlyList<Constraint> constraints) {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pattern in constraints.OfType<PatternConstraint>()) {
            if (!reported.Add(pattern.Pattern)) continue;
            var shape = PatternShape.Of(pattern.Pattern);
            if (shape.IsValid) continue;
            context.ReportError(
                property,
                $"Property '{property.Name}' pattern '{pattern.Pattern}' is not a valid regular expression: {shape.Error}",
                DomainModelDiagnosticCodes.SemanticConstraintMismatch);
        }
    }

    /// <summary>An invalid pattern already has its own diagnostic; it is never reread as a violation.</summary>
    private static bool HasInvalidPattern(Constraint constraint) =>
        constraint is PatternConstraint pattern && !PatternShape.Of(pattern.Pattern).IsValid;
}