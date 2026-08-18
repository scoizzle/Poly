using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Tests for <see cref="PolicySubject"/> invariants: validation, rejection of
/// forbidden subject types, and correct evaluation with allowed types.
/// </summary>
public class PolicySubjectInvariantTests {
    // ── Validation: forbidden types ──────────────────────────────

    [Test]
    public async Task Validate_NullSubject_Throws() {
        await Assert.That(() => PolicySubject.Validate(null))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Validate_DictionaryStringObject_Throws() {
        var dict = new Dictionary<string, object?> { ["Age"] = 25 };
        await Assert.That(() => PolicySubject.Validate(dict))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Validate_ExpandoObject_Throws() {
        dynamic expando = new System.Dynamic.ExpandoObject();
        expando.Age = 25;
        await Assert.That(() => PolicySubject.Validate((object)expando))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Validate_Record_Passes() {
        var record = new { Age = 25 };
        // Should not throw
        await Assert.That(() => PolicySubject.Validate(record))
            .ThrowsNothing();
    }

    [Test]
    public async Task Validate_StrictBag_Passes() {
        var bag = new PolicyTestSubjects.StrictBag(Age: 25, Name: "A", Status: "", Total: 0);
        await Assert.That(() => PolicySubject.Validate(bag))
            .ThrowsNothing();
    }

    // ── TryValidate ─────────────────────────────────────────────

    [Test]
    public async Task TryValidate_Dictionary_ReturnsErrorMessage() {
        var msg = PolicySubject.TryValidate(new Dictionary<string, object?>());
        await Assert.That(msg).IsNotNull();
        await Assert.That(msg).Contains("not supported");
    }

    [Test]
    public async Task TryValidate_Record_ReturnsNull() {
        var msg = PolicySubject.TryValidate(new { Age = 25 });
        await Assert.That(msg).IsNull();
    }

    // ── ValidateType (compile-time guard for CompileVMPredicate) ─

    [Test]
    public async Task ValidateType_Dictionary_Throws() {
        await Assert.That(() => PolicySubject.ValidateType<Dictionary<string, object?>>())
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ValidateType_Record_Passes() {
        await Assert.That(() => PolicySubject.ValidateType<PolicyTestSubjects.SampleAgeSubject>())
            .ThrowsNothing();
    }

    [Test]
    public async Task CompileVMPredicate_Dictionary_Throws() {
        var policy = new Policy("Age18",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18)));

        await Assert.That(() => policy.CompileVMPredicate<Dictionary<string, object?>>())
            .Throws<ArgumentException>();
    }

    // ── SampleFromAge ──────────────────────────────────────────

    [Test]
    public async Task SampleFromAge_EvaluatesCorrectly_Adult() {
        var policy = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18)));

        var adultSubj = PolicyTestSubjects.SampleFromAge(25);
        var minorSubj = PolicyTestSubjects.SampleFromAge(15);

        await Assert.That(policy.Evaluate(adultSubj)).IsTrue();
        await Assert.That(policy.Evaluate(minorSubj)).IsFalse();
    }

    [Test]
    public async Task SampleFromAge_BoundaryValues() {
        var policy = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18)));

        await Assert.That(policy.Evaluate(PolicyTestSubjects.SampleFromAge(18))).IsTrue();
        await Assert.That(policy.Evaluate(PolicyTestSubjects.SampleFromAge(17))).IsFalse();
        await Assert.That(policy.Evaluate(PolicyTestSubjects.SampleFromAge(0))).IsFalse();
    }

    // ── SampleFromBag ──────────────────────────────────────────

    [Test]
    public async Task SampleFromBag_EvaluatesCorrectly() {
        var policy = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18)));

        var adult = PolicyTestSubjects.SampleFromBag(name: "Alice", age: 25, status: null, total: null);
        var minor = PolicyTestSubjects.SampleFromBag(name: "Bob", age: 15, status: null, total: null);

        await Assert.That(policy.Evaluate(adult)).IsTrue();
        await Assert.That(policy.Evaluate(minor)).IsFalse();
    }

    [Test]
    public async Task SampleFromBag_NullDefaults_UseZeroAndEmpty() {
        var subj = PolicyTestSubjects.SampleFromBag(name: null, age: null, status: null, total: null);

        // Age defaults to 0 (non-nullable int)
        var policy = new Policy("IsZero",
            DomainExpression.Equal(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(0)));

        await Assert.That(policy.Evaluate(subj)).IsTrue();
    }

    // ── StrictBag via PolicyEvaluator ───────────────────────────

    [Test]
    public async Task StrictBag_WithMultipleProperties_EvaluatesCorrectly() {
        var policy = new Policy("LargeActive",
            DomainExpression.And(
                DomainExpression.GreaterThan(
                    DomainExpression.Property("Total"),
                    DomainExpression.Literal(100)),
                DomainExpression.Equal(
                    DomainExpression.Property("Status"),
                    DomainExpression.Literal("Active"))));

        var pass = new PolicyTestSubjects.StrictBag(Age: 0, Name: "", Status: "Active", Total: 200);
        var failTotal = new PolicyTestSubjects.StrictBag(Age: 0, Name: "", Status: "Active", Total: 50);
        var failStatus = new PolicyTestSubjects.StrictBag(Age: 0, Name: "", Status: "Cancelled", Total: 200);

        await Assert.That(policy.Evaluate(pass)).IsTrue();
        await Assert.That(policy.Evaluate(failTotal)).IsFalse();
        await Assert.That(policy.Evaluate(failStatus)).IsFalse();
    }

    // ── StrictBag evaluation (ad-hoc property bags) ─────────────

    [Test]
    public async Task StrictBag_EvaluatesAgeGuardCorrectly() {
        var adult = new PolicyTestSubjects.StrictBag(Age: 25, Name: "Alice", Status: "", Total: 0);
        var minor = new PolicyTestSubjects.StrictBag(Age: 15, Name: "Bob", Status: "", Total: 0);

        var policy = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18)));

        await Assert.That(policy.Evaluate(adult)).IsTrue();
        await Assert.That(policy.Evaluate(minor)).IsFalse();
    }

    [Test]
    public async Task StrictBag_WithZeroAge_EvaluatesCorrectly() {
        var subj = new PolicyTestSubjects.StrictBag(Age: 0, Name: "", Status: "", Total: 0);

        var policy = new Policy("IsZero",
            DomainExpression.Equal(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(0)));

        await Assert.That(policy.Evaluate(subj)).IsTrue();
    }

    [Test]
    public async Task StrictBag_AcceptsStringKeys() {
        var subj = new PolicyTestSubjects.StrictBag(Age: 0, Name: "Alice", Status: "", Total: 0);

        var policy = new Policy("IsAlice",
            DomainExpression.Equal(
                DomainExpression.Property("Name"),
                DomainExpression.Literal("Alice")));

        await Assert.That(policy.Evaluate(subj)).IsTrue();
    }

    [Test]
    public async Task StrictBag_WithAgeOnly_EvaluatesCorrectly() {
        var subj = new PolicyTestSubjects.StrictBag(Age: 25, Name: "", Status: "", Total: 0);

        var policy = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18)));

        await Assert.That(policy.Evaluate(subj)).IsTrue();
    }
}