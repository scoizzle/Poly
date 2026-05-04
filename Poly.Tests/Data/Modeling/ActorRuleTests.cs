using Poly.Data.Modeling;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Tests.Data.Modeling;

public class ActorRuleTests {
    // ── Construction ─────────────────────────────────────────────────────────

    [Test]
    public async Task ActorTypeRule_Constructor_SetsActorType() {
        var domain = DomainTestFactory.CreateDomain();
        var actor = new Actor(domain, "AdminUser", null);
        var rule = new ActorTypeRule(domain, "MustBeAdmin", actor);

        await Assert.That(rule.ActorType).IsEqualTo(actor);
        await Assert.That(rule.Name).IsEqualTo("MustBeAdmin");
    }

    [Test]
    public async Task ActorRoleRule_Constructor_SetsRole() {
        var domain = DomainTestFactory.CreateDomain();
        var rule = new ActorRoleRule(domain, "MustHaveEditorRole", "Editor");

        await Assert.That(rule.Role).IsEqualTo("Editor");
        await Assert.That(rule.Name).IsEqualTo("MustHaveEditorRole");
    }

    [Test]
    public async Task CompositeRule_And_CanCombineTwoRules() {
        var domain = DomainTestFactory.CreateDomain();
        var actor = new Actor(domain, "Reviewer", null);
        var left = new ActorTypeRule(domain, "IsReviewer", actor);
        var right = new ActorRoleRule(domain, "HasApproveRole", "Approve");

        var composite = new CompositeRule(domain, "ReviewerWithApproveRole", left, right, LogicalOperator.And);

        await Assert.That(composite.Left).IsEqualTo(left);
        await Assert.That(composite.Right).IsEqualTo(right);
        await Assert.That(composite.Operator).IsEqualTo(LogicalOperator.And);
    }

    [Test]
    public async Task CompositeRule_Or_CanCombineTwoRules() {
        var domain = DomainTestFactory.CreateDomain();
        var actorA = new Actor(domain, "AdminUser", null);
        var actorB = new Actor(domain, "SuperUser", null);
        var left = new ActorTypeRule(domain, "IsAdmin", actorA);
        var right = new ActorTypeRule(domain, "IsSuperUser", actorB);

        var composite = new CompositeRule(domain, "AdminOrSuperUser", left, right, LogicalOperator.Or);

        await Assert.That(composite.Operator).IsEqualTo(LogicalOperator.Or);
    }

    // ── Intent engine ─────────────────────────────────────────────────────────

    [Test]
    public async Task AddPolicyToEntityIntent_AppliesPolicy() {
        var engine = new DomainMutationIntentEngine();
        var domain = new Domain("TestDomain");
        domain.CreateMutation().AddType(new Entity(domain, "Order", null)).Apply(null);

        var result = engine.Apply(domain, new AddPolicyToEntityIntent("Order", "OrderPolicy"));

        var entity = domain.RequireEntity("Order");
        await Assert.That(entity.FindPolicy("OrderPolicy")).IsNotNull();
    }

    [Test]
    public async Task RemovePolicyFromEntityIntent_RemovesPolicy() {
        var engine = new DomainMutationIntentEngine();
        var domain = new Domain("TestDomain");
        domain.CreateMutation().AddType(new Entity(domain, "Order", null)).Apply(null);
        engine.Apply(domain, new AddPolicyToEntityIntent("Order", "OrderPolicy"));

        engine.Apply(domain, new RemovePolicyFromEntityIntent("Order", "OrderPolicy"));

        var entity = domain.RequireEntity("Order");
        await Assert.That(entity.FindPolicy("OrderPolicy")).IsNull();
    }

    [Test]
    public async Task AddActorTypeRuleToPolicyIntent_AddsRule() {
        var engine = new DomainMutationIntentEngine();
        var domain = new Domain("TestDomain");
        domain.CreateMutation()
            .AddType(new Entity(domain, "Order", null))
            .AddType(new Actor(domain, "AdminUser", null))
            .Apply(null);
        engine.Apply(domain, new AddPolicyToEntityIntent("Order", "AccessPolicy"));

        engine.Apply(domain, new AddActorTypeRuleToPolicyIntent("Order", "AccessPolicy", "MustBeAdmin", "AdminUser"));

        var policy = domain.RequireEntity("Order").RequirePolicy("AccessPolicy");
        var rule = policy.FindRule("MustBeAdmin");
        await Assert.That(rule).IsNotNull();
        await Assert.That(rule).IsTypeOf<ActorTypeRule>();
    }

