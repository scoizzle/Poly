using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #367 A-1: <see cref="ConstraintKinds"/> is the one source of per-kind behaviour. Merge, join,
/// identity, validation, description and printing all consult it, so a kind that is not
/// registered cannot be half-supported.
/// </summary>
public class ConstraintKindRegistryTests {
    private sealed record UnregisteredConstraint : Constraint;

    private static IEnumerable<Constraint> Samples() => [
        new RangeConstraint(1, 5),
        new LengthConstraint(1, 5),
        new RequiredConstraint(),
        new OptionalConstraint(),
        new EqualityConstraint("x"),
        new PatternConstraint("a+"),
        new UniqueConstraint(),
        new DefaultValueConstraint(new Literal(1)),
    ];

    [Test]
    public async Task EveryConcreteConstraintType_IsRegistered() {
        var concrete = typeof(Constraint).Assembly.GetTypes()
            .Where(t => typeof(Constraint).IsAssignableFrom(t) && !t.IsAbstract)
            .ToHashSet();
        var registered = ConstraintKinds.All.Select(k => k.Type).ToHashSet();
        await Assert.That(registered.SetEquals(concrete)).IsTrue();
    }

    [Test]
    public async Task EveryRegisteredKind_HasASample_SoEverySeamIsExercised() {
        var sampled = Samples().Select(c => c.GetType()).ToHashSet();
        var registered = ConstraintKinds.All.Select(k => k.Type).ToHashSet();
        await Assert.That(sampled.SetEquals(registered)).IsTrue();
    }

    [Test]
    public async Task EveryRegisteredKind_AnswersEverySeam() {
        var printer = new DomainDslPrinter();
        foreach (var c in Samples()) {
            var kind = ConstraintKinds.Of(c)!;
            await Assert.That(c.MergeResult(c).Kind).IsNotEqualTo(ConstraintMergeKind.Unmergeable);
            await Assert.That(ConstraintMerge.Identical(c, c)).IsTrue();
            await Assert.That(c.Join(c)).IsNotNull();
            await Assert.That(ConstraintValidation.Describe(c)).IsNotEqualTo(c.GetType().Name);
            _ = ConstraintValidation.Evaluate(c, "x");
            var text = printer.PrintConstraintText(c);
            if (kind.PrintedInDsl)
                await Assert.That(text.StartsWith('?')).IsFalse();
            else
                await Assert.That(text).IsEqualTo($"?{c.GetType().Name}");
        }
    }

    [Test]
    public async Task OnlyRequired_HasNoDslWord() {
        var unprinted = ConstraintKinds.All.Where(k => !k.PrintedInDsl).Select(k => k.Type).ToArray();
        await Assert.That(unprinted.Length).IsEqualTo(1);
        await Assert.That(unprinted[0]).IsEqualTo(typeof(RequiredConstraint));
    }

    [Test]
    public async Task UnregisteredKind_IsUnmergeable_Undecided_AndPrintsFailClosed() {
        var u = new UnregisteredConstraint();
        await Assert.That(ConstraintKinds.Of(u)).IsNull();
        await Assert.That(u.MergeResult(u).Kind).IsEqualTo(ConstraintMergeKind.Unmergeable);
        await Assert.That(u.Join(u)).IsNull();
        await Assert.That(ConstraintMerge.Identical(u, u)).IsFalse();
        await Assert.That(ConstraintValidation.Evaluate(u, "x")).IsEqualTo(ConstraintVerdict.Undecided);
        await Assert.That(ConstraintValidation.Describe(u)).IsEqualTo(nameof(UnregisteredConstraint));
        await Assert.That(new DomainDslPrinter().PrintConstraintText(u)).IsEqualTo($"?{nameof(UnregisteredConstraint)}");
    }
}