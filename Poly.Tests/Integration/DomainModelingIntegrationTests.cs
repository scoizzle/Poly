using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Mermaid;
using Poly.Data.Modeling.TypeSystem;
using Poly.Tests.Data.Modeling;

namespace Poly.Tests.Integration;

public class DomainModelingIntegrationTests {
    // ─── Domain construction ───────────────────────────────────────────────────

    [Test]
    public async Task SupportCaseDomain_FullAssembly_DoesNotThrow() {
        var ex = Record(() => BuildSupportCaseDomain());
        await Assert.That(ex).IsNull();
    }

    // ─── Type system ──────────────────────────────────────────────────────────

    [Test]
    public async Task Domain_ContainsExpectedPrimitiveTypes() {
        var domain = BuildSupportCaseDomain();
        var names = domain.Types.OfType<Primitive>().Select(p => p.Name).ToArray();
        await Assert.That(names).Contains("string");
        await Assert.That(names).Contains("int");
        await Assert.That(names).Contains("bool");
        await Assert.That(names).Contains("instant");
    }

    [Test]
    public async Task Domain_ContainsExpectedEntityTypes() {
        var domain = BuildSupportCaseDomain();
        var names = domain.Types.OfType<Entity>().Select(e => e.Name).ToArray();
        await Assert.That(names).Contains("User");
        await Assert.That(names).Contains("Customer");
        await Assert.That(names).Contains("Agent");
        await Assert.That(names).Contains("Note");
        await Assert.That(names).Contains("SupportCase");
    }

    [Test]
    public async Task Domain_TypeCount_MatchesExpected() {
        var domain = BuildSupportCaseDomain();
        // 4 primitives + User + Customer + Agent + Note + SupportCase
        await Assert.That(domain.Types.Count).IsEqualTo(9);
    }

    // ─── Entity properties ─────────────────────────────────────────────────────

    [Test]
    public async Task User_HasExpectedProperties() {
        var domain = BuildSupportCaseDomain();
        var user = domain.RequireEntity("User");
        var names = user.Properties.Select(p => p.Name).ToArray();
        await Assert.That(names).Contains("Name");
        await Assert.That(names).Contains("Email");
        await Assert.That(user.Properties.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Agent_InheritsFromUser() {
        var domain = BuildSupportCaseDomain();
        var user = domain.RequireEntity("User");
        var agent = domain.RequireEntity("Agent");
        await Assert.That(ReferenceEquals(agent.ParentEntity, user)).IsTrue();
    }

    [Test]
    public async Task Note_HasExpectedProperties() {
        var domain = BuildSupportCaseDomain();
        var note = domain.RequireEntity("Note");
        var names = note.Properties.Select(p => p.Name).ToArray();
        await Assert.That(names).Contains("Content");
        await Assert.That(names).Contains("Author");
        await Assert.That(note.Properties.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Note_AuthorProperty_ReferencesUserEntity() {
        var domain = BuildSupportCaseDomain();
        var user = domain.RequireEntity("User");
        var note = domain.RequireEntity("Note");
        var authorProp = note.RequireProperty("Author");
        await Assert.That(ReferenceEquals(authorProp.Type, user)).IsTrue();
    }

    [Test]
    public async Task Customer_InheritsFromUser() {
        var domain = BuildSupportCaseDomain();
        var user = domain.RequireEntity("User");
        var customer = domain.RequireEntity("Customer");
        await Assert.That(ReferenceEquals(customer.ParentEntity, user)).IsTrue();
    }

    [Test]
    public async Task SupportCase_HasExpectedProperties() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var names = supportCase.Properties.Select(p => p.Name).ToArray();
        await Assert.That(names).Contains("Title");
        await Assert.That(names).Contains("Priority");
        await Assert.That(names).Contains("IsEscalated");
        await Assert.That(supportCase.Properties.Count).IsEqualTo(3);
    }

    [Test]
    public async Task SupportCase_PropertyTypes_ReferenceCorrectPrimitives() {
        var domain = BuildSupportCaseDomain();
        var stringType = domain.RequirePrimitive("string");
        var intType = domain.RequirePrimitive("int");
        var supportCase = domain.RequireEntity("SupportCase");
        var titleProp = supportCase.RequireProperty("Title");
        var priorityProp = supportCase.RequireProperty("Priority");
        await Assert.That(ReferenceEquals(titleProp.Type, stringType)).IsTrue();
        await Assert.That(ReferenceEquals(priorityProp.Type, intType)).IsTrue();
    }

    // ─── Stages ───────────────────────────────────────────────────────────────

    [Test]
    public async Task SupportCase_HasExpectedStages() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var names = supportCase.Stages.Select(s => s.Name).ToArray();
        await Assert.That(names).Contains("New");
        await Assert.That(names).Contains("InProgress");
        await Assert.That(names).Contains("Assigned");
        await Assert.That(names).Contains("Resolved");
    }

    [Test]
    public async Task AssignedStage_IsSubstageOfInProgress() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var assigned = supportCase.RequireStage("Assigned");
        var inProgress = supportCase.RequireStage("InProgress");
        await Assert.That(ReferenceEquals(assigned.Parent, inProgress)).IsTrue();
    }

    // ─── Actions and effects ───────────────────────────────────────────────────

    [Test]
    public async Task NewStage_HasAssignAction() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var newStage = supportCase.RequireStage("New");
        await Assert.That(newStage.Actions.Select(a => a.Name)).Contains("Assign");
    }

