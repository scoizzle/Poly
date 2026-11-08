using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #442: a merge result distinguishes a net constraint, an unrepresentable intersection, and
/// an empty one. Identical duplicate constraints are idempotent, not contradictions, and the
/// DSL/evolution path keeps one of them so Lower does not compile the same guard twice.
/// </summary>
public class ConstraintMergeTests {
    private sealed record UnregisteredConstraint : Constraint;

    private static EvolutionResult Parse(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        var empty = DomainTestFactory.Create("_", [], []);
        return new DomainEvolution(empty).ApplyAndAnalyze(changes);
    }

    private static EvolutionResult ParseAndLower(string dsl) {
        var builder = SessionBuilder.CreateEmpty();
        builder.LoadCore();
        builder.Load(new PersistenceEmitLibrary());
        var session = builder.Build();
        var changes = new PolyDslParser(dsl, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (result.Succeeded)
            session.Lower(result.Root!, result.Analysis);
        return result;
    }

    private static EvolutionResult ParseTemporal(string dsl) {
        var session = SessionBuilder.CreateEmpty().LoadCore().Load(new TemporalLibrary()).Build();
        var changes = new PolyDslParser(dsl, session).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
    }

    private static IEnumerable<string> Errors(EvolutionResult result) =>
        result.Analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Message);

    // ── the result ────────────────────────────────────────────────

    [Test]
    public async Task MergeResult_IdenticalUniqueConstraints_IsNet() {
        var result = new UniqueConstraint().MergeResult(new UniqueConstraint());

        await Assert.That(result.Kind).IsEqualTo(ConstraintMergeKind.Net);
        await Assert.That(result.Net).IsTypeOf<UniqueConstraint>();
    }

    [Test]
    public async Task MergeResult_IdenticalLiteralDefaults_IsNet() {
        var result = new DefaultValueConstraint(new Literal("a"))
            .MergeResult(new DefaultValueConstraint(new Literal("a")));

        await Assert.That(result.Kind).IsEqualTo(ConstraintMergeKind.Net);
        await Assert.That(result.Net).IsTypeOf<DefaultValueConstraint>();
    }

    [Test]
    public async Task MergeResult_DifferentLiteralDefaults_IsEmpty() {
        // A second default must not silently win: every consumer takes FirstOrDefault().
        var result = new DefaultValueConstraint(new Literal("a"))
            .MergeResult(new DefaultValueConstraint(new Literal("b")));

        await Assert.That(result.Kind).IsEqualTo(ConstraintMergeKind.Empty);
    }

    [Test]
    public async Task MergeResult_DisjointRanges_IsEmpty() {
        var result = new RangeConstraint(0d, 5d).MergeResult(new RangeConstraint(10d, 20d));

        await Assert.That(result.Kind).IsEqualTo(ConstraintMergeKind.Empty);
    }

    [Test]
    public async Task MergeResult_DisjointLengths_IsEmpty() {
        var result = new LengthConstraint(1, 3).MergeResult(new LengthConstraint(5, 8));

        await Assert.That(result.Kind).IsEqualTo(ConstraintMergeKind.Empty);
    }

    [Test]
    public async Task MergeResult_OverlappingRanges_IsNet() {
        var result = new RangeConstraint(0d, 100d).MergeResult(new RangeConstraint(20d, 80d));

        await Assert.That(result.Kind).IsEqualTo(ConstraintMergeKind.Net);
        var range = (RangeConstraint)result.Net!;
        await Assert.That(Convert.ToDouble(range.Minimum)).IsEqualTo(20d);
        await Assert.That(Convert.ToDouble(range.Maximum)).IsEqualTo(80d);
    }

    [Test]
    public async Task MergeResult_DifferentConstraintTypes_IsUnmergeable() {
        var result = new UniqueConstraint().MergeResult(new RangeConstraint(0d, 5d));

        await Assert.That(result.Kind).IsEqualTo(ConstraintMergeKind.Unmergeable);
    }

    [Test]
    public async Task MergeResult_UnregisteredConstraintKind_IsUnmergeable() {
        var result = new UnregisteredConstraint().MergeResult(new UnregisteredConstraint());

        await Assert.That(result.Kind).IsEqualTo(ConstraintMergeKind.Unmergeable);
    }

    [Test]
    public async Task Merge_KeepsNetOrNullContract() {
        // The in-flight #420 branch and MinimalApiGenerator consume this shape.
        await Assert.That(new UniqueConstraint().Merge(new UniqueConstraint()))
            .IsTypeOf<UniqueConstraint>();
        await Assert.That(new RangeConstraint(0d, 5d).Merge(new RangeConstraint(10d, 20d)))
            .IsNull();
        await Assert.That(new UnregisteredConstraint().Merge(new UnregisteredConstraint()))
            .IsNull();
    }

    // ── the abstract value ────────────────────────────────────────

    [Test]
    public async Task AbstractValue_DuplicateUnique_IsNotUnsatisfiable() {
        var value = AbstractValue.From([new UniqueConstraint(), new UniqueConstraint()]);

        await Assert.That(value.Unsatisfiable).IsFalse();
        await Assert.That(value.Constraints.Count(c => c is UniqueConstraint)).IsEqualTo(1);
    }

