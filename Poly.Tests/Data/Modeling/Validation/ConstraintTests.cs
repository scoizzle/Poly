using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Introspection;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Data.Modeling.Validation;

public class ConstraintTests {
    // NotNullConstraint

    [Test]
    public async Task NotNullConstraint_WithNullValue_FailsValidation() {
        var predicate = CompileConstraintPredicate<object?>(new NotNullConstraint());
        await Assert.That(predicate(null)).IsFalse();
    }

    [Test]
    public async Task NotNullConstraint_WithNonNullValue_PassesValidation() {
        var predicate = CompileConstraintPredicate<object?>(new NotNullConstraint());
        await Assert.That(predicate(new object())).IsTrue();
    }

    // EqualityConstraint

    [Test]
    public async Task EqualityConstraint_WithMatchingIntValue_PassesValidation() {
        var predicate = CompileConstraintPredicate<int>(new EqualityConstraint(42));
        await Assert.That(predicate(42)).IsTrue();
    }

    [Test]
    public async Task EqualityConstraint_WithDifferentIntValue_FailsValidation() {
        var predicate = CompileConstraintPredicate<int>(new EqualityConstraint(42));
        await Assert.That(predicate(43)).IsFalse();
    }

    [Test]
    public async Task EqualityConstraint_WithMatchingStringValue_PassesValidation() {
        var predicate = CompileConstraintPredicate<string>(new EqualityConstraint("hello"));
        await Assert.That(predicate("hello")).IsTrue();
    }

    [Test]
    public async Task EqualityConstraint_WithDifferentStringValue_FailsValidation() {
        var predicate = CompileConstraintPredicate<string>(new EqualityConstraint("hello"));
        await Assert.That(predicate("world")).IsFalse();
    }

    // RangeConstraint

