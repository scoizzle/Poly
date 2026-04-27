using Poly.Data.Modeling;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Data.Modeling;

public class PolicyTests {
    [Test]
    public async Task Stage_AddPolicy_ExposesPolicyInEnumeration() {
        var domain = DomainTestFactory.CreateDomain();
        var stage = new Stage(domain, "Review");
        var policy = new Policy(domain, "ReviewPolicy");

        stage.AddPolicy(policy);

        await Assert.That(stage.Policies.Contains(policy)).IsTrue();
    }

    [Test]
    public async Task Stage_RemovePolicy_RemovesPolicyFromEnumeration() {
        var domain = DomainTestFactory.CreateDomain();
        var stage = new Stage(domain, "Review");
        var policy = new Policy(domain, "ReviewPolicy");
        stage.AddPolicy(policy);

        var wasRemoved = stage.RemovePolicy(policy);

        await Assert.That(wasRemoved).IsTrue();
        await Assert.That(stage.Policies.Contains(policy)).IsFalse();
    }

    [Test]
    public async Task Policy_AllAggregation_WithOneFalseClause_FailsValidation() {
        var domain = DomainTestFactory.CreateDomain();
        var policy = new Policy(domain, "AgeGate") { AggregationStrategy = PolicyAggregationStrategy.All };

        policy.AddRule(new PredicateRule {
            Predicate = subject => subject.GetMember(nameof(PersonInput.Age)).GreaterThanOrEqual(new Constant(18))
        });

        policy.AddRule(new PredicateRule {
            Predicate = subject => subject.GetMember(nameof(PersonInput.IsVerified)).Equal(new Constant(true))
        });

        var predicate = CompilePolicyPredicate<PersonInput>(policy);

        await Assert.That(predicate(new PersonInput(24, false))).IsFalse();
    }

    [Test]
    public async Task Policy_AnyAggregation_WithOneTrueClause_PassesValidation() {
        var domain = DomainTestFactory.CreateDomain();
        var policy = new Policy(domain, "AgeOrAdmin") { AggregationStrategy = PolicyAggregationStrategy.Any };

        policy.AddRule(new PredicateRule {
            Predicate = subject => subject.GetMember(nameof(AccessRequest.Age)).GreaterThanOrEqual(new Constant(21))
        });

        policy.AddRule(new PredicateRule {
            Predicate = subject => subject.GetMember(nameof(AccessRequest.IsAdmin)).Equal(new Constant(true))
        });

        var predicate = CompilePolicyPredicate<AccessRequest>(policy);

        await Assert.That(predicate(new AccessRequest(16, true))).IsTrue();
    }

    [Test]
    public async Task Policy_PredicateRule_CanRepresentCrossPropertyConstraint() {
        var domain = DomainTestFactory.CreateDomain();
        var policy = new Policy(domain, "DateWindow") { AggregationStrategy = PolicyAggregationStrategy.All };

        policy.AddRule(new PredicateRule {
            Predicate = subject => subject
                .GetMember(nameof(DateWindow.StartUtc))
                .LessThanOrEqual(subject.GetMember(nameof(DateWindow.EndUtc)))
        });

        var predicate = CompilePolicyPredicate<DateWindow>(policy);

        await Assert.That(predicate(new DateWindow(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)))).IsTrue();

        await Assert.That(predicate(new DateWindow(
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)))).IsFalse();
    }

    private static Func<T, bool> CompilePolicyPredicate<T>(Policy policy) {
        var parameter = new Parameter("subject", TypeReference.To<T>());
        var interpretation = policy.ToInterpretationNode(parameter);

        return interpretation.CompileLambda<Func<T, bool>>((parameter, typeof(T)));
    }

    private sealed record PersonInput(int Age, bool IsVerified);

    private sealed record AccessRequest(int Age, bool IsAdmin);

    private sealed record DateWindow(DateTime StartUtc, DateTime EndUtc);
}