    [Test]
    public async Task AbstractValue_DisjointRanges_IsStillUnsatisfiable() {
        var value = AbstractValue.From([new RangeConstraint(0d, 5d), new RangeConstraint(10d, 20d)]);

        await Assert.That(value.Unsatisfiable).IsTrue();
    }

    [Test]
    public async Task AbstractValue_UnmergeableKind_IsDroppedAndNotReadded() {
        var value = AbstractValue.From([
            new UnregisteredConstraint(),
            new UnregisteredConstraint(),
            new UnregisteredConstraint(),
        ]);

        await Assert.That(value.Unsatisfiable).IsFalse();
        await Assert.That(value.Constraints.Any(c => c is UnregisteredConstraint)).IsFalse();
    }

    // ── the DSL and evolution path ───────────────────────────────

    [Test]
    public async Task Dsl_DuplicateUnique_AnalyzesCleanAndKeepsOne() {
        var result = Parse("""
            domain Test
            Person: entity {
              Code: Text unique unique
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Errors(result).Any(m => m.Contains("contradictory unique"))).IsFalse();
        var constraints = result.Root!.Types.OfType<Entity>().Single()
            .Properties.Single().Constraints;
        await Assert.That(constraints.Count(c => c is UniqueConstraint)).IsEqualTo(1);
    }

    [Test]
    public async Task Dsl_DuplicateUniqueOnEntityWithAction_NoUnsatisfiablePreconditions() {
        var result = Parse("""
            domain Test
            Person: entity {
              Code: Text unique unique
              Rename: action { assign Code to Code }
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Errors(result).Any(m => m.Contains("unsatisfiable"))).IsFalse();
    }

    [Test]
    public async Task Dsl_DuplicateUnique_WithStorage_Lowers() {
        var result = ParseAndLower("""
            domain Test
            Person: entity {
              Code: Text unique unique
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Dsl_DuplicateLiteralDefault_AnalyzesCleanAndKeepsOne() {
        var result = Parse("""
            domain Test
            Person: entity {
              Kind: Text default("a") default("a")
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Errors(result).Any(m => m.Contains("contradictory"))).IsFalse();
        var constraints = result.Root!.Types.OfType<Entity>().Single()
            .Properties.Single().Constraints;
        await Assert.That(constraints.Count(c => c is DefaultValueConstraint)).IsEqualTo(1);
    }

    [Test]
    public async Task Dsl_DifferentLiteralDefaults_IsStillError() {
        var result = Parse("""
            domain Test
            Person: entity {
              Kind: Text default("a") default("b")
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(m => m.Contains("contradictory"))).IsTrue();
    }

    [Test]
    public async Task Dsl_DuplicateClockDefault_AnalyzesCleanAndKeepsOne() {
        // Today is a library-contributed node, not a PropertyAccess: compare it structurally.
        var result = ParseTemporal("""
            domain Test
            uses temporal
            Event: entity {
              RecordedOn: Date default(Today) default(Today)
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Errors(result).Any(m => m.Contains("contradictory"))).IsFalse();
        var constraints = result.Root!.Types.OfType<Entity>().Single()
            .Properties.Single().Constraints;
        await Assert.That(constraints.Count(c => c is DefaultValueConstraint)).IsEqualTo(1);
        await Assert.That(constraints.OfType<DefaultValueConstraint>().Single().Expression)
            .IsTypeOf<Today>();
    }

    [Test]
    public async Task Dsl_DuplicateDateOperationDefault_AnalyzesCleanAndKeepsOne() {
        var result = ParseTemporal("""
            domain Test
            uses temporal
            Event: entity {
              RecordedOn: Date default(Today - 1 Days) default(Today - 1 Days)
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(Errors(result).Any(m => m.Contains("contradictory"))).IsFalse();
        var constraints = result.Root!.Types.OfType<Entity>().Single()
            .Properties.Single().Constraints;
        await Assert.That(constraints.Count(c => c is DefaultValueConstraint)).IsEqualTo(1);
    }

    [Test]
    public async Task Dsl_DifferentClockDefaults_IsStillError() {
        // Different defaults must not silently pick the first.
        var result = ParseTemporal("""
            domain Test
            uses temporal
            Event: entity {
              RecordedOn: Date default(Today) default(Today - 1 Days)
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(m => m.Contains("contradictory"))).IsTrue();
    }

    [Test]
    public async Task Dsl_DuplicateEquality_StaysClean() {
        var result = Parse("""
            domain Test
            Person: entity {
              Kind: Text equals("a") equals("a")
            }
            """);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Dsl_DifferentEquality_StaysError() {
        var result = Parse("""
            domain Test
            Person: entity {
              Kind: Text equals("a") equals("b")
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(m => m.Contains("contradictory EqualityConstraints"))).IsTrue();
    }

    [Test]
    public async Task Dsl_DisjointRanges_StaysError() {
        var result = Parse("""
            domain Test
            Person: entity {
              Age: Number range(0, 5) range(10, 20)
            }
            """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(Errors(result).Any(m => m.Contains("contradictory range(0, 5)"))).IsTrue();
    }
}