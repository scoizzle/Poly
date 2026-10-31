using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// S10: core claims the five builtin check constraints. Storage still owns
/// <c>unique</c>. <c>optional</c> and <c>default</c> are not checks.
/// </summary>
public class BuiltinConstraintCompilerTests {
    [Test]
    public async Task AllBuiltinConstraints_ClaimedByCompilers() {
        var session = SessionBuilder.CreateEmpty().LoadCore().Build();
        var core = session.ConstraintCompilers
            .Where(c => string.Equals(c.LibraryId, CoreLibrary.CoreId, StringComparison.Ordinal))
            .ToList();

        foreach (var constraint in Claimed()) {
            var matches = core.Where(c => c.Applies(constraint)).ToList();
            await Assert.That(matches.Count).IsEqualTo(1);
            await Assert.That(matches[0].LibraryId).IsEqualTo(CoreLibrary.CoreId);
        }

        foreach (var constraint in NotClaimed()) {
            var matches = core.Where(c => c.Applies(constraint)).ToList();
            await Assert.That(matches.Count).IsEqualTo(0);
        }
    }

    private static IEnumerable<Constraint> Claimed() => [
        new RequiredConstraint(),
        new RangeConstraint(1, 10),
        new LengthConstraint(1, 20),
        new PatternConstraint("^[A-Z]+$"),
        new EqualityConstraint("gold"),
    ];

    private static IEnumerable<Constraint> NotClaimed() => [
        new UniqueConstraint(),
        new OptionalConstraint(),
        new DefaultValueConstraint(DomainExpression.Literal("x")),
    ];
}