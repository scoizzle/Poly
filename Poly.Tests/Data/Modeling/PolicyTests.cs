using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;
using Poly.Tests.TestHelpers;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling;

public class PolicyTests {
    [Test]
    public async Task Policy_ActorTypeRule_InvalidActorType_ReportsDiagnostic() {
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Order");
        MutationApply.AddType(domain, entity);

        // ActorType not added to domain
        var missingActor = new Actor(domain, "Ghost");
        var policy = new Policy(domain, "AuthPolicy");
        MutationApply.AddRule(policy, new ActorTypeRule(domain, "GhostRule", missingActor));

        var result = MutationApply.AddPolicy(entity, policy);

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Code == DomainModelDiagnosticCodes.PolicyActorReference);
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains("Ghost");

        var analysisResult = DomainModelAnalyzer.Analyze(domain);
        var analysisDiagnostic = analysisResult.Diagnostics.FirstOrDefault(d => d.Code == DomainModelDiagnosticCodes.PolicyActorReference);
        await Assert.That(analysisDiagnostic).IsNull();
    }

    [Test]
    public async Task Policy_ActorPropertyRule_InvalidActorProperty_ReportsDiagnostic() {
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Order");
        MutationApply.AddType(domain, entity);

        // Property not attached to any actor
        var strayProperty = new Property(domain, "Stray", new Primitive(domain, "string", TypeCategory.Text));
        var policy = new Policy(domain, "AuthPolicy");
        MutationApply.AddRule(policy, new ActorPropertyRule(domain, "StrayRule", strayProperty, new EqualityConstraint("foo")));
        var result = MutationApply.AddPolicy(entity, policy);

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Code == DomainModelDiagnosticCodes.PolicyActorReference);
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains("Stray");

        var analysisResult = DomainModelAnalyzer.Analyze(domain);
        var analysisDiagnostic = analysisResult.Diagnostics.FirstOrDefault(d => d.Code == DomainModelDiagnosticCodes.PolicyActorReference);
        await Assert.That(analysisDiagnostic).IsNull();
    }

    [Test]
    public async Task Policy_CompositeRule_InvalidChild_ReportsDiagnostic() {
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Order");
        domain.CreateMutation().AddType(entity).Apply(null);

        // Left child is invalid ActorTypeRule
        var missingActor = new Actor(domain, "Ghost");
        var left = new ActorTypeRule(domain, "GhostRule", missingActor);
        // Right child is valid dummy rule
        var right = new PropertyRule(domain, "Ok", new Property(domain, "P", new Primitive(domain, "int", TypeCategory.Integer)), new EqualityConstraint(1));
        var composite = new CompositeRule(domain, "Composite", left, right, LogicalOperator.And);
        var policy = new Policy(domain, "CompositePolicy");
        MutationApply.AddRule(policy, composite);
        var result = MutationApply.AddPolicy(entity, policy);

        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Code == DomainModelDiagnosticCodes.PolicyActorReference);
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains("Ghost");

        var analysisResult = DomainModelAnalyzer.Analyze(domain);
        var analysisDiagnostic = analysisResult.Diagnostics.FirstOrDefault(d => d.Code == DomainModelDiagnosticCodes.PolicyActorReference);
        await Assert.That(analysisDiagnostic).IsNull();
    }

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

    [Test]
    public async Task Action_AddPolicy_ExposesPolicyInEnumeration() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Order");
        var action = new DomainAction(domain, "Submit", entity);
        var policy = new Policy(domain, "SubmitPolicy");

        MutationApply.AddPolicy(action, policy);

        await Assert.That(action.Policies.Contains(policy)).IsTrue();
    }

    [Test]
    public async Task Action_RemovePolicy_RemovesPolicyFromEnumeration() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Order");
        var action = new DomainAction(domain, "Submit", entity);
        var policy = new Policy(domain, "SubmitPolicy");

        MutationApply.AddPolicy(action, policy);
        MutationApply.RemovePolicy(action, policy);

        await Assert.That(action.Policies.Contains(policy)).IsFalse();
    }

    [Test]
    public async Task Action_FindPolicy_ReturnsNullWhenNotFound() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Order");
        var action = new DomainAction(domain, "Submit", entity);

        var result = action.FindPolicy("Missing");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Action_RequirePolicy_ThrowsWhenNotFound() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Order");
        var action = new DomainAction(domain, "Submit", entity);

        await Assert.That(() => action.RequirePolicy("Missing")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AddPolicyToActionIntent_RoundTrip_AttachesPolicyToAction() {
        var engine = new DomainMutationIntentEngine();
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Order", null);
        domain.CreateMutation().AddType(entity).Apply(null);

        engine.Apply(domain, new AddActionToEntityIntent("Order", "Submit"));
        engine.Apply(domain, new AddPolicyToActionIntent("Order", "Submit", "AuthPolicy"));

        var action = entity.RequireAction("Submit");
        await Assert.That(action.FindPolicy("AuthPolicy")).IsNotNull();
    }

    [Test]
    public async Task RemovePolicyFromActionIntent_RoundTrip_DetachesPolicyFromAction() {
        var engine = new DomainMutationIntentEngine();
        var domain = new Domain("TestDomain");
        var entity = new Entity(domain, "Order", null);
        domain.CreateMutation().AddType(entity).Apply(null);

        engine.Apply(domain, new AddActionToEntityIntent("Order", "Submit"));
        engine.Apply(domain, new AddPolicyToActionIntent("Order", "Submit", "AuthPolicy"));
        engine.Apply(domain, new RemovePolicyFromActionIntent("Order", "Submit", "AuthPolicy"));

        var action = entity.RequireAction("Submit");
        await Assert.That(action.FindPolicy("AuthPolicy")).IsNull();
    }

    private sealed record PersonInput(int Age, bool IsVerified);

    private sealed record AccessRequest(int Age, bool IsAdmin);

    private sealed record DateWindow(DateTime StartUtc, DateTime EndUtc);
}