using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling;

public class DomainQueryExtensionsTests {
    [Test]
    public async Task Domain_GetAvailableEntities_IncludesSupportCase() {
        var domain = BuildSupportCaseDomain();

        var names = domain.GetAvailableEntities().Select(entity => entity.Name).ToArray();

        await Assert.That(names).Contains("SupportCase");
    }

    [Test]
    public async Task Domain_GetAvailableRelationships_IncludesCustomerCases() {
        var domain = BuildSupportCaseDomain();

        var names = domain.GetAvailableRelationships().Select(relationship => relationship.Name).ToArray();

        await Assert.That(names).Contains("CustomerCases");
    }

    [Test]
    public async Task Domain_FindEntity_ReturnsExpectedEntity() {
        var domain = BuildSupportCaseDomain();

        var supportCase = domain.FindEntity("SupportCase");

        await Assert.That(supportCase).IsNotNull();
    }

    [Test]
    public async Task Domain_RequireEntity_WhenMissing_ThrowsInvalidOperationException() {
        var domain = BuildSupportCaseDomain();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            _ = domain.RequireEntity("DoesNotExist");
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task Entity_FindAction_IsLocalOnly() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);
        var action = new DomainAction(domain, "Approve", parent);
        MutationApply.AddAction(parent, action);

        var childLocal = child.FindAction("Approve");

        await Assert.That(childLocal).IsNull();
    }

    [Test]
    public async Task Entity_FindActionInHierarchy_FindsParentAction() {
        var domain = DomainTestFactory.CreateDomain();
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);
        var action = new DomainAction(domain, "Approve", parent);
        MutationApply.AddAction(parent, action);

        var resolved = child.FindActionInHierarchy("Approve");

        await Assert.That(ReferenceEquals(resolved, action)).IsTrue();
    }

    [Test]
    public async Task Stage_FindActionInHierarchy_FindsParentStageAction() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Case");
        var parentStage = new Stage(domain, "Parent");
        var childStage = new Stage(domain, "Child") { Parent = parentStage };
        var action = new DomainAction(domain, "Escalate", entity);
        MutationApply.AddAction(parentStage, action);

        var resolved = childStage.FindActionInHierarchy("Escalate");

        await Assert.That(ReferenceEquals(resolved, action)).IsTrue();
    }

    [Test]
    public async Task Stage_GetAvailableActionsInHierarchy_IncludesInheritedActions() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var assigned = supportCase.RequireStage("Assigned");

        var names = assigned.GetAvailableActionsInHierarchy().Select(action => action.Name).ToArray();

        await Assert.That(names).Contains("AddNote");
        await Assert.That(names).Contains("Resolve");
    }

    [Test]
    public async Task Action_RequireEffect_ReturnsTypedEffect() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var assignAction = supportCase.RequireStage("New").RequireAction("Assign");

        var publish = assignAction.RequireEffect<PublishEvent>();

        await Assert.That(publish.Event.Name).IsEqualTo("CaseAssigned");
    }

    [Test]
    public async Task Action_GetAvailablePublishedEvents_IncludesCaseAssigned() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var assignAction = supportCase.RequireStage("New").RequireAction("Assign");

        var names = assignAction.GetAvailablePublishedEvents().Select(@event => @event.Name).ToArray();

        await Assert.That(names).Contains("CaseAssigned");
    }

    [Test]
    public async Task Action_GetAvailableTransitionTargets_IncludesAssigned() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var assignAction = supportCase.RequireStage("New").RequireAction("Assign");

        var names = assignAction.GetAvailableTransitionTargets().Select(stage => stage.Name).ToArray();

        await Assert.That(names).Contains("Assigned");
    }

    [Test]
    public async Task Action_RequireParameter_ReturnsExpectedParameter() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var resolveAction = supportCase.RequireStage("InProgress").RequireAction("Resolve");

        var parameter = resolveAction.RequireParameter("ResolutionSummary");

        await Assert.That(parameter.Name).IsEqualTo("ResolutionSummary");
    }

    [Test]
    public async Task Action_GetCapabilityView_ContainsExpectedCapabilities() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var assignAction = supportCase.RequireStage("New").RequireAction("Assign");

        var capabilities = assignAction.GetCapabilityView();

        await Assert.That(capabilities.ActionName).IsEqualTo("Assign");
        await Assert.That(capabilities.Parameters.Select(parameter => parameter.Name)).Contains("Agent");
        await Assert.That(capabilities.PublishedEvents.Select(@event => @event.Name)).Contains("CaseAssigned");
        await Assert.That(capabilities.TransitionTargets.Select(stage => stage.Name)).Contains("Assigned");
        await Assert.That(capabilities.EffectTypes.Any(type => type == typeof(PublishEvent))).IsTrue();
        await Assert.That(capabilities.EffectTypes.Any(type => type == typeof(StageTransition))).IsTrue();
    }

    [Test]
    public async Task Stage_GetCapabilityView_ContainsLocalAndEffectiveActions() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var assigned = supportCase.RequireStage("Assigned");

        var capabilities = assigned.GetCapabilityView();

        await Assert.That(capabilities.StageName).IsEqualTo("Assigned");
        await Assert.That(capabilities.LocalActions.Count).IsEqualTo(0);
        await Assert.That(capabilities.EffectiveActions.Select(action => action.ActionName)).Contains("AddNote");
        await Assert.That(capabilities.EffectiveActions.Select(action => action.ActionName)).Contains("Resolve");
    }

    [Test]
    public async Task Domain_FindRelationshipsBySource_ReturnsMatchingRelationships() {
        var domain = BuildSupportCaseDomain();
        var customer = domain.RequireEntity("Customer");

        var relationships = domain.FindRelationshipsBySource(customer).Select(r => r.Name).ToArray();

        await Assert.That(relationships).Contains("CustomerCases");
        await Assert.That(relationships).Contains("CustomerNotes");
    }

    [Test]
    public async Task Relationship_GetCapabilityView_ContainsCoreMetadata() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.RequireRelationship("CustomerCases");

        var capability = relationship.GetCapabilityView();

        await Assert.That(capability.RelationshipName).IsEqualTo("CustomerCases");
        await Assert.That(capability.Cardinality).IsEqualTo(RelationshipCardinality.OneToMany);
        await Assert.That(capability.SourceOwnsTarget).IsTrue();
        await Assert.That(capability.Source).IsTypeOf<Entity>();
        await Assert.That(capability.Target).IsTypeOf<Entity>();
    }

    [Test]
    public async Task Relationship_GetCapabilityView_ContainsPropertiesStagesAndPolicies() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.RequireRelationship("AgentSupportCases");

        var capability = relationship.GetCapabilityView();

        await Assert.That(capability.Properties.Select(property => property.Name)).Contains("AssignedAt");
        await Assert.That(capability.Properties.Select(property => property.Name)).Contains("UnassignedAt");
        await Assert.That(capability.Stages.Select(stage => stage.Name)).Contains("Active");
        await Assert.That(capability.Stages.Select(stage => stage.Name)).Contains("Inactive");

        var customerNotes = domain.RequireRelationship("CustomerNotes").GetCapabilityView();
        await Assert.That(customerNotes.Policies.Select(policy => policy.Name)).Contains("OnlyAgentsCanCreateUserNotes");
        await Assert.That(customerNotes.Policies.Select(policy => policy.Name)).Contains("OnlyAgentsCanViewUserNotes");
    }

    private static Domain BuildSupportCaseDomain() {
        return MermaidTestDomainFactory.BuildSupportCaseDomain();
    }
}