    [Test]
    public async Task RangeConstraint_WithValueInRange_PassesValidation() {
        var predicate = CompileConstraintPredicate<int>(new RangeConstraint(0, 100));
        await Assert.That(predicate(50)).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_WithValueBelowMinimum_FailsValidation() {
        var predicate = CompileConstraintPredicate<int>(new RangeConstraint(0, 100));
        await Assert.That(predicate(-1)).IsFalse();
    }

    [Test]
    public async Task RangeConstraint_WithValueAboveMaximum_FailsValidation() {
        var predicate = CompileConstraintPredicate<int>(new RangeConstraint(0, 100));
        await Assert.That(predicate(101)).IsFalse();
    }

    [Test]
    public async Task RangeConstraint_WithValueAtMinimumBoundary_PassesValidation() {
        var predicate = CompileConstraintPredicate<int>(new RangeConstraint(0, 100));
        await Assert.That(predicate(0)).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_WithValueAtMaximumBoundary_PassesValidation() {
        var predicate = CompileConstraintPredicate<int>(new RangeConstraint(0, 100));
        await Assert.That(predicate(100)).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_WithOnlyMinimum_AndValueAboveMinimum_PassesValidation() {
        var predicate = CompileConstraintPredicate<int>(new RangeConstraint(10, null));
        await Assert.That(predicate(15)).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_WithOnlyMinimum_AndValueBelowMinimum_FailsValidation() {
        var predicate = CompileConstraintPredicate<int>(new RangeConstraint(10, null));
        await Assert.That(predicate(5)).IsFalse();
    }

    [Test]
    public async Task RangeConstraint_WithOnlyMaximum_AndValueBelowMaximum_PassesValidation() {
        var predicate = CompileConstraintPredicate<int>(new RangeConstraint(null, 10));
        await Assert.That(predicate(5)).IsTrue();
    }

    [Test]
    public async Task RangeConstraint_WithOnlyMaximum_AndValueAboveMaximum_FailsValidation() {
        var predicate = CompileConstraintPredicate<int>(new RangeConstraint(null, 10));
        await Assert.That(predicate(15)).IsFalse();
    }

    // LengthConstraint

    [Test]
    public async Task LengthConstraint_WithStringWithinBothBounds_PassesValidation() {
        var predicate = CompileConstraintPredicate<string>(new LengthConstraint(1, 10));
        await Assert.That(predicate("hello")).IsTrue();
    }

    [Test]
    public async Task LengthConstraint_WithStringBelowMinimumLength_FailsValidation() {
        var predicate = CompileConstraintPredicate<string>(new LengthConstraint(5, 10));
        await Assert.That(predicate("hi")).IsFalse();
    }

    [Test]
    public async Task LengthConstraint_WithStringAboveMaximumLength_FailsValidation() {
        var predicate = CompileConstraintPredicate<string>(new LengthConstraint(1, 5));
        await Assert.That(predicate("toolongstring")).IsFalse();
    }

    [Test]
    public async Task LengthConstraint_WithStringAtMinimumBoundary_PassesValidation() {
        var predicate = CompileConstraintPredicate<string>(new LengthConstraint(5, 10));
        await Assert.That(predicate("hello")).IsTrue();
    }

    [Test]
    public async Task LengthConstraint_WithStringAtMaximumBoundary_PassesValidation() {
        var predicate = CompileConstraintPredicate<string>(new LengthConstraint(1, 5));
        await Assert.That(predicate("hello")).IsTrue();
    }

    [Test]
    public async Task LengthConstraint_WithOnlyMinimumLength_AndLongEnoughString_PassesValidation() {
        var predicate = CompileConstraintPredicate<string>(new LengthConstraint(3, null));
        await Assert.That(predicate("hello")).IsTrue();
    }

    [Test]
    public async Task LengthConstraint_WithOnlyMinimumLength_AndShortString_FailsValidation() {
        var predicate = CompileConstraintPredicate<string>(new LengthConstraint(3, null));
        await Assert.That(predicate("hi")).IsFalse();
    }

    [Test]
    public async Task LengthConstraint_WithOnlyMaximumLength_AndShortString_PassesValidation() {
        var predicate = CompileConstraintPredicate<string>(new LengthConstraint(null, 10));
        await Assert.That(predicate("hello")).IsTrue();
    }

    [Test]
    public async Task LengthConstraint_WithOnlyMaximumLength_AndLongString_FailsValidation() {
        var predicate = CompileConstraintPredicate<string>(new LengthConstraint(null, 3));
        await Assert.That(predicate("toolong")).IsFalse();
    }

    // ConstraintSet

    [Test]
    public async Task ConstraintSet_WithAllConstraintsPassing_PassesValidation() {
        var constraint = new ConstraintSet(ConstraintAggregationStrategy.All, [
            new RangeConstraint(0, 100),
            new EqualityConstraint(42)
        ]);
        var predicate = CompileConstraintPredicate<int>(constraint);
        await Assert.That(predicate(42)).IsTrue();
    }

    [Test]
    public async Task ConstraintSet_WithOneConstraintFailing_FailsValidation() {
        var constraint = new ConstraintSet(ConstraintAggregationStrategy.All, [
            new RangeConstraint(0, 100),
            new EqualityConstraint(42)
        ]);
        var predicate = CompileConstraintPredicate<int>(constraint);
        await Assert.That(predicate(50)).IsFalse();
    }

    [Test]
    public async Task ConstraintSet_WithEmptyConstraintList_PassesValidation() {
        var constraint = new ConstraintSet(ConstraintAggregationStrategy.All, []);
        var predicate = CompileConstraintPredicate<int>(constraint);
        await Assert.That(predicate(42)).IsTrue();
    }

    [Test]
    public async Task ConstraintSet_WithCompatibleConstraints_UsesSharedApplicableCategories() {
        var constraint = new ConstraintSet(ConstraintAggregationStrategy.All, [
            new RangeConstraint(0, 100),
            new EqualityConstraint(42)
        ]);

        await Assert.That(constraint.ApplicableCategories).IsEqualTo(TypeCategory.Numeric | TypeCategory.Temporal);
    }

    [Test]
    public async Task ConstraintSet_WithUniversalConstraintsOnly_HasUniversalApplicableCategories() {
        var constraint = new ConstraintSet(ConstraintAggregationStrategy.All, [
            new NotNullConstraint(),
            new EqualityConstraint(42)
        ]);

        await Assert.That(constraint.ApplicableCategories).IsEqualTo(TypeCategory.None);
    }

    [Test]
    public async Task ConstraintSet_WithIncompatibleConstraints_ThrowsArgumentException() {
        await Assert.ThrowsAsync<ArgumentException>(async () => {
            _ = new ConstraintSet(ConstraintAggregationStrategy.All, [
                new RangeConstraint(0, 100),
                new LengthConstraint(1, 10)
            ]);

            await Task.CompletedTask;
        });
    }

    private static Func<T, bool> CompileConstraintPredicate<T>(Constraint constraint) {
        var param = new Parameter("value", TypeReference.To<T>());
        var interpretation = constraint.ToInterpretationNode(param);
        return interpretation.CompileLambda<Func<T, bool>>((param, typeof(T)));
    }
}