    [Test]
    public async Task AddActorRoleRuleToPolicyIntent_AddsRule() {
        var engine = new DomainMutationIntentEngine();
        var domain = new Domain("TestDomain");
        domain.CreateMutation().AddType(new Entity(domain, "Order", null)).Apply(null);
        engine.Apply(domain, new AddPolicyToEntityIntent("Order", "AccessPolicy"));

        engine.Apply(domain, new AddActorRoleRuleToPolicyIntent("Order", "AccessPolicy", "HasEditorRole", "Editor"));

        var policy = domain.RequireEntity("Order").RequirePolicy("AccessPolicy");
        var rule = policy.RequireRule("HasEditorRole") as ActorRoleRule;
        await Assert.That(rule).IsNotNull();
        await Assert.That(rule!.Role).IsEqualTo("Editor");
    }

    [Test]
    public async Task AddCompositeRuleToPolicyIntent_CombinesExistingRules() {
        var engine = new DomainMutationIntentEngine();
        var domain = new Domain("TestDomain");
        domain.CreateMutation()
            .AddType(new Entity(domain, "Order", null))
            .AddType(new Actor(domain, "AdminUser", null))
            .Apply(null);
        engine.Apply(domain, new AddPolicyToEntityIntent("Order", "AccessPolicy"));
        engine.Apply(domain, new AddActorTypeRuleToPolicyIntent("Order", "AccessPolicy", "IsAdmin", "AdminUser"));
        engine.Apply(domain, new AddActorRoleRuleToPolicyIntent("Order", "AccessPolicy", "HasEditorRole", "Editor"));

        engine.Apply(domain, new AddCompositeRuleToPolicyIntent("Order", "AccessPolicy", "AdminOrEditor", "IsAdmin", "HasEditorRole", LogicalOperator.Or));

        var policy = domain.RequireEntity("Order").RequirePolicy("AccessPolicy");
        var composite = policy.RequireRule("AdminOrEditor") as CompositeRule;
        await Assert.That(composite).IsNotNull();
        await Assert.That(composite!.Operator).IsEqualTo(LogicalOperator.Or);
        await Assert.That(composite.Left.Name).IsEqualTo("IsAdmin");
        await Assert.That(composite.Right.Name).IsEqualTo("HasEditorRole");
    }

    [Test]
    public async Task RemoveRuleFromPolicyIntent_RemovesRule() {
        var engine = new DomainMutationIntentEngine();
        var domain = new Domain("TestDomain");
        domain.CreateMutation().AddType(new Entity(domain, "Order", null)).Apply(null);
        engine.Apply(domain, new AddPolicyToEntityIntent("Order", "AccessPolicy"));
        engine.Apply(domain, new AddActorRoleRuleToPolicyIntent("Order", "AccessPolicy", "HasEditorRole", "Editor"));

        engine.Apply(domain, new RemoveRuleFromPolicyIntent("Order", "AccessPolicy", "HasEditorRole"));

        var policy = domain.RequireEntity("Order").RequirePolicy("AccessPolicy");
        await Assert.That(policy.FindRule("HasEditorRole")).IsNull();
    }

    // ── Query helpers ─────────────────────────────────────────────────────────

    [Test]
    public async Task FindPolicy_OnEntity_ReturnsNullWhenNotFound() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Order", null);

        var result = entity.FindPolicy("NonExistent");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task RequirePolicy_OnEntity_ThrowsWhenNotFound() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Order", null);

        await Assert.That(() => entity.RequirePolicy("NonExistent")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task FindRule_OnPolicy_ReturnsNullWhenNotFound() {
        var domain = DomainTestFactory.CreateDomain();
        var policy = new Policy(domain, "TestPolicy");

        var result = policy.FindRule("NonExistent");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task RequireRule_OnPolicy_ThrowsWhenNotFound() {
        var domain = DomainTestFactory.CreateDomain();
        var policy = new Policy(domain, "TestPolicy");

        await Assert.That(() => policy.RequireRule("NonExistent")).Throws<InvalidOperationException>();
    }
}