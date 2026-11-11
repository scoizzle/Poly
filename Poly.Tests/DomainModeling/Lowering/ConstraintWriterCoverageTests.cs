using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// S3 (#313): every <see cref="Constraint"/> record either names the checks
/// <see cref="ConstraintCheckWriter"/> can emit or the owner that writes its guard.
/// The records come from reflection, so a new constraint cannot bypass the one
/// writer by adding a seventh switch arm somewhere else.
/// </summary>
public class ConstraintWriterCoverageTests {
    [Test]
    public async Task EveryConstraintRecord_HasWriterKindOrDocumentedException() {
        var records = typeof(Constraint).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(Constraint).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

        await Assert.That(records.Count).IsGreaterThan(0);
        foreach (var record in records) {
            ConstraintCheckWriter.CoverageByConstraint.TryGetValue(record, out var coverage);
            var hasWriterKind = coverage is { Kinds.Count: > 0 };
            var hasOwner = coverage is { Owner: not null };
            await Assert.That(hasWriterKind || hasOwner).IsTrue()
                .Because($"'{record.Name}' has no ConstraintCheckWriter kind and no documented owner. "
                    + "Route it through ConstraintCheckWriter or name its writer in CoverageByConstraint.");
        }
    }

    [Test]
    public async Task CoverageRows_AreConstraintRecords_AndOwnersSayWhoWrites() {
        foreach (var (type, coverage) in ConstraintCheckWriter.CoverageByConstraint) {
            await Assert.That(typeof(Constraint).IsAssignableFrom(type) && !type.IsAbstract).IsTrue()
                .Because($"'{type.Name}' is a coverage row but is not a Constraint record.");
            if (coverage.Kinds.Count > 0) {
                await Assert.That(coverage.Owner).IsNull()
                    .Because($"'{type.Name}' is a writer kind, so it must not also name an owner.");
                continue;
            }
            await Assert.That(string.IsNullOrWhiteSpace(coverage.Owner)).IsFalse()
                .Because($"'{type.Name}' emits no writer kind, so its owner must say who writes the guard.");
        }
    }

    [Test]
    public async Task CoverageRows_WithKinds_AreEmittedByChecksFor() {
        foreach (var (type, coverage) in ConstraintCheckWriter.CoverageByConstraint) {
            var (typeName, sample) = Sample(type);
            // Text is required by default; every row but Required marks the property optional so
            // the Required kind does not mask what the sampled constraint itself emits.
            IReadOnlyList<Constraint> constraints = sample is RequiredConstraint
                ? [sample]
                : [sample, new OptionalConstraint()];
            var property = new Property("P", new DomainTypeReference(typeName), constraints);
            var entity = new Entity("E", [property], [], [], []);
            var (domain, analysis) = LoweringFixtures.Analyze(entity);

            foreach (var style in new[] {
                         ConstraintCheckWriter.ConstraintCheckStyle.Create,
                         ConstraintCheckWriter.ConstraintCheckStyle.Assign,
                     }) {
                var emitted = ConstraintCheckWriter.ChecksFor(
                        entity,
                        property,
                        domain,
                        analysis,
                        style,
                        new Constant(sample is RangeConstraint ? 5L : "value"),
                        nullableValue: false,
                        valueHasDefault: false,
                        includeRequired: true)
                    .Select(check => check.Kind)
                    .Distinct()
                    .OrderBy(kind => kind)
                    .ToList();

                await Assert.That(emitted.SequenceEqual(coverage.Kinds.Distinct().OrderBy(kind => kind))).IsTrue()
                    .Because($"'{type.Name}' coverage row says [{string.Join(", ", coverage.Kinds)}] "
                        + $"but ChecksFor ({style.Order}) emitted [{string.Join(", ", emitted)}].");
            }
        }
    }

    private static (string TypeName, Constraint Sample) Sample(Type type) {
        if (type == typeof(RequiredConstraint)) return ("Text", new RequiredConstraint());
        if (type == typeof(RangeConstraint)) return ("Number", new RangeConstraint(1L, 10L));
        if (type == typeof(LengthConstraint)) return ("Text", new LengthConstraint(1, 20));
        if (type == typeof(PatternConstraint)) return ("Text", new PatternConstraint("^[A-Z]+$"));
        if (type == typeof(EqualityConstraint)) return ("Text", new EqualityConstraint("gold"));
        if (type == typeof(UniqueConstraint)) return ("Text", new UniqueConstraint());
        if (type == typeof(OptionalConstraint)) return ("Text", new OptionalConstraint());
        if (type == typeof(DefaultValueConstraint))
            return ("Text", new DefaultValueConstraint(DomainExpression.Literal("x")));
        if (type == typeof(RelationalConstraint))
            return ("Number", new RelationalConstraint("Other", RelationalOrdering.Less));
        throw new InvalidOperationException($"Add a sample for coverage row '{type.Name}'.");
    }
}