    [Test]
    public async Task AssignAction_HasStageTransitionEffect_TargetingAssigned() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var newStage = supportCase.RequireStage("New");
        var assignAction = newStage.RequireAction("Assign");
        var transition = assignAction.Effects.OfType<StageTransition>().SingleOrDefault();
        await Assert.That(transition).IsNotNull();
        await Assert.That(transition!.TargetStage.Name).IsEqualTo("Assigned");
    }

    [Test]
    public async Task AssignAction_HasPublishEventEffect_ForCaseAssigned() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var newStage = supportCase.RequireStage("New");
        var assignAction = newStage.RequireAction("Assign");
        var publish = assignAction.Effects.OfType<PublishEvent>().SingleOrDefault();
        await Assert.That(publish).IsNotNull();
        await Assert.That(publish!.Event.Name).IsEqualTo("CaseAssigned");
    }

    [Test]
    public async Task AssignAction_HasAgentParameter() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var newStage = supportCase.RequireStage("New");
        var assignAction = newStage.RequireAction("Assign");
        var parameterNames = assignAction.Parameters.Cast<Property>().Select(p => p.Name);
        await Assert.That(parameterNames).Contains("Agent");
    }

    [Test]
    public async Task AssignAction_AgentParameter_ReferencesAgentEntity() {
        var domain = BuildSupportCaseDomain();
        var agentEntity = domain.RequireEntity("Agent");
        var supportCase = domain.RequireEntity("SupportCase");
        var newStage = supportCase.RequireStage("New");
        var assignAction = newStage.RequireAction("Assign");
        var agentParam = assignAction.Parameters.Cast<Property>().Single(p => p.Name == "Agent");
        await Assert.That(ReferenceEquals(agentParam.Type, agentEntity)).IsTrue();
    }

    [Test]
    public async Task InProgressStage_HasAddNoteAndResolveActions() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var inProgress = supportCase.RequireStage("InProgress");
        var names = inProgress.Actions.Select(a => a.Name).ToArray();
        await Assert.That(names).Contains("AddNote");
        await Assert.That(names).Contains("Resolve");
    }

    [Test]
    public async Task AddNoteAction_HasCreateEntityInstanceEffect_ForNote() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var inProgress = supportCase.RequireStage("InProgress");
        var addNoteAction = inProgress.RequireAction("AddNote");
        var create = addNoteAction.Effects.OfType<CreateEntityInstance>().SingleOrDefault();
        await Assert.That(create).IsNotNull();
        await Assert.That(create!.EntityType.Name).IsEqualTo("Note");
    }

    [Test]
    public async Task AddNoteAction_CreateEntityInstance_UsesNoteParentEntityInheritance() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var note = domain.RequireEntity("Note");
        var inProgress = supportCase.RequireStage("InProgress");
        var addNoteAction = inProgress.RequireAction("AddNote");
        var create = addNoteAction.Effects.OfType<CreateEntityInstance>().Single();
        await Assert.That(ReferenceEquals(note.ParentEntity, supportCase)).IsTrue();
        await Assert.That(ReferenceEquals(create.EntityType.ParentEntity, supportCase)).IsTrue();
    }

    [Test]
    public async Task AddNoteAction_CreateEntityInstance_TargetsSpecificNoteStage() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var note = domain.RequireEntity("Note");
        var inProgress = supportCase.RequireStage("InProgress");
        var addNoteAction = inProgress.RequireAction("AddNote");
        var create = addNoteAction.Effects.OfType<CreateEntityInstance>().Single();
        await Assert.That(create.InitialStage).IsNotNull();
        await Assert.That(create.InitialStage!.Name).IsEqualTo("Draft");
        await Assert.That(note.Stages.Select(s => s.Name)).Contains("Draft");
    }

    [Test]
    public async Task AssignedStage_EffectiveActions_IncludeParentStageActions() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var assigned = supportCase.RequireStage("Assigned");
        var effectiveNames = assigned.GetEffectiveActions().Select(a => a.Name).ToArray();
        await Assert.That(effectiveNames).Contains("Resolve");
        await Assert.That(effectiveNames).Contains("AddNote");
    }

    [Test]
    public async Task ResolveAction_HasStageTransitionEffect_TargetingResolved() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var inProgress = supportCase.RequireStage("InProgress");
        var resolveAction = inProgress.RequireAction("Resolve");
        var transition = resolveAction.Effects.OfType<StageTransition>().SingleOrDefault();
        await Assert.That(transition).IsNotNull();
        await Assert.That(transition!.TargetStage.Name).IsEqualTo("Resolved");
    }

    [Test]
    public async Task ResolveAction_HasPublishEventEffect_ForCaseResolved() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var inProgress = supportCase.RequireStage("InProgress");
        var resolveAction = inProgress.RequireAction("Resolve");
        var publish = resolveAction.Effects.OfType<PublishEvent>().SingleOrDefault();
        await Assert.That(publish).IsNotNull();
        await Assert.That(publish!.Event.Name).IsEqualTo("CaseResolved");
        await Assert.That(publish.PropertyBindings.ContainsKey("ResolutionSummary")).IsTrue();
    }

    [Test]
    public async Task ResolveAction_HasResolutionSummaryParameter() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var inProgress = supportCase.RequireStage("InProgress");
        var resolveAction = inProgress.RequireAction("Resolve");
        var parameterNames = resolveAction.Parameters.Cast<Property>().Select(p => p.Name).ToArray();

        await Assert.That(parameterNames).Contains("ResolutionSummary");
    }

    // ─── Events ───────────────────────────────────────────────────────────────

    [Test]
    public async Task SupportCase_HasExpectedEvents() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var names = supportCase.Events.Select(e => e.Name).ToArray();
        await Assert.That(names).Contains("CaseAssigned");
        await Assert.That(names).Contains("CaseResolved");
    }

    [Test]
    public async Task CaseAssignedEvent_HasAssignedToProperty() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var caseAssigned = supportCase.RequireEvent("CaseAssigned");
        await Assert.That(caseAssigned.Properties.Select(p => p.Name)).Contains("AssignedTo");
    }

    [Test]
    public async Task CaseAssignedEvent_AssignedToProperty_ReferencesAgentEntity() {
        var domain = BuildSupportCaseDomain();
        var agentEntity = domain.RequireEntity("Agent");
        var supportCase = domain.RequireEntity("SupportCase");
        var caseAssigned = supportCase.RequireEvent("CaseAssigned");
        var assignedTo = caseAssigned.RequireProperty("AssignedTo");
        await Assert.That(ReferenceEquals(assignedTo.Type, agentEntity)).IsTrue();
    }

    [Test]
    public async Task CaseResolvedEvent_HasResolutionSummaryProperty() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var caseResolved = supportCase.RequireEvent("CaseResolved");

        await Assert.That(caseResolved.Properties.Select(p => p.Name)).Contains("ResolutionSummary");
    }

    // ─── Policies ─────────────────────────────────────────────────────────────

    [Test]
    public async Task SupportCase_HasRequireTitlePolicy() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        await Assert.That(supportCase.Policies.Select(p => p.Name)).Contains("RequireTitle");
    }

    [Test]
    public async Task CustomerNotesRelationship_HasOnlyAgentsCanCreateAndViewUserNotesPolicies() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.RequireRelationship("CustomerNotes");

        await Assert.That(relationship.Policies.Select(p => p.Name)).Contains("OnlyAgentsCanCreateUserNotes");
        await Assert.That(relationship.Policies.Select(p => p.Name)).Contains("OnlyAgentsCanViewUserNotes");
    }

    [Test]
    public async Task CustomerNotesRelationship_AgentOnlyUserNotesPolicies_TargetAuthorProperty() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.RequireRelationship("CustomerNotes");
        var createPolicy = relationship.Policies.Single(p => p.Name == "OnlyAgentsCanCreateUserNotes");
        var viewPolicy = relationship.Policies.Single(p => p.Name == "OnlyAgentsCanViewUserNotes");
        var createRule = createPolicy.Rules.OfType<PropertyRule>().Single();
        var viewRule = viewPolicy.Rules.OfType<PropertyRule>().Single();

        await Assert.That(createRule.Value).IsTypeOf<Property>();
        await Assert.That(((Property)createRule.Value).Name).IsEqualTo("Author");
        await Assert.That(viewRule.Value).IsTypeOf<Property>();
        await Assert.That(((Property)viewRule.Value).Name).IsEqualTo("Author");
    }

    [Test]
    public async Task SupportCase_TransitionRequirements_IncludeEntityRootPolicyRequirements() {
        var domain = BuildSupportCaseDomain();
        var analyzer = new DomainModelAnalyzer();
        var analysis = analyzer.Analyze(domain);

        var supportCase = domain.RequireEntity("SupportCase");
        var inProgress = supportCase.RequireStage("InProgress");

        var requirements = analysis.GetStageTransitionRequirements(inProgress);
        var targetRequiredNames = requirements.TargetRequiredProperties.Select(p => p.Name).ToArray();

        await Assert.That(targetRequiredNames).Contains("Title");
    }

    // ─── Relationships ────────────────────────────────────────────────────────

    [Test]
    public async Task AgentSupportCasesRelationship_ActiveStage_HasRequiredProperties() {
        var domain = BuildSupportCaseDomain();
        var analyzer = new DomainModelAnalyzer();
        var analysis = analyzer.Analyze(domain);

        var relationship = domain.RequireRelationship("AgentSupportCases");
        var active = relationship.RequireStage("Active");

        var requirements = analysis.GetStageTransitionRequirements(active);
        var targetRequiredNames = requirements.TargetRequiredProperties.Select(p => p.Name).ToArray();

        await Assert.That(targetRequiredNames).Contains("AssignedAt");
    }

    [Test]
    public async Task AgentSupportCasesRelationship_InactiveStage_HasRequiredProperties() {
        var domain = BuildSupportCaseDomain();
        var analyzer = new DomainModelAnalyzer();
        var analysis = analyzer.Analyze(domain);

        var relationship = domain.RequireRelationship("AgentSupportCases");
        var inactive = relationship.RequireStage("Inactive");

        var requirements = analysis.GetStageTransitionRequirements(inactive);
        var targetRequiredNames = requirements.TargetRequiredProperties.Select(p => p.Name).ToArray();

        await Assert.That(targetRequiredNames).Contains("UnassignedAt");
    }

    // ─── Relationships ────────────────────────────────────────────────────────

    [Test]
    public async Task Domain_HasCustomerCasesRelationship() {
        var domain = BuildSupportCaseDomain();
        await Assert.That(domain.Relationships.Select(r => r.Name)).Contains("CustomerCases");
    }

    [Test]
    public async Task CustomerCasesRelationship_IsOneToManyOwnership() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.RequireRelationship("CustomerCases");
        await Assert.That(relationship.Cardinality).IsEqualTo(RelationshipCardinality.OneToMany);
        await Assert.That(relationship.SourceOwnsTarget).IsTrue();
    }

    [Test]
    public async Task CustomerCasesRelationship_ConnectsCustomerToSupportCase() {
        var domain = BuildSupportCaseDomain();
        var customer = domain.RequireEntity("Customer");
        var supportCase = domain.RequireEntity("SupportCase");
        var relationship = domain.RequireRelationship("CustomerCases");
        await Assert.That(ReferenceEquals(relationship.Source, customer)).IsTrue();
        await Assert.That(ReferenceEquals(relationship.Target, supportCase)).IsTrue();
    }

    [Test]
    public async Task Customer_OutboundRelationships_ContainsCustomerCases() {
        var domain = BuildSupportCaseDomain();
        var customer = domain.RequireEntity("Customer");
        await Assert.That(customer.Relationships.Select(r => r.Name)).Contains("CustomerCases");
    }

    [Test]
    public async Task Domain_HasSupportCaseNotesRelationship() {
        var domain = BuildSupportCaseDomain();
        await Assert.That(domain.Relationships.Select(r => r.Name)).Contains("SupportCaseNotes");
    }

    [Test]
    public async Task SupportCaseNotesRelationship_IsOneToManyOwnership() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.RequireRelationship("SupportCaseNotes");
        await Assert.That(relationship.Cardinality).IsEqualTo(RelationshipCardinality.OneToMany);
        await Assert.That(relationship.SourceOwnsTarget).IsTrue();
    }

    [Test]
    public async Task SupportCaseNotesRelationship_ConnectsSupportCaseToNote() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var note = domain.RequireEntity("Note");
        var relationship = domain.RequireRelationship("SupportCaseNotes");
        await Assert.That(ReferenceEquals(relationship.Source, supportCase)).IsTrue();
        await Assert.That(ReferenceEquals(relationship.Target, note)).IsTrue();
    }

    [Test]
    public async Task SupportCase_OutboundRelationships_ContainsSupportCaseNotes() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        await Assert.That(supportCase.Relationships.Select(r => r.Name)).Contains("SupportCaseNotes");
    }

    [Test]
    public async Task Domain_HasCustomerNotesRelationship() {
        var domain = BuildSupportCaseDomain();
        await Assert.That(domain.Relationships.Select(r => r.Name)).Contains("CustomerNotes");
    }

    [Test]
    public async Task CustomerNotesRelationship_IsOneToMany_NonOwnership() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.RequireRelationship("CustomerNotes");
        await Assert.That(relationship.Cardinality).IsEqualTo(RelationshipCardinality.OneToMany);
        await Assert.That(relationship.SourceOwnsTarget).IsFalse();
    }

    [Test]
    public async Task CustomerNotesRelationship_ConnectsCustomerToNote() {
        var domain = BuildSupportCaseDomain();
        var customer = domain.RequireEntity("Customer");
        var note = domain.RequireEntity("Note");
        var relationship = domain.RequireRelationship("CustomerNotes");
        await Assert.That(ReferenceEquals(relationship.Source, customer)).IsTrue();
        await Assert.That(ReferenceEquals(relationship.Target, note)).IsTrue();
    }

    [Test]
    public async Task Customer_OutboundRelationships_ContainsCustomerNotes() {
        var domain = BuildSupportCaseDomain();
        var customer = domain.RequireEntity("Customer");
        await Assert.That(customer.Relationships.Select(r => r.Name)).Contains("CustomerNotes");
    }

    [Test]
    public async Task Domain_HasAgentSupportCasesRelationship() {
        var domain = BuildSupportCaseDomain();
        await Assert.That(domain.Relationships.Select(r => r.Name)).Contains("AgentSupportCases");
    }

    [Test]
    public async Task AgentSupportCasesRelationship_IsManyToMany_NonOwnership() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.RequireRelationship("AgentSupportCases");
        await Assert.That(relationship.Cardinality).IsEqualTo(RelationshipCardinality.ManyToMany);
        await Assert.That(relationship.SourceOwnsTarget).IsFalse();
    }

    [Test]
    public async Task AgentSupportCasesRelationship_ConnectsAgentToSupportCase() {
        var domain = BuildSupportCaseDomain();
        var agent = domain.RequireEntity("Agent");
        var supportCase = domain.RequireEntity("SupportCase");
        var relationship = domain.RequireRelationship("AgentSupportCases");
        await Assert.That(ReferenceEquals(relationship.Source, agent)).IsTrue();
        await Assert.That(ReferenceEquals(relationship.Target, supportCase)).IsTrue();
    }

    [Test]
    public async Task AgentSupportCasesRelationship_HasAssignmentTimestampProperties() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.RequireRelationship("AgentSupportCases");
        var names = relationship.Properties.Select(p => p.Name).ToArray();
        await Assert.That(names).Contains("AssignedAt");
        await Assert.That(names).Contains("UnassignedAt");
    }

    [Test]
    public async Task AgentSupportCasesRelationship_HasActiveAndInactiveStages() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.RequireRelationship("AgentSupportCases");
        var names = relationship.Stages.Select(s => s.Name).ToArray();
        await Assert.That(names).Contains("Active");
        await Assert.That(names).Contains("Inactive");
    }

    [Test]
    public async Task AgentSupportCasesRelationship_InactiveStage_IsNotChildOfActive() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.RequireRelationship("AgentSupportCases");
        var active = relationship.RequireStage("Active");
        var inactive = relationship.RequireStage("Inactive");
        await Assert.That(ReferenceEquals(inactive.Parent, active)).IsFalse();
    }

    [Test]
    public async Task AgentSupportCasesRelationship_InactiveStage_IsDefinedAfterActive() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.RequireRelationship("AgentSupportCases");
        var names = relationship.Stages.Select(s => s.Name).ToArray();
        await Assert.That(Array.IndexOf(names, "Active")).IsLessThan(Array.IndexOf(names, "Inactive"));
    }

    [Test]
    public async Task AgentSupportCasesRelationship_TimestampPropertyTypes_ReferenceInstantPrimitive() {
        var domain = BuildSupportCaseDomain();
        var instant = domain.RequirePrimitive("instant");
        var relationship = domain.RequireRelationship("AgentSupportCases");
        var assignedAt = relationship.RequireProperty("AssignedAt");
        var unassignedAt = relationship.RequireProperty("UnassignedAt");
        await Assert.That(ReferenceEquals(assignedAt.Type, instant)).IsTrue();
        await Assert.That(ReferenceEquals(unassignedAt.Type, instant)).IsTrue();
    }

    [Test]
    public async Task Agent_OutboundRelationships_ContainsAgentSupportCases() {
        var domain = BuildSupportCaseDomain();
        var agent = domain.RequireEntity("Agent");
        await Assert.That(agent.Relationships.Select(r => r.Name)).Contains("AgentSupportCases");
    }

    // ─── Mermaid diagram ──────────────────────────────────────────────────────

    [Test]
    public async Task SupportCaseDomain_GeneratesMermaidClassDiagram() {
        var domain = BuildSupportCaseDomain();
        var diagram = new MermaidDomainDiagramGenerator().Generate(domain);
        File.WriteAllText("/tmp/support-case-domain.mmd", diagram);
        await Assert.That(diagram).StartsWith("classDiagram");
    }

    [Test]
    public async Task SupportCaseDomain_GeneratesMermaidStateDiagram() {
        var domain = BuildSupportCaseDomain();
        var diagram = new MermaidDomainStateDiagramGenerator().Generate(domain);
        File.WriteAllText("/tmp/support-case-state.mmd", diagram);
        await Assert.That(diagram).StartsWith("stateDiagram-v2");
    }

    [Test]
    public async Task SupportCaseDomain_GeneratesMermaidSequenceDiagram_ForAssignAction() {
        var domain = BuildSupportCaseDomain();
        var supportCase = domain.RequireEntity("SupportCase");
        var newStage = supportCase.RequireStage("New");
        var assign = newStage.RequireAction("Assign");
        var diagram = new MermaidActionSequenceDiagramGenerator().Generate(assign);
        File.WriteAllText("/tmp/support-case-assign-sequence.mmd", diagram);
        await Assert.That(diagram).StartsWith("sequenceDiagram");
    }

    // ─── Domain factory ────────────────────────────────────────────────────────

    private static Domain BuildSupportCaseDomain() {
        return MermaidTestDomainFactory.BuildSupportCaseDomain();
    }

    private static Exception? Record(System.Action action) {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }
}