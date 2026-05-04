using Poly.Data.Modeling;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Data.Modeling;

public class PolicyTests {
    [Test]
    public async Task Stage_AddPolicy_ExposesPolicyInEnumeration() {
        var domain = DomainTestFactory.CreateDomain();
        var stage = new Stage(domain, "Review");
        var policy = new Policy(domain, "ReviewPolicy");

        MutationApply.AddPolicy(stage, policy);

        await Assert.That(stage.Policies.Contains(policy)).IsTrue();
    }

    [Test]
    public async Task Stage_RemovePolicy_RemovesPolicyFromEnumeration() {
        var domain = DomainTestFactory.CreateDomain();
        var stage = new Stage(domain, "Review");
        var policy = new Policy(domain, "ReviewPolicy");

        MutationApply.AddPolicy(stage, policy);
        MutationApply.RemovePolicy(stage, policy);

        await Assert.That(stage.Policies.Contains(policy)).IsFalse();
    }

    [Test]
    public async Task Policy_AllAggregation_WithOneFalseClause_FailsValidation() {
        var domain = DomainTestFactory.CreateDomain();
        var policy = new Policy(domain, "AgeGate") { AggregationStrategy = PolicyAggregationStrategy.All };

        var intType = new Primitive(domain, "int", TypeCategory.Integer);
        var boolType = new Primitive(domain, "bool", TypeCategory.Primitive);

        MutationApply.AddRule(policy, new PropertyRule(domain, "AgeGate_AgeRule",
            new Property(domain, nameof(PersonInput.Age), intType),
            new RangeConstraint(18, null)));

        MutationApply.AddRule(policy, new PropertyRule(domain, "AgeGate_VerifiedRule",
            new Property(domain, nameof(PersonInput.IsVerified), boolType),
            new EqualityConstraint(true)));

        var predicate = CompilePolicyPredicate<PersonInput>(policy);

        await Assert.That(predicate(new PersonInput(24, false))).IsFalse();
    }

    [Test]
    public async Task Policy_AnyAggregation_WithOneTrueClause_PassesValidation() {
        var domain = DomainTestFactory.CreateDomain();
        var policy = new Policy(domain, "AgeOrAdmin") { AggregationStrategy = PolicyAggregationStrategy.Any };

        var intType = new Primitive(domain, "int", TypeCategory.Integer);
        var boolType = new Primitive(domain, "bool", TypeCategory.Primitive);

        MutationApply.AddRule(policy, new PropertyRule(domain, "AgeRule",
            new Property(domain, nameof(AccessRequest.Age), intType),
            new RangeConstraint(21, null)));

        MutationApply.AddRule(policy, new PropertyRule(domain, "AdminRule",
            new Property(domain, nameof(AccessRequest.IsAdmin), boolType),
            new EqualityConstraint(true)));

        var predicate = CompilePolicyPredicate<AccessRequest>(policy);

        await Assert.That(predicate(new AccessRequest(16, true))).IsTrue();
    }

    [Test]
    public async Task Policy_CrossPropertyRule_CanRepresentCrossPropertyConstraint() {
        var domain = DomainTestFactory.CreateDomain();
        var policy = new Policy(domain, "DateWindow") { AggregationStrategy = PolicyAggregationStrategy.All };

        var instantType = new Primitive(domain, "instant", TypeCategory.Instant);

        MutationApply.AddRule(policy, new CrossPropertyRule(domain, "StartBeforeEnd",
            new Property(domain, nameof(DateWindow.StartUtc), instantType),
            new Property(domain, nameof(DateWindow.EndUtc), instantType),
            DomainComparisonOperator.LessThanOrEqual));

        var predicate = CompilePolicyPredicate<DateWindow>(policy);

        await Assert.That(predicate(new DateWindow(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)))).IsTrue();

        await Assert.That(predicate(new DateWindow(
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)))).IsFalse();
    }

    private static Func<T, bool> CompilePolicyPredicate<T>(Policy policy) {
        var p = new Parameter("subject", TypeReference.To<T>());
        var interpretation = DomainLoweringGenerator.LowerPolicy(policy, p);
        return interpretation.CompileLambda<Func<T, bool>>([(p, typeof(T))]);
    }

    private sealed record PersonInput(int Age, bool IsVerified);

    private sealed record AccessRequest(int Age, bool IsAdmin);

    private sealed record DateWindow(DateTime StartUtc, DateTime EndUtc);
}