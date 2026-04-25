using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;
using Poly.Tests.Data.Modeling;

using DomainAction = Poly.Data.Modeling.Action;
using DomainEvent = Poly.Data.Modeling.Event;

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
        await Assert.That(names).Contains("Customer");
        await Assert.That(names).Contains("Agent");
        await Assert.That(names).Contains("Note");
        await Assert.That(names).Contains("SupportCase");
    }

    [Test]
    public async Task Domain_TypeCount_MatchesExpected() {
        var domain = BuildSupportCaseDomain();
        // 4 primitives + Customer + Agent + Note + SupportCase
        await Assert.That(domain.Types.Count).IsEqualTo(8);
    }

    // ─── Entity properties ─────────────────────────────────────────────────────

    [Test]
    public async Task Agent_HasExpectedProperties() {
        var domain = BuildSupportCaseDomain();
        var agent = RequireEntity(domain, "Agent");
        var names = agent.Properties.Select(p => p.Name).ToArray();
        await Assert.That(names).Contains("Name");
        await Assert.That(names).Contains("Email");
        await Assert.That(agent.Properties.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Note_HasExpectedProperties() {
        var domain = BuildSupportCaseDomain();
        var note = RequireEntity(domain, "Note");
        var names = note.Properties.Select(p => p.Name).ToArray();
        await Assert.That(names).Contains("Content");
        await Assert.That(names).Contains("Author");
        await Assert.That(note.Properties.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Note_AuthorProperty_ReferencesAgentEntity() {
        var domain = BuildSupportCaseDomain();
        var agent = RequireEntity(domain, "Agent");
        var note = RequireEntity(domain, "Note");
        var authorProp = note.Properties.Single(p => p.Name == "Author");
        await Assert.That(ReferenceEquals(authorProp.Type, agent)).IsTrue();
    }

    [Test]
    public async Task Customer_HasExpectedProperties() {
        var domain = BuildSupportCaseDomain();
        var customer = RequireEntity(domain, "Customer");
        var names = customer.Properties.Select(p => p.Name).ToArray();
        await Assert.That(names).Contains("Name");
        await Assert.That(names).Contains("Email");
        await Assert.That(customer.Properties.Count).IsEqualTo(2);
    }

    [Test]
    public async Task SupportCase_HasExpectedProperties() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var names = supportCase.Properties.Select(p => p.Name).ToArray();
        await Assert.That(names).Contains("Title");
        await Assert.That(names).Contains("Priority");
        await Assert.That(names).Contains("IsEscalated");
        await Assert.That(supportCase.Properties.Count).IsEqualTo(3);
    }

    [Test]
    public async Task SupportCase_PropertyTypes_ReferenceCorrectPrimitives() {
        var domain = BuildSupportCaseDomain();
        var stringType = RequirePrimitive(domain, "string");
        var intType = RequirePrimitive(domain, "int");
        var supportCase = RequireEntity(domain, "SupportCase");
        var titleProp = supportCase.Properties.Single(p => p.Name == "Title");
        var priorityProp = supportCase.Properties.Single(p => p.Name == "Priority");
        await Assert.That(ReferenceEquals(titleProp.Type, stringType)).IsTrue();
        await Assert.That(ReferenceEquals(priorityProp.Type, intType)).IsTrue();
    }

    // ─── Stages ───────────────────────────────────────────────────────────────

    [Test]
    public async Task SupportCase_HasExpectedStages() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var names = supportCase.Stages.Select(s => s.Name).ToArray();
        await Assert.That(names).Contains("New");
        await Assert.That(names).Contains("InProgress");
        await Assert.That(names).Contains("Assigned");
        await Assert.That(names).Contains("Resolved");
    }

    [Test]
    public async Task AssignedStage_IsSubstageOfInProgress() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var assigned = supportCase.Stages.Single(s => s.Name == "Assigned");
        var inProgress = supportCase.Stages.Single(s => s.Name == "InProgress");
        await Assert.That(ReferenceEquals(assigned.Parent, inProgress)).IsTrue();
    }

    // ─── Actions and effects ───────────────────────────────────────────────────

    [Test]
    public async Task NewStage_HasAssignAction() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var newStage = supportCase.Stages.Single(s => s.Name == "New");
        await Assert.That(newStage.Actions.Select(a => a.Name)).Contains("Assign");
    }

    [Test]
    public async Task AssignAction_HasStageTransitionEffect_TargetingAssigned() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var newStage = supportCase.Stages.Single(s => s.Name == "New");
        var assignAction = newStage.Actions.Single(a => a.Name == "Assign");
        var transition = assignAction.Effects.OfType<StageTransition>().SingleOrDefault();
        await Assert.That(transition).IsNotNull();
        await Assert.That(transition!.TargetStage.Name).IsEqualTo("Assigned");
    }

    [Test]
    public async Task AssignAction_HasPublishEventEffect_ForCaseAssigned() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var newStage = supportCase.Stages.Single(s => s.Name == "New");
        var assignAction = newStage.Actions.Single(a => a.Name == "Assign");
        var publish = assignAction.Effects.OfType<PublishEvent>().SingleOrDefault();
        await Assert.That(publish).IsNotNull();
        await Assert.That(publish!.Event.Name).IsEqualTo("CaseAssigned");
    }

    [Test]
    public async Task AssignAction_HasAgentParameter() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var newStage = supportCase.Stages.Single(s => s.Name == "New");
        var assignAction = newStage.Actions.Single(a => a.Name == "Assign");
        var parameterNames = assignAction.Parameters.Cast<Property>().Select(p => p.Name);
        await Assert.That(parameterNames).Contains("Agent");
    }

    [Test]
    public async Task AssignAction_AgentParameter_ReferencesAgentEntity() {
        var domain = BuildSupportCaseDomain();
        var agentEntity = RequireEntity(domain, "Agent");
        var supportCase = RequireEntity(domain, "SupportCase");
        var newStage = supportCase.Stages.Single(s => s.Name == "New");
        var assignAction = newStage.Actions.Single(a => a.Name == "Assign");
        var agentParam = assignAction.Parameters.Cast<Property>().Single(p => p.Name == "Agent");
        await Assert.That(ReferenceEquals(agentParam.Type, agentEntity)).IsTrue();
    }

    [Test]
    public async Task InProgressStage_HasAddNoteAndResolveActions() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var inProgress = supportCase.Stages.Single(s => s.Name == "InProgress");
        var names = inProgress.Actions.Select(a => a.Name).ToArray();
        await Assert.That(names).Contains("AddNote");
        await Assert.That(names).Contains("Resolve");
    }

    [Test]
    public async Task AddNoteAction_HasCreateEntityInstanceEffect_ForNote() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var inProgress = supportCase.Stages.Single(s => s.Name == "InProgress");
        var addNoteAction = inProgress.Actions.Single(a => a.Name == "AddNote");
        var create = addNoteAction.Effects.OfType<CreateEntityInstance>().SingleOrDefault();
        await Assert.That(create).IsNotNull();
        await Assert.That(create!.EntityType.Name).IsEqualTo("Note");
    }

    [Test]
    public async Task AddNoteAction_CreateEntityInstance_UsesSupportCaseNotesOwnershipRelationship() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var inProgress = supportCase.Stages.Single(s => s.Name == "InProgress");
        var addNoteAction = inProgress.Actions.Single(a => a.Name == "AddNote");
        var create = addNoteAction.Effects.OfType<CreateEntityInstance>().Single();
        await Assert.That(create.OwnershipRelationship.Name).IsEqualTo("SupportCaseNotes");
        await Assert.That(create.OwnershipRelationship.SourceOwnsTarget).IsTrue();
    }

    [Test]
    public async Task AddNoteAction_CreateEntityInstance_TargetsSpecificNoteStage() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var note = RequireEntity(domain, "Note");
        var inProgress = supportCase.Stages.Single(s => s.Name == "InProgress");
        var addNoteAction = inProgress.Actions.Single(a => a.Name == "AddNote");
        var create = addNoteAction.Effects.OfType<CreateEntityInstance>().Single();
        await Assert.That(create.InitialStage).IsNotNull();
        await Assert.That(create.InitialStage!.Name).IsEqualTo("Draft");
        await Assert.That(note.Stages.Select(s => s.Name)).Contains("Draft");
    }

    [Test]
    public async Task AssignedStage_EffectiveActions_IncludeParentStageActions() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var assigned = supportCase.Stages.Single(s => s.Name == "Assigned");
        var effectiveNames = assigned.GetEffectiveActions().Select(a => a.Name).ToArray();
        await Assert.That(effectiveNames).Contains("Resolve");
        await Assert.That(effectiveNames).Contains("AddNote");
    }

    [Test]
    public async Task ResolveAction_HasStageTransitionEffect_TargetingResolved() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var inProgress = supportCase.Stages.Single(s => s.Name == "InProgress");
        var resolveAction = inProgress.Actions.Single(a => a.Name == "Resolve");
        var transition = resolveAction.Effects.OfType<StageTransition>().SingleOrDefault();
        await Assert.That(transition).IsNotNull();
        await Assert.That(transition!.TargetStage.Name).IsEqualTo("Resolved");
    }

    [Test]
    public async Task ResolveAction_HasPublishEventEffect_ForCaseResolved() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var inProgress = supportCase.Stages.Single(s => s.Name == "InProgress");
        var resolveAction = inProgress.Actions.Single(a => a.Name == "Resolve");
        var publish = resolveAction.Effects.OfType<PublishEvent>().SingleOrDefault();
        await Assert.That(publish).IsNotNull();
        await Assert.That(publish!.Event.Name).IsEqualTo("CaseResolved");
    }

    // ─── Events ───────────────────────────────────────────────────────────────

    [Test]
    public async Task SupportCase_HasExpectedEvents() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var names = supportCase.Events.Select(e => e.Name).ToArray();
        await Assert.That(names).Contains("CaseAssigned");
        await Assert.That(names).Contains("CaseResolved");
    }

    [Test]
    public async Task CaseAssignedEvent_HasAssignedToProperty() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var caseAssigned = supportCase.Events.Single(e => e.Name == "CaseAssigned");
        await Assert.That(caseAssigned.Properties.Select(p => p.Name)).Contains("AssignedTo");
    }

    [Test]
    public async Task CaseAssignedEvent_AssignedToProperty_ReferencesAgentEntity() {
        var domain = BuildSupportCaseDomain();
        var agentEntity = RequireEntity(domain, "Agent");
        var supportCase = RequireEntity(domain, "SupportCase");
        var caseAssigned = supportCase.Events.Single(e => e.Name == "CaseAssigned");
        var assignedTo = caseAssigned.Properties.Single(p => p.Name == "AssignedTo");
        await Assert.That(ReferenceEquals(assignedTo.Type, agentEntity)).IsTrue();
    }

    // ─── Policies ─────────────────────────────────────────────────────────────

    [Test]
    public async Task SupportCase_HasRequireTitlePolicy() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        await Assert.That(supportCase.Policies.Select(p => p.Name)).Contains("RequireTitle");
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
        var relationship = domain.Relationships.Single(r => r.Name == "CustomerCases");
        await Assert.That(relationship.Cardinality).IsEqualTo(RelationshipCardinality.OneToMany);
        await Assert.That(relationship.SourceOwnsTarget).IsTrue();
    }

    [Test]
    public async Task CustomerCasesRelationship_ConnectsCustomerToSupportCase() {
        var domain = BuildSupportCaseDomain();
        var customer = RequireEntity(domain, "Customer");
        var supportCase = RequireEntity(domain, "SupportCase");
        var relationship = domain.Relationships.Single(r => r.Name == "CustomerCases");
        await Assert.That(ReferenceEquals(relationship.Source, customer)).IsTrue();
        await Assert.That(ReferenceEquals(relationship.Target, supportCase)).IsTrue();
    }

    [Test]
    public async Task Customer_OutboundRelationships_ContainsCustomerCases() {
        var domain = BuildSupportCaseDomain();
        var customer = RequireEntity(domain, "Customer");
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
        var relationship = domain.Relationships.Single(r => r.Name == "SupportCaseNotes");
        await Assert.That(relationship.Cardinality).IsEqualTo(RelationshipCardinality.OneToMany);
        await Assert.That(relationship.SourceOwnsTarget).IsTrue();
    }

    [Test]
    public async Task SupportCaseNotesRelationship_ConnectsSupportCaseToNote() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
        var note = RequireEntity(domain, "Note");
        var relationship = domain.Relationships.Single(r => r.Name == "SupportCaseNotes");
        await Assert.That(ReferenceEquals(relationship.Source, supportCase)).IsTrue();
        await Assert.That(ReferenceEquals(relationship.Target, note)).IsTrue();
    }

    [Test]
    public async Task SupportCase_OutboundRelationships_ContainsSupportCaseNotes() {
        var domain = BuildSupportCaseDomain();
        var supportCase = RequireEntity(domain, "SupportCase");
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
        var relationship = domain.Relationships.Single(r => r.Name == "CustomerNotes");
        await Assert.That(relationship.Cardinality).IsEqualTo(RelationshipCardinality.OneToMany);
        await Assert.That(relationship.SourceOwnsTarget).IsFalse();
    }

    [Test]
    public async Task CustomerNotesRelationship_ConnectsCustomerToNote() {
        var domain = BuildSupportCaseDomain();
        var customer = RequireEntity(domain, "Customer");
        var note = RequireEntity(domain, "Note");
        var relationship = domain.Relationships.Single(r => r.Name == "CustomerNotes");
        await Assert.That(ReferenceEquals(relationship.Source, customer)).IsTrue();
        await Assert.That(ReferenceEquals(relationship.Target, note)).IsTrue();
    }

    [Test]
    public async Task Customer_OutboundRelationships_ContainsCustomerNotes() {
        var domain = BuildSupportCaseDomain();
        var customer = RequireEntity(domain, "Customer");
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
        var relationship = domain.Relationships.Single(r => r.Name == "AgentSupportCases");
        await Assert.That(relationship.Cardinality).IsEqualTo(RelationshipCardinality.ManyToMany);
        await Assert.That(relationship.SourceOwnsTarget).IsFalse();
    }

    [Test]
    public async Task AgentSupportCasesRelationship_ConnectsAgentToSupportCase() {
        var domain = BuildSupportCaseDomain();
        var agent = RequireEntity(domain, "Agent");
        var supportCase = RequireEntity(domain, "SupportCase");
        var relationship = domain.Relationships.Single(r => r.Name == "AgentSupportCases");
        await Assert.That(ReferenceEquals(relationship.Source, agent)).IsTrue();
        await Assert.That(ReferenceEquals(relationship.Target, supportCase)).IsTrue();
    }

    [Test]
    public async Task AgentSupportCasesRelationship_HasAssignmentTimestampProperties() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.Relationships.Single(r => r.Name == "AgentSupportCases");
        var names = relationship.Properties.Select(p => p.Name).ToArray();
        await Assert.That(names).Contains("AssignedAt");
        await Assert.That(names).Contains("UnassignedAt");
    }

    [Test]
    public async Task AgentSupportCasesRelationship_HasActiveAndInactiveStages() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.Relationships.Single(r => r.Name == "AgentSupportCases");
        var names = relationship.Stages.Select(s => s.Name).ToArray();
        await Assert.That(names).Contains("Active");
        await Assert.That(names).Contains("Inactive");
    }

    [Test]
    public async Task AgentSupportCasesRelationship_InactiveStage_IsNotChildOfActive() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.Relationships.Single(r => r.Name == "AgentSupportCases");
        var active = relationship.Stages.Single(s => s.Name == "Active");
        var inactive = relationship.Stages.Single(s => s.Name == "Inactive");
        await Assert.That(ReferenceEquals(inactive.Parent, active)).IsFalse();
    }

    [Test]
    public async Task AgentSupportCasesRelationship_InactiveStage_IsDefinedAfterActive() {
        var domain = BuildSupportCaseDomain();
        var relationship = domain.Relationships.Single(r => r.Name == "AgentSupportCases");
        var names = relationship.Stages.Select(s => s.Name).ToArray();
        await Assert.That(Array.IndexOf(names, "Active")).IsLessThan(Array.IndexOf(names, "Inactive"));
    }

    [Test]
    public async Task AgentSupportCasesRelationship_TimestampPropertyTypes_ReferenceInstantPrimitive() {
        var domain = BuildSupportCaseDomain();
        var instant = RequirePrimitive(domain, "instant");
        var relationship = domain.Relationships.Single(r => r.Name == "AgentSupportCases");
        var assignedAt = relationship.Properties.Single(p => p.Name == "AssignedAt");
        var unassignedAt = relationship.Properties.Single(p => p.Name == "UnassignedAt");
        await Assert.That(ReferenceEquals(assignedAt.Type, instant)).IsTrue();
        await Assert.That(ReferenceEquals(unassignedAt.Type, instant)).IsTrue();
    }

    [Test]
    public async Task Agent_OutboundRelationships_ContainsAgentSupportCases() {
        var domain = BuildSupportCaseDomain();
        var agent = RequireEntity(domain, "Agent");
        await Assert.That(agent.Relationships.Select(r => r.Name)).Contains("AgentSupportCases");
    }

    // ─── Domain factory ────────────────────────────────────────────────────────

    private static Domain BuildSupportCaseDomain() {
        var domain = DomainTestFactory.CreateDomain("Support Case Management");

        var stringType = new Primitive { Domain = domain, Name = "string", Category = TypeCategory.Text };
        var intType = new Primitive { Domain = domain, Name = "int", Category = TypeCategory.Integer };
        var boolType = new Primitive { Domain = domain, Name = "bool", Category = TypeCategory.Primitive };
        var instantType = new Primitive { Domain = domain, Name = "instant", Category = TypeCategory.Instant };
        domain.AddType(stringType);
        domain.AddType(intType);
        domain.AddType(boolType);
        domain.AddType(instantType);

        var customer = new Entity { Domain = domain, Name = "Customer" };
        customer.AddProperty(new Property { Domain = domain, Name = "Name", Type = stringType });
        customer.AddProperty(new Property { Domain = domain, Name = "Email", Type = stringType });
        domain.AddType(customer);

        var agent = new Entity { Domain = domain, Name = "Agent" };
        agent.AddProperty(new Property { Domain = domain, Name = "Name", Type = stringType });
        agent.AddProperty(new Property { Domain = domain, Name = "Email", Type = stringType });
        domain.AddType(agent);

        var note = new Entity { Domain = domain, Name = "Note" };
        note.AddProperty(new Property { Domain = domain, Name = "Content", Type = stringType });
        note.AddProperty(new Property { Domain = domain, Name = "Author", Type = agent });
        var noteDraftStage = new Stage { Domain = domain, Name = "Draft" };
        note.AddStage(noteDraftStage);
        domain.AddType(note);

        var supportCase = new Entity { Domain = domain, Name = "SupportCase" };
        supportCase.AddProperty(new Property { Domain = domain, Name = "Title", Type = stringType });
        supportCase.AddProperty(new Property { Domain = domain, Name = "Priority", Type = intType });
        supportCase.AddProperty(new Property { Domain = domain, Name = "IsEscalated", Type = boolType });
        domain.AddType(supportCase);

        var newStage = new Stage { Domain = domain, Name = "New" };
        var inProgressStage = new Stage { Domain = domain, Name = "InProgress" };
        var assignedStage = new Stage { Domain = domain, Name = "Assigned", Parent = inProgressStage };
        var resolvedStage = new Stage { Domain = domain, Name = "Resolved" };

        var caseAssignedEvent = new DomainEvent { Domain = domain, Name = "CaseAssigned" };
        caseAssignedEvent.AddProperty(new Property { Domain = domain, Name = "AssignedTo", Type = agent });
        var caseResolvedEvent = new DomainEvent { Domain = domain, Name = "CaseResolved" };
        supportCase.AddEvent(caseAssignedEvent);
        supportCase.AddEvent(caseResolvedEvent);

        var assignAction = new DomainAction { Domain = domain, Entity = supportCase, Name = "Assign" };
        assignAction.AddParameter(new Property { Domain = domain, Name = "Agent", Type = agent });
        assignAction.AddEffect(new StageTransition { TargetStage = assignedStage });
        assignAction.AddEffect(new PublishEvent { Event = caseAssignedEvent });
        newStage.AddAction(assignAction);

        var addNoteAction = new DomainAction { Domain = domain, Entity = supportCase, Name = "AddNote" };
        addNoteAction.AddParameter(new Property { Domain = domain, Name = "NoteText", Type = stringType });
        inProgressStage.AddAction(addNoteAction);

        var resolveAction = new DomainAction { Domain = domain, Entity = supportCase, Name = "Resolve" };
        resolveAction.AddEffect(new StageTransition { TargetStage = resolvedStage });
        resolveAction.AddEffect(new PublishEvent { Event = caseResolvedEvent });
        inProgressStage.AddAction(resolveAction);

        supportCase.AddStage(newStage);
        supportCase.AddStage(inProgressStage);
        supportCase.AddStage(assignedStage);
        supportCase.AddStage(resolvedStage);

        var requireTitle = new Policy {
            Domain = domain,
            Name = "RequireTitle",
            AggregationStrategy = PolicyAggregationStrategy.All
        };
        supportCase.AddPolicy(requireTitle);

        var ownership = new Relationship {
            Domain = domain,
            Name = "CustomerCases",
            Source = customer,
            Target = supportCase,
            Cardinality = RelationshipCardinality.OneToMany,
            SourceOwnsTarget = true
        };
        domain.AddRelationship(ownership);
        customer.AddRelationship(ownership);

        var caseNotes = new Relationship {
            Domain = domain,
            Name = "SupportCaseNotes",
            Source = supportCase,
            Target = note,
            Cardinality = RelationshipCardinality.OneToMany,
            SourceOwnsTarget = true
        };
        domain.AddRelationship(caseNotes);
        supportCase.AddRelationship(caseNotes);

        addNoteAction.AddEffect(new CreateEntityInstance {
            EntityType = note,
            OwnershipRelationship = caseNotes,
            InitialStage = noteDraftStage
        });

        var customerNotes = new Relationship {
            Domain = domain,
            Name = "CustomerNotes",
            Source = customer,
            Target = note,
            Cardinality = RelationshipCardinality.OneToMany,
            SourceOwnsTarget = false
        };
        domain.AddRelationship(customerNotes);
        customer.AddRelationship(customerNotes);

        var agentCases = new Relationship {
            Domain = domain,
            Name = "AgentSupportCases",
            Source = agent,
            Target = supportCase,
            Cardinality = RelationshipCardinality.ManyToMany,
            SourceOwnsTarget = false
        };
        var activeAssignmentStage = new Stage { Domain = domain, Name = "Active" };
        var inactiveAssignmentStage = new Stage { Domain = domain, Name = "Inactive" };
        agentCases.AddStage(activeAssignmentStage);
        agentCases.AddStage(inactiveAssignmentStage);
        agentCases.AddProperty(new Property { Domain = domain, Name = "AssignedAt", Type = instantType, Constraints = [new RequiredConstraint()] });
        agentCases.AddProperty(new Property { Domain = domain, Name = "UnassignedAt", Type = instantType });
        domain.AddRelationship(agentCases);
        agent.AddRelationship(agentCases);

        return domain;
    }

    private static Entity RequireEntity(Domain domain, string name) =>
        domain.Types.OfType<Entity>().Single(e => e.Name == name);

    private static Primitive RequirePrimitive(Domain domain, string name) =>
        domain.Types.OfType<Primitive>().Single(p => p.Name == name);

    private static Exception? Record(System.Action action) {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }
}